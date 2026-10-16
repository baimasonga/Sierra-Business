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

public sealed class TaxScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"rokela-tax-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new(); private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Factory factory = null!; private BusinessService business = null!; private ComplianceService compliance = null!; private FinanceService finance = null!;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions()); business = new(factory, options); compliance = new(factory, session, options, new NoFiscalDevice()); finance = new(factory, session, options);
        await compliance.SaveProfile(new(Guid.NewGuid(), new(LegalName: "Screen tax company", TaxRegistered: true, Identifiers: [new("TIN", "123456")])));
        await finance.CreateLedger(new(Guid.NewGuid(), 6401, "Input GST recovery", LedgerGroup.Asset));
        context.Services.AddSingleton<IComplianceService>(compliance); context.Services.AddSingleton<IFinanceService>(finance); context.JSInterop.Mode = JSRuntimeMode.Loose;
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private static AngleSharp.Dom.IElement Field<T>(IRenderedComponent<T> screen, string label) where T : Microsoft.AspNetCore.Components.IComponent => screen.FindAll("label").First(l => l.TextContent.Trim().StartsWith(label, StringComparison.Ordinal)).QuerySelector("input,select,textarea")!;
    private static void Click<T>(IRenderedComponent<T> screen, string text) where T : Microsoft.AspNetCore.Components.IComponent => screen.FindAll("button").Single(b => b.TextContent.Trim() == text).Click();

    [Fact] public async Task StockClaimPreviewsAndConfirmsIssuedGoodsAllocation()
    {
        var product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "TAX-UI", "Taxed stock", "General", "piece", 200, TrackingMode.Quantity, MinimumPrice: 1));
        var receipt = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Tax supplier", today, [new(product, 2, 115)], null));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Cash customer", today, [new(product, 1, 200)], PaymentMethod.Cash));
        var screen = context.Render<InputTaxWorkspace>(p => p.Add(x => x.Actor, session.Actor)); screen.WaitForAssertion(() => Assert.Contains("Reviewed input-tax recovery", screen.Markup));
        Field(screen, "GST is included in a direct stock receipt").Change(true); Field(screen, "Source expense payment or goods receipt").Change(receipt.ToString());
        Field(screen, "Supplier name").Change("Tax supplier"); Field(screen, "Supplier TIN").Change("TIN-UI"); Field(screen, "Invoice or customs reference").Change("INV-UI");
        Field(screen, "Purchase excluding GST").Change("200"); Field(screen, "GST paid").Change("30"); Field(screen, "Reviewed allowable GST").Change("30"); Field(screen, "Allowability review reason").Change("Reviewed");
        Assert.True(screen.FindAll("button").Single(b => b.TextContent.Trim() == "Record reviewed input-tax recovery").HasAttribute("disabled"));
        Click(screen, "Preview stock GST allocation"); screen.WaitForAssertion(() => Assert.Contains("Reviewed GST for goods already issued", screen.Markup));
        Click(screen, "Record reviewed input-tax recovery"); screen.WaitForAssertion(() => Assert.Contains("Reviewed input-tax recovery posted", screen.Markup));
        Assert.Equal(100, (await business.Read(session.Actor)).Stock.Where(s => s.ProductId == product).Sum(s => s.Value));
    }
    [Fact] public async Task RecordedReturnIsCorrectedFromTheFilingScreen()
    {
        await finance.CreateLedger(new(Guid.NewGuid(), 6501, "GST carry forward", LedgerGroup.Asset)); await finance.CreateLedger(new(Guid.NewGuid(), 6502, "GST corrections", LedgerGroup.Expense));
        var to = new DateOnly(today.Year, today.Month, 1).AddDays(-1); var from = new DateOnly(to.Year, to.Month, 1);
        var preview = await compliance.PreviewTaxFiling(from, to); await compliance.RecordTaxFiling(new(Guid.NewGuid(), from, to, preview.CalculationFingerprint, "NRA-UI", "Reconciled", 1010, 6501, 0, true));
        var screen = context.Render<TaxFilingWorkspace>(p => p.Add(x => x.Actor, session.Actor)); screen.WaitForAssertion(() => Assert.Contains("Correct a recorded GST return", screen.Markup));
        Field(screen, "Return to correct").Change(Assert.Single(await compliance.TaxFilings()).Id.ToString()); Field(screen, "Amendment reference").Change("NRA-UI-A1"); Field(screen, "Correction review reason").Change("Missed invoice");
        Field(screen, "Output GST change").Change("12.5"); Field(screen, "Correction counterpart ledger").Change("6502"); Field(screen, "Correction carry-forward asset").Change("6501");
        Click(screen, "Record reviewed GST return correction"); screen.WaitForAssertion(() => Assert.Contains("Reviewed GST return correction recorded", screen.Markup));
        Assert.Contains("NRA-UI-A1", screen.Markup); Assert.Equal(12.5m, Assert.Single(await compliance.TaxFilingCorrections()).Paid);
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
