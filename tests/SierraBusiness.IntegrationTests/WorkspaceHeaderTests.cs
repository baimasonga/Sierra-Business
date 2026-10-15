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
    [Fact] public void ConfigurableFunctionKeysNavigateAndRejectDuplicateBindings()
    {
        var screen = Render(session.Actor);
        screen.WaitForAssertion(() => Assert.DoesNotContain("Loading your branch workspace", screen.Markup));
        screen.Find(".workspace").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "F2" });
        screen.WaitForAssertion(() => Assert.Equal("New sale", screen.Find("h1").TextContent.Trim()));
        screen.Find(".workspace").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "F3" });
        screen.WaitForAssertion(() => Assert.Equal("Receive stock", screen.Find("h1").TextContent.Trim()));
        Open(screen, "Keyboard shortcuts");
        screen.Find("section[aria-label='Keyboard shortcuts'] select").Change("F6");
        Open(screen, "Save keyboard shortcuts");
        screen.WaitForAssertion(() => Assert.Contains("Keyboard shortcuts saved", screen.Markup));
        var shortcutCall = context.JSInterop.Invocations.Last(i => i.Identifier == "sierraConfigureShortcuts");
        Assert.Single(shortcutCall.Arguments);
        Assert.Equal(new[] { "F6", "F3", "F4" }, Assert.IsType<string[]>(shortcutCall.Arguments[0]));
        screen.Find(".workspace").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "F6" });
        screen.WaitForAssertion(() => Assert.Equal("New sale", screen.Find("h1").TextContent.Trim()));
        screen.Find("section[aria-label='Keyboard shortcuts'] select").Change("F3");
        Open(screen, "Save keyboard shortcuts");
        screen.WaitForAssertion(() => Assert.Contains("Choose a different function key", screen.Markup));
        session.Actor = new Actor("reader", "FREETOWN", new HashSet<string> { "business.read" });
        var readOnly = Render(session.Actor);
        readOnly.WaitForAssertion(() => Assert.DoesNotContain("Loading your branch workspace", readOnly.Markup));
        readOnly.Find(".workspace").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "F2" });
        Assert.NotEqual("New sale", readOnly.Find("h1").TextContent.Trim());
    }
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-shell-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private readonly Session session = new();
    public async Task InitializeAsync()
    {
        var factory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
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

    [Fact] public void GoToSearchNavigatesAcrossAreasAndReportsNoMatches()
    {
        var screen = Render(session.Actor);
        screen.WaitForAssertion(() => Assert.DoesNotContain("Loading your branch workspace", screen.Markup));
        screen.Find("#screen-search").Input("New sale");
        screen.Find(".screen-results button").Click();
        screen.WaitForAssertion(() => Assert.Equal("New sale", screen.Find("h1").TextContent.Trim()));
        screen.Find("#screen-search").Input("stock summary");
        screen.Find(".screen-results button").Click();
        screen.WaitForAssertion(() => Assert.Equal("Stock summary", screen.Find("h1").TextContent.Trim()));
        screen.FindAll("button.nav").First(b => b.QuerySelector(".area-name")!.TextContent == "Sales & purchases").Click();
        screen.WaitForAssertion(() => Assert.Equal("New sale", screen.Find("h1").TextContent.Trim()));
        screen.Find("#screen-search").Input("zzzz");
        Assert.Empty(screen.FindAll(".screen-results button"));
        Assert.Contains("No matching screens", screen.Find(".screen-results").TextContent);
    }

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

    [Fact] public void CatalogueInputsReachSavedProduct()
    {
        var screen = Render(session.Actor);
        screen.WaitForAssertion(() => Assert.DoesNotContain("Loading your branch workspace", screen.Markup));
        screen.FindAll("button.nav").Single(b => b.QuerySelector(".area-name")!.TextContent == "Inventory").Click();
        Open(screen, "Catalogue");
        var form = screen.Find("form");
        form.QuerySelector("label:nth-child(1) input")!.Change("CAB-FIL4");
        form.QuerySelector("label:nth-child(2) input")!.Change("Test cabinet");
        form.QuerySelector("label:nth-child(5) input")!.Change("100");
        form.Submit();
        screen.WaitForAssertion(() => Assert.Contains("CAB-FIL4", screen.Find("table").TextContent));
        Assert.Contains("Test cabinet", screen.Find("table").TextContent);
    }

    [Fact] public async Task ReceiptPickerChangesReachPersistedDocument()
    {
        var product = await context.Services.GetRequiredService<IBusinessService>().AddProduct(session.Actor,
            new(Guid.NewGuid(), "A3-80G", "A3 paper", "Stationery", "ream", 500, SierraBusiness.Domain.TrackingMode.Quantity));
        var screen = Render(session.Actor);
        screen.WaitForAssertion(() => Assert.DoesNotContain("Loading your branch workspace", screen.Markup));
        screen.FindAll("button.nav").Single(b => b.QuerySelector(".area-name")!.TextContent == "Sales & purchases").Click();
        Open(screen, "Receive stock");
        AngleSharp.Dom.IElement Field(string label) => screen.FindAll("label")
            .Single(element => element.TextContent.StartsWith(label) && element.QuerySelector("input:not([type=checkbox]),select") is not null)
            .QuerySelector("input:not([type=checkbox]),select")!;
        Field("Supplier").Change("Picker test supplier");
        Field("Business date").Change("2026-10-05");
        Field("Bill due date").Change("2026-10-10");
        Field("Product").Change(product.ToString());
        Field("Quantity").Change("5");
        Field("Agreed unit price").Change("250");
        Assert.True(Field("Business date").HasAttribute("required"));
        screen.Find("form").Submit();
        Open(screen, "Post document");
        screen.WaitForAssertion(() => Assert.Equal("Documents", screen.Find("h1").TextContent.Trim()));
        using var document = System.Text.Json.JsonDocument.Parse(screen.Find("pre").TextContent);
        var request = document.RootElement.GetProperty("Request");
        Assert.Equal("2026-10-05", request.GetProperty("BusinessDate").GetString());
        Assert.Equal("2026-10-10", request.GetProperty("DueDate").GetString());
        Assert.Equal(product, request.GetProperty("Lines")[0].GetProperty("ProductId").GetGuid());
        Assert.Equal(5, request.GetProperty("Lines")[0].GetProperty("Quantity").GetDecimal());
    }

    [Fact] public async Task RejectedCreditSaleShowsFeedbackBesidePostAndCanBeCorrected()
    {
        var business = context.Services.GetRequiredService<IBusinessService>();
        var product = await business.AddProduct(session.Actor,
            new(Guid.NewGuid(), "POST-CHECK", "Posting check", "General", "piece", 55, SierraBusiness.Domain.TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), SierraBusiness.Domain.DocumentKind.Receipt, "Test supplier",
            DateOnly.FromDateTime(DateTime.UtcNow), [new(product, 2, 30)], null));
        var screen = Render(session.Actor);
        screen.WaitForAssertion(() => Assert.DoesNotContain("Loading your branch workspace", screen.Markup));
        screen.FindAll("button.nav").Single(b => b.QuerySelector(".area-name")!.TextContent == "Sales & purchases").Click();
        screen.WaitForAssertion(() => Assert.False(screen.Find("fieldset").HasAttribute("disabled")));
        screen.FindAll("label").Single(x => x.TextContent.StartsWith("Product")).QuerySelector("select")!.Change(product.ToString());
        screen.FindAll("form").Single(form => form.TextContent.Contains("+ Add line")).Submit();
        Assert.Contains("55", screen.Find(".total-bar").TextContent);
        var settlement = screen.FindAll("label").Single(x => x.TextContent.StartsWith("Settlement terms")).QuerySelector("select")!;
        settlement.Change("Credit");
        Open(screen, "Post document");
        screen.WaitForAssertion(() => Assert.Contains("Credit sales require a named customer", screen.Find(".posting-feedback").TextContent));
        Assert.Contains("55", screen.Find(".total-bar").TextContent);
        screen.FindAll("label").Single(x => x.TextContent.StartsWith("Settlement terms")).QuerySelector("select")!.Change("Cash");
        Open(screen, "Post document");
        screen.WaitForAssertion(() => Assert.Equal("Documents", screen.Find("h1").TextContent.Trim()));
        Assert.Contains("Document posted", screen.Markup);
    }

    [Fact] public async Task SaleRecoveryRestoresEditorAndHonoursDiscardConfirmation()
    {
        var recovery = new MemoryRecovery(); context.Services.AddSingleton<ISaleDraftRecovery>(recovery);
        var product = await context.Services.GetRequiredService<IBusinessService>().AddProduct(session.Actor,
            new(Guid.NewGuid(), "AUTO-SAVE", "Auto save", "General", "piece", 55, SierraBusiness.Domain.TrackingMode.Quantity, MinimumPrice: 1));
        var screen = Render(session.Actor);
        screen.WaitForAssertion(() => Assert.DoesNotContain("Loading your branch workspace", screen.Markup));
        Open(screen, "+ New sale");
        screen.WaitForAssertion(() => Assert.False(screen.Find("fieldset").HasAttribute("disabled")));
        screen.FindAll("label").Single(x => x.TextContent.StartsWith("Product")).QuerySelector("select")!.Change(product.ToString());
        screen.FindAll("label").Single(x => x.TextContent.StartsWith("Quantity")).QuerySelector("input")!.Change("3");
        screen.FindAll("form").Single(f => f.TextContent.Contains("+ Add line")).Submit();
        screen.WaitForAssertion(() => Assert.Equal(3, Assert.Single(recovery.Saved!.Document.Lines).Quantity));
        var operation = recovery.Saved!.Document.OperationId;
        screen.Dispose(); // simulate a lost UI session
        screen = Render(session.Actor);
        screen.WaitForAssertion(() => Assert.Contains("An unfinished sale was recovered", screen.Markup));
        Open(screen, "Recover sale");
        screen.WaitForAssertion(() => Assert.Contains("165", screen.Find(".total-bar").TextContent));
        Assert.Equal(operation, recovery.Saved!.Document.OperationId);
        screen.FindAll("button.nav").Single(b => b.QuerySelector(".area-name")!.TextContent == "Overview").Click();
        Assert.Equal("New sale", screen.Find("h1").TextContent.Trim());
        Assert.NotNull(recovery.Saved);
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        screen.FindAll("button.nav").Single(b => b.QuerySelector(".area-name")!.TextContent == "Overview").Click();
        screen.WaitForAssertion(() => Assert.Null(recovery.Saved));
        Assert.Equal("Overview", screen.Find("h1").TextContent.Trim());
    }
    [Fact] public async Task RecalledHeldSaleContinuesAutomaticRecovery()
    {
        var recovery = new MemoryRecovery(); context.Services.AddSingleton<ISaleDraftRecovery>(recovery);
        var product = await context.Services.GetRequiredService<IBusinessService>().AddProduct(session.Actor,
            new(Guid.NewGuid(), "RECALL-SAVE", "Recall save", "General", "piece", 55, SierraBusiness.Domain.TrackingMode.Quantity, MinimumPrice: 1));
        var held = await context.Services.GetRequiredService<IPosService>().Hold(new(Guid.NewGuid(), "Recall test",
            new(Guid.NewGuid(), SierraBusiness.Domain.DocumentKind.Sale, "Walk-in", DateOnly.FromDateTime(DateTime.UtcNow), [new(product, 2, 55)], SierraBusiness.Domain.PaymentMethod.Cash)));
        var screen = Render(session.Actor);
        screen.WaitForAssertion(() => Assert.DoesNotContain("Loading your branch workspace", screen.Markup));
        Open(screen, "+ New sale");
        screen.WaitForAssertion(() => Assert.Single(screen.FindAll("button"), b => b.TextContent.Trim() == "Recall"));
        Open(screen, "Recall");
        screen.WaitForAssertion(() => Assert.Equal(held, recovery.Saved!.Document.HeldSaleId));
        Assert.Equal(2, Assert.Single(recovery.Saved!.Document.Lines).Quantity);
        screen.FindAll("label").Single(x => x.TextContent.StartsWith("Customer")).QuerySelector("input")!.Change("Updated customer");
        screen.WaitForAssertion(() => Assert.Equal("Updated customer", recovery.Saved!.Document.Party));
    }

    [Fact] public void RecoveryCleanupFailureDoesNotBreakNavigationOrEditing()
    {
        var recovery = new MemoryRecovery(); context.Services.AddSingleton<ISaleDraftRecovery>(recovery);
        var screen = Render(session.Actor);
        screen.WaitForAssertion(() => Assert.DoesNotContain("Loading your branch workspace", screen.Markup));
        Open(screen, "+ New sale");
        screen.WaitForAssertion(() => Assert.Equal("New sale", screen.Find("h1").TextContent.Trim()));
        recovery.FailClear = true;
        screen.FindAll("button.nav").Single(b => b.QuerySelector(".area-name")!.TextContent == "Inventory").Click();
        screen.WaitForAssertion(() => Assert.Contains("recovery copy could not be cleared", screen.Markup));
        Assert.Equal("New sale", screen.Find("h1").TextContent.Trim());
        var customerInput = screen.FindAll("label").Single(l => l.TextContent.StartsWith("Customer")).QuerySelector("input")!;
        customerInput.Change("Keep editing");
        recovery.FailClear = false;
        screen.FindAll("button.nav").Single(b => b.QuerySelector(".area-name")!.TextContent == "Inventory").Click();
        Assert.Equal("New sale", screen.Find("h1").TextContent.Trim()); // changed customer makes a draft; default Cancel keeps it
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        screen.FindAll("button.nav").Single(b => b.QuerySelector(".area-name")!.TextContent == "Inventory").Click();
        screen.WaitForAssertion(() => Assert.Equal("Stock summary", screen.Find("h1").TextContent.Trim()));
    }
    private sealed class MemoryRecovery : ISaleDraftRecovery
    {
        public bool FailClear;
        public RecoverableSaleDraft? Saved;
        public Task<RecoverableSaleDraft?> Read() => Task.FromResult(Saved);
        public Task Save(RecoverableSaleDraft draft) { Saved = System.Text.Json.JsonSerializer.Deserialize<RecoverableSaleDraft>(System.Text.Json.JsonSerializer.Serialize(draft)); return Task.CompletedTask; }
        public Task Clear() { if (FailClear) throw new IOException("Recovery folder unavailable."); Saved = null; return Task.CompletedTask; }
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
