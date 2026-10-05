using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.Infrastructure.Postgres;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>0.27.0: head office on PostgreSQL (as on Supabase). Runs when SIERRA_POSTGRES holds a connection string to a server where the test may create and
/// drop a database (for example "Host=localhost;Username=postgres;Password=…"); skipped otherwise.</summary>
public sealed class PostgresTests : IAsyncLifetime
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("SIERRA_POSTGRES");
    private readonly string database = $"sierra_test_{Guid.NewGuid():N}";
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly Session session = new();
    private IDbContextFactory<BusinessDb> factory = null!;
    private readonly IOptions<TradingOptions> options = Options.Create(new TradingOptions { Branch = "HQ" });
    private string Connection => new NpgsqlConnectionStringBuilder(Server) { Database = database }.ConnectionString;

    public async Task InitializeAsync()
    {
        if (Server is null) return;
        factory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UsePostgres(Connection).AddInterceptors(new SyncCaptureInterceptor(SyncRole.Central, "HQ")).Options);
        await using var db = factory.CreateDbContext();
        await HistoryProtection.MigrateAsync(db);
    }
    public async Task DisposeAsync()
    {
        if (Server is null) return;
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureDeletedAsync();
    }

    [PostgresFact] public async Task HeadOfficeRunsOnPostgresWithProtectedHistory()
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
            var ex = await Assert.ThrowsAnyAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE sierra.\"Documents\" SET \"Total\" = 1 WHERE \"Id\" = {0}", sale));
            Assert.Contains("immutable", ex.Message);
            await Assert.ThrowsAnyAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM sierra.\"Journal\""));
            Assert.True(await db.SyncChanges.AnyAsync(x => x.Entity == nameof(BusinessDocument))); // changes queued for the branches
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'").SingleAsync()); // nothing for Supabase's web API to publish
        }
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db); // a restart keeps the guards
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }

    [PostgresFact] public async Task SqliteBranchSyncsIntoPostgresHeadOffice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sierra-pgbranch-{Guid.NewGuid():N}.db");
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

    [PostgresFact] public async Task ExistingBranchHistoryAndRetriesArriveWithoutConflicts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sierra-pghistory-{Guid.NewGuid():N}.db");
        try
        {
            var owner = new Actor("owner", "FREETOWN", BusinessPermissions.All.ToHashSet());
            var branchOptions = new TradingOptions { Branch = "FREETOWN" };
            var plain = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options); // used alone before joining
            await using (var local = plain.CreateDbContext()) await HistoryProtection.MigrateAsync(local);
            var branch = new BusinessService(plain, Options.Create(branchOptions));
            var item = await branch.AddProduct(owner, new(Guid.NewGuid(), "OLD-1", "Old item", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: 1));
            await branch.Post(owner, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(item, 500, 40)], null));
            for (var i = 0; i < 150; i++) await branch.Post(owner, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(item, 1, 100)], PaymentMethod.Cash));
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            var joined = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").AddInterceptors(new SyncCaptureInterceptor(SyncRole.Branch, "FREETOWN")).Options);
            var server = new SyncServer(factory, Options.Create(new SyncOptions { Role = SyncRole.Central }), options);
            var run = await SyncService.Run(joined, branchOptions, new SyncOptions { Role = SyncRole.Branch, BatchRows = 300 }, new Direct(server), default);
            Assert.Equal(0, run.Conflicts); Assert.True(run.Pushed > 0);

            // A reply lost on the way back: the branch sends everything again. Nothing is applied twice and no false conflicts appear.
            List<SyncChangeDto> all;
            await using (var local = joined.CreateDbContext()) all = (await local.SyncChanges.AsNoTracking().OrderBy(x => x.Seq).ToListAsync()).Select(x => SyncEngine.Dto(x, "FREETOWN")).ToList();
            await using (var db = factory.CreateDbContext()) { var peer = await db.SyncPeers.SingleAsync(x => x.Name == "FREETOWN"); peer.Pushed = 0; await db.SaveChangesAsync(); }
            var again = await server.Push(new("FREETOWN", all));
            Assert.Equal((0, 0), (again.Applied, again.Conflicts));

            await using (var db = factory.CreateDbContext())
            {
                Assert.Equal(151, await db.Documents.CountAsync(x => x.Branch == "FREETOWN"));
                Assert.Empty(await db.SyncConflicts.ToListAsync());
                var report = await HistoryProtection.Verify(db); Assert.True(report.Ok, string.Join("; ", report.Problems));
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [PostgresFact] public async Task OffsetTimestampsRoundTripWithValidSeals()
    {
        var id = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Operations.Add(new() { Id = id, ResultId = Guid.NewGuid(), Branch = "HQ", Actor = "owner", Action = "review.timestamp", Time = DateTimeOffset.Parse("2026-10-04T12:00:00.1234567+02:00"), Payload = "{}", Fingerprint = "timestamp-regression" });
            await db.SaveChangesAsync();
        }
        await using (var db = factory.CreateDbContext())
        {
            var row = await db.Operations.SingleAsync(x => x.Id == id);
            Assert.Equal(DateTimeOffset.Parse("2026-10-04T10:00:00.1234560Z"), row.Time);
            Assert.Equal(TimeSpan.Zero, row.Time.Offset);
            var report = await HistoryProtection.Verify(db);
            Assert.True(report.Ok, string.Join("; ", report.Problems));
        }
    }

    private sealed class Direct(SyncServer server) : ISyncTransport
    {
        public Task<SyncPushResult> Push(SyncPushRequest request, CancellationToken ct = default) => server.Push(request, ct);
        public Task<SyncPullResult> Pull(string branch, long after, int max, CancellationToken ct = default) => server.Pull(branch, after, max, ct);
    }
    private sealed class PostgresFactAttribute : FactAttribute { public PostgresFactAttribute() { if (Server is null) Skip = "Set SIERRA_POSTGRES to run the PostgreSQL tests."; } }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "HQ", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
