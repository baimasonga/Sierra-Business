using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Specification Skill 21 (0.18.0): dashboard and reports reconcile to the ledger and stock movements, drill down to documents and page large registers.</summary>
public sealed class ReportTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-reports-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private BusinessService business = null!; private ReportService reports = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid widget, unsold;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options); reports = new(factory, session, options);
        widget = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 250, TrackingMode.Quantity, MinimumPrice: 1));
        unsold = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAD-1", "Note pad", "Stationery", "piece", 20, TrackingMode.Quantity, MinimumPrice: 1));
        await TestParties.Register(factory, "Kissy Clinic");
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today.AddDays(-45), [new(widget, 10, 100), new(unsold, 4, 5)], null));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Kissy Clinic", today.AddDays(-40), [new(widget, 2, 300)], null, DueDate: today.AddDays(-35)));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(widget, 3, 250)], PaymentMethod.Cash));
        var mistake = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(widget, 1, 250)], PaymentMethod.Cash));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), mistake, "Keyed twice"));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    [Fact] public async Task DashboardFiguresReconcileToTheLedgerAndStock()
    {
        var d = await reports.Dashboard(today.AddDays(-60), today);
        Assert.True(d.Financial);
        Assert.Equal((1350m, 500m, 850m, 63.0m), (d.NetSales, d.CostOfSales!.Value, d.GrossProfit!.Value, d.Margin!.Value));
        Assert.Equal((750m, 600m, 600m, 1020m, 1020m), (d.Cash!.Value, d.Receivables!.Value, d.OverdueReceivables!.Value, d.Payables!.Value, d.OverduePayables!.Value));
        Assert.Equal(520m, d.StockValue);
        Assert.All(d.Checks, c => Assert.True(c.Ok, $"{c.Name}: {c.Report} vs {c.Source}"));
        Assert.Equal(61, d.Trend.Count); Assert.False(d.MonthlyTrend);
        Assert.Equal(600m, d.Trend.Single(x => x.Date == today.AddDays(-40)).NetSales);
        Assert.Equal(750m, d.Trend.Single(x => x.Date == today).NetSales); // the reversed sale nets to nothing
        Assert.Equal(["Walk-in", "Kissy Clinic"], d.TopCustomers.Select(x => x.Name));
        Assert.Equal((1350m, 5m), (d.TopProducts.Single().Amount, d.TopProducts.Single().Quantity));
        Assert.Equal("FREETOWN", d.Branches.Single().Branch);
        Assert.True((await reports.Dashboard(today.AddYears(-1), today)).MonthlyTrend);

        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read" } };
        var limited = await reports.Dashboard(today.AddDays(-60), today);
        Assert.Equal((false, 1350m, (decimal?)null, (decimal?)null), (limited.Financial, limited.NetSales, limited.GrossProfit, limited.Cash));
        Assert.Empty(limited.Checks); Assert.Null(limited.Trend[0].GrossProfit);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reports.Ageing(today, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reports.Register(new(today.AddDays(-60), today, 1100)));
    }

    [Fact] public async Task SalesAnalysisDrillsFromPeriodToCustomerToDocuments()
    {
        var byCustomer = await reports.Sales(today.AddDays(-60), today, SalesGrouping.Customer);
        Assert.True(byCustomer.Check!.Ok);
        Assert.Equal([("Walk-in", 750m, 300m), ("Kissy Clinic", 600m, 200m)], byCustomer.Rows.Select(x => (x.Label, x.NetSales, x.Cost!.Value)));
        var byDay = await reports.Sales(today.AddDays(-60), today, SalesGrouping.Day);
        Assert.Equal(2, byDay.Rows.Count); Assert.Equal(1350m, byDay.Rows.Sum(x => x.NetSales));
        var byProduct = await reports.Sales(today.AddDays(-60), today, SalesGrouping.Product);
        var row = byProduct.Rows.Single();
        Assert.Equal((1350m, 5m, 500m, 850m, 2, 0m), (row.NetSales, row.Quantity, row.Cost!.Value, row.GrossProfit!.Value, row.Documents, byProduct.Adjustments));
        Assert.True(byProduct.Check!.Ok);

        var drill = await reports.Register(new(today.AddDays(-60), today, Party: "kissy clinic"));
        var invoice = drill.Rows.Items.Single();
        Assert.Equal((DocumentKind.Sale, 600m, "Credit"), (invoice.Kind, invoice.Total, invoice.Settlement));
        var receivables = await reports.Register(new(today.AddDays(-60), today, 1100));
        Assert.Equal(("Trade receivables", 0m, 600m, 0m, 600m), (receivables.AccountName, receivables.Opening, receivables.Debit, receivables.Credit, receivables.Closing));
        Assert.Equal(600m, receivables.Rows.Items.Single().Debit);
        var cash = await reports.Register(new(today, today, 1000));
        Assert.Equal((3, 1000m, 250m, 750m), (cash.Rows.Total, cash.Debit, cash.Credit, cash.Closing)); // sale, keyed-twice sale and its reversal
        Assert.Contains(cash.Rows.Items, x => x.Reversed);
    }

    [Fact] public async Task StockValuationAndMovementsReconcileAndAgeingBucketsBills()
    {
        var stock = await reports.Stock(today);
        Assert.True(stock.Check!.Ok);
        Assert.Equal([("WID-1", 5m, 500m, 100m), ("PAD-1", 4m, 20m, 5m)], stock.Rows.Select(x => (x.Sku, x.Quantity, x.Value, x.AverageCost)));
        Assert.Empty((await reports.Stock(today.AddDays(-50))).Rows);

        var moves = await reports.Movements(new(widget, today.AddDays(-41), today));
        Assert.Equal((10m, 1000m, 1m, 6m, 5m, 500m), (moves.Opening, moves.OpeningValue, moves.In, moves.Out, moves.Closing, moves.ClosingValue));
        Assert.Equal(4, moves.Rows.Total);
        Assert.Equal(5m, moves.Rows.Items[0].Balance); // newest first, with the running balance

        var ageing = await reports.Ageing(today, true);
        var kissy = ageing.Rows.Single();
        Assert.Equal(("Kissy Clinic", 0m, 0m, 600m, 600m), (kissy.Party, kissy.Current, kissy.Days30, kissy.Days60, kissy.Total));
        Assert.Equal(35, kissy.Bills.Single().OverdueDays);
        Assert.True(ageing.Check.Ok);
        var payables = await reports.Ageing(today, false);
        Assert.Equal((1020m, 1020m), (payables.Days60, payables.Total)); Assert.True(payables.Check.Ok);
    }

    [Fact] public async Task LargeRegistersArePagedOnTheServerAndChangesAreAnnounced()
    {
        var announcements = 0; void Count() => Interlocked.Increment(ref announcements);
        ChangeNotifier.Changed += Count;
        try
        {
            for (var i = 0; i < 23; i++) await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(unsold, 0.1m, 20)], PaymentMethod.Cash));
            Assert.True(announcements >= 23);
        }
        finally { ChangeNotifier.Changed -= Count; }
        var first = await reports.Register(new(today.AddDays(-60), today, PageSize: 10));
        Assert.Equal((28, 3, 10), (first.Rows.Total, first.Rows.Pages, first.Rows.Items.Count));
        var last = await reports.Register(new(today.AddDays(-60), today, Page: 3, PageSize: 10));
        Assert.Equal(8, last.Rows.Items.Count);
        Assert.Equal(DocumentKind.Receipt, last.Rows.Items[^1].Kind); // newest first: the receipt is on the last page
        Assert.Empty(first.Rows.Items.Select(x => x.DocumentId).Intersect(last.Rows.Items.Select(x => x.DocumentId)));
        var sales = await reports.Register(new(today, today, Kind: DocumentKind.Sale, PageSize: 10));
        Assert.Equal(25, sales.Rows.Total);
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
