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

/// <summary>Drives the Service jobs screen against the real services: log against a sold unit, issue parts, claim warranty, complete, close; then a maintenance plan.</summary>
public sealed class ServiceScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-service-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private Factory factory = null!; private BusinessService business = null!;
    private readonly Session session = new();
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid analyser, fuses;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options);
        var equipment = new EquipmentService(factory, session, options);
        context.Services.AddSingleton<IServiceDesk>(new ServiceDesk(factory, session, options));
        context.Services.AddSingleton<IBusinessService>(business);
        context.Services.AddSingleton<IDocumentLibrary>(new DocumentLibrary(factory, session, options, new DatabaseFileStore()));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        analyser = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "HEM-5", "Haematology analyser", "Medical", "unit", 150000, TrackingMode.Serial, MinimumPrice: 100000));
        fuses = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "FUS-2A", "Fuse 2A", "Spares", "piece", 500, TrackingMode.Quantity, MinimumPrice: 400));
        await equipment.SaveTerms(new(Guid.NewGuid(), new(analyser, 0, 12, WarrantyStart.Sale, 0, false, [], "Parts and labour")));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(analyser, 1, 100000, 1, "HX-77"), new(fuses, 10, 10)], null));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Connaught Hospital", today, [new(analyser, 1, 150000, 1, "HX-77")], PaymentMethod.Cash));
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private static void Click<T>(IRenderedComponent<T> screen, string text) where T : Microsoft.AspNetCore.Components.IComponent => screen.FindAll("button").First(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal)).Click();

    [Fact] public void EngineerLogsRepairsUnderWarrantyAndClosesAJob()
    {
        var screen = context.Render<ServiceWorkspace>(p => p.Add(x => x.Actor, session.Actor).Add(x => x.CompanyName, "Index Limited"));
        screen.WaitForAssertion(() => Assert.Contains("Log a service job", screen.Markup));
        screen.Find("select").Change($"{analyser}|HX-77");
        Assert.Contains("Under customer warranty", screen.Find(".warranty-hint").TextContent);
        Assert.Equal("Connaught Hospital", screen.FindAll("input[maxlength='160']")[0].GetAttribute("value"));
        screen.Find("textarea").Change("Error E12 on start-up");
        screen.FindAll("input[maxlength='100']").First(x => x.GetAttribute("placeholder") == "Assign now (optional)").Change("Musa");
        Click(screen, "Log service job");
        screen.WaitForAssertion(() => Assert.Contains("Service job logged", screen.Markup));
        Assert.Contains("s-Assigned", screen.Markup);

        screen.Find("select[aria-label='Part to issue']").Change(fuses.ToString());
        screen.Find("input[aria-label='Part quantity']").Change("2");
        Click(screen, "Issue part from stock");
        screen.WaitForAssertion(() => Assert.Contains("Part issued", screen.Markup));
        Assert.Contains("s-InProgress", screen.Markup);

        Click(screen, "Record decision"); // warranty is proposed for a covered unit
        screen.WaitForAssertion(() => Assert.Contains("Billing decision recorded", screen.Markup));
        Assert.Contains("(parts and labour)", screen.Markup);
        Assert.Contains("Covered", screen.Find("table").TextContent); // covered parts are not charged

        screen.FindAll("input[maxlength='120']").Last().Change("Lab manager");
        screen.FindAll("textarea").Last().Change("Replaced two fuses; tested OK");
        Click(screen, "Record completion and acceptance");
        screen.WaitForAssertion(() => Assert.Contains("Work completed and accepted", screen.Markup));
        Click(screen, "Close job (no charge)");
        screen.WaitForAssertion(() => Assert.Contains("closed at no charge", screen.Markup));
        Assert.Contains("s-Closed", screen.Markup);
        var report = screen.Find("#service-print").TextContent;
        Assert.Contains("Index Limited", report); Assert.Contains("Lab manager", report); Assert.Contains("No charge", report);
        Assert.Contains("Signed reports, photos and evidence", screen.Markup);
    }

    [Fact] public void PlansCreateAndGenerateVisits()
    {
        var screen = context.Render<ServiceWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        screen.WaitForAssertion(() => Assert.Contains("Maintenance plans", screen.Markup));
        Click(screen, "Maintenance plans");
        screen.FindAll("select").First().Change($"{analyser}|HX-77");
        screen.Find("input[type=date]").Change(today.ToString("yyyy-MM-dd")); // through
        screen.FindAll("input[type=date]")[1].Change(today.ToString("yyyy-MM-dd")); // first visit
        screen.Find("textarea").Change("Clean optics\nCalibrate");
        Click(screen, "Start plan");
        screen.WaitForAssertion(() => Assert.Contains("Maintenance plan started", screen.Markup));
        Click(screen, "Generate due visits");
        screen.WaitForAssertion(() => Assert.Contains("1 planned visit added", screen.Markup));
        Click(screen, "Service jobs");
        Assert.Contains("preventive", screen.Find(".service-row").TextContent);
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
