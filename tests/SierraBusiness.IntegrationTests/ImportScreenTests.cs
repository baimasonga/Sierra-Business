using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.Infrastructure.Imports;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Drives the import wizard like a user, against the real import service and database.</summary>
public sealed class ImportScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-import-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private readonly Saved saved = new();
    private Factory factory = null!; private BusinessService business = null!;
    private readonly Session session = new();
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        business = new(factory, Options.Create(new TradingOptions()));
        await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: 1));
        context.Services.AddSingleton<IImportService>(new ImportService(factory, session, Options.Create(new TradingOptions())));
        context.Services.AddSingleton<IFileExport>(saved);
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private static byte[] Xlsx(params string[][] rows) => WorkbookWriter.Write([("Data", rows.Select(r => (IReadOnlyList<string>)r).ToArray())]);

    [Fact] public async Task UserDownloadsTemplateFixesErrorsAndImportsOpeningStock()
    {
        var screen = context.Render<ImportWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        screen.Find("select").Change(nameof(ImportType.OpeningStock));
        screen.FindAll("button").Single(b => b.TextContent == "Download Excel template").Click();
        Assert.Contains("Opening stock template", saved.Name); Assert.Contains("Saved to", screen.Markup);

        screen.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(Xlsx(["Item Code", "Qty", "Cost"], ["PAPER", "10", "50"], ["GHOST", "1", "1"]), "stock.xlsx"));
        screen.WaitForAssertion(() => Assert.Contains("Match the columns", screen.Markup));
        Assert.Equal("Item Code", screen.FindAll("table select")[0].GetAttribute("value"));
        screen.FindAll("button").Single(b => b.TextContent == "Check the data").Click();
        screen.WaitForAssertion(() => Assert.Contains("1 row(s) to fix", screen.Markup));
        Assert.Contains("not in the catalogue", screen.Markup);
        Assert.True(screen.FindAll("button.primary").Last().HasAttribute("disabled"));
        screen.FindAll("button").Single(b => b.TextContent == "Download error workbook").Click();
        Assert.Contains("rows to fix", saved.Name);

        screen.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(Xlsx(["Item Code", "Qty", "Cost"], ["PAPER", "10", "50"]), "stock-fixed.xlsx"));
        screen.WaitForAssertion(() => Assert.Contains("Match the columns", screen.Markup));
        screen.FindAll("button").Single(b => b.TextContent == "Check the data").Click();
        screen.WaitForAssertion(() => Assert.Contains("Ready to import", screen.Markup));
        screen.FindAll("button").Single(b => b.TextContent == "Import 1 rows").Click();
        screen.WaitForAssertion(() => Assert.Contains("Posted opening stock worth NLe 500.00", screen.Markup));
        Assert.Contains("Imported", screen.Markup);
        Assert.Equal(10, (await business.Read(session.Actor)).Stock.Single().Quantity);
    }

    [Fact] public void OnlyPermittedImportTypesAreOffered()
    {
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read", "inventory.receive" } };
        var screen = context.Render<ImportWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        Assert.Equal(["Stock count"], screen.Find("select").QuerySelectorAll("option").Select(o => o.TextContent));
    }

    private sealed class Saved : IFileExport { public string Name = ""; public Task<string> Save(string fileName, byte[] content) { Name = fileName; Assert.NotEmpty(WorkbookReader.Read(content)); return Task.FromResult("C:\\Users\\me\\Documents\\Sierra Business\\" + fileName); } }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
