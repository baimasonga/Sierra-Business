using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SierraBusiness.SharedUI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class TradeAdvanceTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "rokela-advances-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly Session session = new();
    private Factory factory = null!;
    private BankingService banking = null!;
    private CommerceService commerce = null!;
    private BusinessService business = null!;
    private FinanceService finance = null!;
    private PartyService parties = null!;
    private Guid customer, supplier, product;
    private DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").AddInterceptors(new SyncCaptureInterceptor(SyncRole.Branch, "FREETOWN")).Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        banking = new(factory, session, options); commerce = new(factory, session, options); business = new(factory, options); finance = new(factory, session, options); parties = new(factory, session, options);
        await TestParties.Register(factory, "Deposit customer"); await TestParties.Register(factory, "Advance supplier", PartyType.Supplier);
        var all = await parties.Read(); customer = all.Single(p => p.Type == PartyType.Customer).Id; supplier = all.Single(p => p.Type == PartyType.Supplier).Id;
        product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "ADV-P", "Paper", "General", "piece", 100, TrackingMode.Quantity, 1));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    [Fact] public async Task RefundableCurrencyAdvanceTracksForeignBillUnitsRefundsAndReversal()
    {
        var fx = new PurchasingService(factory,session,Options.Create(new TradingOptions())); await fx.SetRate(new(Guid.NewGuid(),"USD",Today.AddDays(-1),20,"Reviewed daily rate"));
        await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Advance supplier",Today,[new(product,10,20)],null));
        var bill = await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Sale,"Deposit customer",Today,[new(product,1,5)],null,Currency:"USD"));
        var deposit = await banking.PostForeignAdvance(new(Guid.NewGuid(),TradeAdvanceKind.CustomerDeposit,customer,Today,"USD",10,1000,200,"Refundable currency deposit")); var credit = Assert.Single(await banking.ForeignAdvances()); Assert.Equal(10,credit.RemainingSource);
        await Assert.ThrowsAsync<BusinessException>(() => commerce.UseCredit(new(Guid.NewGuid(),credit.CreditId,10,null,bill,"Cannot treat currency units as local credit")));
        var request = new ForeignAdvanceUseCommand(Guid.NewGuid(),credit.CreditId,Today,3,bill,null,0,"Apply three dollars"); var preview = await banking.PreviewForeignAdvanceUse(request); Assert.Equal(60,preview.BillCarryingUsed); var applied = await banking.UseForeignAdvance(request with { ExpectedCalculation = preview.CalculationFingerprint });
        Assert.Equal(2,Assert.Single(await fx.ForeignReceivables()).SourceOutstanding); Assert.Equal(7,Assert.Single(await banking.ForeignAdvances()).RemainingSource);
        var refund = new ForeignAdvanceUseCommand(Guid.NewGuid(),credit.CreditId,Today,2,null,1000,40,"Refund two dollars"); var refundPreview = await banking.PreviewForeignAdvanceUse(refund); var refunded = await banking.UseForeignAdvance(refund with { ExpectedCalculation = refundPreview.CalculationFingerprint }); Assert.Equal(5,Assert.Single(await banking.ForeignAdvances()).RemainingSource);
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor,new(Guid.NewGuid(),deposit,"Advance is already used")));
        await business.Reverse(session.Actor,new(Guid.NewGuid(),refunded,"Cancel refund")); await business.Reverse(session.Actor,new(Guid.NewGuid(),applied,"Cancel allocation")); Assert.Equal(10,Assert.Single(await banking.ForeignAdvances()).RemainingSource); Assert.Equal(5,Assert.Single(await fx.ForeignReceivables()).SourceOutstanding);
        await business.Reverse(session.Actor,new(Guid.NewGuid(),deposit,"Cancel unused deposit")); Assert.Empty(await banking.ForeignAdvances());
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task CurrencyBankAdvanceUnitsAndClosingValuationStayTogether()
    {
        var fx = new PurchasingService(factory,session,Options.Create(new TradingOptions())); await fx.SetRate(new(Guid.NewGuid(),"USD",Today.AddDays(-1),20,"Initial rate")); await finance.CreateLedger(new(Guid.NewGuid(),6301,"Dollar bank",LedgerGroup.Asset,true)); await fx.ConfigureForeignBank(new(Guid.NewGuid(),6301,"USD"));
        await banking.PostForeignAdvance(new(Guid.NewGuid(),TradeAdvanceKind.CustomerDeposit,customer,Today,"USD",10,6301,0,"Currency deposit")); Assert.Equal(10,Assert.Single(await fx.ForeignBanks(Today)).SourceBalance);
        var advance = Assert.Single(await banking.ForeignAdvances()); var refund = new ForeignAdvanceUseCommand(Guid.NewGuid(),advance.CreditId,Today,2,null,6301,0,"Currency bank refund"); var review = await banking.PreviewForeignAdvanceUse(refund); await banking.UseForeignAdvance(refund with { ExpectedCalculation = review.CalculationFingerprint }); Assert.Equal(8,Assert.Single(await fx.ForeignBanks(Today)).SourceBalance);
        await fx.SetRate(new(Guid.NewGuid(),"USD",Today,22,"Closing rate")); var closing = await fx.PreviewForeignRevaluation(new(Today,["USD"])); Assert.Equal(0,closing.NetGain); Assert.Equal(-16,closing.Rows.Single(r => r.AccountCode == 2400).Gain); Assert.Equal(16,closing.Rows.Single(r => r.AccountCode == 6301).Gain);
        var valuation = await fx.PostForeignRevaluation(new(Guid.NewGuid(),closing.Request,closing.CalculationFingerprint,"Refundable balances at closing rate")); Assert.Equal(176,Assert.Single(await banking.ForeignAdvances()).LocalCarrying); await fx.CancelForeignRevaluation(new(Guid.NewGuid(),valuation,"Review cancelled")); Assert.Equal(160,Assert.Single(await banking.ForeignAdvances()).LocalCarrying);
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task SupplierCurrencyAdvanceAllocatesAtBillCarryingValueAndRefundsWithGain()
    {
        var fx = new PurchasingService(factory,session,Options.Create(new TradingOptions())); await fx.SetRate(new(Guid.NewGuid(),"USD",Today.AddDays(-1),20,"Original rate"));
        await banking.PostForeignAdvance(new(Guid.NewGuid(),TradeAdvanceKind.SupplierAdvance,supplier,Today,"USD",10,1000,200,"Refundable supplier currency advance"));
        await fx.SetRate(new(Guid.NewGuid(),"USD",Today,22,"Invoice-day rate")); var bill = await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Advance supplier",Today,[new(product,1,20)],null,Currency:"USD")); var credit = Assert.Single(await banking.ForeignAdvances());
        await Assert.ThrowsAsync<BusinessException>(() => banking.UseSupplierCredit(new(Guid.NewGuid(),credit.CreditId,100,null,bill,"Local credit use is prohibited")));
        var request = new ForeignAdvanceUseCommand(Guid.NewGuid(),credit.CreditId,Today,5,bill,null,0,"Apply half the advance"); var preview = await banking.PreviewForeignAdvanceUse(request); Assert.Equal(110,preview.BillCarryingUsed); Assert.Equal(100,preview.CreditCarryingUsed); Assert.Equal(10,preview.Lines.Single(l => l.AccountCode == (int)Fx.Gains).Credit);
        await banking.UseForeignAdvance(request with { ExpectedCalculation = preview.CalculationFingerprint }); Assert.Equal(15,Assert.Single(await fx.ForeignBills()).SourceOutstanding);
        var refund = new ForeignAdvanceUseCommand(Guid.NewGuid(),credit.CreditId,Today,5,null,1000,110,"Supplier returned remaining dollars converted to local cash"); var refundPreview = await banking.PreviewForeignAdvanceUse(refund); await banking.UseForeignAdvance(refund with { ExpectedCalculation = refundPreview.CalculationFingerprint }); Assert.Equal(0,Assert.Single(await banking.ForeignAdvances()).RemainingSource);
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task ChequeDepositIsUnavailableUntilClearedAndBounceOnlyReopensUsedAmount()
    {
        await banking.PostAdvance(new(Guid.NewGuid(),TradeAdvanceKind.CustomerDeposit,customer,1020,150,"Cheque deposit",new("DEP-001","Customer bank",Today)));
        var advance = Assert.Single(await banking.Advances()); Assert.Equal(0,advance.Remaining); var cheque = Assert.Single((await banking.Read()).Cheques);
        await Assert.ThrowsAsync<BusinessException>(() => commerce.UseCredit(new(Guid.NewGuid(),advance.CreditId,60,1000,null,"Cannot refund uncleared cheque")));
        await banking.ActOnCheque(new(Guid.NewGuid(),cheque.Id,0,ChequeAction.Deposit,Today,1010,"Bank deposit")); await banking.ActOnCheque(new(Guid.NewGuid(),cheque.Id,1,ChequeAction.Clear,Today,1010,"Cleared")); Assert.Equal(150,Assert.Single(await banking.Advances()).Remaining);
        var used = await commerce.UseCredit(new(Guid.NewGuid(),advance.CreditId,60,1000,null,"Part refund")); await banking.ActOnCheque(new(Guid.NewGuid(),cheque.Id,2,ChequeAction.Bounce,Today,1010,"Bank clawback")); Assert.Equal(0,Assert.Single(await banking.Advances()).Remaining); Assert.Equal(60,Assert.Single((await finance.Read(Today,Today)).Bills).Outstanding);
        await banking.ActOnCheque(new(Guid.NewGuid(),cheque.Id,3,ChequeAction.Undo,Today,1010,"Bank corrected clawback")); Assert.Equal(90,Assert.Single(await banking.Advances()).Remaining);
        await Assert.ThrowsAsync<BusinessException>(() => banking.ActOnCheque(new(Guid.NewGuid(),cheque.Id,4,ChequeAction.Undo,Today,1010,"Clearance cannot be undone with applied credit")));
        await business.Reverse(session.Actor,new(Guid.NewGuid(),used,"Cancel refund before undoing clearance")); await banking.ActOnCheque(new(Guid.NewGuid(),cheque.Id,4,ChequeAction.Undo,Today,1010,"Undo clearance")); Assert.Equal(0,Assert.Single(await banking.Advances()).Remaining);
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task SupplierChequeAdvanceSharesPrintingTrackingAndCannotReversePresentedPayment()
    {
        var extras = new BankingExtrasService(factory,session,Options.Create(new TradingOptions())); var book = await extras.SaveChequeBook(new(Guid.NewGuid(),null,0,1010,"Advance book","ADV",1,10,3,true));
        var doc = await banking.PostAdvance(new(Guid.NewGuid(),TradeAdvanceKind.SupplierAdvance,supplier,1010,100,"Supplier advance cheque",new("ADV001","",Today,book))); var cheque = Assert.Single(await extras.IssuedCheques()); Assert.Equal(doc,cheque.VoucherId); Assert.Equal(100,Assert.Single(await banking.Advances()).Remaining);
        await extras.ActOnIssuedCheque(new(Guid.NewGuid(),cheque.Id,IssuedChequeState.Presented,Today,"Bank paid")); await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor,new(Guid.NewGuid(),doc,"Cannot cancel a paid cheque")));
        await banking.UseSupplierCredit(new(Guid.NewGuid(),Assert.Single(await banking.Advances()).CreditId,100,1010,null,"Supplier refunded advance")); Assert.Equal(0,Assert.Single(await banking.Advances()).Remaining); Assert.Equal("ADV002",Assert.Single(await extras.ChequeBooks()).NextNumber);
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task CustomerDepositAllocatesPartiallyRefundsAndReversesWithoutDuplicateMoney()
    {
        var input = new TradeAdvanceCommand(Guid.NewGuid(), TradeAdvanceKind.CustomerDeposit, customer, 1000, 150, "Customer transfer");
        await banking.OpenTill(new(Guid.NewGuid(), 1000, 0));
        var deposit = await banking.PostAdvance(input); Assert.Equal(deposit, await banking.PostAdvance(input));
        var advance = Assert.Single(await banking.Advances()); Assert.Equal(150, advance.Remaining);
        Assert.Equal(150, Assert.Single(await banking.Tills()).Movement);
        await using (var db = factory.CreateDbContext())
        {
            var sourceChanges = await db.SyncChanges.Where(c => c.Entity == nameof(BusinessDocument) && c.Payload.Contains(deposit.ToString())).ToListAsync();
            var commit = Assert.Single(sourceChanges).Commit;
            Assert.True(await db.SyncChanges.AnyAsync(c => c.Commit == commit && c.Entity == nameof(CustomerCredit)));
            Assert.True(await db.SyncChanges.AnyAsync(c => c.Commit == commit && c.Entity == nameof(JournalLine)));
        }
        var initial = await finance.Read(Today, Today); Assert.Equal(0, initial.Profit); Assert.Equal(-150, initial.Balances.Single(b => b.Code == 2400).Closing);
        var statement = await parties.Statement(customer, Today, Today); Assert.Equal(150, statement.UnappliedCredit);
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Advance supplier", Today, [new(product, 10, 20)], null));
        var invoice = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Deposit customer", Today, [new(product, 1, 100)], null));
        var apply = new UseCreditCommand(Guid.NewGuid(), advance.CreditId, 60, null, invoice, "Apply deposit");
        var allocation = await commerce.UseCredit(apply); Assert.Equal(allocation, await commerce.UseCredit(apply));
        Assert.Equal(40, (await finance.Read(Today, Today)).Bills.Single(b => b.Id == invoice).Outstanding);
        var refund = await commerce.UseCredit(new(Guid.NewGuid(), advance.CreditId, 90, 1000, null, "Refund remainder"));
        Assert.Equal(0, Assert.Single(await banking.Advances()).Remaining);
        await Assert.ThrowsAsync<BusinessException>(() => commerce.UseCredit(new(Guid.NewGuid(), advance.CreditId, 1, 1000, null, "Excess")));
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), deposit, "Source still applied")));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), refund, "Undo refund"));
        Assert.Equal(90, Assert.Single(await banking.Advances()).Remaining);
        await business.Reverse(session.Actor, new(Guid.NewGuid(), allocation, "Undo allocation"));
        Assert.Equal(100, (await finance.Read(Today, Today)).Bills.Single(b => b.Id == invoice).Outstanding);
        await business.Reverse(session.Actor, new(Guid.NewGuid(), deposit, "Undo deposit")); Assert.Empty(await banking.Advances());
        var report = await finance.Read(Today, Today); Assert.Equal(report.Entries.Sum(e => e.Debit), report.Entries.Sum(e => e.Credit));
    }
    [Fact] public async Task SupplierAdvanceSettlesBillAndRejectsOtherPartiesAndOverspending()
    {
        var source = await banking.PostAdvance(new(Guid.NewGuid(), TradeAdvanceKind.SupplierAdvance, supplier, 1010, 250, "Supplier transfer"));
        var advance = Assert.Single(await banking.Advances()); Assert.Equal(source, advance.DocumentId);
        var invoice = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Advance supplier", Today, [new(product, 10, 20)], null));
        var other = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Other supplier", Today, [new(product, 1, 20)], null));
        await Assert.ThrowsAsync<BusinessException>(() => banking.UseSupplierCredit(new(Guid.NewGuid(), advance.CreditId, 10, null, other, "Wrong supplier")));
        await Assert.ThrowsAsync<BusinessException>(() => banking.UseSupplierCredit(new(Guid.NewGuid(), advance.CreditId, 201, null, invoice, "Over bill")));
        await banking.UseSupplierCredit(new(Guid.NewGuid(), advance.CreditId, 200, null, invoice, "Apply advance"));
        Assert.DoesNotContain((await finance.Read(Today, Today)).Bills, b => b.Id == invoice);
        await banking.UseSupplierCredit(new(Guid.NewGuid(), advance.CreditId, 50, 1010, null, "Supplier refunded remainder"));
        Assert.Equal(0, Assert.Single(await banking.Advances()).Remaining);
        var statement = await parties.Statement(supplier, Today, Today, true); Assert.Equal(0, statement.UnappliedCredit);
    }
    [Fact] public async Task InvalidInputsPermissionsAndClosedPeriodsLeaveNoAdvance()
    {
        var valid = new TradeAdvanceCommand(Guid.NewGuid(), TradeAdvanceKind.CustomerDeposit, customer, 1000, 20, "Deposit");
        foreach (var input in new[] { valid with { Amount = 0 }, valid with { Amount = 1.001m }, valid with { FundsAccount = 2400 }, valid with { PartyId = supplier }, valid with { PartyId = Guid.NewGuid() }, valid with { Kind = (TradeAdvanceKind)999 }, valid with { Reference = "" } })
            await Assert.ThrowsAsync<BusinessException>(() => banking.PostAdvance(input));
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "accounting.read" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => banking.PostAdvance(valid));
        session.Actor = session.Actor with { Permissions = BusinessPermissions.All.ToHashSet() };
        var closed = new BankingService(factory, session, Options.Create(new TradingOptions { ClosedThrough = Today }));
        await Assert.ThrowsAsync<BusinessException>(() => closed.PostAdvance(valid)); Assert.Empty(await banking.Advances());
        await banking.PostAdvance(valid);
        await Assert.ThrowsAsync<BusinessException>(() => banking.PostAdvance(valid with { Amount = 21 }));
        session.Actor = session.Actor with { Branch = "OTHER" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => banking.PostAdvance(valid));
    }
    [Fact] public async Task FormRecordsDepositAppliesInvoiceAndHidesWritesForReadOnlyStaff()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IBankingService>(banking); context.Services.AddSingleton<ICommerceService>(commerce);
        context.Services.AddSingleton<IFinanceService>(finance); context.Services.AddSingleton<IPartyService>(parties);
        var screen = context.Render<AdvancesWorkspace>(p => p.Add(c => c.Actor, session.Actor));
        screen.WaitForAssertion(() => Assert.DoesNotContain("Loading deposits", screen.Markup));
        screen.FindAll("label").Single(l => l.TextContent.StartsWith("Customer / supplier")).QuerySelector("select")!.Change(customer.ToString());
        screen.FindAll("label").Single(l => l.TextContent.StartsWith("Amount (NLe)")).QuerySelector("input")!.Change("120");
        screen.FindAll("label").Single(l => l.TextContent.StartsWith("Payment reference")).QuerySelector("input")!.Change("UI payment");
        Assert.False(screen.Find("form button.primary").HasAttribute("disabled")); screen.Find("form").Submit();
        screen.WaitForAssertion(() => Assert.Contains("Payment recorded", screen.Markup));
        Assert.Equal(120, Assert.Single(await banking.Advances()).Remaining);
        var receipt = (await business.Read(session.Actor)).Documents.Single(d => d.Settlement == "Customer deposit");
        var print = context.Render<BusinessDocumentOutput>(p => p.Add(c => c.Document, receipt));
        Assert.Contains("Customer deposit receipt", print.Markup); Assert.Contains("UI payment", print.Markup);
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Advance supplier", Today, [new(product, 10, 20)], null));
        var invoice = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Deposit customer", Today, [new(product, 1, 100)], null));
        screen.FindAll("button").Single(b => b.TextContent == "Refresh balances").Click();
        screen.WaitForAssertion(() => Assert.False(screen.FindAll("button").Single(b => b.TextContent == "Apply or refund").HasAttribute("disabled")));
        screen.FindAll("button").Single(b => b.TextContent == "Apply or refund").Click();
        screen.FindAll("label").Single(l => l.TextContent.StartsWith("Outstanding invoice")).QuerySelector("select")!.Change(invoice.ToString());
        screen.FindAll("label").Single(l => l.TextContent.StartsWith("Amount to apply")).QuerySelector("input")!.Change("100");
        screen.FindAll("label").Single(l => l.TextContent.StartsWith("Allocation / refund reason")).QuerySelector("input")!.Change("Apply UI deposit");
        screen.FindAll("form").Last().Submit();
        screen.WaitForAssertion(() => Assert.Contains("Allocation or refund posted", screen.Markup));
        Assert.Equal(20, Assert.Single(await banking.Advances()).Remaining);
        Assert.DoesNotContain((await finance.Read(Today, Today)).Bills, b => b.Id == invoice);
        screen.Dispose(); session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read", "accounting.read" } };
        var viewer = context.Render<AdvancesWorkspace>(p => p.Add(c => c.Actor, session.Actor));
        viewer.WaitForAssertion(() => Assert.Contains("Deposit customer", viewer.Markup));
        Assert.Empty(viewer.FindAll("form")); Assert.DoesNotContain("Apply or refund", viewer.Markup);
    }

    private sealed class Session : IActorSession { public Actor Actor = new("advances-owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
