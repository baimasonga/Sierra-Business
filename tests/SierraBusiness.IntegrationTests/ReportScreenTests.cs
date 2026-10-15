using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Drives the dashboard and reports: drill from a figure to its documents, and live refresh when someone posts.</summary>
public sealed class ReportScreenTests : IAsyncLifetime
{
    [Fact] public async Task ReportPreferencesRestoreForTheUserAndNeverStorePartyNamesOrAmounts()
    {
        var key = session.Actor.Id + "." + session.Actor.Branch + ".Reports";
        var json = System.Text.Json.JsonSerializer.Serialize(new { From = today, To = today, View = "Stock ageing & slow-moving", Grouping = SalesGrouping.Day, Account = 0, Kind = "" });
        context.JSInterop.Setup<string>("sierraPreferenceRead", i => i.Arguments[0]!.ToString() == key).SetResult(json);
        var screen = context.Render<ReportsWorkspace>(p => p.Add(x => x.Actor, session.Actor).Add(x => x.StartView, "Reports"));
        await Eventually(screen, () => screen.Markup.Contains("Age buckets show"), "saved report restored");
        await screen.InvokeAsync(() => screen.FindAll("button").Single(b => b.TextContent == "Save report settings").Click());
        await Eventually(screen, () => screen.Markup.Contains("Report settings saved"), "settings saved");
        var saved = context.JSInterop.Invocations.Single(i => i.Identifier == "sierraPreferenceWrite");
        Assert.Equal(key, saved.Arguments[0]);
        Assert.DoesNotContain("Connaught", saved.Arguments[1]!.ToString());
        Assert.DoesNotContain("600", saved.Arguments[1]!.ToString());
        await screen.InvokeAsync(() => screen.FindAll("button").Single(b => b.TextContent == "Reset saved settings").Click());
        await Eventually(screen, () => screen.Markup.Contains("Saved report settings cleared"), "settings reset");
    }
    [Fact] public async Task StockAgeingTabDrillsToMovements()
    {
        var screen = context.Render<ReportsWorkspace>(p => p.Add(x => x.Actor, session.Actor).Add(x => x.StartView, "Reports"));
        await Eventually(screen, () => screen.Markup.Contains("Sales by"), "report loading");
        await screen.InvokeAsync(() => screen.FindAll("button").Single(b => b.TextContent == "Stock ageing & slow-moving").Click());
        await Eventually(screen, () => screen.Markup.Contains("Age buckets show") && screen.Markup.Contains("WID-1"), "stock ageing");
        await screen.InvokeAsync(() => screen.FindAll("button").Single(b => b.TextContent.Contains("WID-1")).Click());
        await Eventually(screen, () => screen.Markup.Contains("Serial / batch") && screen.Markup.Contains("Connaught Hospital"), "movement drill-down");
    }
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-report-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private Factory factory = null!; private BusinessService business = null!;
    private readonly Session session = new(); private readonly TestFeed feed = new();
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.Now);
    private Guid widget;
    public async Task InitializeAsync()
    {
        context.JSInterop.Setup<string?>(i => i.Identifier == "sierraPreferenceRead", isCatchAllHandler: true).SetResult(null);
        context.JSInterop.SetupVoid("sierraPreferenceWrite", _ => true).SetVoidResult();
        context.JSInterop.SetupVoid("sierraPreferenceRemove", _ => true).SetVoidResult();
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options);
        context.Services.AddSingleton<IReportService>(new ReportService(factory, session, options));
        context.Services.AddSingleton<IChangeFeed>(feed); // a private feed: other tests' postings must not keep this dashboard busy
        widget = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 250, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(widget, 10, 100)], null));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Connaught Hospital", today, [new(widget, 2, 300)], PaymentMethod.Cash));
    }
    public async Task DisposeAsync()
    {
        context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; ; attempt++) // a background refresh may still be closing its connection
            try { File.Delete(path); return; }
            catch (IOException) when (attempt < 20) { await Task.Delay(250); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); }
    }

    /// <summary>Background refreshes render from the change-feed thread. The markup is read on the renderer's dispatcher, so the test never reads
    /// the render tree while a background render is writing it (that race stalled the Windows test host before 0.26.0).</summary>
    private static async Task Eventually(IRenderedComponent<ReportsWorkspace> screen, Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            var met = false;
            await screen.InvokeAsync(() => { try { met = condition(); } catch (Bunit.ElementNotFoundException) { } });
            if (met) return;
            if (DateTime.UtcNow > until)
            {
                var markup = ""; await screen.InvokeAsync(() => markup = screen.Markup);
                throw new Xunit.Sdk.XunitException($"Timed out waiting for {what}. Markup: {markup[..Math.Min(3000, markup.Length)]}");
            }
            await Task.Delay(250);
        }
    }

    [Fact] public async Task DashboardDrillsToDocumentsAndRefreshesWhenSomeonePosts()
    {
        Guid? opened = null;
        var screen = context.Render<ReportsWorkspace>(p => p.Add(x => x.Actor, session.Actor).Add(x => x.OpenDocument, (Guid id) => { opened = id; }));
        await Eventually(screen, () => screen.Find(".kpi").TextContent.Contains("NLe 600.00"), "the first sale on the dashboard");
        Assert.Contains("Reconciled", screen.Find(".checks").TextContent);
        Assert.DoesNotContain("Difference", screen.Find(".checks").TextContent);

        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Kissy Clinic", today, [new(widget, 1, 400)], PaymentMethod.Cash));
        feed.Raise(); // what the database does after every posting
        await Eventually(screen, () => screen.Find(".kpi").TextContent.Contains("NLe 1,000.00"), "the live refresh after a new sale");

        screen.Find(".kpi").Click(); // net sales → sales by day
        screen.WaitForAssertion(() => Assert.Contains("Sales by day", screen.Markup));
        Assert.Contains("✓ Reconciled", screen.Find(".check-line").TextContent);
        screen.FindAll(".view-toggle button").First(b => b.TextContent == "Customer").Click();
        screen.WaitForAssertion(() => Assert.Contains("Kissy Clinic", screen.Find(".report-table").TextContent));
        screen.FindAll(".report-table tbody tr").First(r => r.TextContent.Contains("Kissy Clinic")).Click(); // customer → day book filtered
        screen.WaitForAssertion(() => Assert.Single(screen.FindAll(".report-table tbody tr")));
        Assert.Contains("Dashboard", screen.Find(".report-trail").TextContent);
        screen.Find(".report-table tbody tr").Click();
        Assert.NotNull(opened);
        screen.FindAll(".report-trail button")[0].Click(); // back to the dashboard
        screen.WaitForAssertion(() => Assert.NotEmpty(screen.FindAll(".kpi")));
    }

    [Fact] public void StockValuationDrillsToMovementsAndViewersSeeNoMoney()
    {
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read" } };
        var screen = context.Render<ReportsWorkspace>(p => p.Add(x => x.Actor, session.Actor).Add(x => x.StartView, "Reports"));
        screen.WaitForAssertion(() => Assert.Contains("Sales by day", screen.Markup));
        Assert.DoesNotContain("Receivables ageing", screen.Markup);
        Assert.DoesNotContain("Gross profit", screen.Markup);
        screen.FindAll(".report-tabs button").First(b => b.TextContent == "Stock valuation").Click();
        screen.WaitForAssertion(() => Assert.Contains("WID-1 · Widget", screen.Markup));
        screen.Find(".report-table tbody tr").Click();
        screen.WaitForAssertion(() => Assert.Contains("movements", screen.Markup));
        Assert.Equal(2, screen.FindAll(".report-table tbody tr").Count);
    }

    private sealed class TestFeed : IChangeFeed { public event Action? Changed; public void Raise() => Changed?.Invoke(); }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
