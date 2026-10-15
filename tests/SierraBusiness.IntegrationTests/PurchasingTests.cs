using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Specification Skill 13 acceptance: foreign-currency purchase, partial goods receipt, landed-cost allocation, supplier payment, purchase return.</summary>
public sealed class PurchasingTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-purchasing-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private BusinessService business = null!; private PurchasingService purchasing = null!; private FinanceService finance = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid paper, pens;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options); purchasing = new(factory, session, options); finance = new(factory, session, options);
        paper = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: 1));
        pens = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PENS", "Pens", "Office", "box", 50, TrackingMode.Quantity, MinimumPrice: 1));
        await purchasing.SetRate(new(Guid.NewGuid(), "usd", today.AddDays(-10), 22.5m, "Bank of Sierra Leone"));
        await purchasing.SetRate(new(Guid.NewGuid(), "USD", today, 23m, "Bank of Sierra Leone"));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    private Task<Guid> Receive(Guid product, decimal qty, decimal price, string? currency = null, DateOnly? date = null, Guid? order = null) =>
        business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Global Supplies", date ?? today, [new(product, qty, price)], null, OrderId: order, Currency: currency));
    private Task<Guid> Sell(Guid product, decimal qty) => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(product, qty, 100)], PaymentMethod.Cash));
    private async Task<BusinessSnapshot> Snapshot() => await business.Read(session.Actor);
    private static decimal Net(BusinessSnapshot s, int account) => s.Ledger.Where(x => (int)x.Account == account).Sum(x => x.Debit - x.Credit);
    private static void Reconciles(BusinessSnapshot s)
    {
        Assert.Equal(s.Ledger.Sum(x => x.Debit), s.Ledger.Sum(x => x.Credit));
        Assert.Equal(s.Stock.Sum(x => x.Value), Net(s, 1200));
    }

    [Fact] public async Task ForeignCurrencyPurchaseIsCarriedInNLeAtTheReceiptDateRate()
    {
        var receipt = await Receive(paper, 10, 12.34m, "usd");
        var s = await Snapshot();
        Assert.Equal(2838.20m, s.Stock.Single().Value); // USD 123.40 × 23
        Assert.Equal(-2838.20m, Net(s, 2000)); Reconciles(s);
        Assert.Equal("Supplier payable (USD)", s.Documents.Single(x => x.Id == receipt).Settlement);
        var bill = (await purchasing.ForeignBills()).Single();
        Assert.Equal(("USD", 23m, 123.40m, 123.40m, 2838.20m), (bill.Currency, bill.Rate, bill.SourceAmount, bill.SourceOutstanding, bill.CarryingOutstanding));

        var older = await Receive(pens, 1, 10, "USD", today.AddDays(-5)); // earlier effective rate applies
        Assert.Equal(225m, (await Snapshot()).Stock.Single(x => x.ProductId == pens).Value);
        Assert.Contains("No USD exchange rate", (await Assert.ThrowsAsync<BusinessException>(() => Receive(pens, 1, 10, "USD", today.AddDays(-20)))).Message);
        Assert.Contains("No GBP exchange rate", (await Assert.ThrowsAsync<BusinessException>(() => Receive(pens, 1, 10, "GBP"))).Message);
        await Assert.ThrowsAsync<BusinessException>(() => Receive(pens, 1, 10, "US"));
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(paper, 1, 100)], PaymentMethod.Cash, Currency: "USD")));
        Assert.Equal(receipt, await business.Post(session.Actor, new(await OperationOf(receipt), DocumentKind.Receipt, "Global Supplies", today, [new(paper, 10, 12.34m)], null, Currency: "usd"))); // retry replays
    }
    private async Task<Guid> OperationOf(Guid result) { await using var db = factory.CreateDbContext(); return (await db.Operations.SingleAsync(x => x.ResultId == result)).Id; }

    [Fact] public async Task RatesAreHistoryAndOnlyAccountantsSetThem()
    {
        await Assert.ThrowsAsync<BusinessException>(() => purchasing.SetRate(new(Guid.NewGuid(), "USD", today, 24m, "Duplicate date")));
        await Assert.ThrowsAsync<BusinessException>(() => purchasing.SetRate(new(Guid.NewGuid(), "SLE", today, 1m, "Local")));
        await Assert.ThrowsAsync<BusinessException>(() => purchasing.SetRate(new(Guid.NewGuid(), "EUR", today, 25.123456m, "Too precise")));
        Assert.Equal([23m, 22.5m], (await purchasing.Rates()).Select(x => x.Rate));
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read", "inventory.receive" } };
        Assert.Equal(2, (await purchasing.Rates()).Count); // receivers can see rates
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => purchasing.SetRate(new(Guid.NewGuid(), "EUR", today, 25m, "Clerk")));
    }

    [Fact] public async Task ForeignSupplierPaymentRecordsExchangeLossThenGainAndClearsTheBill()
    {
        var receipt = await Receive(paper, 10, 10, "USD"); // USD 100 → NLe 2,300
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Local voucher", [new(2000, 500, 0), new(1010, 0, 500)], [new(receipt, 500)])));

        await purchasing.PayForeign(new(Guid.NewGuid(), receipt, today, 40, 960, 1010, "TT-1")); // paid at 24: loss 40
        var part = (await purchasing.ForeignBills()).Single();
        Assert.Equal((60m, 1380m), (part.SourceOutstanding, part.CarryingOutstanding));
        await Assert.ThrowsAsync<BusinessException>(() => purchasing.PayForeign(new(Guid.NewGuid(), receipt, today, 61, 1400, 1010, "Too much")));
        var final = await purchasing.PayForeign(new(Guid.NewGuid(), receipt, today, 60, 1350, 1010, "TT-2")); // paid at 22.50: gain 30

        Assert.Empty(await purchasing.ForeignBills());
        var s = await Snapshot();
        Assert.Equal(0, Net(s, 2000)); Assert.Equal(40, Net(s, 5300)); Assert.Equal(-30, Net(s, 4200)); Assert.Equal(-2310, Net(s, 1010)); Reconciles(s);
        Assert.DoesNotContain((await finance.Read(today.AddDays(-30), today)).Bills, x => x.Id == receipt);
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), receipt, "Payments exist")));

        await business.Reverse(session.Actor, new(Guid.NewGuid(), final, "Bank recalled transfer"));
        Assert.Equal((60m, 1380m), ((await purchasing.ForeignBills()).Single().SourceOutstanding, (await purchasing.ForeignBills()).Single().CarryingOutstanding));
        Assert.Equal(0, Net(await Snapshot(), 4200));
    }

    [Fact] public async Task PartialGoodsReceiptsFulfilAPurchaseOrder()
    {
        var orders = new OrderService(factory, session, Options.Create(new TradingOptions()));
        var order = await orders.Create(new(Guid.NewGuid(), OrderType.PurchaseOrder, "Global Supplies", today, today.AddDays(30), "PO-7", [new(paper, 10, 50)]));
        await Receive(paper, 4, 50, order: order);
        await Assert.ThrowsAsync<BusinessException>(() => Receive(paper, 7, 50, order: order)); // only 6 outstanding
        await Receive(paper, 6, 50, order: order);
        await Assert.ThrowsAsync<BusinessException>(() => Receive(paper, 1, 50, order: order));
        Assert.Equal(10, (await Snapshot()).Stock.Single().Quantity);
        Assert.Contains("order is in NLe", (await Assert.ThrowsAsync<BusinessException>(() => Receive(pens, 1, 10, "USD", order: order))).Message);
    }

    [Fact] public async Task LandedCostByValueGoesToStockOnHandAndCostOfSalesForUnitsSold()
    {
        var a = await Receive(paper, 10, 50); var b = await Receive(pens, 5, 20);
        await Sell(paper, 4);
        var command = new LandedCostCommand(Guid.NewGuid(), "Freetown Clearing Agency", today, [a, b], [new(LandedCostCharge.Freight, 120), new(LandedCostCharge.Clearing, 30, "Agency fee")], LandedCostBasis.Value, Reference: "INV-77");
        var preview = await purchasing.PreviewLandedCost(command);
        Assert.Equal(150, preview.LocalTotal);
        var paperLine = preview.Lines.Single(x => x.Sku == "PAPER"); var pensLine = preview.Lines.Single(x => x.Sku == "PENS");
        Assert.Equal((125m, 75m, 50m), (paperLine.Allocated, paperLine.ToInventory, paperLine.ToCostOfSales));
        Assert.Equal((25m, 25m, 0m), (pensLine.Allocated, pensLine.ToInventory, pensLine.ToCostOfSales));

        var doc = await purchasing.PostLandedCost(command);
        var s = await Snapshot();
        Assert.Equal(375m, s.Stock.Single(x => x.ProductId == paper).Value); Assert.Equal(125m, s.Stock.Single(x => x.ProductId == pens).Value);
        Assert.Equal(6, s.Stock.Single(x => x.ProductId == paper).Quantity); Reconciles(s);
        var bill = (await finance.Read(today.AddDays(-1), today)).Bills.Single(x => x.Id == doc);
        Assert.Equal(("Freetown Clearing Agency", 150m, false), (bill.Party, bill.Outstanding, bill.Receivable));
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Pay clearing agent", [new(2000, 150, 0), new(1000, 0, 150)], [new(doc, 150)]));

        await Sell(paper, 6); // remaining units now carry the landed cost
        s = await Snapshot();
        Assert.Equal(0, s.Stock.Single(x => x.ProductId == paper).Value); Reconciles(s);
        Assert.Equal(200 + 375 + 50, Net(s, 5000)); // first sale 200, last 375, sold share of landed cost 50
    }

    [Fact] public async Task QuantityAndManualBasesShareExactlyWithRoundingOnTheLargestLine()
    {
        var a = await Receive(paper, 3, 10); var b = await Receive(pens, 3, 10); var c = await Receive(paper, 1, 10, date: today.AddDays(-1));
        var byQuantity = await purchasing.PreviewLandedCost(new(Guid.NewGuid(), "Truck", today, [a, b, c], [new(LandedCostCharge.LocalTransport, 100)], LandedCostBasis.Quantity, FundsAccount: 1000));
        Assert.Equal(100m, byQuantity.Lines.Sum(x => x.Allocated));
        Assert.Equal([42.86m, 42.85m, 14.29m], byQuantity.Lines.OrderByDescending(x => x.Allocated).Select(x => x.Allocated).ToArray()[..1].Concat(byQuantity.Lines.OrderByDescending(x => x.Allocated).Skip(1).Select(x => x.Allocated)).ToArray());

        var lines = (await purchasing.Receipts()).Where(x => x.Id == a || x.Id == b).SelectMany(x => x.Lines).ToArray();
        var manual = new LandedCostCommand(Guid.NewGuid(), "Truck", today, [a, b], [new(LandedCostCharge.LocalTransport, 100)], LandedCostBasis.Manual, FundsAccount: 1000, ManualShares: new() { [lines[0].MovementId] = 70, [lines[1].MovementId] = 20 });
        Assert.Contains("must equal", (await Assert.ThrowsAsync<BusinessException>(() => purchasing.PreviewLandedCost(manual))).Message);
        await Assert.ThrowsAsync<BusinessException>(() => purchasing.PreviewLandedCost(manual with { ManualShares = new() { [Guid.NewGuid()] = 100 } }));
        await purchasing.PostLandedCost(manual with { ManualShares = new() { [lines[0].MovementId] = 80, [lines[1].MovementId] = 20 } });
        var s = await Snapshot();
        Assert.Equal(-100, Net(s, 1000)); Assert.DoesNotContain((await finance.Read(today.AddDays(-1), today)).Bills, x => x.Party == "Truck"); Reconciles(s);
    }

    [Fact] public async Task ForeignCurrencyLandedCostIsAForeignBill()
    {
        var a = await Receive(paper, 10, 50);
        var doc = await purchasing.PostLandedCost(new(Guid.NewGuid(), "Ocean Freight Ltd", today, [a], [new(LandedCostCharge.Freight, 50), new(LandedCostCharge.Insurance, 10)], LandedCostBasis.Value, Currency: "USD"));
        Assert.Equal(500m + 1380m, (await Snapshot()).Stock.Single().Value);
        var bill = (await purchasing.ForeignBills()).Single(x => x.BillId == doc);
        Assert.Equal((60m, 1380m), (bill.SourceAmount, bill.CarryingOutstanding));
        await purchasing.PayForeign(new(Guid.NewGuid(), doc, today, 60, 1380, 1010, "TT-9"));
        Assert.Empty(await purchasing.ForeignBills());
        await Assert.ThrowsAsync<BusinessException>(() => purchasing.PostLandedCost(new(Guid.NewGuid(), "Ocean", today, [a], [new(LandedCostCharge.Freight, 5)], LandedCostBasis.Value, FundsAccount: 1000, Currency: "USD")));
    }

    [Fact] public async Task ReversalsRespectLandedCostDependencies()
    {
        var a = await Receive(paper, 10, 50);
        var landed = await purchasing.PostLandedCost(new(Guid.NewGuid(), "Agent", today, [a], [new(LandedCostCharge.Duty, 100)], LandedCostBasis.Value));
        Assert.Contains("landed cost", (await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), a, "Wrong receipt")))).Message);
        await business.Reverse(session.Actor, new(Guid.NewGuid(), landed, "Wrong duty"));
        Assert.Equal(500m, (await Snapshot()).Stock.Single().Value);
        await business.Reverse(session.Actor, new(Guid.NewGuid(), a, "Now allowed"));
        var s = await Snapshot(); Assert.Equal(0, s.Stock.Sum(x => x.Quantity)); Reconciles(s);

        var b = await Receive(pens, 5, 20);
        var second = await purchasing.PostLandedCost(new(Guid.NewGuid(), "Agent", today, [b], [new(LandedCostCharge.Duty, 10)], LandedCostBasis.Value));
        await Sell(pens, 2);
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), second, "After sale")));
        Assert.True((await new IntegrityService(factory, session, Options.Create(new TradingOptions())).Verify()).Ok);
    }

    [Fact] public async Task PurchaseReturnOfAForeignReceiptIsValuedAtItsOwnRate()
    {
        var receipt = await Receive(paper, 10, 10, "USD"); // NLe 2,300
        var returns = new ReturnService(factory, session, Options.Create(new TradingOptions()));
        await returns.Post(new(Guid.NewGuid(), receipt, "Damaged on arrival", [new(paper, null, null, null, 4)]));
        var s = await Snapshot();
        Assert.Equal(1380m, s.Stock.Single().Value); Assert.Equal(-1380m, Net(s, 2000)); Reconciles(s);
        var bill = (await purchasing.ForeignBills()).Single();
        Assert.Equal((60m, 1380m), (bill.SourceOutstanding, bill.CarryingOutstanding));
        await purchasing.PayForeign(new(Guid.NewGuid(), receipt, today, 60, 1380, 1010, "TT-3"));
        Assert.Empty(await purchasing.ForeignBills());
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
