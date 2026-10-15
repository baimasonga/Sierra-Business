using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class InventoryTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-inventory-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private InventoryService inventory = null!; private BusinessService business = null!;
    private readonly Session session = new(); private Guid product; private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using var db = factory.CreateDbContext(); await TestDatabase.MigrateAsync(db, guarded: false);
        inventory = new(factory, session, Options.Create(new TradingOptions())); business = new(factory, Options.Create(new TradingOptions()));
        product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: .0001m));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(product, 10, 60)], null));
        await inventory.CreateLocation(new(Guid.NewGuid(), "SECOND", "Second warehouse"));
    }
    public Task DisposeAsync() { File.Delete(path); return Task.CompletedTask; }
    [Fact] public async Task TransferRetainsValueAndSaleUsesOnlyItsWarehouse()
    {
        var transfer = new TransferCommand(Guid.NewGuid(), product, "MAIN", "SECOND", 4, null, null, null, "Replenish second warehouse");
        var id = await inventory.Transfer(transfer); Assert.Equal(id, await inventory.Transfer(transfer));
        var snapshot = await business.Read(session.Actor); Assert.Equal(600, snapshot.Stock.Sum(x => x.Value));
        Assert.Equal(4, snapshot.Stock.Single(x => x.Location == "SECOND").Quantity);
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Buyer", today, [new(product, 5, 100)], PaymentMethod.Cash, Location: "SECOND")));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Buyer", today, [new(product, 2, 100)], PaymentMethod.Cash, Location: "SECOND"));
        snapshot = await business.Read(session.Actor); Assert.Equal(6, snapshot.Stock.Single(x => x.Location == "MAIN").Quantity); Assert.Equal(2, snapshot.Stock.Single(x => x.Location == "SECOND").Quantity);
        Assert.Equal(snapshot.Stock.Sum(x => x.Value), snapshot.Ledger.Single(x => x.Account == Account.Inventory).Debit - snapshot.Ledger.Single(x => x.Account == Account.Inventory).Credit);
    }
    [Fact] public async Task CountPostsBalancedAdjustmentAndRejectsStaleCounts()
    {
        await inventory.Count(new(Guid.NewGuid(), product, "MAIN", 10, 8, 0, "Damaged stock"));
        var snapshot = await business.Read(session.Actor); Assert.Equal(480, snapshot.Stock.Single().Value);
        Assert.Equal(120, snapshot.Ledger.Single(x => (int)x.Account == 5100).Debit);
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(new(Guid.NewGuid(), product, "MAIN", 10, 9, 50, "Stale count")));
        await inventory.Count(new(Guid.NewGuid(), product, "MAIN", 8, 9, 70, "Unrecorded unit found"));
        snapshot = await business.Read(session.Actor); Assert.Equal(550, snapshot.Stock.Single().Value); Assert.Equal(snapshot.Ledger.Sum(x => x.Debit), snapshot.Ledger.Sum(x => x.Credit));
    }
    [Fact] public async Task ReversalOfTransferCannotRemoveStockAlreadyConsumed()
    {
        var id = await inventory.Transfer(new(Guid.NewGuid(), product, "MAIN", "SECOND", 4, null, null, null, "Transfer"));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Buyer", today, [new(product, 3, 100)], PaymentMethod.Cash, Location: "SECOND"));
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), id, "Undo transfer")));
        Assert.Equal(1, (await business.Read(session.Actor)).Stock.Single(x => x.Location == "SECOND").Quantity);
    }
    private async Task<Guid> BatchProduct(TrackingMode mode = TrackingMode.BatchExpiry)
    {
        var id = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "BATCH", "Batch goods", "Stock", "unit", 100, mode, MinimumPrice: .0001m));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today,
            [new(id, 10, 60, Batch: "LOT", Expiry: today.AddDays(10)), new(id, 4, 80, Batch: "LOT", Expiry: today.AddDays(20))], null));
        return id;
    }
    [Fact] public async Task BatchCountIsolatesExpiryAndWarehouseAndReplaysAndReverses()
    {
        var id = await BatchProduct();
        await inventory.Transfer(new(Guid.NewGuid(), id, "MAIN", "SECOND", 2, null, "LOT", today.AddDays(10), "Move batch"));
        var command = new StockCountCommand(Guid.NewGuid(), id, "MAIN", 8, 6, 0, "Count shortage", " lot ", today.AddDays(10));
        var doc = await inventory.Count(command); Assert.Equal(doc, await inventory.Count(command));
        var stock = (await business.Read(session.Actor)).Stock.Where(x => x.ProductId == id).ToList();
        Assert.Equal(6, stock.Single(x => x.Location == "MAIN" && x.Expiry == today.AddDays(10)).Quantity);
        Assert.Equal(360, stock.Single(x => x.Location == "MAIN" && x.Expiry == today.AddDays(10)).Value);
        Assert.Equal(4, stock.Single(x => x.Expiry == today.AddDays(20)).Quantity);
        Assert.Equal(2, stock.Single(x => x.Location == "SECOND").Quantity);
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(command with { OperationId = Guid.NewGuid() }));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), doc, "Recount correction"));
        Assert.Equal(8, (await business.Read(session.Actor)).Stock.Single(x => x.ProductId == id && x.Location == "MAIN" && x.Expiry == today.AddDays(10)).Quantity);
    }
    [Fact] public async Task UnrecordedExpiredBatchCanBeCountedButNotSoldAndCanBeWrittenOff()
    {
        var id = await BatchProduct(); var expiry = today.AddDays(-1);
        await inventory.Count(new(Guid.NewGuid(), id, "MAIN", 0, 3, 25, "Found expired lot", "OLD", expiry));
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Buyer", today, [new(id, 1, 100, Batch: "OLD", Expiry: expiry)], PaymentMethod.Cash)));
        await inventory.Count(new(Guid.NewGuid(), id, "MAIN", 3, 0, 0, "Disposed expired stock", "OLD", expiry));
        var data = await business.Read(session.Actor);
        Assert.DoesNotContain(data.Stock, x => x.ProductId == id && x.Batch == "OLD" && (x.Quantity != 0 || x.Value != 0));
        Assert.Equal(data.Ledger.Sum(x => x.Debit), data.Ledger.Sum(x => x.Credit));
    }
    [Fact] public async Task BatchCountValidatesIdentityAndProtectsReservations()
    {
        var id = await BatchProduct();
        var command = new StockCountCommand(Guid.NewGuid(), id, "MAIN", 10, 1, 0, "Physical shortage", "LOT", today.AddDays(10));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(command with { Batch = null }));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(command with { Expiry = null }));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(command with { ProductId = product }));
        var options = Options.Create(new TradingOptions());
        var orders = new OrderService(factory, session, options); var commerce = new CommerceService(factory, session, options);
        var order = await orders.Create(new(Guid.NewGuid(), OrderType.SalesOrder, "Buyer", today, today.AddDays(1), "", [new(id, 3, 100)]));
        var reservation = await commerce.Reserve(new(Guid.NewGuid(), order, id, "MAIN", 3, Batch: "LOT", Expiry: today.AddDays(10)));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(command));
        await commerce.Release(new(Guid.NewGuid(), reservation, "Release for recount"));
        await inventory.Count(command);
        Assert.Equal(1, (await business.Read(session.Actor)).Stock.Single(x => x.ProductId == id && x.Expiry == today.AddDays(10)).Quantity);
    }
    [Fact] public void QuantityCountPayloadPreservesLegacyOperationFingerprint()
    {
        var command = new StockCountCommand(Guid.NewGuid(), product, "MAIN", 10, 8, 0, "Count");
        var legacy = new { command.OperationId, command.ProductId, command.Location, command.ExpectedQuantity, command.CountedQuantity, command.SurplusUnitCost, command.Reason };
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(legacy), System.Text.Json.JsonSerializer.Serialize(command));
    }
    [Fact] public async Task SuggestionsSkipExpiredAndReservedAndDraftStockWithoutPostingAnything()
    {
        var id = await BatchProduct();
        await inventory.Count(new(Guid.NewGuid(), id, "MAIN", 0, 20, 60, "Expired lot", "EXPIRED", today.AddDays(-1)));
        var options = Options.Create(new TradingOptions()); var orders = new OrderService(factory, session, options); var commerce = new CommerceService(factory, session, options);
        var order = await orders.Create(new(Guid.NewGuid(), OrderType.SalesOrder, "Buyer", today, today.AddDays(1), "", [new(id, 3, 100)]));
        await commerce.Reserve(new(Guid.NewGuid(), order, id, "MAIN", 3, Batch: "LOT", Expiry: today.AddDays(10)));
        await using var db = factory.CreateDbContext(); var count = await db.Operations.CountAsync();
        var picks = await inventory.SuggestBatches(new(id, "MAIN", today, 6, [new(" lot ", today.AddDays(10), 2), new("LOT", today.AddDays(10), 1)]));
        Assert.Equal(new BatchPick("LOT", today.AddDays(10), 4), picks[0]); Assert.Equal(new BatchPick("LOT", today.AddDays(20), 2), picks[1]);
        Assert.Equal(count, await db.Operations.CountAsync());
        Assert.Equal(10, (await business.Read(session.Actor)).Stock.Single(x => x.ProductId == id && x.Expiry == today.AddDays(10)).Quantity);
        await Assert.ThrowsAsync<BusinessException>(() => inventory.SuggestBatches(new(id, "MAIN", today, 12)));
    }
    [Fact] public async Task SuggestionsRespectWarehouseFutureDateAndFinalPostingRechecksStock()
    {
        var id = await BatchProduct();
        await inventory.Transfer(new(Guid.NewGuid(), id, "MAIN", "SECOND", 2, null, "LOT", today.AddDays(10), "Transfer"));
        var picks = await inventory.SuggestBatches(new(id, "SECOND", today, 2)); Assert.Single(picks);
        await Assert.ThrowsAsync<BusinessException>(() => inventory.SuggestBatches(new(id, "SECOND", today, 3)));
        var future = await inventory.SuggestBatches(new(id, "MAIN", today.AddDays(11), 4)); Assert.Equal(today.AddDays(20), future.Single().Expiry);
        var sale = new TradeCommand(Guid.NewGuid(), DocumentKind.Sale, "Buyer", today, picks.Select(x => new TradeLine(id, x.Quantity, 100, Batch: x.Batch, Expiry: x.Expiry)).ToList(), PaymentMethod.Cash, Location: "SECOND");
        await business.Post(session.Actor, sale);
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, sale with { OperationId = Guid.NewGuid() }));
    }
    [Fact] public async Task SuggestionsUseStableBatchOrderWithUndatedLotsLastAndAllowExpiryToday()
    {
        var id = await BatchProduct(TrackingMode.Batch);
        await inventory.Count(new(Guid.NewGuid(), id, "MAIN", 0, 2, 60, "Undated batch", "UNDATED"));
        await inventory.Count(new(Guid.NewGuid(), id, "MAIN", 0, 1.5m, 60, "Today batch", "Z", today));
        await inventory.Count(new(Guid.NewGuid(), id, "MAIN", 0, 1.5m, 60, "Today batch", "A", today));
        var picks = await inventory.SuggestBatches(new(id, "MAIN", today, 18));
        Assert.Equal("A", picks[0].Batch); Assert.Equal("Z", picks[1].Batch); Assert.Equal("UNDATED", picks[^1].Batch); Assert.Equal(1, picks[^1].Quantity);
        var backdated = await inventory.SuggestBatches(new(id, "MAIN", today.AddDays(-5), 1)); Assert.Equal(today, backdated.Single().Expiry);
    }
    [Fact] public async Task SuggestionsRejectInvalidInputAndUnauthorizedAccess()
    {
        await Assert.ThrowsAsync<BusinessException>(() => inventory.SuggestBatches(new(product, "MAIN", today, 1)));
        var id = await BatchProduct();
        await Assert.ThrowsAsync<BusinessException>(() => inventory.SuggestBatches(new(id, "MISSING", today, 1)));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.SuggestBatches(new(id, "MAIN", today, 0)));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.SuggestBatches(new(id, "MAIN", today, 1, [new("LOT", today, -1)])));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.SuggestBatches(new(id, "MAIN", today, 1, [null!])));
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inventory.SuggestBatches(new(id, "MAIN", today, 1)));
        session.Actor = session.Actor with { Branch = "OTHER", Permissions = new HashSet<string> { "business.read", "sales.create" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inventory.SuggestBatches(new(id, "MAIN", today, 1)));
    }
    private Task<Guid> SerialProduct() => business.AddProduct(session.Actor, new(Guid.NewGuid(), "SERIAL", "Serialized unit", "Goods", "unit", 100, TrackingMode.Serial, MinimumPrice: 1));
    [Fact] public async Task SerialCountPostsReplaysWritesOffAndReversesWithValue()
    {
        var id = await SerialProduct(); var command = new StockCountCommand(Guid.NewGuid(), id, "MAIN", 0, 1, 60, "Discovered unit", Serial: " sn-01 ");
        var found = await inventory.Count(command); Assert.Equal(found, await inventory.Count(command));
        var missing = await inventory.Count(new(Guid.NewGuid(), id, "MAIN", 1, 0, 0, "Missing unit", Serial: "SN-01"));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(command with { OperationId = Guid.NewGuid() }));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), missing, "Unit located"));
        var data = await business.Read(session.Actor); var stock = data.Stock.Single(x => x.ProductId == id);
        Assert.Equal("SN-01", stock.Serial); Assert.Equal(1, stock.Quantity); Assert.Equal(60, stock.Value);
        Assert.Equal(data.Ledger.Sum(x => x.Debit), data.Ledger.Sum(x => x.Credit));
    }
    [Fact] public async Task SerialCountRejectsDuplicateOtherWarehouseAndSoldSerial()
    {
        var id = await SerialProduct(); var command = new StockCountCommand(Guid.NewGuid(), id, "MAIN", 0, 1, 60, "Found", Serial: "SN-02");
        await inventory.Count(command);
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(command with { OperationId = Guid.NewGuid(), Location = "SECOND" }));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Buyer", today, [new(id, 1, 100, Serial: "SN-02")], PaymentMethod.Cash));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(command with { OperationId = Guid.NewGuid() }));
    }
    [Fact] public async Task SerialCountRejectsInvalidIdentityFractionAndStaleQuantity()
    {
        var id = await SerialProduct(); var command = new StockCountCommand(Guid.NewGuid(), id, "MAIN", 0, 1, 60, "Found", Serial: "SN-03");
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(command with { Serial = null }));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(command with { CountedQuantity = .5m }));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(command with { Batch = "B" }));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(command with { ProductId = product }));
        await inventory.Count(command);
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(command with { OperationId = Guid.NewGuid(), CountedQuantity = 0 }));
    }
    [Fact] public async Task SerialCountCannotWriteOffReservedUnit()
    {
        var id = await SerialProduct(); await inventory.Count(new(Guid.NewGuid(), id, "MAIN", 0, 1, 60, "Found", Serial: "SN-04"));
        var options = Options.Create(new TradingOptions()); var orders = new OrderService(factory, session, options); var commerce = new CommerceService(factory, session, options);
        var order = await orders.Create(new(Guid.NewGuid(), OrderType.SalesOrder, "Buyer", today, today.AddDays(1), "", [new(id, 1, 100)]));
        await commerce.Reserve(new(Guid.NewGuid(), order, id, "MAIN", 1, Serial: "SN-04"));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(new(Guid.NewGuid(), id, "MAIN", 1, 0, 0, "Missing", Serial: "SN-04")));
    }
    [Fact] public async Task SerialTransferReversalRetainsOneUnitAndSurplusReversalRejectsConsumedStock()
    {
        var id = await SerialProduct(); var found = await inventory.Count(new(Guid.NewGuid(), id, "MAIN", 0, 1, 60, "Found", Serial: "SN-TRANSFER"));
        var transfer = await inventory.Transfer(new(Guid.NewGuid(), id, "MAIN", "SECOND", 1, "SN-TRANSFER", null, null, "Move"));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), transfer, "Undo move"));
        Assert.Equal(1, (await business.Read(session.Actor)).Stock.Where(x => x.ProductId == id).Sum(x => x.Quantity));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Buyer", today, [new(id, 1, 100, Serial: "SN-TRANSFER")], PaymentMethod.Cash));
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), found, "Undo found")));
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", new HashSet<string> { "business.read", "catalogue.write", "pricing.manage", "inventory.receive", "sales.create", "documents.reverse", "accounting.read" }); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}

