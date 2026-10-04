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

/// <summary>Drives the Customers & suppliers statement and the Variants & bundles screen.</summary>
public sealed class CustomerMerchandiseScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-crm-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private Factory factory = null!; private BusinessService business = null!;
    private readonly Session session = new();
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid widget;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options);
        context.Services.AddSingleton<IPartyService>(new PartyService(factory, session, options));
        context.Services.AddSingleton<IMerchandiseService>(new MerchandiseService(factory, session, options));
        context.Services.AddSingleton<IBusinessService>(business);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        widget = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: 50));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(widget, 20, 40)], null));
        await TestParties.Register(factory, "Kissy Clinic");
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Kissy Clinic", today, [new(widget, 3, 100)], null));
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private static void Click<T>(IRenderedComponent<T> screen, string text) where T : Microsoft.AspNetCore.Components.IComponent => screen.FindAll("button").First(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal)).Click();
    private static AngleSharp.Dom.IElement Field<T>(IRenderedComponent<T> screen, string label) where T : Microsoft.AspNetCore.Components.IComponent =>
        screen.FindAll("label").First(l => l.TextContent.Trim().StartsWith(label, StringComparison.Ordinal)).QuerySelector("input,select")!;

    [Fact] public void StatementPrintsAndVariantsAreCreatedFromTheScreens()
    {
        var parties = context.Render<PartyWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        parties.WaitForAssertion(() => Assert.Contains("Kissy Clinic", parties.Markup));
        Click(parties, "Statement");
        parties.WaitForAssertion(() => Assert.Contains("Statement of account", parties.Find("#statement-print").TextContent));
        Assert.Contains("300.00", parties.Find("#statement-print tfoot").TextContent);

        var screen = context.Render<MerchandiseWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        screen.WaitForAssertion(() => Assert.Contains("No variant groups yet", screen.Markup));
        Field(screen, "Base SKU").Change("TBL"); Field(screen, "Product name").Change("Meeting table"); Field(screen, "Selling price").Change("3000"); Field(screen, "Minimum price").Change("2500");
        screen.FindAll("label").First(l => l.TextContent.Trim() == "Values" || l.TextContent.Trim().StartsWith("Values")).QuerySelector("input")!.Change("Oak, Walnut, White");
        screen.WaitForAssertion(() => Assert.Contains("That makes 3 products", screen.Markup));
        Click(screen, "Create variants");
        screen.WaitForAssertion(() => Assert.Contains("TBL-WAL", screen.Markup));
        Click(screen, "Bundle offers");
        screen.WaitForAssertion(() => Assert.Contains("No bundle offers yet", screen.Markup));
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
