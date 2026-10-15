using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Billing reviewed worksheet interest to customers: posting, collection, cancellation and no double charging.</summary>
public sealed class InterestChargeTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "rokela-interest-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly Session session = new(); private Factory factory = null!; private FinanceService finance = null!; private BusinessService business = null!; private Guid product;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly Date = Today.AddDays(-10);
    // 1,000 overdue for 10 days at 36.5% on actual/365 is exactly 10.00.
    private static readonly InterestRequest Request = new(Date.AddDays(1), Today, 36.5m, 0, InterestBasis.Actual365);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=" + path + ";Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions()); finance = new(factory, session, options); business = new(factory, options);
        await TestParties.Register(factory, "School"); await TestParties.Register(factory, "College");
        product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "INT-CHARGE", "Credit item", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", Date, [new(product, 20, 50)], null));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private Task<Guid> CreditSale(string party) => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, party, Date, [new(product, 10, 100)], null, DueDate: Date));
    private async Task<(decimal Receivables, decimal InterestIncome)> Ledgers()
    {
        var balances = (await finance.Read(Date, Today)).Balances;
        return (balances.Single(b => b.Code == 1100).Closing, -balances.Single(b => b.Code == 4400).Closing);
    }

    [Fact] public async Task ChargePostsACustomerBillAndTheSamePeriodIsNotChargedTwice()
    {
        var bill = await CreditSale("School");
        Assert.Equal(10.00m, (await finance.Interest(Request)).Total);
        var before = await Ledgers();
        var command = new ChargeInterestCommand(Guid.NewGuid(), Request, Today, [bill], 10.00m, 14);
        var charge = await finance.ChargeInterest(command);
        Assert.Equal(charge, await finance.ChargeInterest(command));

        var after = await Ledgers(); Assert.Equal(before.Receivables + 10, after.Receivables); Assert.Equal(10, after.InterestIncome);
        var snapshot = await finance.Read(Date, Today);
        var outstanding = Assert.Single(snapshot.Bills, b => b.Id == charge);
        Assert.Equal("School", outstanding.Party); Assert.Equal(10, outstanding.Outstanding); Assert.Equal(Today.AddDays(14), outstanding.DueDate);

        // The charged period no longer appears, and the interest charge itself bears no interest.
        Assert.Empty((await finance.Interest(Request)).Rows);
        await Assert.ThrowsAsync<BusinessException>(() => finance.ChargeInterest(command with { OperationId = Guid.NewGuid() }));
    }

    [Fact] public async Task StaleSupplierMixedCustomerAndBackdatedChargesAreRefused()
    {
        var school = await CreditSale("School"); var college = await CreditSale("College");
        var command = new ChargeInterestCommand(Guid.NewGuid(), Request, Today, [school], 10.00m);
        await Assert.ThrowsAsync<BusinessException>(() => finance.ChargeInterest(command with { ExpectedTotal = 9.99m }));
        await Assert.ThrowsAsync<BusinessException>(() => finance.ChargeInterest(command with { Request = Request with { Receivable = false } }));
        await Assert.ThrowsAsync<BusinessException>(() => finance.ChargeInterest(command with { DocumentIds = [school, college], ExpectedTotal = 20.00m }));
        await Assert.ThrowsAsync<BusinessException>(() => finance.ChargeInterest(command with { Request = Request with { To = Today.AddDays(-2) }, Date = Today.AddDays(-3) }));
        await Assert.ThrowsAsync<BusinessException>(() => finance.ChargeInterest(command with { DueDays = 366 }));
        Assert.Equal(0, (await Ledgers()).InterestIncome);
        Assert.Equal(20.00m, (await finance.Interest(Request)).Total);
    }

    [Fact] public async Task ReceiptsCollectTheChargeAndReversingItReopensThePeriod()
    {
        var bill = await CreditSale("School");
        var charge = await finance.ChargeInterest(new(Guid.NewGuid(), Request, Today, [bill], 10.00m));
        var receipt = await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, Today, "School interest", [new(1000, 10, 0), new(1100, 0, 10)], [new(charge, 10)]));
        Assert.DoesNotContain((await finance.Read(Date, Today)).Bills, b => b.Id == charge);

        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), charge, "Charged in error")));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), receipt, "Receipt entered against the wrong bill"));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), charge, "Charged in error"));

        Assert.Equal(0, (await Ledgers()).InterestIncome);
        Assert.Equal(10.00m, (await finance.Interest(Request)).Total);
        var again = await finance.ChargeInterest(new(Guid.NewGuid(), Request, Today, [bill], 10.00m));
        Assert.NotEqual(charge, again);
    }
    [Fact] public async Task ThePrintedNoteListsEachOverdueBillAndThePaymentDueDate()
    {
        var bill = await CreditSale("School");
        var charge = await finance.ChargeInterest(new(Guid.NewGuid(), Request, Today, [bill], 10.00m, 14));
        var document = (await business.Read(session.Actor)).Documents.Single(d => d.Id == charge);
        var sale = (await business.Read(session.Actor)).Documents.Single(d => d.Id == bill);
        using var context = new BunitContext(); context.JSInterop.Mode = JSRuntimeMode.Loose;
        var print = context.Render<BusinessDocumentOutput>(p => p.Add(c => c.Document, document));
        Assert.Contains("Interest charge note", print.Markup); Assert.Contains("36.5% a year", print.Markup); Assert.Contains(sale.Number, print.Markup);
        Assert.Contains("Payment due: " + Today.AddDays(14).ToString("dd MMM yyyy"), print.Markup);
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
