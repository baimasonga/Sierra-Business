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

public sealed class AdvancedAccountingScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "rokela-analysis-ui-" + Guid.NewGuid().ToString("N") + ".db"); private readonly BunitContext context = new(); private readonly Session session = new(); private Factory factory = null!; private FinanceService finance = null!;
    public async Task InitializeAsync()
    {
        factory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=" + path + ";Pooling=False").Options); await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        finance = new(factory, session, Options.Create(new TradingOptions())); await finance.CreateLedger(new(Guid.NewGuid(), 6001, "Operating expenses", LedgerGroup.Expense));
        context.Services.AddSingleton<IFinanceService>(finance); context.JSInterop.Mode = JSRuntimeMode.Loose;
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private IRenderedComponent<AdvancedAccountingWorkspace> Render() => context.Render<AdvancedAccountingWorkspace>(p => p.Add(x => x.Actor, session.Actor));
    private static void Click(IRenderedComponent<AdvancedAccountingWorkspace> screen, string text) => screen.FindAll("button").First(b => b.TextContent.Trim() == text).Click();
    private static AngleSharp.Dom.IElement Field(IRenderedComponent<AdvancedAccountingWorkspace> screen, string label) => screen.FindAll("label").First(l => l.TextContent.Trim().StartsWith(label, StringComparison.Ordinal)).QuerySelector("input,select")!;
    [Fact] public async Task OwnerCreatesGroupsAllocatesCostsAndExportsFromTheScreen()
    {
        var screen = Render(); screen.WaitForAssertion(() => Assert.Contains("New account group", screen.Markup));
        Field(screen, "Group name").Change("Local costs"); Click(screen, "Save account group"); screen.WaitForAssertion(() => Assert.Contains("Account group saved", screen.Markup)); Assert.False(screen.Instance.HasDraft);
        var id = Assert.Single((await finance.Structure()).Groups).Id; Field(screen, "Ledger account").Change("6001"); Field(screen, "Account group").Change(id.ToString()); Click(screen, "Save ledger grouping"); screen.WaitForAssertion(() => Assert.Contains("Ledger grouping saved", screen.Markup));
        Click(screen, "Cost centres"); Field(screen, "Centre code").Change("OFFICE"); Field(screen, "Centre name").Change("Office costs"); Click(screen, "Save cost centre"); screen.WaitForAssertion(() => Assert.Contains("Cost centre saved", screen.Markup));
        Field(screen, "Income or expense account").Change("6001"); Field(screen, "Allocation amount").Change("100"); Field(screen, "Narration").Change("Office allocation"); screen.Find("input[aria-label='Share for OFFICE']").Change("100"); Click(screen, "Review allocation"); screen.WaitForAssertion(() => Assert.Contains("Post allocated voucher", screen.Markup)); Click(screen, "Post allocated voucher"); screen.WaitForAssertion(() => Assert.Contains("Allocated voucher posted", screen.Markup)); Assert.False(screen.Instance.HasDraft);
        var report = await finance.StructureReport(DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today)); Assert.Equal(100, report.Groups.Single(g => g.Id == id).Amount); Assert.Equal(100, report.Centres.Single(c => c.Code == "OFFICE").Expenses);
        Click(screen, "Export accounting analysis"); screen.WaitForAssertion(() => Assert.Contains(context.JSInterop.Invocations, i => i.Identifier == "sierraDownload"));
    }
    [Fact] public async Task MemorandumIsNonPostingAndEditingItDoesNotMarkItDirty()
    {
        var screen = Render(); screen.WaitForAssertion(() => Assert.Contains("New account group", screen.Markup)); Click(screen, "Scenarios");
        Field(screen, "Scenario name").Change("Rent review"); Field(screen, "Scenario type").Change(ScenarioKind.Memorandum.ToString()); Field(screen, "Scenario note").Change("Discuss next month"); Click(screen, "Save non posting scenario"); screen.WaitForAssertion(() => Assert.Contains("Actual accounting records are unchanged", screen.Markup)); Assert.False(screen.Instance.HasDraft);
        Click(screen, "Edit scenario"); Assert.False(screen.Instance.HasDraft); Field(screen, "Scenario note").Change("Discuss next quarter"); Assert.True(screen.Instance.HasDraft);
        Assert.Equal(0, (await finance.Read(DateOnly.MinValue, DateOnly.FromDateTime(DateTime.Today))).Profit);
    }
    [Fact] public void ReadOnlyUserCanCalculateInterestButDoesNotSeeWriteControls()
    {
        session.Actor = new("reader", "FREETOWN", new HashSet<string> { "accounting.read" }); var screen = Render(); screen.WaitForAssertion(() => Assert.Contains("Grouped balances", screen.Markup)); Assert.DoesNotContain("Save account group", screen.Markup);
        Click(screen, "Interest"); Field(screen, "Annual rate percent").Change("10"); Click(screen, "Calculate interest worksheet"); screen.WaitForAssertion(() => Assert.Contains("Calculating creates no bill or journal", screen.Markup)); Assert.DoesNotContain("Charge selected interest", screen.Markup);
        Click(screen, "Scenarios"); Assert.DoesNotContain("Save non posting scenario", screen.Markup);
    }
    [Fact] public async Task OwnerChargesSelectedWorksheetInterestToTheCustomer()
    {
        var business = new BusinessService(factory, Options.Create(new TradingOptions())); await TestParties.Register(factory, "School"); var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
        var product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "INT-UI", "Credit item", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", date, [new(product, 10, 50)], null));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "School", date, [new(product, 10, 100)], null, DueDate: date));
        var screen = Render(); screen.WaitForAssertion(() => Assert.Contains("New account group", screen.Markup));
        Click(screen, "Interest"); Field(screen, "Annual rate percent").Change("36.5"); Click(screen, "Calculate interest worksheet");
        screen.WaitForAssertion(() => Assert.Contains("Charge selected interest", screen.Markup));
        screen.Find("input[aria-label^='Charge interest on']").Change(true); Click(screen, "Charge selected interest");
        screen.WaitForAssertion(() => Assert.Contains("Interest charge posted to School's account", screen.Markup));
        var today = DateOnly.FromDateTime(DateTime.UtcNow); var income = -(await finance.Read(date, today)).Balances.Single(b => b.Code == 4400).Closing;
        Assert.True(income > 0); Assert.Contains((await finance.Read(date, today)).Bills, b => b.Party == "School" && b.Outstanding == income);
        Assert.DoesNotContain("aria-label=\"Charge interest on", screen.Markup);
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
