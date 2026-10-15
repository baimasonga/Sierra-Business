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

public sealed class ForeignExchangeScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "rokela-fx-ui-" + Guid.NewGuid().ToString("N") + ".db"); private readonly BunitContext context = new(); private readonly Session session = new(); private PurchasingService fx = null!; private BusinessService business = null!; private FinanceService finance = null!; private Guid product; private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        var factory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=" + path + ";Pooling=False").Options); await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db); await TestParties.Register(factory, "School");
        var options = Options.Create(new TradingOptions()); fx = new(factory, session, options); finance = new(factory, session, options); business = new(factory, options);
        await fx.SetRate(new(Guid.NewGuid(), "USD", today.AddDays(-1), 20, "Training reference")); product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "FX-UI", "Invoice item", "General", "piece", 200, TrackingMode.Quantity, MinimumPrice: 100)); await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today.AddDays(-1), [new(product, 2, 100)], null));
        await finance.CreateLedger(new(Guid.NewGuid(), 6010, "USD bank", LedgerGroup.Asset, true));
        context.Services.AddSingleton<IPurchasingService>(fx); context.Services.AddSingleton<IFinanceService>(finance); context.Services.AddSingleton<IBusinessService>(business); context.Services.AddSingleton<IPartyService>(new PartyService(factory, session, options)); context.Services.AddSingleton<IInventoryService>(new InventoryService(factory, session, options)); context.JSInterop.Mode = JSRuntimeMode.Loose;
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private IRenderedComponent<ForeignExchangeWorkspace> Render() => context.Render<ForeignExchangeWorkspace>(p => p.Add(x => x.Actor, session.Actor));
    private static void Click(IRenderedComponent<ForeignExchangeWorkspace> screen, string text) => screen.FindAll("button").First(b => b.TextContent.Trim() == text).Click();
    private static AngleSharp.Dom.IElement Field(IRenderedComponent<ForeignExchangeWorkspace> screen, string label) => screen.FindAll("label").First(l => l.TextContent.Trim().StartsWith(label, StringComparison.Ordinal)).QuerySelector("input,select")!;
    [Fact] public async Task InvoiceSelectionPostingAndCustomerCollectionWorkFromTheScreen()
    {
        var screen = Render(); screen.WaitForAssertion(() => Assert.Contains("Foreign customer invoice", screen.Markup));
        Field(screen, "Customer").Change("School"); Field(screen, "Invoice currency").Change("USD"); Field(screen, "Product").Change(product.ToString()); Field(screen, "Unit price in invoice currency").Change("10"); Click(screen, "Add foreign invoice line"); Click(screen, "Post foreign customer invoice"); screen.WaitForAssertion(() => Assert.Contains("Foreign customer invoice posted:", screen.Markup)); Assert.False(screen.Instance.HasDraft);
        var bill = Assert.Single(await fx.ForeignReceivables()); Assert.Equal(10, bill.SourceOutstanding);
        Click(screen, "Settlements"); Field(screen, "Foreign bill").Change(bill.BillId.ToString()); Field(screen, "Settlement reference").Change("Customer paid converted value"); Click(screen, "Post foreign settlement"); screen.WaitForAssertion(() => Assert.Contains("Foreign settlement posted", screen.Markup)); Assert.Empty(await fx.ForeignReceivables()); Assert.False(screen.Instance.HasDraft);
    }
    [Fact] public async Task CurrencyBankFundingPreviewAndValuationWorkFromTheScreen()
    {
        var screen = Render(); screen.WaitForAssertion(() => Assert.Contains("Foreign customer invoice", screen.Markup)); Click(screen, "Currency banks"); Field(screen, "Custom bank ledger").Change("6010"); Field(screen, "Bank currency").Change("USD"); Click(screen, "Save currency bank"); screen.WaitForAssertion(() => Assert.Contains("Currency bank assigned", screen.Markup));
        Field(screen, "Currency bank").Change("6010"); Field(screen, "Counter ledger").Change("3000"); Field(screen, "Trade date").Change(today.AddDays(-1).ToString("yyyy-MM-dd")); Field(screen, "Foreign units").Change("100"); Field(screen, "NLe value for deposit").Change("2000"); Field(screen, "Trade reference").Change("Equity funding"); Click(screen, "Review currency bank transaction"); screen.WaitForAssertion(() => Assert.Contains("Post currency bank transaction", screen.Markup)); Click(screen, "Post currency bank transaction"); screen.WaitForAssertion(() => Assert.Contains("Currency bank transaction posted", screen.Markup));
        await fx.SetRate(new(Guid.NewGuid(), "USD", today, 22, "Closing reference"));
        Click(screen, "Closing valuation"); Field(screen, "Valuation reason").Change("Daily closing"); Click(screen, "Review closing valuation"); screen.WaitForAssertion(() => Assert.Contains("200.00", screen.Markup)); Click(screen, "Post paired FX valuation"); screen.WaitForAssertion(() => Assert.Contains("Closing valuation and next-day reversal posted together", screen.Markup));
        Field(screen, "Cancellation reason").Change("Recheck closing input"); Click(screen, "Cancel paired valuation"); screen.WaitForAssertion(() => Assert.Contains("Both valuation journals cancelled", screen.Markup)); Assert.True(Assert.Single(await fx.ForeignRevaluations()).Cancelled);
    }
    [Fact] public void SalesOnlyRoleDoesNotReceiveBankBalancesOrSettlementControls()
    {
        session.Actor = new("seller", "FREETOWN", new HashSet<string> { "business.read", "sales.create" }); var screen = Render(); screen.WaitForAssertion(() => Assert.Contains("Foreign customer invoice", screen.Markup)); Assert.DoesNotContain("Currency bank balances", screen.Markup); Assert.DoesNotContain("Post foreign settlement", screen.Markup); Assert.DoesNotContain(screen.FindAll("button"), b => b.TextContent.Trim() == "Currency banks");
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
