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

/// <summary>Drives the Imports &amp; landed cost screen against the real services and database.</summary>
public sealed class PurchasingScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-purchasing-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private Factory factory = null!; private BusinessService business = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options);
        context.Services.AddSingleton<IPurchasingService>(new PurchasingService(factory, session, options));
        context.Services.AddSingleton<IFinanceService>(new FinanceService(factory, session, options));
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private IRenderedComponent<PurchasingWorkspace> Render() => context.Render<PurchasingWorkspace>(p => p.Add(x => x.Actor, session.Actor));
    private static void Click(IRenderedComponent<PurchasingWorkspace> screen, string text) => screen.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

    [Fact] public async Task AccountantAddsRateReceivesInUsdPaysTheBillAndPostsLandedCost()
    {
        var screen = Render();
        screen.Find("input[placeholder='USD']").Change("USD");
        screen.Find("input[step='0.0001']").Change("23");
        screen.Find("input[placeholder^='Bank of Sierra Leone']").Change("BSL mid-rate");
        Click(screen, "Add rate");
        screen.WaitForAssertion(() => Assert.Contains("Exchange rate saved", screen.Markup));

        var paper = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Global Supplies", today, [new(paper, 10, 10)], null, Currency: "USD"));
        screen = Render();
        Assert.Contains("USD 100.00", screen.Markup);
        screen.FindAll("button").First(b => b.TextContent == "Pay…").Click();
        var pay = screen.FindAll("input[step='0.01']");
        pay[1].Change("2400"); // NLe actually paid
        screen.Find("input[maxlength='100']").Change("TT-55");
        Assert.Contains("exchange loss NLe 100.00", screen.Markup);
        Click(screen, "Record payment");
        screen.WaitForAssertion(() => Assert.Contains("No foreign-currency amounts are owed", screen.Markup));

        screen.Find("input[list='lc-parties']").Change("Freetown Clearing");
        screen.FindAll("input[step='0.01']")[0].Change("200");
        screen.Find("input[type=checkbox]").Change(true);
        Click(screen, "Show allocation");
        screen.WaitForAssertion(() => Assert.Contains("Total NLe 200.00", screen.Markup));
        Click(screen, "Post landed cost");
        screen.WaitForAssertion(() => Assert.Contains("Landed cost posted", screen.Markup));
        Assert.Equal(2500m, (await business.Read(session.Actor)).Stock.Single().Value);
    }

    [Fact] public void ReadOnlyAccountantSeesRatesButNoForms()
    {
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read", "accounting.read" } };
        var screen = Render();
        Assert.DoesNotContain("Add rate", screen.Markup); Assert.DoesNotContain("Landed cost", screen.Markup);
        Assert.Contains("Foreign-currency bills", screen.Markup);
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
