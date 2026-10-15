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

public sealed class DocumentDraftScreenTests : IAsyncLifetime
{
    [Fact] public async Task TenderBidRecoveryKeepsItsVersionAndRejectsAChangedBid()
    {
        var tenders = context.Services.GetRequiredService<ITenderService>();
        var id = await tenders.Create(new(Guid.NewGuid(), "BID-RECOVER", "MOH", "Equipment", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14)));
        List<TenderLineInput> lines = [new(null, "Equipment line", 1, "piece", 10, "SLE", 1, 0, 25, 20)];
        await tenders.SaveBid(new(Guid.NewGuid(), id, 0, lines));
        var screen = context.Render<TenderWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        screen.WaitForAssertion(() => Assert.Contains("BID-RECOVER", screen.Markup));
        screen.Find("button.tender-row").Click();
        screen.WaitForAssertion(() => Assert.Contains("Bid price for line 1", screen.Markup));
        screen.Find("input[aria-label='Bid price for line 1']").Change("25");
        screen.WaitForAssertion(() => Assert.Contains("25", memory.Drafts["tenders"].Json));
        screen.Dispose();
        var restored = context.Render<TenderWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        restored.WaitForAssertion(() => Assert.Contains("Restore draft", restored.Markup));
        restored.FindAll("button").Single(b => b.TextContent == "Restore draft").Click();
        restored.WaitForAssertion(() => Assert.Equal("25", restored.Find("input[aria-label='Bid price for line 1']").GetAttribute("value")));
        restored.Dispose();
        await tenders.SaveBid(new(Guid.NewGuid(), id, 1, lines, "Another officer's change"));
        var changed = context.Render<TenderWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        changed.WaitForAssertion(() => Assert.Contains("Restore draft", changed.Markup));
        changed.FindAll("button").Single(b => b.TextContent == "Restore draft").Click();
        changed.WaitForAssertion(() => Assert.Contains("tender changed since this draft was saved", changed.Markup));
        Assert.True(memory.Drafts.ContainsKey("tenders"));
        Assert.Equal(2, (await tenders.List()).Single().Bids.First().Version);
    }
    private readonly string path = Path.Combine(Path.GetTempPath(), $"rokela-draft-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new(); private readonly Session session = new(); private readonly MemoryRecovery memory = new();
    public async Task InitializeAsync()
    {
        var factory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        context.Services.AddSingleton<IDocumentDraftRecovery>(memory);
        context.Services.AddSingleton<IBusinessService>(new BusinessService(factory, options));
        context.Services.AddSingleton<IOrderService>(new OrderService(factory, session, options));
        context.Services.AddSingleton<IInventoryService>(new InventoryService(factory, session, options));
        context.Services.AddSingleton<IPurchasingService>(new PurchasingService(factory, session, options));
        context.Services.AddSingleton<ITenderService>(new TenderService(factory, session, options));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    [Fact] public void QuotationHeaderIsRecoveredAfterComponentRecreation()
    {
        var screen = context.Render<OrderWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        screen.WaitForAssertion(() => Assert.Contains("Create quotation or order", screen.Markup));
        screen.FindAll("label").Single(l => l.TextContent.StartsWith("Customer / supplier", StringComparison.Ordinal) && !l.TextContent.Contains("reference")).QuerySelector("input")!.Change("Recovery customer");
        screen.WaitForAssertion(() => Assert.Contains("Recovery customer", memory.Drafts["orders"].Json));
        var originalOperation = memory.Drafts["orders"].OperationId;
        screen.Dispose();
        var restored = context.Render<OrderWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        restored.WaitForAssertion(() => Assert.Contains("Restore draft", restored.Markup));
        restored.FindAll("button").Single(b => b.TextContent == "Restore draft").Click();
        restored.WaitForAssertion(() => Assert.Contains("Unfinished quotation/order restored", restored.Markup));
        Assert.Equal(originalOperation, memory.Drafts["orders"].OperationId);
        Assert.Equal("Recovery customer", restored.FindAll("label").Single(l => l.TextContent.StartsWith("Customer / supplier", StringComparison.Ordinal) && !l.TextContent.Contains("reference")).QuerySelector("input")!.GetAttribute("value"));
    }
    [Fact] public void TenderHeaderIsRecoveredAndRecoveryFailureDoesNotStopEditing()
    {
        var screen = context.Render<TenderWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        screen.WaitForAssertion(() => Assert.Contains("Register a tender", screen.Markup));
        screen.FindAll("label").Single(l => l.TextContent.StartsWith("Tender reference", StringComparison.Ordinal)).QuerySelector("input")!.Change("TENDER-RECOVER");
        screen.WaitForAssertion(() => Assert.Contains("TENDER-RECOVER", memory.Drafts["tenders"].Json));
        screen.Dispose();
        var restored = context.Render<TenderWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        restored.WaitForAssertion(() => Assert.Contains("Restore draft", restored.Markup));
        restored.FindAll("button").Single(b => b.TextContent == "Restore draft").Click();
        restored.WaitForAssertion(() => Assert.Contains("Unfinished tender restored", restored.Markup));
        memory.Fail = true;
        restored.FindAll("label").Single(l => l.TextContent.StartsWith("Tender reference", StringComparison.Ordinal)).QuerySelector("input")!.Change("CHANGED-RECOVERY");
        restored.WaitForAssertion(() => Assert.Contains("Automatic draft recovery is unavailable", restored.Markup));
        restored.FindAll("label").Single(l => l.TextContent.StartsWith("Tender reference", StringComparison.Ordinal)).QuerySelector("input")!.Change("KEEP-EDITING");
        Assert.True(restored.Instance.HasDraft);
    }
    private sealed class MemoryRecovery : IDocumentDraftRecovery
    {
        public Dictionary<string, DocumentDraft> Drafts = []; public bool Fail;
        public Task<DocumentDraft?> Read(string scope) => Task.FromResult(Drafts.GetValueOrDefault(scope));
        public Task Save(DocumentDraft draft) { if (Fail) throw new IOException("Recovery unavailable"); Drafts[draft.Scope] = draft; return Task.CompletedTask; }
        public Task Clear(string scope) { Drafts.Remove(scope); return Task.CompletedTask; }
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
