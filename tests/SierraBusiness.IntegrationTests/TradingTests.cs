using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class TradingTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-{Guid.NewGuid():N}.db");
    private TestFactory factory = default!;
    private BusinessService service = default!;
    private readonly Actor owner = new("test-owner", "FREETOWN", new HashSet<string> { "business.read", "catalogue.write", "pricing.manage", "sales.create", "inventory.receive", "documents.reverse", "accounting.read", "audit.read" });
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();
        service = new(factory, Options.Create(new TradingOptions()));
    }
    public Task DisposeAsync() { File.Delete(path); return Task.CompletedTask; }
    private Task<Guid> Product(TrackingMode mode = TrackingMode.Quantity) => service.AddProduct(owner, new(Guid.NewGuid(), $"SKU-{Guid.NewGuid():N}", "Printer paper", "Stationery", "ream", 100, mode, MinimumPrice: .0001m));
    private TradeCommand Receipt(Guid product, decimal qty = 10, string? serial = null, string? batch = null, DateOnly? expiry = null) => new(Guid.NewGuid(), DocumentKind.Receipt, "Local supplier", today, [new(product, qty, 60, 1, serial, batch, expiry)], null);
    private TradeCommand Sale(Guid product, decimal qty = 2, PaymentMethod? payment = PaymentMethod.Cash, string? serial = null, string? batch = null, DateOnly? expiry = null) => new(Guid.NewGuid(), DocumentKind.Sale, "ABC School", today, [new(product, qty, 100, 1, serial, batch, expiry)], payment);
    [Fact] public async Task OfflineSalePersistsBalancedJournalAndOutboxAfterRestart()
    {
        var product = await Product(); await service.Post(owner, Receipt(product));
        var command = Sale(product); var id = await service.Post(owner, command);
        var restarted = new BusinessService(factory, Options.Create(new TradingOptions()));
        Assert.Equal(id, await restarted.Post(owner, command));
        var snapshot = await restarted.Read(owner);
        Assert.Equal(8, Assert.Single(snapshot.Stock).Quantity);
        Assert.Equal(480, Assert.Single(snapshot.Stock).Value);
        Assert.Equal(2, snapshot.Documents.Count);
        Assert.Equal(3, snapshot.PendingSync);
        Assert.Equal(snapshot.Ledger.Sum(x => x.Debit), snapshot.Ledger.Sum(x => x.Credit));
        Assert.Equal(200, snapshot.Ledger.Single(x => x.Account == Account.Cash).Debit);
        Assert.Equal(120, snapshot.Ledger.Single(x => x.Account == Account.CostOfSales).Debit);
    }
    [Fact] public async Task InsufficientStockRollsBackAllLinesAndJournal()
    {
        var product = await Product(); await service.Post(owner, Receipt(product));
        var command = Sale(product) with { Lines = [new(product, 2, 100), new(product, 20, 100)] };
        await Assert.ThrowsAsync<BusinessException>(() => service.Post(owner, command));
        var snapshot = await service.Read(owner);
        Assert.Equal(10, Assert.Single(snapshot.Stock).Quantity); Assert.Single(snapshot.Documents); Assert.Equal(2, snapshot.PendingSync);
    }
    [Fact] public async Task DifferentPayloadCannotReuseOperationId()
    {
        var product = await Product(); var command = Receipt(product); await service.Post(owner, command);
        await Assert.ThrowsAsync<BusinessException>(() => service.Post(owner, command with { Party = "Different supplier" }));
    }
    [Fact] public async Task PermissionsAndBranchScopeAreEnforced()
    {
        var product = await Product();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.Post(owner with { Permissions = new HashSet<string>() }, Receipt(product)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.Read(owner with { Branch = "BO" }));
    }
    [Fact] public async Task SerialCannotBeReceivedOrSoldTwice()
    {
        var p = await Product(TrackingMode.Serial); await service.Post(owner, Receipt(p, 1, "SN-001"));
        await Assert.ThrowsAsync<BusinessException>(() => service.Post(owner, Receipt(p, 1, "SN-001")));
        await service.Post(owner, Sale(p, 1, serial: "SN-001"));
        await Assert.ThrowsAsync<BusinessException>(() => service.Post(owner, Sale(p, 1, serial: "SN-001")));
    }
    [Fact] public async Task ExpiredBatchCannotBeSold()
    {
        var p = await Product(TrackingMode.BatchExpiry); await service.Post(owner, Receipt(p, batch: "B1", expiry: today.AddDays(-1)));
        await Assert.ThrowsAsync<BusinessException>(() => service.Post(owner, Sale(p, batch: "B1", expiry: today.AddDays(-1))));
    }
    [Fact] public async Task SaleReversalRestoresStockAndFinancialEffectWithoutDeletingHistory()
    {
        var p = await Product(); await service.Post(owner, Receipt(p)); var id = await service.Post(owner, Sale(p));
        var reverse = new ReverseCommand(Guid.NewGuid(), id, "Incorrect sale, goods returned"); await service.Reverse(owner, reverse);
        Assert.Equal(await service.Reverse(owner, reverse), await service.Reverse(owner, reverse));
        var snapshot = await service.Read(owner); Assert.Equal(10, Assert.Single(snapshot.Stock).Quantity); Assert.Equal(3, snapshot.Documents.Count);
        var cash = snapshot.Ledger.Single(x => x.Account == Account.Cash); Assert.Equal(cash.Debit, cash.Credit);
        await Assert.ThrowsAsync<BusinessException>(() => service.Reverse(owner, reverse with { OperationId = Guid.NewGuid() }));
    }
    [Fact] public async Task ReceiptReversalIsBlockedAfterStockConsumption()
    {
        var p = await Product(); var receipt = await service.Post(owner, Receipt(p)); await service.Post(owner, Sale(p));
        await Assert.ThrowsAsync<BusinessException>(() => service.Reverse(owner, new(Guid.NewGuid(), receipt, "Supplier return")));
    }
    [Fact] public async Task CreditPostsReceivableAndChequeNeedsBankDetails()
    {
        var p = await Product(); await service.Post(owner, Receipt(p)); await service.Post(owner, Sale(p, payment: null));
        await Assert.ThrowsAsync<BusinessException>(() => service.Post(owner, Sale(p, payment: PaymentMethod.Cheque)));
        Assert.Equal(200, (await service.Read(owner)).Ledger.Single(x => x.Account == Account.Receivables).Debit);
    }
    [Fact] public async Task UnitConversionUsesBaseQuantities()
    {
        var p = await Product(); await service.Post(owner, Receipt(p) with { Lines = [new(p, 2, 600, 10)] });
        await service.Post(owner, Sale(p, 3)); var stock = Assert.Single((await service.Read(owner)).Stock);
        Assert.Equal(17, stock.Quantity); Assert.Equal(1020, stock.Value);
    }
    [Fact] public async Task ClosedPeriodRejectsTransaction()
    {
        var p = await Product(); var locked = new BusinessService(factory, Options.Create(new TradingOptions { ClosedThrough = today }));
        await Assert.ThrowsAsync<BusinessException>(() => locked.Post(owner, Receipt(p)));
    }
    [Fact] public async Task MigrationIsRepeatableAndPreservesUnsyncedWork()
    {
        var p = await Product(); await service.Post(owner, Receipt(p));
        await using var db = factory.CreateDbContext(); await db.Database.MigrateAsync();
        Assert.Equal(2, await db.Operations.CountAsync()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }
    [Fact] public async Task NativeSqliteMeetsSecurityBaseline()
    {
        await using var db = factory.CreateDbContext();
        await db.Database.OpenConnectionAsync();
        var version = Version.Parse(db.Database.GetDbConnection().ServerVersion);
        Assert.True(version >= new Version(3, 50, 2), $"SQLite {version} is below the CVE-2025-6965 fixed baseline.");
    }
    [Fact] public async Task CompetingSalesCannotOversellLocalStock()
    {
        var p = await Product(); await service.Post(owner, Receipt(p));
        async Task<bool> TrySale() { try { await service.Post(owner, Sale(p, 6)); return true; } catch (BusinessException) { return false; } }
        var results = await Task.WhenAll(Task.Run(TrySale), Task.Run(TrySale));
        Assert.Single(results, x => x);
        Assert.Equal(4, Assert.Single((await service.Read(owner)).Stock).Quantity);
    }
    [Fact] public async Task PostedDocumentCannotBeEditedThroughPersistence()
    {
        var p = await Product(); var id = await service.Post(owner, Receipt(p));
        await using var db = factory.CreateDbContext();
        var document = await db.Documents.SingleAsync(x => x.Id == id); document.Total = 1;
        await Assert.ThrowsAsync<BusinessException>(() => db.SaveChangesAsync());
    }
    [Theory]
    [InlineData(PaymentMethod.Cash, Account.Cash)]
    [InlineData(PaymentMethod.Cheque, Account.ChequesInHand)]
    [InlineData(PaymentMethod.BankTransfer, Account.Bank)]
    public async Task AuditPaymentPostsToCorrectAccountAndReverses(PaymentMethod method, Account account)
    {
        var p = await Product(); await service.Post(owner, Receipt(p));
        var sale = Sale(p, payment: method) with { PaymentReference = "AUDIT-REF", Bank = "Test bank", PaymentDate = today };
        var id = await service.Post(owner, sale);
        Assert.Equal(id, await service.Post(owner, sale));
        var snapshot = await service.Read(owner);
        Assert.Equal(200, snapshot.Ledger.Single(x => x.Account == account).Debit);
        Assert.Equal(8, Assert.Single(snapshot.Stock).Quantity);
        await service.Reverse(owner, new(Guid.NewGuid(), id, "Audit reversal"));
        snapshot = await service.Read(owner);
        var settlement = snapshot.Ledger.Single(x => x.Account == account);
        Assert.Equal(settlement.Debit, settlement.Credit);
        Assert.Equal(10, Assert.Single(snapshot.Stock).Quantity);
        Assert.Equal(snapshot.Ledger.Sum(x => x.Debit), snapshot.Ledger.Sum(x => x.Credit));
    }
    [Fact] public async Task AuditTaxRateChangeDoesNotRewritePostedInvoice()
    {
        var p = await Product(); await service.Post(owner, Receipt(p));
        // Synthetic rates only, not a statement of statutory tax requirements.
        var first = new BusinessService(factory, Options.Create(new TradingOptions { TaxRate = .1m }));
        var id = await first.Post(owner, Sale(p));
        var second = new BusinessService(factory, Options.Create(new TradingOptions { TaxRate = .2m }));
        var other = await second.Post(owner, Sale(p));
        var snapshot = await second.Read(owner);
        Assert.Equal(220, snapshot.Documents.Single(x => x.Id == id).Total);
        Assert.Equal(240, snapshot.Documents.Single(x => x.Id == other).Total);
        using var details = System.Text.Json.JsonDocument.Parse(snapshot.Documents.Single(x => x.Id == id).Detail);
        Assert.Equal(.1m, details.RootElement.GetProperty("TaxRate").GetDecimal());
    }
    [Fact] public async Task AuditReadPermissionsHideLedgerAndAudit()
    {
        var p = await Product(); await service.Post(owner, Receipt(p));
        var reader = owner with { Permissions = new HashSet<string> { "business.read" } };
        var snapshot = await service.Read(reader);
        Assert.Empty(snapshot.Ledger); Assert.Empty(snapshot.Audit);
        Assert.Single(snapshot.Stock);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AuditHistoryCannotBeChangedThroughAnySaveOverload(bool asynchronous, bool delete)
    {
        await Product();
        await using var db = factory.CreateDbContext();
        var operation = await db.Operations.SingleAsync();
        if (delete) db.Operations.Remove(operation); else operation.Payload = "tampered";
        if (asynchronous) await Assert.ThrowsAsync<BusinessException>(() => db.SaveChangesAsync(true));
        else Assert.Throws<BusinessException>(() => db.SaveChanges(true));
        await using var verify = factory.CreateDbContext();
        Assert.NotEqual("tampered", (await verify.Operations.SingleAsync()).Payload);
    }
    [Theory]
    [InlineData(TrackingMode.Variant)]
    [InlineData(TrackingMode.Compatibility)]
    public async Task UnimplementedTrackingModesAreRejected(TrackingMode mode)
    {
        await Assert.ThrowsAsync<BusinessException>(() => Product(mode));
        Assert.Empty((await service.Read(owner)).Products);
    }
    [Fact] public async Task NetSalesExcludeTaxAndIncludeReversals()
    {
        var p = await Product(); await service.Post(owner, Receipt(p));
        var taxable = new BusinessService(factory, Options.Create(new TradingOptions { TaxRate = .1m }));
        var id = await taxable.Post(owner, Sale(p));
        Assert.Equal(200, (await taxable.Read(owner)).NetSales);
        await taxable.Reverse(owner, new(Guid.NewGuid(), id, "Correction"));
        Assert.Equal(0, (await taxable.Read(owner)).NetSales);
    }
    private sealed class TestFactory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb>
    {
        public BusinessDb CreateDbContext() => new(options);
    }
}

