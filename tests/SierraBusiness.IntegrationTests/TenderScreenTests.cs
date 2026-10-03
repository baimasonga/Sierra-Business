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

/// <summary>Drives the Tenders and Document library screens against the real services.</summary>
public sealed class TenderScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-tender-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private Factory factory = null!; private BusinessService business = null!; private DocumentLibrary library = null!;
    private readonly Session session = new();
    private Guid analyser;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options); library = new(factory, session, options, new DatabaseFileStore());
        context.Services.AddSingleton<ITenderService>(new TenderService(factory, session, options));
        context.Services.AddSingleton<IBusinessService>(business);
        context.Services.AddSingleton<IDocumentLibrary>(library);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        analyser = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "HEM-5", "Haematology analyser", "Medical", "unit", 150000, TrackingMode.Serial, MinimumPrice: 100000));
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private static void Click<T>(IRenderedComponent<T> screen, string text) where T : Microsoft.AspNetCore.Components.IComponent => screen.FindAll("button").First(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal)).Click();

    [Fact] public void OfficerRegistersCostsApprovesSubmitsAndWinsATender()
    {
        var screen = context.Render<TenderWorkspace>(p => p.Add(x => x.Actor, session.Actor).Add(x => x.CompanyName, "Index Limited"));
        screen.Find("input[placeholder='As printed on the invitation']").Change("NHSL/RFQ/2026/31");
        screen.FindAll("input[maxlength='160']")[0].Change("National Hospital Services");
        screen.Find("input[maxlength='200']").Change("Haematology analysers");
        Click(screen, "Register tender");
        screen.WaitForAssertion(() => Assert.Contains("Tender registered", screen.Markup));
        Assert.Contains("Closes in 14 days", screen.Markup);

        screen.Find("select[aria-label='Product for line 1']").Change(analyser.ToString());
        screen.Find("input[aria-label='Quantity for line 1']").Change("2");
        screen.Find("input[aria-label='Supplier cost for line 1']").Change("100000");
        screen.Find("input[aria-label='Margin percent for line 1']").Change("30");
        screen.FindAll("button").First(b => b.GetAttribute("title") == "Use the suggested price").Click(); // 130,000
        Assert.Contains("margin", screen.Find(".bid-summary").TextContent);
        Assert.Contains("23.1%", screen.Find(".bid-summary").TextContent);
        Click(screen, "Save bid version");
        screen.WaitForAssertion(() => Assert.Contains("needs price approval", screen.Markup));
        Assert.Contains("Price schedule · NHSL/RFQ/2026/31", screen.Find("#tender-print").TextContent);
        Assert.Contains("Index Limited", screen.Find("#tender-print").TextContent);

        Click(screen, "Approve price");
        screen.WaitForAssertion(() => Assert.Contains("Price approved", screen.Markup));
        Click(screen, "Record submission of version 1");
        screen.WaitForAssertion(() => Assert.Contains("now locked", screen.Markup));
        Assert.DoesNotContain("Save bid version", screen.Markup); // submitted bid cannot be edited

        screen.Find("label.award-line input[type=checkbox]").Change(true);
        Click(screen, "Record win and create sales order");
        screen.WaitForAssertion(() => Assert.Contains("Tender won", screen.Markup));
        Assert.Contains("s-Won", screen.Markup);
        Assert.Contains("Tender documents", screen.Markup);
    }

    [Fact] public async Task LibraryShowsCardsWithTypeBadgesDeadlinesAndAFormattedPreview()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        await library.Add(new(Guid.NewGuid(), new(LibraryDocumentType.TenderDocument, "Invitation to tender", "NHSL/RFQ/2026/31", "National Hospital Services", DueDate: today.AddDays(3)), new("itt.csv", "Item,Qty\nAnalyser,2\n"u8.ToArray())));
        await library.Add(new(Guid.NewGuid(), new(LibraryDocumentType.Cheque, "Cheque 000451", "000451", Amount: 4500, Currency: "SLE"), new("cheque.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1])));
        var screen = context.Render<DocumentLibraryWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        screen.WaitForAssertion(() => Assert.Equal(2, screen.FindAll(".doc-card").Count));
        Assert.Contains("Due in 3 days", screen.Find(".due-chip.soon").TextContent);
        Assert.Contains("SLE 4,500.00", screen.Markup);
        Assert.NotEmpty(screen.FindAll(".type-badge.t-Cheque"));
        Assert.Contains("CSV", screen.Find(".file-tile.excel").TextContent);

        screen.FindAll(".doc-card").First(x => x.TextContent.Contains("Invitation to tender")).Click();
        screen.WaitForAssertion(() => Assert.Contains("<td>Analyser</td>", screen.Find(".viewer-html").InnerHtml)); // CSV shown as a formatted table
        screen.FindAll(".view-toggle button")[1].Click();
        Assert.NotEmpty(screen.FindAll("table .type-badge"));
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
