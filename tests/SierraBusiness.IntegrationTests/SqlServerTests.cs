using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.Infrastructure.SqlServer;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>0.25.0: head office on SQL Server. Runs when SIERRA_SQLSERVER holds a connection string to a server where the test may create and drop a database
/// (for example "Server=localhost;User Id=sa;Password=…;TrustServerCertificate=True"); skipped otherwise.</summary>
public sealed class SqlServerTests : IAsyncLifetime
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("SIERRA_SQLSERVER");
    private readonly string database = $"SierraTest_{Guid.NewGuid():N}";
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly Session session = new();
    private IDbContextFactory<BusinessDb> factory = null!;
    private readonly IOptions<TradingOptions> options = Options.Create(new TradingOptions { Branch = "HQ" });
    private string Connection => new SqlConnectionStringBuilder(Server) { InitialCatalog = database }.ConnectionString;

    public async Task InitializeAsync()
    {
        if (Server is null) return;
        factory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlServer(Connection, x => x.MigrationsAssembly(SqlServerDesignFactory.Assembly)).AddInterceptors(new SyncCaptureInterceptor(SyncRole.Central, "HQ")).Options);
        await using var db = factory.CreateDbContext();
        await HistoryProtection.MigrateAsync(db);
    }
    public async Task DisposeAsync()
    {
        if (Server is null) return;
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureDeletedAsync();
    }

    [SqlServerFact] public async Task HeadOfficeRunsOnSqlServerWithProtectedHistory()
    {
        var business = new BusinessService(factory, options);
        var widget = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 200, TrackingMode.Quantity, MinimumPrice: 150));
        await new PartyService(factory, session, options).Save(new(Guid.NewGuid(), null, 0, "KISSY", "Kissy Clinic", PartyType.Customer, "", "", "", "", 30, 1_000_000, false, Category: "Health"));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(widget, 10, 100)], null));
        var sale = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Kissy Clinic", today, [new(widget, 3, 200)], null, DueDate: today.AddDays(30)));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(widget, 1, 200)], PaymentMethod.Cash)), "Keyed twice"));
        await new FinanceService(factory, session, options).PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Rent", [new(5100, 50, 0), new(1000, 0, 50)], []));

        var snapshot = await business.Read(session.Actor);
        Assert.Equal(snapshot.Ledger.Sum(x => x.Debit), snapshot.Ledger.Sum(x => x.Credit));
        var statements = new StatementsService(factory, session, options);
        Assert.Equal(0m, (await statements.BalanceSheet(today)).Difference);
        Assert.Equal(250m, (await statements.ProfitAndLoss(today.AddDays(-1), today)).Profit); // sales 600 − cost 300 − rent 50
        Assert.True((await statements.CashFlow(today.AddDays(-1), today)).Reconciled);
        var reports = new ReportService(factory, session, options);
        Assert.Equal(600m, (await reports.Sales(today, today, SalesGrouping.Day)).Total);
        Assert.Equal(600m, (await reports.Ageing(today, true)).Total);
        Assert.NotEmpty((await reports.Register(new(today, today, null, "", null, "", 1, 50))).Rows.Items);
        Assert.Equal(7m, (await reports.Stock(today)).Rows.Single().Quantity);

        await using (var db = factory.CreateDbContext())
        {
            var report = await HistoryProtection.Verify(db);
            Assert.True(report.Ok, string.Join("; ", report.Problems));
            Assert.Equal(HistoryProtection.ExpectedGuardCount(db), await HistoryProtection.GuardCount(db));
            var ex = await Assert.ThrowsAnyAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Documents SET Total = 1 WHERE Id = {0}", sale));
            Assert.Contains("immutable", ex.Message);
            await Assert.ThrowsAnyAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM Journal"));
            Assert.True(await db.SyncChanges.AnyAsync(x => x.Entity == nameof(BusinessDocument))); // changes queued for the branches
        }
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db); // a restart keeps the guards
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }

    [SqlServerFact] public async Task SqliteBranchSyncsIntoSqlServerHeadOffice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sierra-sqlbranch-{Guid.NewGuid():N}.db");
        try
        {
            var branchFactory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").AddInterceptors(new SyncCaptureInterceptor(SyncRole.Branch, "FREETOWN")).Options);
            await using (var local = branchFactory.CreateDbContext()) await HistoryProtection.MigrateAsync(local);
            var widget = await new BusinessService(factory, options).AddProduct(session.Actor, new(Guid.NewGuid(), "WID-9", "Widget", "General", "piece", 200, TrackingMode.Quantity, MinimumPrice: 1));
            var sync = new SyncOptions { Role = SyncRole.Central, Keys = { ["FREETOWN"] = "freetown-key-0123456789" } };
            var server = new SyncServer(factory, Options.Create(sync), options);
            var branchOptions = new TradingOptions { Branch = "FREETOWN" };
            var transport = new Direct(server);
            await SyncService.Run(branchFactory, branchOptions, new SyncOptions { Role = SyncRole.Branch }, transport, default);
            var owner = new Actor("owner", "FREETOWN", BusinessPermissions.All.ToHashSet());
            var branch = new BusinessService(branchFactory, Options.Create(branchOptions));
            await branch.Post(owner, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(widget, 4, 90)], null));
            await branch.Post(owner, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(widget, 2, 200)], PaymentMethod.Cash));
            var run = await SyncService.Run(branchFactory, branchOptions, new SyncOptions { Role = SyncRole.Branch }, transport, default);
            Assert.Equal(0, run.Conflicts);
            await using var db = factory.CreateDbContext();
            Assert.Equal(2, await db.Documents.CountAsync(x => x.Branch == "FREETOWN"));
            Assert.True((await HistoryProtection.Verify(db)).Ok);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    private sealed class Direct(SyncServer server) : ISyncTransport
    {
        public Task<SyncPushResult> Push(SyncPushRequest request, CancellationToken ct = default) => server.Push(request, ct);
        public Task<SyncPullResult> Pull(string branch, long after, int max, CancellationToken ct = default) => server.Pull(branch, after, max, ct);
    }
    private sealed class SqlServerFactAttribute : FactAttribute { public SqlServerFactAttribute() { if (Server is null) Skip = "Set SIERRA_SQLSERVER to run the SQL Server tests."; } }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "HQ", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
