using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class ReturnAndPartyTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-returns-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private ReturnService returns = null!; private PartyService parties = null!; private BusinessService business = null!; private FinanceService finance = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow); private Guid product;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using var db = factory.CreateDbContext(); await TestDatabase.MigrateAsync(db, guarded: false); var options = Options.Create(new TradingOptions { TaxRate = .15m });
        returns = new(factory, session, options); parties = new(factory, session, options); business = new(factory, options); finance = new(factory, session, options);
        product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: .0001m));
    }
    public Task DisposeAsync() { File.Delete(path); return Task.CompletedTask; }
    private Task<Guid> Receive(decimal quantity = 10, decimal price = 60) => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(product, quantity, price)], null));
    private Task<Guid> Sell(decimal quantity = 2, decimal price = 100, PaymentMethod? payment = null) => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "School", today, [new(product, quantity, price)], payment));
    private Task<FinanceSnapshot> Report() => finance.Read(today.AddYears(-1), today);
    [Fact] public async Task PartialSalesReturnsReconcileStockTaxAndOutstandingAndCannotOverReturn()
    {
        await TestParties.Register(factory, "School");
        await Receive(); var sale = await Sell(); var command = new ReturnCommand(Guid.NewGuid(), sale, "Damaged item", [new(product, null, null, null, 1)]);
        var id = await returns.Post(command); Assert.Equal(id, await returns.Post(command));
        Assert.Equal(115, (await Report()).Bills.Single(x => x.Id == sale).Outstanding);
        Assert.Equal(9, (await business.Read(session.Actor)).Stock.Single().Quantity);
        await returns.Post(command with { OperationId = Guid.NewGuid() });
        await Assert.ThrowsAsync<BusinessException>(() => returns.Post(command with { OperationId = Guid.NewGuid() }));
        var report = await Report(); Assert.DoesNotContain(report.Bills, x => x.Id == sale); Assert.Equal(0, report.Profit);
        Assert.Equal(600, (await business.Read(session.Actor)).Stock.Single().Value);
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), sale, "Already returned")));
    }
    [Fact] public async Task TinyPartialReturnsPreserveFullOriginalTaxAndCost()
    {
        await Receive(3, .01m); var sale = await Sell(3, .03m, PaymentMethod.Cash);
        for (var i = 0; i < 3; i++) await returns.Post(new(Guid.NewGuid(), sale, "Return", [new(product, null, null, null, 1)]));
        var report = await Report(); Assert.Equal(0, report.Balances.Single(x => x.Code == 2100).Closing); Assert.Equal(0, report.Balances.Single(x => x.Code == 1000).Closing); Assert.Equal(0, report.Profit);
    }
    [Fact] public async Task PurchaseReturnUsesCurrentCarryingCostAndPostsDifference()
    {
        var original = await Receive(10, 60); await Receive(10, 80);
        await returns.Post(new(Guid.NewGuid(), original, "Return five", [new(product, null, null, null, 5)]));
        var report = await Report(); Assert.Equal(300, report.Bills.Single(x => x.Id == original).Outstanding);
        var stock = (await business.Read(session.Actor)).Stock.Single(); Assert.Equal(15, stock.Quantity); Assert.Equal(1050, stock.Value);
        Assert.Equal(50, report.Balances.Single(x => x.Code == 5100).Debit); Assert.Equal(report.Assets, report.Liabilities + report.Equity + report.RetainedProfit);
    }
    [Fact] public async Task ReturnCannotOverCreditPaidBillAndReceiptCannotOverpayReturnedBill()
    {
        await TestParties.Register(factory, "School");
        await Receive(); var sale = await Sell();
        await returns.Post(new(Guid.NewGuid(), sale, "One unit", [new(product, null, null, null, 1)]));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, today, "Too much", [new(1000, 200, 0), new(1100, 0, 200)], [new(sale, 200)])));
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, today, "Settle", [new(1000, 115, 0), new(1100, 0, 115)], [new(sale, 115)]));
        await Assert.ThrowsAsync<BusinessException>(() => returns.Post(new(Guid.NewGuid(), sale, "Over credit", [new(product, null, null, null, 1)])));
        Assert.Equal(9, (await business.Read(session.Actor)).Stock.Single().Quantity);
    }
    [Fact] public async Task UnconsumedSalesReturnCanBeReversedAndQuantityReturnedAgain()
    {
        await TestParties.Register(factory, "School");
        await Receive(); var sale = await Sell(); var input = new ReturnCommand(Guid.NewGuid(), sale, "Return", [new(product, null, null, null, 1)]);
        var returned = await returns.Post(input); await business.Reverse(session.Actor, new(Guid.NewGuid(), returned, "Wrong return"));
        Assert.Equal(230, (await Report()).Bills.Single(x => x.Id == sale).Outstanding);
        await returns.Post(input with { OperationId = Guid.NewGuid() }); Assert.Equal(9, (await business.Read(session.Actor)).Stock.Single().Quantity);
    }
    [Fact] public async Task PartyTermsCreditLimitHoldAndConcurrentEditsAreEnforced()
    {
        var input = new PartyCommand(Guid.NewGuid(), null, 0, "SCHOOL", "School", PartyType.Customer, "Contact", "123", "school@example.test", "Freetown", 30, 230, false);
        var id = await parties.Save(input); await Receive(); var sale = await Sell();
        Assert.Equal(today.AddDays(30), (await Report()).Bills.Single(x => x.Id == sale).DueDate);
        await Assert.ThrowsAsync<BusinessException>(() => Sell(1));
        await returns.Post(new(Guid.NewGuid(), sale, "Return", [new(product, null, null, null, 1)]));
        await Sell(1);
        await parties.Save(input with { OperationId = Guid.NewGuid(), Id = id, ExpectedVersion = 1, CreditHold = true, CreditLimit = 1000 });
        await Assert.ThrowsAsync<BusinessException>(() => Sell(1));
        await Sell(1, payment: PaymentMethod.Cash);
        await Assert.ThrowsAsync<BusinessException>(() => parties.Save(input with { OperationId = Guid.NewGuid(), Id = id, ExpectedVersion = 1 }));
        await Assert.ThrowsAsync<BusinessException>(() => parties.Save(input with { OperationId = Guid.NewGuid(), Id = id, ExpectedVersion = 2, Name = "Different" }));
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", new HashSet<string> { "business.read", "catalogue.write", "pricing.manage", "inventory.receive", "sales.create", "documents.reverse", "accounting.read", "accounting.write" }); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}

