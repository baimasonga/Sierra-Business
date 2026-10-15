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

/// <summary>Drives the After-sales screens against the real services and database.</summary>
public sealed class EquipmentScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-equipment-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private Factory factory = null!; private BusinessService business = null!; private EquipmentService equipment = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid xray, sale;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options); equipment = new(factory, session, options);
        context.Services.AddSingleton<IEquipmentService>(equipment);
        context.Services.AddSingleton<IBusinessService>(business);
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        xray = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "XRAY-M1", "Mobile X-ray unit", "Medical", "unit", 250000, TrackingMode.Serial, MinimumPrice: 1));
        await equipment.SaveTerms(new(Guid.NewGuid(), new(xray, 0, 24, WarrantyStart.Installation, 12, true, ["Power and earthing checked", "Operator training"], "Parts and labour")));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(xray, 1, 180000, 1, "XR-1001")], null));
        sale = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Connaught Hospital", today, [new(xray, 1, 300000, 1, "XR-1001")], PaymentMethod.BankTransfer, "TT-1", "Rokel Bank", today));
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private static void Click<T>(IRenderedComponent<T> screen, string text) where T : Microsoft.AspNetCore.Components.IComponent => screen.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

    [Fact] public void EngineerSchedulesAndCompletesAnInstallationAndPrintsTheCertificate()
    {
        var screen = context.Render<InstallationWorkspace>(p => p.Add(x => x.Actor, session.Actor).Add(x => x.CompanyName, "Index Limited"));
        screen.WaitForAssertion(() => Assert.Contains("Connaught Hospital", screen.Markup));
        screen.Find("select").Change(sale.ToString());
        screen.Find("input[placeholder^='Department']").Change("Radiology, Connaught Hospital");
        screen.Find("input[maxlength='100']").Change("Bangz");
        Click(screen, "Schedule installation");
        screen.WaitForAssertion(() => Assert.Contains("Installation job sheet", screen.Markup));
        Assert.Contains("Index Limited", screen.Find("#installation-print").TextContent);

        screen.Find("input[maxlength='120']").Change("Dr Kamara");
        Click(screen, "Record completion");
        screen.WaitForAssertion(() => Assert.Contains("Complete every commissioning check", screen.Markup));
        for (var i = 0; i < screen.FindAll("label.check input[type=checkbox]").Count; i++) screen.FindAll("label.check input[type=checkbox]")[i].Change(true);
        screen.Find("input[maxlength='120']").Change("Dr Kamara");
        Click(screen, "Record completion");
        screen.WaitForAssertion(() => Assert.Contains("Installation and commissioning certificate", screen.Markup));
        Assert.Contains("☑ Operator training", screen.Find("#installation-print").TextContent);
        Assert.Contains("Dr Kamara", screen.Find("#installation-print").TextContent);
    }

    [Fact] public async Task LookupShowsCoverageHistoryAndSavesSpecifications()
    {
        var job = await equipment.ScheduleInstallation(new(Guid.NewGuid(), sale, "Radiology", today, "Bangz", [new(xray, "XR-1001")]));
        await equipment.CompleteInstallation(new(Guid.NewGuid(), job, today, ["Power and earthing checked", "Operator training"], "Dr Kamara"));
        var screen = context.Render<EquipmentLookupWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        screen.Find("input[maxlength='100']").Change("xr-10");
        screen.Find("form").Submit();
        screen.WaitForAssertion(() => Assert.Contains("Covered by customer warranty", screen.Markup));
        Assert.Contains("WITH CUSTOMER, INSTALLED", screen.Markup);
        Assert.Contains("Installed and commissioned; accepted by Dr Kamara", screen.Markup);
        Assert.Contains("Received from MedEquip Ltd", screen.Markup);

        screen.Find("input[placeholder^='Processor']").Change("Tube");
        screen.Find("input[maxlength='160']").Change("Toshiba E7239X");
        Click(screen, "Save unit record");
        screen.WaitForAssertion(() => Assert.Contains("Unit record saved", screen.Markup));
        Assert.Equal("Toshiba E7239X", (await equipment.Unit(xray, "XR-1001"))!.Specifications.Single().Value);
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
