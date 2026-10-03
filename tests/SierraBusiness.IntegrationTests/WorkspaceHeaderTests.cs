using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Infrastructure;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Renders the workspace shell: page heading actions, company name, signed-in user and version.</summary>
public sealed class WorkspaceHeaderTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-shell-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private readonly Session session = new();
    public async Task InitializeAsync()
    {
        var factory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        context.Services.AddSingleton<IBusinessService>(new BusinessService(factory, options));
        context.Services.AddSingleton<IPosService>(new PosService(factory, session, options));
        context.Services.AddSingleton<IInventoryService>(new InventoryService(factory, session, options));
        context.Services.AddSingleton<IInventoryManagement>(new InventoryManagement(factory, session, options));
        context.Services.AddSingleton<IPartyService>(new PartyService(factory, session, options));
        context.Services.AddSingleton<IPurchasingService>(new PurchasingService(factory, session, options));
        context.Services.AddSingleton<IFinanceService>(new FinanceService(factory, session, options));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    private IRenderedComponent<Workspace> Render(Actor actor) => context.Render<Workspace>(p => p
        .Add(x => x.Actor, actor).Add(x => x.CompanyName, "Index Limited").Add(x => x.UserName, "owner@example.sl").Add(x => x.Version, "0.12.1"));
    private static bool HasNewSaleButton(IRenderedComponent<Workspace> screen) => screen.FindAll(".heading button").Any(b => b.TextContent.Contains("New sale"));
    private static void Open(IRenderedComponent<Workspace> screen, string text) => screen.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

    [Fact] public void NewSaleShortcutAppearsOnlyOnOverview()
    {
        var screen = Render(session.Actor);
        screen.WaitForAssertion(() => Assert.DoesNotContain("Loading your branch workspace", screen.Markup));
        Assert.True(HasNewSaleButton(screen));
        foreach (var (area, page) in new[] { ("Inventory", "Stock summary"), ("Sales & purchases", "New sale"), ("Sales & purchases", "Receive stock"), ("Finance", "Accounting") })
        {
            screen.FindAll("button.nav").First(b => b.QuerySelector(".area-name")!.TextContent == area).Click();
            if (screen.FindAll(".section-link.active").All(x => x.TextContent.Trim() != page)) Open(screen, page);
            screen.WaitForAssertion(() => Assert.Equal(page, screen.Find("h1").TextContent.Trim()));
            Assert.False(HasNewSaleButton(screen), $"{page} should not show the New sale shortcut");
        }
    }

    [Fact] public void ShellShowsCompanyUserAndVersionNotInternalIds()
    {
        var screen = Render(session.Actor);
        Assert.Contains("Index Limited", screen.Find(".company-context strong").TextContent);
        Assert.Contains("Signed in as owner@example.sl", screen.Find(".workspace-header").TextContent);
        Assert.DoesNotContain(session.Actor.Id, screen.Find(".workspace-header").TextContent);
        Assert.Contains("Version 0.12.1", screen.Find("footer").TextContent);
        Assert.DoesNotContain("Development build", screen.Markup);
    }

    [Fact] public void UsersWhoCannotSellDoNotSeeTheShortcut()
    {
        var viewer = new Actor("viewer-7f3a", "FREETOWN", new HashSet<string> { "business.read" });
        var screen = Render(viewer);
        screen.WaitForAssertion(() => Assert.DoesNotContain("Loading your branch workspace", screen.Markup));
        Assert.False(HasNewSaleButton(screen));
    }

    private sealed class Session : IActorSession { public Actor Actor = new("3c1e2b7d-owner-id", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
