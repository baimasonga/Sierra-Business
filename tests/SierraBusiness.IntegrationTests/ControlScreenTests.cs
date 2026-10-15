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

/// <summary>Drives the Controls and Approvals screens: switch on the recommended controls, a held sale is approved by a manager and posted by the cashier, and the exception appears for review.</summary>
public sealed class ControlScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-controls-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private Factory factory = null!; private BusinessService business = null!;
    private readonly Session session = new();
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid widget;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options);
        context.Services.AddSingleton<IControlService>(new ControlService(factory, session, options));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        widget = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 200, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(widget, 20, 100)], null));
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private static void Click<T>(IRenderedComponent<T> screen, string text) where T : Microsoft.AspNetCore.Components.IComponent => screen.FindAll("button").First(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal)).Click();

    [Fact] public async Task OwnerSwitchesOnControlsApprovesAHeldSaleAndReviewsIt()
    {
        var controls = context.Render<ControlsWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        controls.WaitForAssertion(() => Assert.Contains("Off", controls.Find(".status-badge").TextContent));
        Click(controls, "Use recommended settings");
        Click(controls, "Save controls");
        controls.WaitForAssertion(() => Assert.Contains("Controls saved", controls.Markup));
        Assert.Contains("On · version 1", controls.Markup);

        var cashier = new Actor("cashier", "FREETOWN", new HashSet<string> { "business.read", "sales.create" });
        var sale = new TradeCommand(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(widget, 1, 150)], PaymentMethod.Cash);
        await Assert.ThrowsAsync<ApprovalRequiredException>(() => business.Post(cashier, sale));

        var approvals = context.Render<ApprovalsWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        approvals.WaitForAssertion(() => Assert.Contains("25% below its list price", approvals.Markup));
        Assert.Contains("Waiting", approvals.Find(".status-badge").TextContent);
        Click(approvals, "Approve");
        approvals.WaitForAssertion(() => Assert.Contains("approved. cashier can now post it", approvals.Markup));

        session.Actor = cashier;
        var mine = context.Render<ApprovalsWorkspace>(p => p.Add(x => x.Actor, cashier));
        mine.WaitForAssertion(() => Assert.Contains("Post now", mine.Markup));
        Click(mine, "Post now");
        mine.WaitForAssertion(() => Assert.Contains("posted", mine.Find(".notice.success").TextContent));
        Assert.Contains("Posted", mine.Find(".status-badge").TextContent);

        session.Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet());
        var review = context.Render<ControlsWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        review.WaitForAssertion(() => Assert.Contains("k-Discount", review.Markup));
        Assert.Contains("cashier", review.Find(".report-table").TextContent);
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
