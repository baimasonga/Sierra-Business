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

/// <summary>Drives the Supplier invoices screen against the real services and database.</summary>
public sealed class SupplierInvoiceScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-invoice-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private Factory factory = null!; private BusinessService business = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options);
        context.Services.AddSingleton<IPurchasingService>(new PurchasingService(factory, session, options));
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private static void Click(IRenderedComponent<SupplierInvoiceWorkspace> screen, string text) => screen.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

    [Fact] public async Task AccountantMatchesAnInvoiceWithAPriceDifferenceAndPostsIt()
    {
        var paper = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Global Supplies", today, [new(paper, 10, 50)], null, InvoiceLater: true));

        var screen = context.Render<SupplierInvoiceWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        screen.WaitForAssertion(() => Assert.Contains("Global Supplies", screen.Markup));
        Assert.Contains("500.00", screen.Find("tfoot").TextContent); // goods received not invoiced

        screen.Find("select").Change(screen.FindAll("select option")[1].GetAttribute("value"));
        screen.Find("input[maxlength='60']").Change("INV-55");
        screen.Find("input[type=checkbox]").Change(true);
        screen.Find("input[aria-label^='Invoice amount']").Change("520");
        Click(screen, "Check against receipts");
        screen.WaitForAssertion(() => Assert.Contains("price difference NLe 20.00", screen.Markup));
        screen.Find("input[maxlength='200']").Change("Price increase notified 1 October");
        Click(screen, "Post supplier invoice");
        screen.WaitForAssertion(() => Assert.Contains("Supplier invoice posted", screen.Markup));
        Assert.Contains("Nothing is waiting for a supplier invoice", screen.Markup);

        var snapshot = await business.Read(session.Actor);
        Assert.Equal(-520, snapshot.Ledger.Where(x => (int)x.Account == 2000).Sum(x => x.Debit - x.Credit));
        Assert.Equal(520, snapshot.Stock.Single().Value);
    }

    [Fact] public async Task ViewersWithoutAccountingWriteSeeTheListOnly()
    {
        var paper = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Global Supplies", today, [new(paper, 2, 50)], null, InvoiceLater: true));
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read", "accounting.read" } };
        var screen = context.Render<SupplierInvoiceWorkspace>(p => p.Add(x => x.Actor, session.Actor));
        screen.WaitForAssertion(() => Assert.Contains("Global Supplies", screen.Markup));
        Assert.DoesNotContain("Record a supplier invoice", screen.Markup);
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
