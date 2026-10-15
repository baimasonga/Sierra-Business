using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>0.13.0: Purchase Order → Goods Receipt → Supplier Invoice → Supplier Payment, foreign-currency purchase orders and the registered-customer rule for credit sales.</summary>
public sealed class SupplierInvoiceTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-invoices-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private BusinessService business = null!; private PurchasingService purchasing = null!; private FinanceService finance = null!; private ReturnService returns = null!; private BankingService banking = null!; private OrderService orders = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid paper, pens;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options); purchasing = new(factory, session, options); finance = new(factory, session, options); returns = new(factory, session, options); banking = new(factory, session, options); orders = new(factory, session, options);
        paper = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: 1));
        pens = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PENS", "Pens", "Office", "box", 50, TrackingMode.Quantity, MinimumPrice: 1));
        await purchasing.SetRate(new(Guid.NewGuid(), "USD", today.AddDays(-10), 22.5m, "Bank of Sierra Leone"));
        await purchasing.SetRate(new(Guid.NewGuid(), "USD", today, 23m, "Bank of Sierra Leone"));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    private Task<Guid> ReceiveLater(Guid product, decimal qty, decimal price, string? currency = null, DateOnly? date = null, Guid? order = null, string supplier = "Global Supplies") =>
        business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, supplier, date ?? today, [new(product, qty, price)], null, OrderId: order, Currency: currency, InvoiceLater: true));
    private Task<Guid> Sell(Guid product, decimal qty) => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(product, qty, 100)], PaymentMethod.Cash));
    private async Task<BusinessSnapshot> Snapshot() => await business.Read(session.Actor);
    private static decimal Net(BusinessSnapshot s, int account) => s.Ledger.Where(x => (int)x.Account == account).Sum(x => x.Debit - x.Credit);
    private static void Reconciles(BusinessSnapshot s)
    {
        Assert.Equal(s.Ledger.Sum(x => x.Debit), s.Ledger.Sum(x => x.Credit));
        Assert.Equal(s.Stock.Sum(x => x.Value), Net(s, 1200));
    }
    private async Task<AwaitingInvoiceLine> Awaiting(Guid receipt) => (await purchasing.AwaitingInvoice()).Single(x => x.ReceiptId == receipt);
    private SupplierInvoiceCommand Invoice(string number, params (Guid Line, decimal Quantity, decimal Amount)[] lines) => new(Guid.NewGuid(), "Global Supplies", number, today, null, [.. lines.Select(x => new InvoiceMatchInput(x.Line, x.Quantity, x.Amount))]);
    private async Task Verified() { await using var db = factory.CreateDbContext(); var report = await HistoryProtection.Verify(db); Assert.True(report.Ok, string.Join("; ", report.Problems)); }

    [Fact] public async Task ReceiptWaitsForInvoiceThenPartialInvoicesClearGoodsReceivedNotInvoiced()
    {
        var receipt = await ReceiveLater(paper, 10, 50);
        var s = await Snapshot();
        Assert.Equal(-500, Net(s, 2050)); Assert.Equal(0, Net(s, 2000)); Reconciles(s);
        Assert.Equal(Grni.Awaiting, s.Documents.Single(x => x.Id == receipt).Settlement);
        Assert.DoesNotContain((await finance.Read(today.AddDays(-30), today)).Bills, x => x.Id == receipt); // not a bill yet
        var line = await Awaiting(receipt);
        Assert.Equal((10m, 0m, 10m, 500m, 500m, "SLE"), (line.Received, line.Invoiced, line.Outstanding, line.OutstandingAmount, line.OutstandingLocal, line.Currency));

        var first = Invoice("INV-1001", (line.LineId, 4, 200));
        var preview = await purchasing.PreviewSupplierInvoice(first);
        Assert.Equal((200m, 200m, 0m, 0m, false), (preview.LocalTotal, preview.GoodsCleared, preview.PriceVariance, preview.ExchangeDifference, preview.NeedsReason));
        var invoice = await purchasing.PostSupplierInvoice(first);
        Assert.Equal(invoice, await purchasing.PostSupplierInvoice(first)); // retry replays
        s = await Snapshot();
        Assert.Equal((-300m, -200m), (Net(s, 2050), Net(s, 2000))); Reconciles(s);
        Assert.Equal(200, (await finance.Read(today.AddDays(-30), today)).Bills.Single(x => x.Id == invoice).Outstanding);
        Assert.Equal((4m, 6m, 300m), ((await Awaiting(receipt)).Invoiced, (await Awaiting(receipt)).Outstanding, (await Awaiting(receipt)).OutstandingAmount));

        Assert.Contains("already recorded", (await Assert.ThrowsAsync<BusinessException>(() => purchasing.PostSupplierInvoice(Invoice("inv-1001", (line.LineId, 1, 50))))).Message);
        Assert.Contains("Only 6", (await Assert.ThrowsAsync<BusinessException>(() => purchasing.PostSupplierInvoice(Invoice("INV-1002", (line.LineId, 7, 350))))).Message);
        await Assert.ThrowsAsync<BusinessException>(() => purchasing.PostSupplierInvoice(Invoice("INV-1002", (line.LineId, 6, 300)) with { Supplier = "Someone Else" }));
        await Assert.ThrowsAsync<BusinessException>(() => purchasing.PostSupplierInvoice(Invoice("INV-1002", (line.LineId, 6, 300)) with { Date = today.AddDays(-1) })); // before the receipt

        // Supplier charges 55 instead of 50 for the last 6 reams; 8 reams were sold meanwhile, so only 2 of the 6 are still on hand.
        await Sell(paper, 8);
        var second = Invoice("INV-1002", (line.LineId, 6, 330));
        preview = await purchasing.PreviewSupplierInvoice(second);
        Assert.True(preview.NeedsReason);
        Assert.Equal((330m, 300m, 30m), (preview.LocalTotal, preview.GoodsCleared, preview.PriceVariance));
        Assert.Equal((10m, 20m), (preview.Lines.Single().ToInventory, preview.Lines.Single().ToCostOfSales));
        Assert.Contains("reason", (await Assert.ThrowsAsync<BusinessException>(() => purchasing.PostSupplierInvoice(second))).Message);
        var last = await purchasing.PostSupplierInvoice(second with { VarianceReason = "Supplier price increase from 1 October" });
        s = await Snapshot();
        Assert.Equal((0m, -530m), (Net(s, 2050), Net(s, 2000))); Reconciles(s);
        Assert.Equal(110m, s.Stock.Single(x => x.ProductId == paper).Value); // 2 reams × 50 + 10 price difference
        Assert.Empty(await purchasing.AwaitingInvoice());

        // Payment voucher settles the supplier invoice like any NLe bill.
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Pay INV-1002", [new(2000, 330, 0), new(1010, 0, 330)], [new(last, 330)]));
        Assert.DoesNotContain((await finance.Read(today.AddDays(-30), today)).Bills, x => x.Id == last);

        Assert.Contains("supplier invoices matched", (await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), receipt, "Wrong receipt")))).Message);
        await business.Reverse(session.Actor, new(Guid.NewGuid(), invoice, "Wrong invoice entered"));
        Assert.Equal(4, (await Awaiting(receipt)).Outstanding);
        s = await Snapshot(); Assert.Equal(-200, Net(s, 2050)); Reconciles(s);
        await Verified();
    }

    [Fact] public async Task CheaperInvoiceLowersStockForUnitsOnHandAndCostOfSalesForUnitsSold()
    {
        var receipt = await ReceiveLater(paper, 10, 50);
        await Sell(paper, 5);
        var line = await Awaiting(receipt);
        var preview = await purchasing.PreviewSupplierInvoice(Invoice("CR-1", (line.LineId, 10, 450)) with { VarianceReason = "Volume discount" });
        Assert.Equal((-50m, -25m, -25m), (preview.PriceVariance, preview.Lines.Single().ToInventory, preview.Lines.Single().ToCostOfSales));
        var invoice = await purchasing.PostSupplierInvoice(Invoice("CR-1", (line.LineId, 10, 450)) with { VarianceReason = "Volume discount" });
        var s = await Snapshot();
        Assert.Equal((0m, -450m, 225m, 225m), (Net(s, 2050), Net(s, 2000), s.Stock.Single().Value, Net(s, 5000))); Reconciles(s); // 5 × 45 left; cost of sales 250 − 25

        await Sell(paper, 1); // stock moved after the invoice: correct with a further adjustment, not a reversal
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), invoice, "Too late")));
        await Verified();
    }

    [Fact] public async Task ForeignInvoiceRecordsPriceAndExchangeDifferencesSeparatelyAndIsPaidInItsCurrency()
    {
        var receipt = await ReceiveLater(pens, 10, 10, "USD", today.AddDays(-5)); // USD 100 at 22.50 → NLe 2,250
        Assert.Equal(-2250, Net(await Snapshot(), 2050));
        Assert.Empty(await purchasing.ForeignBills()); // nothing owed until the invoice arrives
        var line = await Awaiting(receipt);
        Assert.Equal(("USD", 100m, 2250m), (line.Currency, line.OutstandingAmount, line.OutstandingLocal));

        var nle = await ReceiveLater(paper, 1, 50);
        var nleLine = (await Awaiting(nle)).LineId;
        Assert.Contains("one currency", (await Assert.ThrowsAsync<BusinessException>(() => purchasing.PreviewSupplierInvoice(Invoice("X", (line.LineId, 10, 100), (nleLine, 1, 50))))).Message);

        // USD 110 invoiced on today's rate 23: price difference USD 10 at the receipt rate (NLe 225) goes to stock; rate movement (NLe 55) is an exchange loss.
        var command = Invoice("GS-77", (line.LineId, 10, 110)) with { VarianceReason = "Freight surcharge on invoice" };
        var preview = await purchasing.PreviewSupplierInvoice(command);
        Assert.Equal(("USD", 23m, 110m, 2530m, 2250m, 225m, 55m), (preview.Currency, preview.Rate, preview.SourceTotal, preview.LocalTotal, preview.GoodsCleared, preview.PriceVariance, preview.ExchangeDifference));
        var invoice = await purchasing.PostSupplierInvoice(command);
        var s = await Snapshot();
        Assert.Equal((-50m, -2530m, 55m, 2475m), (Net(s, 2050), Net(s, 2000), Net(s, 5300), s.Stock.Single(x => x.ProductId == pens).Value)); Reconciles(s);
        Assert.Equal("Supplier payable (USD)", s.Documents.Single(x => x.Id == invoice).Settlement);

        var bill = (await purchasing.ForeignBills()).Single();
        Assert.Equal((invoice, 110m, 2530m), (bill.BillId, bill.SourceOutstanding, bill.CarryingOutstanding));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Local voucher", [new(2000, 500, 0), new(1010, 0, 500)], [new(invoice, 500)])));
        await purchasing.PayForeign(new(Guid.NewGuid(), invoice, today, 110, 2530, 1010, "TT-9"));
        s = await Snapshot(); Assert.Equal(0, Net(s, 2000)); Reconciles(s);
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), invoice, "Paid already")));
        await Verified();
    }

    [Fact] public async Task ReturnsBeforeInvoicingReduceGoodsReceivedNotInvoicedAndAfterInvoicingBecomeSupplierCredit()
    {
        var receipt = await ReceiveLater(paper, 10, 50);
        Assert.Contains("Original settlement", (await Assert.ThrowsAsync<BusinessException>(() => returns.Post(new(Guid.NewGuid(), receipt, "Damaged", [new(paper, null, null, null, 3)], ReturnDisposition.CashRefund)))).Message);
        var early = await returns.Post(new(Guid.NewGuid(), receipt, "Damaged on arrival", [new(paper, null, null, null, 3)]));
        var s = await Snapshot();
        Assert.Equal((-350m, 350m), (Net(s, 2050), s.Stock.Single().Value)); Reconciles(s);
        Assert.Equal("Returned before invoicing", s.Documents.Single(x => x.Id == early).Settlement);
        var line = await Awaiting(receipt);
        Assert.Equal((3m, 7m, 350m), (line.Returned, line.Outstanding, line.OutstandingAmount));

        var invoice = await purchasing.PostSupplierInvoice(Invoice("INV-9", (line.LineId, 7, 350)));
        s = await Snapshot(); Assert.Equal((0m, -350m), (Net(s, 2050), Net(s, 2000))); Reconciles(s);

        // Invoiced goods go back as a supplier credit, which is then applied to the supplier's invoice.
        Assert.Contains("already on a supplier invoice", (await Assert.ThrowsAsync<BusinessException>(() => returns.Post(new(Guid.NewGuid(), receipt, "Wrong colour", [new(paper, null, null, null, 2)])))).Message);
        await returns.Post(new(Guid.NewGuid(), receipt, "Wrong colour", [new(paper, null, null, null, 2)], ReturnDisposition.SupplierCredit));
        var credit = (await banking.Read()).SupplierCredits.Single();
        Assert.Equal(100, credit.Remaining);
        await banking.UseSupplierCredit(new(Guid.NewGuid(), credit.Id, 100, null, invoice, "Apply credit note CN-4"));
        Assert.Equal(250, (await finance.Read(today.AddDays(-30), today)).Bills.Single(x => x.Id == invoice).Outstanding);
        s = await Snapshot(); Assert.Equal(-250, Net(s, 2000)); Reconciles(s);
        await Verified();
    }

    [Fact] public async Task PurchaseOrdersCanBeInForeignCurrencyAndReceiptsMustUseIt()
    {
        var order = await orders.Create(new(Guid.NewGuid(), OrderType.PurchaseOrder, "Global Supplies", today, today.AddDays(30), "PO-USD-1", [new(pens, 10, 12.5m)], Currency: "usd"));
        Assert.Equal("USD", (await orders.Read()).Single().Currency);
        Assert.Contains("order is in USD", (await Assert.ThrowsAsync<BusinessException>(() => ReceiveLater(pens, 4, 12.5m, order: order))).Message);
        await Assert.ThrowsAsync<BusinessException>(() => ReceiveLater(pens, 4, 13m, "USD", order: order)); // price must match the order
        var receipt = await ReceiveLater(pens, 4, 12.5m, "USD", order: order);
        Assert.Equal(4, (await orders.Read()).Single().Lines.Single().Fulfilled);
        Assert.Equal(1150m, (await Snapshot()).Stock.Single().Value); // USD 50 × 23
        var line = await Awaiting(receipt);
        await purchasing.PostSupplierInvoice(Invoice("GS-PO-1", (line.LineId, 4, 50)));
        Assert.Equal(50m, (await purchasing.ForeignBills()).Single().SourceOutstanding);

        // Received without "invoice later": the receipt is the bill, as before.
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Global Supplies", today, [new(pens, 6, 12.5m)], null, OrderId: order, Currency: "USD"));
        Assert.Equal(2, (await purchasing.ForeignBills()).Count);
        Assert.Contains("purchase orders", (await Assert.ThrowsAsync<BusinessException>(() => orders.Create(new(Guid.NewGuid(), OrderType.SalesOrder, "Customer", today, today.AddDays(30), "SO", [new(pens, 1, 100)], Currency: "USD")))).Message);
        Assert.Contains("Only goods receipts", (await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(pens, 1, 100)], PaymentMethod.Cash, InvoiceLater: true)))).Message);
        Reconciles(await Snapshot());
        await Verified();
    }

    [Fact] public async Task CreditSalesNeedARegisteredCustomer()
    {
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Global Supplies", today, [new(paper, 10, 50)], null));
        var credit = new TradeCommand(Guid.NewGuid(), DocumentKind.Sale, "Unknown School", today, [new(paper, 1, 100)], null);
        Assert.Contains("registered customer", (await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, credit))).Message);
        await business.Post(session.Actor, credit with { OperationId = Guid.NewGuid(), Payment = PaymentMethod.Cash }); // paying now needs no registration
        await TestParties.Register(factory, "Unknown School", creditLimit: 150);
        await business.Post(session.Actor, credit with { OperationId = Guid.NewGuid() });
        Assert.Contains("credit limit", (await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, credit with { OperationId = Guid.NewGuid() }))).Message); // limit now applies
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
