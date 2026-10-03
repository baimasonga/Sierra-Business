using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class CommerceTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "sierra-commerce-" + Guid.NewGuid().ToString("N") + ".db");
    private Factory factory = null!; private readonly Session session = new(); private BusinessService business = null!; private CommerceService commerce = null!; private OrderService orders = null!; private ReturnService returns = null!; private FinanceService finance = null!; private InventoryService inventory = null!;
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow); private Guid product;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options); await using var db = factory.CreateDbContext(); await db.Database.MigrateAsync();
        var options = Options.Create(new TradingOptions()); business = new(factory, options); commerce = new(factory, session, options); orders = new(factory, session, options); returns = new(factory, session, options); finance = new(factory, session, options); inventory = new(factory, session, options);
        product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, 60));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(product, 20, 40)], null));
    }
    public Task DisposeAsync() { File.Delete(path); return Task.CompletedTask; }
    private Task<Guid> Order(decimal quantity = 5) => orders.Create(new(Guid.NewGuid(), OrderType.SalesOrder, "School", today, today.AddDays(30), "Negotiated order", [new(product, quantity, 80)]));
    private Task<Guid> Sale(decimal quantity = 2, PaymentMethod? payment = PaymentMethod.Cash) => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "School", today, [new(product, quantity, 80)], payment));
    private Task<FinanceSnapshot> Report() => finance.Read(today, today);
    [Fact] public async Task NegotiatedPricesRespectConfidentialFloorIncludingUnitConversions()
    {
        var sale = new TradeCommand(Guid.NewGuid(), DocumentKind.Sale, "School", today, [new(product, 1, 59)], PaymentMethod.Cash);
        var error = await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, sale)); Assert.DoesNotContain("60", error.Message);
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, sale with { Lines = [new(product, 1, 100, 2)] }));
        await business.Post(session.Actor, sale with { Lines = [new(product, 1, 120, 2)] });
        await Assert.ThrowsAsync<BusinessException>(() => orders.Create(new(Guid.NewGuid(), OrderType.Quotation, "School", today, today.AddDays(1), "", [new(product, 1, 59)])));
        var json = JsonSerializer.Serialize(await business.Read(session.Actor)); Assert.DoesNotContain("MinimumPrice", json); Assert.DoesNotContain("PricePolicy", json);
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read", "sales.create", "catalogue.write", "audit.read" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => commerce.Prices());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => commerce.SetPrice(new(Guid.NewGuid(), product, 1, "Bypass")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.AddProduct(session.Actor, new(Guid.NewGuid(), "BAD", "Bad", "Office", "unit", 100, TrackingMode.Quantity, 1)));
    }
    [Fact] public async Task MissingPricePolicyBlocksSalesAndPolicyChangesAreAuditedAndEnforced()
    {
        var p = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "NEW", "New", "General", "unit", 100, TrackingMode.Quantity));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(p, 2, 40)], null));
        var sale = new TradeCommand(Guid.NewGuid(), DocumentKind.Sale, "School", today, [new(p, 1, 80)], PaymentMethod.Cash);
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, sale));
        var command = new PricePolicyCommand(Guid.NewGuid(), p, 80, "Owner-approved floor"); var id = await commerce.SetPrice(command); Assert.Equal(id, await commerce.SetPrice(command));
        await business.Post(session.Actor, sale);
        await commerce.SetPrice(new(Guid.NewGuid(), p, 90, "New purchase cost"));
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, sale with { OperationId = Guid.NewGuid() }));
        await using var db = factory.CreateDbContext(); var policy = await db.PricePolicies.FirstAsync(); policy.MinimumPrice = 1; await Assert.ThrowsAsync<BusinessException>(() => db.SaveChangesAsync());
    }
    [Fact] public async Task ReservationProtectsOtherSalesTransfersAndCountsAndIsConsumedByItsOrder()
    {
        var order = await Order(15); var input = new ReservationCommand(Guid.NewGuid(), order, product, "MAIN", 15);
        var reservation = await commerce.Reserve(input); Assert.Equal(reservation, await commerce.Reserve(input));
        await Assert.ThrowsAsync<BusinessException>(() => Sale(6));
        await inventory.CreateLocation(new(Guid.NewGuid(), "SECOND", "Second store"));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Transfer(new(Guid.NewGuid(), product, "MAIN", "SECOND", 6, null, null, null, "Test")));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(new(Guid.NewGuid(), product, "MAIN", 20, 14, 0, "Count")));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "School", today, [new(product, 10, 80)], PaymentMethod.Cash, OrderId: order));
        Assert.Equal(5, (await commerce.Reservations()).Single().Remaining);
        await commerce.Release(new(Guid.NewGuid(), reservation, "Customer changed quantity")); Assert.Empty(await commerce.Reservations());
    }
    [Fact] public async Task PartialDeliveryThenInvoiceIssuesStockOnceAndReturnsOriginalCost()
    {
        var order = await Order(); await commerce.Reserve(new(Guid.NewGuid(), order, product, "MAIN", 5));
        var request = new TradeCommand(Guid.NewGuid(), DocumentKind.Delivery, "School", today, [new(product, 2, 80)], null, OrderId: order);
        var delivery = await business.Post(session.Actor, request);
        Assert.Equal(18, (await business.Read(session.Actor)).Stock.Single().Quantity);
        Assert.Equal(80, (await Report()).Balances.Single(x => x.Code == 1250).Closing);
        Assert.Equal(0, (await business.Read(session.Actor)).NetSales);
        var invoice = await business.Post(session.Actor, request with { OperationId = Guid.NewGuid(), Kind = DocumentKind.Sale, DeliveryId = delivery, Payment = PaymentMethod.Cash });
        Assert.Equal(18, (await business.Read(session.Actor)).Stock.Single().Quantity);
        Assert.Equal(0, (await Report()).Balances.Single(x => x.Code == 1250).Closing);
        Assert.Equal(160, (await business.Read(session.Actor)).NetSales);
        Assert.Equal(2, (await orders.Read()).Single().Lines.Single().Fulfilled);
        Assert.Equal(3, (await commerce.Reservations()).Single().Remaining);
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, request with { OperationId = Guid.NewGuid(), Kind = DocumentKind.Sale, DeliveryId = delivery }));
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), delivery, "Already invoiced")));
        Assert.Contains(await returns.Read(), x => x.Id == invoice && x.Lines.Single().OriginalQuantity == 2);
        await returns.Post(new(Guid.NewGuid(), invoice, "One returned", [new(product, null, null, null, 1)], ReturnDisposition.CashRefund));
        Assert.Equal(19, (await business.Read(session.Actor)).Stock.Single().Quantity);
        Assert.Equal(760, (await business.Read(session.Actor)).Stock.Single().Value);
        Assert.Equal(80, (await business.Read(session.Actor)).NetSales);
    }
    [Fact] public async Task DeliveryInvoiceReversalLeavesGoodsDeliveredUntilDeliveryReversed()
    {
        var order = await Order(); var request = new TradeCommand(Guid.NewGuid(), DocumentKind.Delivery, "School", today, [new(product, 2, 80)], null, OrderId: order);
        var delivery = await business.Post(session.Actor, request);
        var invoice = await business.Post(session.Actor, request with { OperationId = Guid.NewGuid(), Kind = DocumentKind.Sale, DeliveryId = delivery });
        await business.Reverse(session.Actor, new(Guid.NewGuid(), invoice, "Correct invoice"));
        Assert.Equal(18, (await business.Read(session.Actor)).Stock.Single().Quantity);
        Assert.Equal(80, (await Report()).Balances.Single(x => x.Code == 1250).Closing);
        await business.Reverse(session.Actor, new(Guid.NewGuid(), delivery, "Goods not delivered"));
        Assert.Equal(20, (await business.Read(session.Actor)).Stock.Single().Quantity);
        Assert.Equal(0, (await Report()).Balances.Single(x => x.Code == 1250).Closing);
        Assert.Equal(0, (await orders.Read()).Single().Lines.Single().Fulfilled);
    }
    [Fact] public async Task PaidCreditInvoiceReturnCreatesCreditWhichCanBePartRefundedAndApplied()
    {
        var invoice = await Sale(2, null);
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, today, "School payment", [new(1000, 160, 0), new(1100, 0, 160)], [new(invoice, 160)]));
        var returned = await returns.Post(new(Guid.NewGuid(), invoice, "Returned one", [new(product, null, null, null, 1)], ReturnDisposition.CustomerCredit));
        var credit = (await commerce.Credits()).Single(); Assert.Equal(80, credit.Remaining);
        Assert.DoesNotContain((await Report()).Bills, x => x.Id == invoice);
        var command = new UseCreditCommand(Guid.NewGuid(), credit.Id, 30, 1000, null, "Partial refund"); var refund = await commerce.UseCredit(command); Assert.Equal(refund, await commerce.UseCredit(command));
        Assert.Equal(50, (await commerce.Credits()).Single().Remaining);
        var next = await Sale(1, null); await commerce.UseCredit(new(Guid.NewGuid(), credit.Id, 50, null, next, "Apply balance"));
        Assert.Equal(30, (await Report()).Bills.Single(x => x.Id == next).Outstanding);
        Assert.Equal(0, (await commerce.Credits()).Single().Remaining);
        await Assert.ThrowsAsync<BusinessException>(() => commerce.UseCredit(command with { OperationId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), returned, "Credit already used")));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), refund, "Undo refund")); Assert.Equal(30, (await commerce.Credits()).Single().Remaining);
    }
    [Fact] public async Task PartPaidInvoiceReturnSplitsBillCreditAndCashRefundWithoutNegativeReceivable()
    {
        var invoice = await Sale(2, null);
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, today, "Payment", [new(1000, 100, 0), new(1100, 0, 100)], [new(invoice, 100)]));
        await returns.Post(new(Guid.NewGuid(), invoice, "Both returned", [new(product, null, null, null, 2)], ReturnDisposition.CashRefund));
        var report = await Report(); Assert.Equal(0, report.Balances.Single(x => x.Code == 1100).Closing); Assert.Equal(0, report.Balances.Single(x => x.Code == 1000).Closing); Assert.DoesNotContain(report.Bills, x => x.Id == invoice);
    }
    [Fact] public async Task HeldCashSaleCreditCannotBeAllocatedToAnotherCustomer()
    {
        var invoice = await Sale(); await returns.Post(new(Guid.NewGuid(), invoice, "Held credit", [new(product, null, null, null, 1)], ReturnDisposition.CustomerCredit));
        var credit = (await commerce.Credits()).Single();
        var other = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Other school", today, [new(product, 1, 80)], null));
        await Assert.ThrowsAsync<BusinessException>(() => commerce.UseCredit(new(Guid.NewGuid(), credit.Id, 80, null, other, "Wrong customer")));
        Assert.Equal(80, (await commerce.Credits()).Single().Remaining);
    }
    [Fact] public async Task InvoiceForPreviouslyDeliveredBatchDoesNotRecheckWarehouseExpiry()
    {
        var p = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "BATCH", "Batch item", "Medical", "box", 100, TrackingMode.BatchExpiry, 60));
        var expiry = today.AddDays(-1);
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today.AddDays(-3), [new(p, 2, 40, Batch: "B1", Expiry: expiry)], null));
        var order = await orders.Create(new(Guid.NewGuid(), OrderType.SalesOrder, "School", today.AddDays(-2), today, "Agreed", [new(p, 2, 80)]));
        var request = new TradeCommand(Guid.NewGuid(), DocumentKind.Delivery, "School", today.AddDays(-2), [new(p, 2, 80, Batch: "B1", Expiry: expiry)], null, OrderId: order);
        var delivery = await business.Post(session.Actor, request);
        await business.Post(session.Actor, request with { OperationId = Guid.NewGuid(), Kind = DocumentKind.Sale, DeliveryId = delivery, BusinessDate = today, Payment = PaymentMethod.Cash });
        Assert.Equal(0, (await business.Read(session.Actor)).Stock.Single(x => x.ProductId == p).Quantity);
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}

