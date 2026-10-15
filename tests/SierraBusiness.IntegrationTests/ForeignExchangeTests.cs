using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class ForeignExchangeTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "rokela-fx-" + Guid.NewGuid().ToString("N") + ".db"); private Factory factory = null!; private PurchasingService fx = null!; private FinanceService finance = null!; private BusinessService business = null!; private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow); private Guid product;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=" + path + ";Pooling=False").Options); await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db); await TestParties.Register(factory, "School");
        var options = Options.Create(new TradingOptions()); fx = new(factory, session, options); finance = new(factory, session, options); business = new(factory, options);
        await fx.SetRate(new(Guid.NewGuid(), "USD", today.AddDays(-1), 20, "Test reference")); await fx.SetRate(new(Guid.NewGuid(), "EUR", today.AddDays(-1), 30, "Test reference"));
        product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "FXITEM", "Currency invoice item", "General", "piece", 200, TrackingMode.Quantity, MinimumPrice: 100));
        await finance.CreateLedger(new(Guid.NewGuid(), 6010, "USD currency bank", LedgerGroup.Asset, true)); await finance.CreateLedger(new(Guid.NewGuid(), 6011, "EUR currency bank", LedgerGroup.Asset, true)); await finance.CreateLedger(new(Guid.NewGuid(), 6020, "Misc income", LedgerGroup.Income));
        await fx.ConfigureForeignBank(new(Guid.NewGuid(), 6010, "USD")); await fx.ConfigureForeignBank(new(Guid.NewGuid(), 6011, "EUR"));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private async Task<Guid> Trade(ForeignBankTradeCommand command) { var preview = await fx.PreviewForeignBankTrade(command); return await fx.PostForeignBankTrade(command with { ExpectedCalculation = preview.CalculationFingerprint }); }
    private async Task<Guid> Invoice(decimal price = 10)
    {
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today.AddDays(-1), [new(product, 2, 100)], null));
        return await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "School", today.AddDays(-1), [new(product, 2, price)], null, Currency: "USD"));
    }
    [Fact] public async Task ForeignSaleRetainsTaxRateAndCollectionsRecordUnitsAndGain()
    {
        business = new(factory, Options.Create(new TradingOptions { TaxRate = .15m })); var invoice = await Invoice(); var state = await business.Read(session.Actor); var doc = state.Documents.Single(d => d.Id == invoice); Assert.Equal(460, doc.Total);
        Assert.Equal(23, Assert.Single(await fx.ForeignReceivables()).SourceOutstanding); Assert.Empty(await fx.ForeignBills());
        await fx.SetRate(new(Guid.NewGuid(), "USD", today, 22, "New reference"));
        var command = new ForeignCollectionCommand(Guid.NewGuid(), invoice, today, 11.5m, 253, 6010, "Bank receipt"); var received = await fx.CollectForeign(command); Assert.Equal(received, await fx.CollectForeign(command));
        var bank = (await fx.ForeignBanks(today)).Single(b => b.AccountCode == 6010); Assert.Equal(11.5m, bank.SourceBalance); Assert.Equal(253, bank.LocalCarrying);
        var remaining = Assert.Single(await fx.ForeignReceivables()); Assert.Equal(11.5m, remaining.SourceOutstanding); Assert.Equal(230, remaining.LocalCarrying);
        state = await business.Read(session.Actor); Assert.Equal(23, state.Ledger.Single(l => (int)l.Account == 4200).Credit); Assert.Equal(state.Ledger.Sum(l => l.Debit), state.Ledger.Sum(l => l.Credit));
        using var json = System.Text.Json.JsonDocument.Parse(doc.Detail); Assert.Equal(.15m, json.RootElement.GetProperty("TaxRate").GetDecimal()); Assert.Equal(20, json.RootElement.GetProperty("ExchangeRate").GetDecimal());
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, today, "Untracked bank money", [new(6010, 20, 0), new(6020, 0, 20)], [])));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), received, "Undo receipt")); Assert.Equal(0, (await fx.ForeignBanks(today)).Single(b => b.AccountCode == 6010).SourceBalance); Assert.Equal(23, Assert.Single(await fx.ForeignReceivables()).SourceOutstanding);
    }
    [Fact] public async Task SupplierPaymentUsesTheForeignBanksCarryingCostAndRejectsOtherCurrency()
    {
        await Trade(new(Guid.NewGuid(), ForeignBankTradeKind.Deposit, today.AddDays(-1), 6010, 3000, 100, 2000, 0, "Opening equity"));
        var bill = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today.AddDays(-1), [new(product, 2, 50)], null, Currency: "USD"));
        await fx.SetRate(new(Guid.NewGuid(), "USD", today, 22, "New reference"));
        await Assert.ThrowsAsync<BusinessException>(() => fx.PayForeign(new(Guid.NewGuid(), bill, today, 50, 1100, 6010, "Wrong carrying")));
        await Assert.ThrowsAsync<BusinessException>(() => fx.PayForeign(new(Guid.NewGuid(), bill, today, 50, 1000, 6011, "Wrong currency")));
        var id = await fx.PayForeign(new(Guid.NewGuid(), bill, today, 50, 1000, 6010, "Supplier paid"));
        var bank = (await fx.ForeignBanks(today)).Single(b => b.AccountCode == 6010); Assert.Equal(50, bank.SourceBalance); Assert.Equal(1000, bank.LocalCarrying);
        Assert.Equal(50, Assert.Single(await fx.ForeignBills()).SourceOutstanding);
        await business.Reverse(session.Actor, new(Guid.NewGuid(), id, "Undo payment")); Assert.Equal(100, (await fx.ForeignBanks(today)).Single(b => b.AccountCode == 6010).SourceBalance);
    }
    [Fact] public async Task CrossCurrencyTransferBalancesAndChangedPreviewsCannotPost()
    {
        var opening = await Trade(new(Guid.NewGuid(), ForeignBankTradeKind.Deposit, today.AddDays(-1), 6010, 3000, 100, 2000, 0, "Opening"));
        var transfer = new ForeignBankTradeCommand(Guid.NewGuid(), ForeignBankTradeKind.Transfer, today, 6010, 6011, 60, 0, 40, "Exchange USD to EUR");
        var preview = await fx.PreviewForeignBankTrade(transfer); Assert.Equal(1200, preview.SourceCarryingUsed); Assert.Equal(preview.Lines.Sum(l => l.Debit), preview.Lines.Sum(l => l.Credit));
        var id = await fx.PostForeignBankTrade(transfer with { ExpectedCalculation = preview.CalculationFingerprint });
        var banks = await fx.ForeignBanks(today); Assert.Equal(40, banks.Single(b => b.AccountCode == 6010).SourceBalance); Assert.Equal(40, banks.Single(b => b.AccountCode == 6011).SourceBalance);
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), opening, "Earlier incoming bank money")));
        var withdraw = new ForeignBankTradeCommand(Guid.NewGuid(), ForeignBankTradeKind.Withdrawal, today, 6010, 1000, 10, 220, 0, "Conversion"); var old = await fx.PreviewForeignBankTrade(withdraw);
        await Trade(new(Guid.NewGuid(), ForeignBankTradeKind.Deposit, today, 6010, 3000, 10, 250, 0, "Later contribution"));
        await Assert.ThrowsAsync<BusinessException>(() => fx.PostForeignBankTrade(withdraw with { ExpectedCalculation = old.CalculationFingerprint }));
        await Assert.ThrowsAsync<BusinessException>(() => fx.PreviewForeignBankTrade(withdraw with { SourceAmount = 500 }));
    }
    [Fact] public async Task ClosingValuationAndCancellationReverseBothDatesAndFreezeEarlierHistory()
    {
        await Trade(new(Guid.NewGuid(), ForeignBankTradeKind.Deposit, today.AddDays(-1), 6010, 3000, 100, 2000, 0, "Opening")); await fx.SetRate(new(Guid.NewGuid(), "USD", today, 22, "Closing rate"));
        var request = new ForeignRevaluationRequest(today, ["USD"]); var preview = await fx.PreviewForeignRevaluation(request); Assert.Equal(200, preview.NetGain);
        var command = new ForeignRevaluationCommand(Guid.NewGuid(), request, preview.CalculationFingerprint, "Month closing"); var id = await fx.PostForeignRevaluation(command); Assert.Equal(id, await fx.PostForeignRevaluation(command));
        Assert.Equal(2200, (await fx.ForeignBanks(today)).Single(b => b.AccountCode == 6010).LocalCarrying); Assert.Equal(2000, (await fx.ForeignBanks(today.AddDays(1))).Single(b => b.AccountCode == 6010).LocalCarrying);
        await Assert.ThrowsAsync<BusinessException>(() => Trade(new(Guid.NewGuid(), ForeignBankTradeKind.Deposit, today.AddDays(-1), 6010, 3000, 1, 20, 0, "Backdated")));
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), id, "Single-side reversal")));
        await fx.CancelForeignRevaluation(new(Guid.NewGuid(), id, "Closing inputs need revision"));
        Assert.True(Assert.Single(await fx.ForeignRevaluations()).Cancelled); Assert.Equal(2000, (await fx.ForeignBanks(today)).Single(b => b.AccountCode == 6010).LocalCarrying); Assert.Equal(2000, (await fx.ForeignBanks(today.AddDays(1))).Single(b => b.AccountCode == 6010).LocalCarrying);
        await Trade(new(Guid.NewGuid(), ForeignBankTradeKind.Deposit, today, 6010, 3000, 1, 22, 0, "After cancellation"));
        await using var db = factory.CreateDbContext(); Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task ForeignReturnsReduceSourceBillsAndPricesAreCheckedInLocalCurrency()
    {
        var invoice = await Invoice();
        var returns = new ReturnService(factory, session, Options.Create(new TradingOptions()));
        await returns.Post(new(Guid.NewGuid(), invoice, "One item returned", [new(product, null, null, null, 1)]));
        var balance = Assert.Single(await fx.ForeignReceivables()); Assert.Equal(10, balance.SourceOutstanding); Assert.Equal(200, balance.LocalCarrying);
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "School", today, [new(product, 1, 1)], null, Currency: "USD")));
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "School", today, [new(product, 1, 10)], PaymentMethod.Cash, Currency: "USD")));
    }
    [Fact] public async Task ForeignUninvoicedReceiptsAreValuedWithoutChangingInventory()
    {
        var receipt = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today.AddDays(-1), [new(product, 2, 50)], null, Currency: "USD", InvoiceLater: true));
        await fx.SetRate(new(Guid.NewGuid(), "USD", today, 22, "Closing reference"));
        var preview = await fx.PreviewForeignRevaluation(new(today, ["USD"])); var row = Assert.Single(preview.Rows); Assert.Equal("Goods received not invoiced", row.Kind); Assert.Equal(-200, row.Gain);
        await fx.PostForeignRevaluation(new(Guid.NewGuid(), preview.Request, preview.CalculationFingerprint, "Unbilled closing"));
        var state = await business.Read(session.Actor); Assert.Equal(2000, Assert.Single(state.Stock).Value); Assert.Equal(2200, -(await finance.Read(today, today)).Balances.Single(b => b.Code == 2050).Closing);
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), receipt, "Cannot alter valuation history")));
    }
    [Fact] public async Task CurrencyBanksRejectExistingLocalHistoryAndCurrentPermissionRevocation()
    {
        await finance.CreateLedger(new(Guid.NewGuid(), 6030, "Used local bank", LedgerGroup.Asset, true)); await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Journal, today, "Opening local value", [new(6030, 100, 0), new(3000, 0, 100)], []));
        await Assert.ThrowsAsync<BusinessException>(() => fx.ConfigureForeignBank(new(Guid.NewGuid(), 6030, "USD")));
        session.Actor = new("reader", "FREETOWN", new HashSet<string> { "business.read", "accounting.read" }); Assert.Equal(2, (await fx.ForeignBanks(today)).Count);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fx.ConfigureForeignBank(new(Guid.NewGuid(), 6030, "USD")));
        session.Actor = new("owner", "OTHER", BusinessPermissions.All.ToHashSet()); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fx.ForeignReceivables());
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
