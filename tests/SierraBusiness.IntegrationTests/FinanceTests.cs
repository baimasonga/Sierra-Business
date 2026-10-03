using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class FinanceTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-finance-{Guid.NewGuid():N}.db");
    private Factory factory = null!;
    private FinanceService finance = null!;
    private BusinessService trading = null!;
    private readonly Session session = new();
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using var db = factory.CreateDbContext(); await db.Database.MigrateAsync();
        finance = new(factory, session, Options.Create(new TradingOptions())); trading = new(factory, Options.Create(new TradingOptions()));
    }
    public Task DisposeAsync() { File.Delete(path); return Task.CompletedTask; }
    private Task<FinanceSnapshot> Report() => finance.Read(today.AddYears(-1), today);
    private Task<Guid> Fund() => finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Journal, today, "Owner capital", [new(1000, 1000, 0), new(3000, 0, 1000)], []));
    private async Task<Guid> CreditInvoice()
    {
        var p = await trading.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: .0001m));
        await trading.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today.AddDays(-10), [new(p, 10, 50)], null));
        return await trading.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "School", today.AddDays(-5), [new(p, 2, 100)], null, DueDate: today.AddDays(-2)));
    }
    [Fact] public async Task VoucherBalancesReportsAndReplaysWithoutDuplicateJournal()
    {
        await Fund();
        var command = new VoucherCommand(Guid.NewGuid(), VoucherType.Payment, today, "Office rent", [new(5100, 125, 0, "Head office"), new(1000, 0, 125)], []);
        var id = await finance.PostVoucher(command); Assert.Equal(id, await finance.PostVoucher(command));
        var report = await Report(); Assert.Equal(-125, report.Profit); Assert.Equal(875, report.Assets); Assert.Equal(1000, report.Equity);
        Assert.Equal(report.Assets, report.Liabilities + report.Equity + report.RetainedProfit);
        Assert.Equal(report.Balances.Sum(x => x.Debit), report.Balances.Sum(x => x.Credit));
        Assert.Equal(2, report.Entries.Count(x => x.DocumentId == id));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(command with { Narration = "Changed" }));
    }
    [Fact] public async Task AllocatedReceiptsReduceAgeingAndBlockBillReversalUntilSettlementReversed()
    {
        var invoice = await CreditInvoice();
        var before = await Report(); Assert.Equal(2, before.Bills.Single(x => x.Id == invoice).OverdueDays);
        var input = new VoucherCommand(Guid.NewGuid(), VoucherType.Receipt, today, "School receipt", [new(1000, 80, 0), new(1100, 0, 80)], [new(invoice, 80)]);
        var receipt = await finance.PostVoucher(input);
        Assert.Equal(120, (await Report()).Bills.Single(x => x.Id == invoice).Outstanding);
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(input with { OperationId = Guid.NewGuid(), Lines = [new(1000, 150, 0), new(1100, 0, 150)], Allocations = [new(invoice, 150)] }));
        await Assert.ThrowsAsync<BusinessException>(() => trading.Reverse(session.Actor, new(Guid.NewGuid(), invoice, "Wrong bill")));
        await trading.Reverse(session.Actor, new(Guid.NewGuid(), receipt, "Wrong receipt"));
        Assert.Equal(200, (await Report()).Bills.Single(x => x.Id == invoice).Outstanding);
        await trading.Reverse(session.Actor, new(Guid.NewGuid(), invoice, "Return entire sale"));
        Assert.DoesNotContain((await Report()).Bills, x => x.Id == invoice);
    }
    [Fact] public async Task AsAtReportDoesNotApplyFutureSettlementsOrFutureReversals()
    {
        var invoice = await CreditInvoice();
        var receipt = await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, today.AddDays(-1), "Receipt", [new(1000, 50, 0), new(1100, 0, 50)], [new(invoice, 50)]));
        Assert.Equal(200, (await finance.Read(today.AddMonths(-1), today.AddDays(-2))).Bills.Single(x => x.Id == invoice).Outstanding);
        await trading.Reverse(session.Actor, new(Guid.NewGuid(), receipt, "Undo"));
        Assert.Equal(150, (await finance.Read(today.AddMonths(-1), today.AddDays(-1))).Bills.Single(x => x.Id == invoice).Outstanding);
        Assert.Equal(200, (await Report()).Bills.Single(x => x.Id == invoice).Outstanding);
    }
    [Fact] public async Task SupplierPaymentIsAllocatedAndControlJournalBypassIsRejected()
    {
        await CreditInvoice(); var bill = (await Report()).Bills.Single(x => !x.Receivable);
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Pay supplier", [new(2000, 100, 0), new(1010, 0, 100)], [new(bill.Id, 100)]));
        Assert.Equal(400, (await Report()).Bills.Single(x => x.Id == bill.Id).Outstanding);
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Journal, today, "Bypass", [new(1100, 20, 0), new(4100, 0, 20)], [])));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Journal, today, "Inventory bypass", [new(1200, 20, 0), new(3000, 0, 20)], [])));
    }
    [Fact] public async Task InvalidVouchersRollBackAndPermissionsAreCheckedOnEveryCall()
    {
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Journal, today, "Unbalanced", [new(1000, 20, 0), new(3000, 0, 10)], [])));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Journal, today.AddDays(1), "Future", [new(1000, 20, 0), new(3000, 0, 20)], [])));
        Assert.Empty((await Report()).Entries);
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "accounting.read" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(Fund);
        await Report(); session.Actor = session.Actor with { Branch = "OTHER" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(Report);
    }
    [Fact] public async Task CustomLedgerBudgetAndCostCentreActualIncludeReversals()
    {
        await finance.CreateLedger(new(Guid.NewGuid(), 6000, "Rent", LedgerGroup.Expense));
        await finance.SetBudget(new(Guid.NewGuid(), 6000, today.AddDays(-1), today.AddDays(1), 100, "Office"));
        var id = await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Rent", [new(6000, 80, 0, "Office"), new(1000, 0, 80)], []));
        Assert.Equal(80, (await Report()).Budgets.Single().Actual);
        await trading.Reverse(session.Actor, new(Guid.NewGuid(), id, "Correction"));
        Assert.Equal(0, (await Report()).Budgets.Single().Actual);
        await Assert.ThrowsAsync<BusinessException>(() => finance.SetBudget(new(Guid.NewGuid(), 6000, today, today.AddDays(2), 20, "Office")));
    }
    [Fact] public async Task BankMatchIsImmutableUniqueAndDateChecked()
    {
        await Fund(); var entry = (await Report()).Entries.Single(x => x.AccountCode == 1000);
        await Assert.ThrowsAsync<BusinessException>(() => finance.Reconcile(new(Guid.NewGuid(), entry.Id, today.AddDays(-1), "Statement")));
        var command = new BankMatchCommand(Guid.NewGuid(), entry.Id, today, "Statement page 1");
        var id = await finance.Reconcile(command); Assert.Equal(id, await finance.Reconcile(command));
        await Assert.ThrowsAsync<BusinessException>(() => finance.Reconcile(command with { OperationId = Guid.NewGuid() }));
        Assert.Equal(today, (await Report()).Entries.Single(x => x.Id == entry.Id).ClearedDate);
        await using var db = factory.CreateDbContext(); var match = await db.BankMatches.SingleAsync(); match.StatementReference = "tampered";
        await Assert.ThrowsAsync<BusinessException>(() => db.SaveChangesAsync());
    }
    [Fact] public async Task CancelledMatchRetainsHistoryAllowsReplacementAndNeverChangesLedger()
    {
        await Fund(); var entry = (await Report()).Entries.Single(x => x.AccountCode == 1000);
        var first = await finance.Reconcile(new(Guid.NewGuid(), entry.Id, today, "Wrong statement"));
        var command = new CancelBankMatchCommand(Guid.NewGuid(), first, "Wrong reference selected");
        var cancellation = await finance.CancelMatch(command);
        Assert.Equal(cancellation, await finance.CancelMatch(command));
        await Assert.ThrowsAsync<BusinessException>(() => finance.CancelMatch(command with { OperationId = Guid.NewGuid() }));
        Assert.Null((await Report()).Entries.Single(x => x.Id == entry.Id).ClearedDate);
        var second = await finance.Reconcile(new(Guid.NewGuid(), entry.Id, today, "Correct statement"));
        var history = await finance.MatchHistory(); Assert.Equal(2, history.Count);
        Assert.Equal("Wrong reference selected", history.Single(x => x.Id == first).CancellationReason);
        Assert.Null(history.Single(x => x.Id == second).CancellationReason);
        Assert.Equal(1000, (await Report()).BankBalances.Single(x => x.AccountCode == 1000).Cleared);
        await using var db = factory.CreateDbContext(); var stored = await db.BankMatchCancellations.SingleAsync(); db.Remove(stored);
        await Assert.ThrowsAsync<BusinessException>(() => db.SaveChangesAsync());
    }
    [Fact] public async Task CashFlowExcludesTransfersAndNoncashAndReconcilesOpeningAndReversals()
    {
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Journal, today.AddDays(-1), "Opening capital", [new(1000, 1000, 0), new(3000, 0, 1000)], []));
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Contra, today, "Deposit cash", [new(1010, 200, 0), new(1000, 0, 200)], []));
        var expense = await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Rent", [new(5100, 100, 0), new(1010, 0, 100)], []));
        await CreditInvoice();
        var report = await finance.CashFlow(today, today);
        Assert.Equal(1000, report.Opening); Assert.Equal(0, report.Received); Assert.Equal(100, report.Paid); Assert.Equal(900, report.Closing); Assert.Single(report.Entries);
        await trading.Reverse(session.Actor, new(Guid.NewGuid(), expense, "Correction"));
        report = await finance.CashFlow(today, today);
        Assert.Equal(100, report.Received); Assert.Equal(100, report.Paid); Assert.Equal(1000, report.Closing);
        Assert.Equal(report.Closing, report.Opening + report.Received - report.Paid);
    }
    [Fact] public async Task MatchCorrectionRequiresCurrentWritePermissionAndBranch()
    {
        await Fund(); var entry = (await Report()).Entries.Single(x => x.AccountCode == 1000);
        var match = await finance.Reconcile(new(Guid.NewGuid(), entry.Id, today, "Statement"));
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "accounting.read" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => finance.CancelMatch(new(Guid.NewGuid(), match, "Denied")));
        await finance.MatchHistory(); await finance.CashFlow(today, today);
        session.Actor = session.Actor with { Branch = "OTHER" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => finance.MatchHistory());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => finance.CashFlow(today, today));
    }
    private sealed class Session : IActorSession
    {
        public Actor Actor = new("finance-owner", "FREETOWN", new HashSet<string> { "business.read", "catalogue.write", "pricing.manage", "sales.create", "inventory.receive", "documents.reverse", "accounting.read", "accounting.write", "audit.read" });
        public Task<Actor> CurrentActor() => Task.FromResult(Actor);
    }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}

