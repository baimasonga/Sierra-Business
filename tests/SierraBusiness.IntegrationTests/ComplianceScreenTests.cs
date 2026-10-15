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

/// <summary>Drives the Company &amp; tax screen: the owner records the TIN and tax registration, then the printed sale carries the letterhead, tax-invoice title, exempt marking, amount in words and pending fiscal status.</summary>
public sealed class ComplianceScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-compliance-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private Factory factory = null!; private BusinessService business = null!;
    private readonly Session session = new();
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid widget, drug;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions { TaxRate = .15m });
        business = new(factory, options);
        context.Services.AddSingleton<IComplianceService>(new ComplianceService(factory, session, options, new NoFiscalDevice()));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        widget = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 200, TrackingMode.Quantity, MinimumPrice: 1));
        drug = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "AMX-500", "Amoxicillin 500 mg", "Medicines", "pack", 50, TrackingMode.Quantity, MinimumPrice: 1));
        foreach (var p in new[] { widget, drug }) await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(p, 20, 10)], null));
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private static void Click<T>(IRenderedComponent<T> screen, string text) where T : Microsoft.AspNetCore.Components.IComponent => screen.FindAll("button").First(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal)).Click();
    private static void Input<T>(IRenderedComponent<T> screen, string label, string value) where T : Microsoft.AspNetCore.Components.IComponent =>
        screen.FindAll("label").First(l => l.TextContent.Trim().StartsWith(label, StringComparison.Ordinal)).QuerySelector("input")!.Change(value);

    [Fact] public async Task OwnerRecordsTaxDetailsAndTheInvoicePrintsThem()
    {
        var screen = context.Render<ComplianceWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        screen.WaitForAssertion(() => Assert.Contains("Company details", screen.Markup));
        Input(screen, "Legal name", "Index Medical Supplies Ltd");
        screen.FindAll(".identifier-row")[0].QuerySelectorAll("input")[1].Change("1000123456");
        screen.FindAll("label").First(l => l.TextContent.Contains("Registered for tax")).QuerySelector("input")!.Change(true);
        screen.FindAll("label").First(l => l.TextContent.Contains("Register sales and service invoices")).QuerySelector("input")!.Change(true);
        Click(screen, "Save company details");
        screen.WaitForAssertion(() => Assert.Contains("Company details saved", screen.Markup));

        Click(screen, "Tax categories");
        screen.WaitForAssertion(() => Assert.Contains("AMX-500 · Amoxicillin 500 mg", screen.Markup));
        Click(screen, "AMX-500");
        screen.FindAll("label").First(l => l.TextContent.StartsWith("Reason")).QuerySelector("input")!.Change("GST Act: medicines exempt");
        Click(screen, "Schedule change");
        screen.WaitForAssertion(() => Assert.Contains("AMX-500 is exempt from", screen.Markup));

        var id = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Connaught Hospital", today, [new(widget, 1, 200), new(drug, 2, 50)], PaymentMethod.Cash));
        var snapshot = await business.Read(session.Actor);
        var document = snapshot.Documents.Single(x => x.Id == id);
        Assert.Equal(330m, document.Total); // 15% on the widget only
        var print = context.Render<TradeDocumentPreview>(p => p.Add(x => x.Document, document).Add(x => x.Products, snapshot.Products).Add(x => x.Branch, "FREETOWN"));
        print.WaitForAssertion(() => Assert.Contains("Index Medical Supplies Ltd", print.Find(".letterhead").TextContent));
        Assert.Contains("Tax invoice", print.Find(".lh-doc").TextContent);
        Assert.Contains("TIN: 1000123456", print.Find(".lh-ids").TextContent);
        Assert.Contains("(exempt)", print.Markup);
        Assert.Contains("Three hundred and thirty Leones only", print.Find(".doc-footer").TextContent);
        print.WaitForAssertion(() => Assert.Contains("Fiscal registration pending", print.Find(".doc-footer").TextContent));

        Click(screen, "Fiscal");
        screen.WaitForAssertion(() => Assert.Contains("invoices wait here as pending", screen.Markup));
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
