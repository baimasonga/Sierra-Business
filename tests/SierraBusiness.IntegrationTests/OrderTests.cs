using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class OrderTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-order-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private OrderService orders = null!; private BusinessService business = null!;
    private readonly Session session = new(); private Guid product; private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using var db = factory.CreateDbContext(); await db.Database.MigrateAsync();
        orders = new(factory, session, Options.Create(new TradingOptions())); business = new(factory, Options.Create(new TradingOptions()));
        product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: .0001m));
    }
    public Task DisposeAsync() { File.Delete(path); return Task.CompletedTask; }
    private OrderCommand Command(OrderType type) => new(Guid.NewGuid(), type, "School / Supplier", today, today.AddDays(30), "REF", [new(product, 10, 100)]);
    [Fact] public async Task QuoteConversionPreservesTermsAndIsUnique()
    {
        var q = Command(OrderType.Quotation); var id = await orders.Create(q);
        Assert.Equal(id, await orders.Create(q)); Assert.Empty((await business.Read(session.Actor)).Documents);
        var convert = q with { OperationId = Guid.NewGuid(), Type = OrderType.SalesOrder, QuotationId = id };
        await Assert.ThrowsAsync<BusinessException>(() => orders.Create(convert with { Lines = [new(product, 10, 99)] }));
        var sale = await orders.Create(convert); Assert.Equal(sale, (await orders.Read()).Single(x => x.Id == id).ConvertedTo);
        await Assert.ThrowsAsync<BusinessException>(() => orders.Create(convert with { OperationId = Guid.NewGuid() }));
    }
    [Fact] public async Task PartialPurchaseReceiptsCannotOverFulfillAndReversalReopensQuantity()
    {
        var id = await orders.Create(Command(OrderType.PurchaseOrder));
        var post = new TradeCommand(Guid.NewGuid(), DocumentKind.Receipt, "School / Supplier", today, [new(product, 4, 100)], null, OrderId: id);
        var receipt = await business.Post(session.Actor, post); Assert.Equal(receipt, await business.Post(session.Actor, post));
        Assert.Equal(4, (await orders.Read()).Single().Lines.Single().Fulfilled);
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, post with { OperationId = Guid.NewGuid(), Lines = [new(product, 7, 100)] }));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), receipt, "Wrong delivery"));
        Assert.Equal(0, (await orders.Read()).Single().Lines.Single().Fulfilled);
        await business.Post(session.Actor, post with { OperationId = Guid.NewGuid(), Lines = [new(product, 10, 100)] });
        Assert.Equal(10, (await orders.Read()).Single().Lines.Single().Fulfilled);
    }
    [Fact] public async Task FailedPostingDoesNotConsumeOrderAndClosedOrderRejectsPosting()
    {
        var id = await orders.Create(Command(OrderType.SalesOrder));
        var post = new TradeCommand(Guid.NewGuid(), DocumentKind.Sale, "School / Supplier", today, [new(product, 2, 100)], null, OrderId: id);
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, post));
        Assert.Equal(0, (await orders.Read()).Single().Lines.Single().Fulfilled);
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Vendor", today, [new(product, 10, 60)], null));
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, post with { Lines = [new(product, 2, 99)] }));
        await business.Post(session.Actor, post);
        await orders.Close(new(Guid.NewGuid(), id, "Customer cancelled remainder"));
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, post with { OperationId = Guid.NewGuid() }));
        Assert.Equal(2, (await orders.Read()).Single().Lines.Single().Fulfilled);
        Assert.Equal(8, (await business.Read(session.Actor)).Stock.Single().Quantity);
    }
    [Fact] public async Task CrossBranchAndPermissionChangesBlockAccess()
    {
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => orders.Create(Command(OrderType.PurchaseOrder)));
        session.Actor = session.Actor with { Branch = "OTHER" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => orders.Read());
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", new HashSet<string> { "business.read", "catalogue.write", "pricing.manage", "inventory.receive", "sales.create", "documents.reverse" }); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}

