using System.Text;
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

/// <summary>Drives the Document library page and the attached-documents panel against the real services and database.</summary>
public sealed class DocumentLibraryScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-library-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private Factory factory = null!; private BusinessService business = null!; private DocumentLibrary library = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options); library = new(factory, session, options, new DatabaseFileStore());
        context.Services.AddSingleton<IDocumentLibrary>(library);
        context.Services.AddSingleton<IDocumentTextReader>(new FakeReader());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private static void Click<T>(IRenderedComponent<T> screen, string text) where T : Microsoft.AspNetCore.Components.IComponent => screen.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

    [Fact] public async Task ClerkFilesATenderWithSuggestedDetailsAndLinksItToATransaction()
    {
        var paper = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: 1));
        var receipt = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Global Supplies", today, [new(paper, 10, 40)], null));
        var screen = context.Render<DocumentLibraryWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        screen.FindComponents<Microsoft.AspNetCore.Components.Forms.InputFile>()[0].UploadFiles(InputFileContent.CreateFromBinary(Encoding.ASCII.GetBytes("%PDF-1.4\n%tender\n%%EOF"), "rfq-lab-equipment.pdf"));
        screen.WaitForAssertion(() => Assert.Contains("suggested from the document text", screen.Markup));
        Assert.Equal("NHSL/RFQ/2026/31", screen.Find("input[placeholder='Tender, invoice or cheque number']").GetAttribute("value"));
        screen.Find("input[placeholder^='For example: RFQ']").Change("Laboratory equipment RFQ");
        screen.FindAll("input[type=date]")[1].Change(today.AddDays(5).ToString("yyyy-MM-dd")); // deadline / expiry
        screen.Find("input[placeholder^='Number, name']").Change("Global");
        Click(screen, "Find records");
        screen.WaitForAssertion(() => Assert.Contains("Global Supplies", screen.Find("select:not([id])").TextContent + screen.Markup));
        Click(screen, "+ Add link");
        Click(screen, "Save document");
        screen.WaitForAssertion(() => Assert.Contains("Document stored", screen.Markup));
        Assert.Contains("Laboratory equipment RFQ", screen.Markup);
        Assert.Contains("Transaction: Receipt", screen.Markup);

        var stored = (await library.Search(new("calibration certificate"))).Single(); // found by text inside the file
        Assert.Equal((LibraryDocumentType.TenderDocument, 1), (stored.Details.Type, stored.Links));
        Assert.Single(await library.Search(new(Target: DocumentLinkTarget.Transaction, Key: receipt.ToString())));
    }

    [Fact] public async Task AttachedDocumentsPanelStoresAFileAgainstARecord()
    {
        var paper = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: 1));
        var receipt = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Global Supplies", today, [new(paper, 10, 40)], null));
        var panel = context.Render<AttachedDocuments>(p => p.Add(x => x.Actor, session.Actor).Add(x => x.Target, DocumentLinkTarget.Transaction).Add(x => x.Key, receipt.ToString()).Add(x => x.DefaultType, LibraryDocumentType.DeliveryNote).Add(x => x.DefaultTitle, "Signed GRN"));
        panel.WaitForAssertion(() => Assert.Contains("No documents are attached yet", panel.Markup));
        panel.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromBinary([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3], "grn-photo.jpg"));
        Click(panel, "Attach document");
        panel.WaitForAssertion(() => Assert.Contains("Document attached", panel.Markup));
        Assert.Contains("grn-photo.jpg", panel.Markup);
        Assert.Equal(LibraryDocumentType.DeliveryNote, (await library.Search(new(Target: DocumentLinkTarget.Transaction, Key: receipt.ToString()))).Single().Details.Type);
    }

    private sealed class FakeReader : IDocumentTextReader
    {
        public Task<DocumentText> Read(byte[] content, string extension, IProgress<string>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new DocumentText("REQUEST FOR QUOTATION\nReference: NHSL/RFQ/2026/31\nProcuring entity: National Hospital Services\nClosing date: 9 October 2026\nBidders must include a calibration certificate.", 2, .93f));
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
