using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class BankingTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "sierra-bank-" + Guid.NewGuid().ToString("N") + ".db");
    private Factory factory = null!; private readonly Session session = new(); private BusinessService business = null!; private BankingService banking = null!; private FinanceService finance = null!; private ReturnService returns = null!;
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow); private Guid product, purchase;
    public async Task InitializeAsync() { factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options); await using var db = factory.CreateDbContext(); await db.Database.MigrateAsync(); var options = Options.Create(new TradingOptions()); business = new(factory, options); banking = new(factory, session, options); finance = new(factory, session, options); returns = new(factory, session, options); product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "P", "Paper", "Office", "ream", 100, TrackingMode.Quantity, 1)); purchase = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(product, 20, 40)], null)); }
    public Task DisposeAsync() { File.Delete(path); return Task.CompletedTask; }
    private Task<Guid> Sale(PaymentMethod? payment = PaymentMethod.Cash) => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "School", today, [new(product, 1, 100)], payment, "CH001", "Test bank", today));
    private Task<FinanceSnapshot> Report() => finance.Read(today, today);
    private Task<Guid> Action(ChequeView c, ChequeAction action) => banking.ActOnCheque(new(Guid.NewGuid(), c.Id, c.Version, action, today, 1010, "Bank confirmation"));
    private async Task<ChequeView> Cheque() => (await banking.Read()).Cheques.Single();
    [Fact] public void CsvValidatesQuotedReferencesAndAmounts() { Assert.Equal("REF, ONE", BankStatementCsv.Parse("Date,Reference,Amount\n2026-01-01,\"REF, ONE\",-12.50").Single().Reference); Assert.Throws<BusinessException>(() => BankStatementCsv.Parse("Date,Reference,Amount\n2026-01-01,X,1.001")); Assert.Throws<BusinessException>(() => BankStatementCsv.Parse("Date,Reference,Amount\n2026-01-01,\"X,1")); }
    [Fact] public async Task StatementMatchesAreReviewedUniqueAndVoidRequiresCancellation()
    {
        await Sale(PaymentMethod.BankTransfer); var report = await Report(); var entry = report.Entries.Single(x => x.AccountCode == 1010); var amount = entry.Debit;
        var input = new ImportStatementCommand(Guid.NewGuid(), 1010, "September", 0, amount, $"Date,Reference,Amount\n{today:yyyy-MM-dd},CH001,{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        var id = await banking.Import(input); Assert.Equal(id, await banking.Import(input)); await Assert.ThrowsAsync<BusinessException>(() => banking.Import(input with { OperationId = Guid.NewGuid(), Reference = "Duplicate" }));
        var row = (await banking.Read()).Rows.Single(); Assert.Contains(row.Candidates, x => x.JournalLineId == entry.Id); Assert.Null(row.MatchId);
        await Assert.ThrowsAsync<BusinessException>(() => finance.Reconcile(new(Guid.NewGuid(), entry.Id, today.AddDays(-1), "wrong", row.Id)));
        var match = await finance.Reconcile(new(Guid.NewGuid(), entry.Id, today, "CH001", row.Id)); Assert.Equal(match, (await banking.Read()).Rows.Single().MatchId);
        await Assert.ThrowsAsync<BusinessException>(() => banking.VoidStatement(new(Guid.NewGuid(), id, "Wrong statement")));
        await finance.CancelMatch(new(Guid.NewGuid(), match, "Correction")); await banking.VoidStatement(new(Guid.NewGuid(), id, "Wrong statement")); Assert.Empty((await banking.Read()).Rows);
    }
    [Fact] public async Task ChequeDepositClearBounceSettlementAndUndoKeepAccountsBalanced()
    {
        var source = await Sale(PaymentMethod.Cheque); var c = await Cheque(); var amount = c.Amount;
        await Action(c, ChequeAction.Deposit); Assert.Equal(amount, (await Report()).Balances.Single(x => x.Code == 1030).Closing); Assert.Equal(0, (await Report()).Balances.Single(x => x.Code == 1010).Closing);
        await Assert.ThrowsAsync<BusinessException>(() => Action(c, ChequeAction.Clear));
        await Action(await Cheque(), ChequeAction.Clear); Assert.Equal(amount, (await Report()).Balances.Single(x => x.Code == 1010).Closing);
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), source, "Need undo first")));
        var bounce = await Action(await Cheque(), ChequeAction.Bounce); Assert.Equal(amount, (await Report()).Bills.Single(x => x.Id == bounce).Outstanding);
        var receipt = await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, today, "Recovered bounced cheque", [new(1000, amount, 0), new(1100, 0, amount)], [new(bounce, amount)]));
        await Assert.ThrowsAsync<BusinessException>(async () => await Action(await Cheque(), ChequeAction.Undo));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), receipt, "Correct recovery"));
        await Action(await Cheque(), ChequeAction.Undo); await Action(await Cheque(), ChequeAction.Undo); await Action(await Cheque(), ChequeAction.Undo);
        Assert.Equal(ChequeState.Received, (await Cheque()).State); await business.Reverse(session.Actor, new(Guid.NewGuid(), source, "Cancelled sale")); Assert.Empty((await banking.Read()).Cheques);
        var r = await Report(); Assert.Equal(r.Balances.Sum(x => x.Debit), r.Balances.Sum(x => x.Credit));
    }
    [Fact] public async Task ReceivedChequeSettlesCreditInvoiceAndRejectsOverpayment()
    {
        var invoice = await Sale(null); var amount = (await Report()).Bills.Single(x => x.Id == invoice).Outstanding;
        await Assert.ThrowsAsync<BusinessException>(() => banking.ReceiveCheque(new(Guid.NewGuid(), invoice, amount + 1, "R1", "Bank", today)));
        var command = new ReceiveChequeCommand(Guid.NewGuid(), invoice, amount, "R1", "Bank", today.AddDays(1)); var id = await banking.ReceiveCheque(command); Assert.Equal(id, await banking.ReceiveCheque(command));
        Assert.DoesNotContain((await Report()).Bills, x => x.Id == invoice && x.Outstanding > 0); await Assert.ThrowsAsync<BusinessException>(async () => await Action(await Cheque(), ChequeAction.Deposit));
    }
    [Fact] public async Task PaidPurchaseCanBecomeSupplierCreditWithPartialRefundAndAllocation()
    {
        var bill = (await Report()).Bills.Single(x => x.Id == purchase); await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Supplier paid", [new(2000, bill.Outstanding, 0), new(1000, 0, bill.Outstanding)], [new(purchase, bill.Outstanding)]));
        var returned = await returns.Post(new(Guid.NewGuid(), purchase, "Damaged goods", [new(product, null, null, null, 2)], ReturnDisposition.SupplierCredit));
        var credit = (await banking.Read()).SupplierCredits.Single(); Assert.Equal(80, credit.Remaining);
        await banking.UseSupplierCredit(new(Guid.NewGuid(), credit.Id, 30, 1000, null, "Cash received"));
        await Assert.ThrowsAsync<BusinessException>(() => banking.UseSupplierCredit(new(Guid.NewGuid(), credit.Id, 51, 1000, null, "Too much")));
        var next = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(product, 2, 40)], null));
        await banking.UseSupplierCredit(new(Guid.NewGuid(), credit.Id, 50, null, next, "Apply credit")); Assert.Equal(0, (await banking.Read()).SupplierCredits.Single().Remaining); Assert.Equal(30, (await Report()).Bills.Single(x => x.Id == next).Outstanding);
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), returned, "Credit already used")));
    }
    [Fact] public async Task TillCountsIncludeLedgerActivityAndRejectStaleClosing()
    {
        var input = new OpenTillCommand(Guid.NewGuid(), 1000, 25); var shift = await banking.OpenTill(input); Assert.Equal(shift, await banking.OpenTill(input)); await Assert.ThrowsAsync<BusinessException>(() => banking.OpenTill(input with { OperationId = Guid.NewGuid() }));
        var sale = await Sale(); var t = (await banking.Tills()).Single(); Assert.Single(t.Movements); Assert.Equal(25 + t.Movement, t.Expected);
        await Assert.ThrowsAsync<BusinessException>(() => banking.CloseTill(new(Guid.NewGuid(), shift, 0, t.Expected, "Stale count")));
        await banking.CloseTill(new(Guid.NewGuid(), shift, t.Movement, t.Expected - 2, "Short two leones")); await business.Reverse(session.Actor, new(Guid.NewGuid(), sale, "After closing"));
        t = (await banking.Tills()).Single(); Assert.Equal(-2, t.Variance); Assert.Single(t.Movements); await using var db = factory.CreateDbContext(); var close = await db.TillCloses.SingleAsync(); close.Counted = 999; await Assert.ThrowsAsync<BusinessException>(() => db.SaveChangesAsync());
    }
    [Fact] public async Task BankingAndTillPermissionsAreEnforced() { session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read" } }; await Assert.ThrowsAsync<UnauthorizedAccessException>(() => banking.Read()); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => banking.OpenTill(new(Guid.NewGuid(), 1000, 0))); Assert.Empty(await banking.Tills()); }
    [Fact] public async Task PartPaidPurchaseReturnSplitsBillCreditFromActualBankRefund()
    {
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Part payment", [new(2000, 760, 0), new(1000, 0, 760)], [new(purchase, 760)]));
        var returned = await returns.Post(new(Guid.NewGuid(), purchase, "Return two", [new(product, null, null, null, 2)], ReturnDisposition.BankRefund));
        await using var db = factory.CreateDbContext(); Assert.Equal(40, (await db.ReturnAllocations.Where(x => x.ReturnDocumentId == returned).ToListAsync()).Sum(x => x.BillCredit));
        Assert.Equal(40, (await Report()).Balances.Single(x => x.Code == 1010).Closing); Assert.DoesNotContain((await Report()).Bills, x => x.Id == purchase); Assert.Empty((await banking.Read()).SupplierCredits);
    }
    [Fact] public async Task TillOwnershipAndOtherStaffPostingsRemainTraceable()
    {
        var shift = await banking.OpenTill(new(Guid.NewGuid(), 1000, 0)); session.Actor = session.Actor with { Id = "clerk", Permissions = new HashSet<string> { "business.read", "sales.create", "tills.use" } };
        await Sale(); Assert.Empty(await banking.Tills()); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => banking.CloseTill(new(Guid.NewGuid(), shift, 100, 100, "Another till")));
        session.Actor = session.Actor with { Id = "owner", Permissions = BusinessPermissions.All.ToHashSet() }; var till = (await banking.Tills()).Single(); Assert.Equal("clerk", till.Movements.Single().Actor); Assert.Equal(100, till.Expected);
    }
    [Fact] public async Task StatementBalanceMismatchAndAncientDatesAreHandledSafely()
    {
        var input = new ImportStatementCommand(Guid.NewGuid(), 1010, "Ancient", 0, 2, "Date,Reference,Amount\n0001-01-01,OLD,1");
        await Assert.ThrowsAsync<BusinessException>(() => banking.Import(input)); Assert.Empty((await banking.Read()).Statements);
        await banking.Import(input with { ClosingBalance = 1 }); Assert.Empty((await banking.Read()).Rows.Single().Candidates);
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}


