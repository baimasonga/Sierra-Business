using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class PosTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "sierra-pos-" + Guid.NewGuid().ToString("N") + ".db");
    private Factory factory = null!; private readonly Session session = new(); private BusinessService business = null!; private PosService pos = null!; private BankingService banking = null!; private CommerceService commerce = null!; private ReturnService returns = null!;
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow); private Guid product;
    public async Task InitializeAsync() { factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options); await using var db = factory.CreateDbContext(); await db.Database.MigrateAsync(); var options = Options.Create(new TradingOptions()); business = new(factory, options); pos = new(factory, session, options); banking = new(factory, session, options); commerce = new(factory, session, options); returns = new(factory, session, options); product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, 60)); await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(product, 20, 40)], null)); }
    public Task DisposeAsync() { File.Delete(path); return Task.CompletedTask; }
    private TradeCommand Draft(decimal price = 80) => new(Guid.NewGuid(), DocumentKind.Sale, "School", today, [new(product, 2, price)], PaymentMethod.Cash);
    [Fact] public async Task HoldSurvivesNewServiceWithoutPostingOrReservingAndCanOnlyPostOnce()
    {
        var draft = Draft(); var input = new HoldSaleCommand(Guid.NewGuid(), "School bargaining", draft); var id = await pos.Hold(input); Assert.Equal(id, await pos.Hold(input));
        pos = new(factory, session, Options.Create(new TradingOptions())); Assert.Equal(draft.Lines, (await pos.HeldSales()).Single().Draft.Lines); var before = await business.Read(session.Actor); Assert.Single(before.Documents); Assert.Equal(20, before.Stock.Single().Quantity); Assert.Empty(await commerce.Reservations());
        var post = draft with { HeldSaleId = id }; var invoice = await business.Post(session.Actor, post); Assert.Equal(invoice, await business.Post(session.Actor, post)); Assert.Empty(await pos.HeldSales());
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, post with { OperationId = Guid.NewGuid() })); Assert.Equal(18, (await business.Read(session.Actor)).Stock.Single().Quantity);
        await using var db = factory.CreateDbContext(); Assert.Equal(invoice, (await db.HeldSaleEnds.SingleAsync()).DocumentId);
    }
    [Fact] public async Task RecallRevalidatesPricesAndStockWithoutConsumingFailedHold()
    {
        var draft = Draft(); var id = await pos.Hold(new(Guid.NewGuid(), "Agreed", draft)); await commerce.SetPrice(new(Guid.NewGuid(), product, 90, "Updated cost"));
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, draft with { HeldSaleId = id })); Assert.Single(await pos.HeldSales());
        await business.Post(session.Actor, Draft(100) with { Lines = [new(product, 20, 100)] });
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, draft with { HeldSaleId = id, Lines = [new(product, 2, 100)] })); Assert.Single(await pos.HeldSales());
    }
    [Fact] public async Task ReholdingInvalidatesStaleCopiesAndCancellationIsAudited()
    {
        var draft = Draft(); var old = await pos.Hold(new(Guid.NewGuid(), "First", draft)); var replacement = await pos.Hold(new(Guid.NewGuid(), "Revised", draft with { Party = "Revised customer" }, old));
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, draft with { HeldSaleId = old }));
        var cancel = new CancelHeldSaleCommand(Guid.NewGuid(), replacement, "Customer left"); var end = await pos.Cancel(cancel); Assert.Equal(end, await pos.Cancel(cancel)); Assert.Empty(await pos.HeldSales());
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, draft with { HeldSaleId = replacement }));
        await using var db = factory.CreateDbContext(); Assert.Equal(2, await db.HeldSaleEnds.CountAsync()); var held = await db.HeldSales.FirstAsync(); held.Label = "Tampered"; await Assert.ThrowsAsync<BusinessException>(() => db.SaveChangesAsync());
    }
    [Fact] public async Task BarcodesPreserveLeadingZerosPackUnitsAndRejectSkuAmbiguity()
    {
        var command = new BarcodeCommand(Guid.NewGuid(), product, "00123456", 6); var id = await pos.AddBarcode(command); Assert.Equal(id, await pos.AddBarcode(command)); var barcode = (await pos.Barcodes()).Single(); Assert.Equal("00123456", barcode.Code); Assert.Equal(6, barcode.Conversion);
        await Assert.ThrowsAsync<BusinessException>(() => pos.AddBarcode(command with { OperationId = Guid.NewGuid() })); await Assert.ThrowsAsync<BusinessException>(() => pos.AddBarcode(command with { OperationId = Guid.NewGuid(), Code = "paper" }));
        await Assert.ThrowsAsync<BusinessException>(() => business.AddProduct(session.Actor, new(Guid.NewGuid(), "00123456", "Collision", "Office", "unit", 100, TrackingMode.Quantity, 1)));
        var serial = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "SER", "Machine", "Equipment", "unit", 100, TrackingMode.Serial, 1)); await Assert.ThrowsAsync<BusinessException>(() => pos.AddBarcode(new(Guid.NewGuid(), serial, "SERPACK", 2)));
    }
    [Fact] public async Task FullCounterDayPostsHeldSaleRefundAndImmutableTillClose()
    {
        var shift = await banking.OpenTill(new(Guid.NewGuid(), 1000, 50)); var mapping = new BarcodeCommand(Guid.NewGuid(), product, "PACK", 2); await pos.AddBarcode(mapping); var barcode = (await pos.Barcodes()).Single();
        var draft = Draft() with { Lines = [new(product, 1, 160, barcode.Conversion)] }; var held = await pos.Hold(new(Guid.NewGuid(), "School pack", draft)); Assert.Equal(0, (await banking.Tills()).Single().Movement);
        var sale = await business.Post(session.Actor, draft with { HeldSaleId = held }); await returns.Post(new(Guid.NewGuid(), sale, "One ream returned", [new(product, null, null, null, 1)], ReturnDisposition.CashRefund));
        var till = (await banking.Tills()).Single(); Assert.Equal(80, till.Movement); Assert.Equal(130, till.Expected); await banking.CloseTill(new(Guid.NewGuid(), shift, till.Movement, 130, "Balanced")); Assert.Equal(0, (await banking.Tills()).Single().Variance); Assert.Equal(19, (await business.Read(session.Actor)).Stock.Single().Quantity);
    }
    [Fact] public async Task HoldCannotBypassTrackingAndPermissionsOrCrossBranches()
    {
        var serial = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "SER", "Machine", "Equipment", "unit", 100, TrackingMode.Serial, 1)); await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(serial, 1, 40, Serial: "A1")], null));
        var draft = Draft() with { Lines = [new(serial, 1, 100)] }; var held = await pos.Hold(new(Guid.NewGuid(), "Needs serial", draft)); await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, draft with { HeldSaleId = held })); Assert.Single(await pos.HeldSales());
        session.Actor = session.Actor with { Branch = "OTHER" }; await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pos.HeldSales()); session.Actor = session.Actor with { Branch = "FREETOWN", Permissions = new HashSet<string> { "business.read" } }; await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pos.HeldSales()); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pos.AddBarcode(new(Guid.NewGuid(), product, "BLOCKED"))); Assert.Empty(await pos.Barcodes());
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
