using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Covers the append-only inventory management workflows and their enforcement inside trading.</summary>
public sealed class InventoryManagementTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-management-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private InventoryManagement management = null!; private BusinessService business = null!; private PosService pos = null!;
    private readonly Session session = new(); private Guid paper; private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using var db = factory.CreateDbContext(); await db.Database.MigrateAsync();
        var options = Options.Create(new TradingOptions());
        management = new(factory, session, options); business = new(factory, options); pos = new(factory, session, options);
        paper = await AddProduct("PAPER", TrackingMode.Quantity);
        await Receive(paper, 10);
    }
    public Task DisposeAsync() { File.Delete(path); return Task.CompletedTask; }

    private Task<Guid> AddProduct(string sku, TrackingMode mode) => business.AddProduct(session.Actor, new(Guid.NewGuid(), sku, sku, "Goods", "unit", 100, mode, MinimumPrice: .0001m));
    private Task<Guid> Receive(Guid product, decimal quantity, string? serial = null, string? batch = null, DateOnly? expiry = null) =>
        business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(product, quantity, 60, 1, serial, batch, expiry)], null));
    private Task<Guid> Sell(Guid product, decimal quantity = 1, string? serial = null) =>
        business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(product, quantity, 100, 1, serial)], PaymentMethod.Cash));
    private async Task<decimal> OnHand(Guid product) => (await business.Read(session.Actor)).Stock.Where(x => x.ProductId == product).Sum(x => x.Quantity);

    [Fact] public async Task InactiveProductCannotBeTradedUntilReactivated()
    {
        var save = new SaveProductSettings(Guid.NewGuid(), new(paper, Version: 0, Active: false, Brand: "Acme"));
        await management.SaveProduct(save);
        Assert.Equal(paper, await management.SaveProduct(save)); // replay is idempotent and does not add a revision
        Assert.False((await management.Products()).Single(x => x.ProductId == paper).Active);

        await Assert.ThrowsAsync<BusinessException>(() => Sell(paper));
        await Assert.ThrowsAsync<BusinessException>(() => Receive(paper, 1));
        await Assert.ThrowsAsync<BusinessException>(() => management.SaveProduct(new(Guid.NewGuid(), new(paper, Version: 0, Active: true)))); // stale version

        await management.SaveProduct(new(Guid.NewGuid(), new(paper, Version: 1, Active: true)));
        await Sell(paper);
        Assert.Equal(9, await OnHand(paper));
    }

    [Fact] public async Task ProductSettingsValidateUnitsAndPermissions()
    {
        var machine = await AddProduct("MACHINE", TrackingMode.Serial);
        await Assert.ThrowsAsync<BusinessException>(() => management.SaveProduct(new(Guid.NewGuid(), new(machine, SalesUnit: "box", SalesConversion: 2))));
        await Assert.ThrowsAsync<BusinessException>(() => management.SaveProduct(new(Guid.NewGuid(), new(paper, SalesConversion: 5))));
        await management.SaveProduct(new(Guid.NewGuid(), new(paper, SalesUnit: "box", SalesConversion: 5)));
        Assert.Equal(5, (await management.Products()).Single(x => x.ProductId == paper).SalesConversion);

        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read", "sales.create" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => management.SaveProduct(new(Guid.NewGuid(), new(paper, Version: 1, Active: false))));
    }

    [Fact] public async Task ReorderSuggestsTopUpExcludingExpiredStockAndInactiveProducts()
    {
        await Assert.ThrowsAsync<BusinessException>(() => management.SaveReorder(new(Guid.NewGuid(), new(paper, "MAIN", Minimum: 5, Target: 4))));
        await management.SaveReorder(new(Guid.NewGuid(), new(paper, "MAIN", Minimum: 5, Target: 20)));
        Assert.Equal(0, (await management.Reorder()).Single().SuggestedQuantity); // 10 available > minimum 5

        for (var i = 0; i < 6; i++) await Sell(paper);
        var view = (await management.Reorder()).Single();
        Assert.Equal(4, view.OnHand); Assert.Equal(16, view.SuggestedQuantity);

        var medicine = await AddProduct("MEDICINE", TrackingMode.BatchExpiry);
        await Receive(medicine, 8, batch: "OLD", expiry: today.AddDays(-1));
        await Receive(medicine, 2, batch: "NEW", expiry: today.AddDays(90));
        await management.SaveReorder(new(Guid.NewGuid(), new(medicine, "MAIN", Minimum: 3, Target: 10)));
        var lot = (await management.Reorder()).Single(x => x.Rule.ProductId == medicine);
        Assert.Equal(10, lot.OnHand); Assert.Equal(8, lot.Expired); Assert.Equal(8, lot.SuggestedQuantity);

        await management.SaveProduct(new(Guid.NewGuid(), new(paper, Active: false)));
        Assert.Equal(0, (await management.Reorder()).Single(x => x.Rule.ProductId == paper).SuggestedQuantity);
    }

    [Fact] public async Task PreferredSupplierMustBeASupplierInTheBranch()
    {
        var parties = new PartyService(factory, session, Options.Create(new TradingOptions()));
        var customer = await parties.Save(new(Guid.NewGuid(), null, 0, "C1", "Customer One", PartyType.Customer, "", "", "", "", 0, 0, false));
        var supplier = await parties.Save(new(Guid.NewGuid(), null, 0, "S1", "Supplier One", PartyType.Supplier, "", "", "", "", 0, 0, false));
        await Assert.ThrowsAsync<BusinessException>(() => management.SaveReorder(new(Guid.NewGuid(), new(paper, "MAIN", Minimum: 1, Target: 5, SupplierId: customer))));
        await management.SaveReorder(new(Guid.NewGuid(), new(paper, "MAIN", Minimum: 1, Target: 5, SupplierId: supplier)));
        Assert.Equal(supplier, (await management.Reorder()).Single().Rule.SupplierId);
    }

    [Fact] public async Task CountSessionPostsOnlyAfterApprovalAndOnlyOnce()
    {
        var session1 = await management.SubmitCount(new(Guid.NewGuid(), "Month-end", [new(Guid.NewGuid(), paper, "MAIN", 10, 7, 0, "Damaged")]));
        Assert.Equal("Pending approval", (await management.Counts()).Single().Status);
        Assert.Equal(10, await OnHand(paper)); // submission alone never moves stock

        await management.DecideCount(new(Guid.NewGuid(), session1, true, "Checked by manager"));
        var decided = (await management.Counts()).Single();
        Assert.Equal("Approved", decided.Status); Assert.Single(decided.Documents);
        Assert.Equal(7, await OnHand(paper));
        await Assert.ThrowsAsync<BusinessException>(() => management.DecideCount(new(Guid.NewGuid(), session1, false, "Second decision")));

        var trace = await management.Trace(paper);
        Assert.Contains(trace, x => x.Kind == nameof(DocumentKind.StockCount) && x.Quantity == -3 && x.User == session.Actor.Id);
    }

    [Fact] public async Task RejectedCountMovesNothing()
    {
        var id = await management.SubmitCount(new(Guid.NewGuid(), "Spot check", [new(Guid.NewGuid(), paper, "MAIN", 10, 2, 0, "Suspicious")]));
        await management.DecideCount(new(Guid.NewGuid(), id, false, "Recount required"));
        var view = (await management.Counts()).Single();
        Assert.Equal("Rejected", view.Status); Assert.Empty(view.Documents); Assert.Equal(10, await OnHand(paper));
    }

    [Fact] public async Task StaleCountLineRollsBackTheWholeApproval()
    {
        var pens = await AddProduct("PENS", TrackingMode.Quantity); await Receive(pens, 5);
        var id = await management.SubmitCount(new(Guid.NewGuid(), "Two lines", [
            new(Guid.NewGuid(), paper, "MAIN", 10, 9, 0, "Missing ream"),
            new(Guid.NewGuid(), pens, "MAIN", 5, 4, 0, "Missing pen")]));
        await Sell(pens); // book quantity changes after the count was prepared
        await Assert.ThrowsAsync<BusinessException>(() => management.DecideCount(new(Guid.NewGuid(), id, true, "Approve")));
        Assert.Equal(10, await OnHand(paper)); // first line must not have posted
        Assert.Equal("Pending approval", (await management.Counts()).Single().Status);
    }

    [Fact] public async Task CountApprovalRequiresApprovalPermission()
    {
        var id = await management.SubmitCount(new(Guid.NewGuid(), "Clerk count", [new(Guid.NewGuid(), paper, "MAIN", 10, 9, 0, "Missing")]));
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read", "inventory.receive" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => management.DecideCount(new(Guid.NewGuid(), id, true, "Self approval")));
    }

    [Fact] public async Task CountSessionRejectsDuplicateIdentities()
    {
        StockCountCommand Line() => new(Guid.NewGuid(), paper, "MAIN", 10, 9, 0, "Count");
        await Assert.ThrowsAsync<BusinessException>(() => management.SubmitCount(new(Guid.NewGuid(), "Duplicate", [Line(), Line()])));
    }

    [Fact] public async Task RetiredBarcodeDisappearsFromPointOfSale()
    {
        var barcode = await pos.AddBarcode(new(Guid.NewGuid(), paper, "000123", 6));
        Assert.Contains(await pos.Barcodes(), x => x.Code == "000123" && x.Conversion == 6);

        await management.ChangeBarcode(new(Guid.NewGuid(), barcode, 0, paper, 12, false, "Pack size changed"));
        Assert.Equal(12, (await pos.Barcodes()).Single(x => x.Code == "000123").Conversion);
        await Assert.ThrowsAsync<BusinessException>(() => management.ChangeBarcode(new(Guid.NewGuid(), barcode, 0, paper, 12, true, "Stale")));

        await management.ChangeBarcode(new(Guid.NewGuid(), barcode, 1, paper, 12, true, "Discontinued pack"));
        Assert.DoesNotContain(await pos.Barcodes(), x => x.Code == "000123");
        Assert.True((await management.Barcodes()).Single(x => x.Code == "000123").Retired);

        var machine = await AddProduct("MACHINE", TrackingMode.Serial);
        await Assert.ThrowsAsync<BusinessException>(() => management.ChangeBarcode(new(Guid.NewGuid(), barcode, 2, machine, 2, false, "Wrong")));
    }

    [Fact] public async Task SerialInServiceCannotBeSoldUntilReleased()
    {
        var machine = await AddProduct("MACHINE", TrackingMode.Serial);
        await Receive(machine, 1, serial: "SN-1");
        await Assert.ThrowsAsync<BusinessException>(() => management.SetSerialService(new(Guid.NewGuid(), machine, "SN-404", true, 0, "Not on hand")));

        await management.SetSerialService(new(Guid.NewGuid(), machine, "sn-1", true, 0, "Repair"));
        Assert.Equal("InService", (await management.Serials()).Single().Status);
        await Assert.ThrowsAsync<BusinessException>(() => Sell(machine, serial: "SN-1"));
        await Assert.ThrowsAsync<BusinessException>(() => management.SetSerialService(new(Guid.NewGuid(), machine, "SN-1", true, 1, "Already in service")));

        await management.SetSerialService(new(Guid.NewGuid(), machine, "SN-1", false, 1, "Repaired"));
        await Sell(machine, serial: "SN-1");
        Assert.Equal("Sold", (await management.Serials()).Single().Status);
    }

    [Fact] public async Task BatchMetadataRequiresRecordedStockAndValidDates()
    {
        var medicine = await AddProduct("MEDICINE", TrackingMode.BatchExpiry);
        var expiry = today.AddDays(60);
        await Assert.ThrowsAsync<BusinessException>(() => management.SaveBatch(new(Guid.NewGuid(), new(medicine, "LOT1", expiry))));
        await Receive(medicine, 4, batch: "LOT1", expiry: expiry);
        await Assert.ThrowsAsync<BusinessException>(() => management.SaveBatch(new(Guid.NewGuid(), new(medicine, "LOT1", expiry, Manufactured: expiry.AddDays(1)))));
        await management.SaveBatch(new(Guid.NewGuid(), new(medicine, "lot1", expiry, Manufactured: today.AddDays(-30), SupplierLot: "SUP-9")));

        var lot = (await management.Batches()).Single();
        Assert.Equal("LOT1", lot.Metadata.Batch); Assert.Equal("SUP-9", lot.Metadata.SupplierLot); Assert.Equal(1, lot.Metadata.Version);
        Assert.Equal(4, lot.Quantity); Assert.False(lot.Expired); Assert.Equal(today, lot.FirstReceived);
    }

    [Fact] public async Task OperationIdCannotBeReusedWithDifferentData()
    {
        var operation = Guid.NewGuid();
        await management.SaveReorder(new(operation, new(paper, "MAIN", Minimum: 1, Target: 5)));
        await Assert.ThrowsAsync<BusinessException>(() => management.SaveReorder(new(operation, new(paper, "MAIN", Minimum: 2, Target: 5))));
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
