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

/// <summary>Drives the 0.24.0 screens: create a price rule, read the financial statements, write a cheque.</summary>
public sealed class SmallerGapsScreenTests : IAsyncLifetime
{
    [Fact] public void ComparativeStatementDatesAndExportWork()
    {
        var screen = context.Render<StatementsWorkspace>(p => p.Add(x => x.Actor, owner));
        screen.WaitForAssertion(() => Assert.Contains("Total income", screen.Markup));
        screen.FindAll("button").Single(b => b.TextContent == "Comparative statements").Click();
        screen.WaitForAssertion(() => Assert.Contains("Net profit / loss", screen.Markup));
        screen.Find("select").Change("Balance sheet");
        screen.WaitForAssertion(() => Assert.Contains("Total assets", screen.Markup));
        screen.FindAll("button").Single(b => b.TextContent == "Export to Excel").Click();
        screen.WaitForAssertion(() => Assert.Contains(context.JSInterop.Invocations, i => i.Identifier == "sierraDownload" && i.Arguments[0]!.ToString()!.StartsWith("Comparative statements", StringComparison.Ordinal)));
        screen.FindAll("input[type=date]")[0].Change("2027-01-01");
        screen.WaitForAssertion(() => Assert.Contains("Each comparison period must end", screen.Markup));
        screen.FindAll("input[type=date]")[1].Change("2026-01-01");
        screen.WaitForAssertion(() => Assert.Contains("Each comparison period must end", screen.Markup));
        Assert.DoesNotContain("Total assets", screen.Markup);
    }
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-gaps-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private Factory factory = null!;
    private readonly Session session = new();
    private readonly Actor owner = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet());
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        var business = new BusinessService(factory, options);
        var widget = await business.AddProduct(owner, new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 200, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(owner, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2), [new(widget, 10, 100)], null));
        await business.Post(owner, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), [new(widget, 2, 200)], PaymentMethod.Cash));
        context.Services.AddSingleton<IBusinessService>(business);
        context.Services.AddSingleton<IPartyService>(new PartyService(factory, session, options));
        context.Services.AddSingleton<IPricingService>(new PricingService(factory, session, options));
        context.Services.AddSingleton<IStatementsService>(new StatementsService(factory, session, options));
        context.Services.AddSingleton<IBankingExtras>(new BankingExtrasService(factory, session, options));
        context.Services.AddSingleton<IBankingService>(new BankingService(factory, session, options));
        context.Services.AddSingleton<IFinanceService>(new FinanceService(factory, session, options));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    [Fact] public void PriceRulesStatementsAndCheques()
    {
        var pricing = context.Render<PricingWorkspace>(p => p.Add(x => x.Actor, owner));
        pricing.WaitForAssertion(() => Assert.Contains("No price rules yet.", pricing.Markup));
        pricing.FindAll("input")[0].Change("easter"); pricing.FindAll("input")[1].Change("Easter offer");
        pricing.FindAll("input[type=number]")[0].Change("5");
        pricing.FindAll("button").First(b => b.TextContent == "Save rule").Click();
        pricing.WaitForAssertion(() => Assert.Contains("Rule EASTER saved.", pricing.Markup));
        Assert.Contains("5% off", pricing.Markup);

        var statements = context.Render<StatementsWorkspace>(p => p.Add(x => x.Actor, owner));
        statements.WaitForAssertion(() => Assert.Contains("Sales", statements.Markup));
        statements.FindAll("button").First(b => b.TextContent == "Balance sheet").Click();
        statements.WaitForAssertion(() => Assert.Contains("✓ Balanced", statements.Markup));
        statements.FindAll("button").First(b => b.TextContent == "Cash flow").Click();
        statements.WaitForAssertion(() => Assert.Contains("✓ Reconciled", statements.Markup));
        statements.FindAll("button").First(b => b.TextContent == "Financial ratios").Click();
        statements.WaitForAssertion(() => Assert.Contains("Gross margin", statements.Markup));
        Assert.Contains("50.00%", statements.Markup);
        Assert.Contains("not annualised", statements.Markup);
        statements.FindAll("button").First(b => b.TextContent == "Export to Excel").Click();
        statements.WaitForAssertion(() => Assert.Contains(context.JSInterop.Invocations, x => x.Identifier == "sierraDownload"));

        var banking = context.Render<BankingToolsWorkspace>(p => p.Add(x => x.Actor, owner));
        banking.WaitForAssertion(() => Assert.Contains("Debit and credit columns", banking.Markup));
        banking.FindAll("button").First(b => b.TextContent == "Cheques issued").Click();
        banking.WaitForAssertion(() => Assert.Contains("No cheques issued yet.", banking.Markup));
        banking.FindAll("input").First(i => i.ParentElement!.TextContent.StartsWith("Cheque number")).Change("000777");
        banking.FindAll("input").First(i => i.ParentElement!.TextContent.StartsWith("Payee")).Change("Landlord");
        banking.FindAll("input").First(i => i.ParentElement!.TextContent.StartsWith("Amount")).Change("250");
        banking.FindAll("button").First(b => b.TextContent == "Record cheque").Click();
        banking.WaitForAssertion(() => Assert.Contains("Cheque 000777 recorded", banking.Markup));
        Assert.Contains("Landlord", banking.Markup);
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
