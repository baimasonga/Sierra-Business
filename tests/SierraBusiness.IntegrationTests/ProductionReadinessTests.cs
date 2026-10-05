using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Desktop;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
using Xunit.Abstractions;
namespace SierraBusiness.IntegrationTests;

/// <summary>0.26.0: update checks, an upgrade-and-rollback rehearsal on the desktop store, and performance with a realistic year of trading.</summary>
[Collection("Production readiness performance")]
public sealed class ProductionReadinessTests(ITestOutputHelper output)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
    private static async Task<(Factory Factory, string Path)> NewDb(string name, bool sync = false)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sierra-{name}-{Guid.NewGuid():N}.db");
        var builder = new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False");
        if (sync) builder.AddInterceptors(new SyncCaptureInterceptor(SyncRole.Branch, "FREETOWN"));
        var factory = new Factory(builder.Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        return (factory, path);
    }
    private static void Delete(params string[] paths) { SqliteConnection.ClearAllPools(); foreach (var p in paths) if (File.Exists(p)) File.Delete(p); }

    [Fact] public async Task NewerVersionsAreAnnouncedFromAValidatedFeed()
    {
        var (factory, path) = await NewDb("updates");
        var feed = """{"version":"0.27.0","url":"https://downloads.example/SierraBusiness-Setup-0.27.0.exe","sha256":"76370a8aba7fb05f87e0bcac2c8c7fb2eee197a9552e31983021072da78fbe2f","notes":"Faster reports."}""";
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapGet("/latest.json", () => Results.Text(feed, "application/json"));
        await app.StartAsync();
        try
        {
            var options = new UpdateOptions { Feed = app.Urls.First() + "/latest.json", CurrentVersion = "0.26.0" };
            var services = new ServiceCollection().AddSingleton(Options.Create(options)).BuildServiceProvider();
            var trading = Options.Create(new TradingOptions());
            var runner = new JobRunner(factory, trading, [new UpdateCheckJob()], services);
            var session = new Session();
            var updates = new UpdateService(factory, session, trading, Options.Create(options), runner);
            Assert.Null((await updates.Status()).Checked);
            var info = await updates.CheckNow();
            Assert.True(info.Available);
            Assert.Equal(("0.27.0", "76370A8ABA7FB05F87E0BCAC2C8C7FB2EEE197A9552E31983021072DA78FBE2F", "Faster reports."), (info.Latest, info.Sha256, info.Notes));

            options.CurrentVersion = "0.27.0";
            Assert.False((await updates.Status()).Available); // already on it
            feed = """{"version":"0.28.0","url":"http://downloads.example/setup.exe","sha256":"76370a8aba7fb05f87e0bcac2c8c7fb2eee197a9552e31983021072da78fbe2f"}""";
            info = await updates.CheckNow();
            Assert.Contains("not https", info.Problem); Assert.Equal("0.27.0", info.Latest); // a bad feed never replaces the last good answer
            feed = """{"version":"0.28.0","url":"https://downloads.example/setup.exe","sha256":"abc"}""";
            Assert.Contains("SHA-256", (await updates.CheckNow()).Problem);

            Assert.Throws<PermanentJobException>(() => UpdateFeed.Read(new HttpClient(), "http://downloads.example/latest.json", default).GetAwaiter().GetResult());
            Assert.True(UpdateFeed.Newer("0.26.1", "0.26.0")); Assert.False(UpdateFeed.Newer("0.9.0", "0.26.0")); Assert.False(UpdateFeed.Newer("next", "0.26.0"));
            await Assert.ThrowsAsync<BusinessException>(() => new UpdateService(factory, session, trading, Options.Create(new UpdateOptions()), runner).CheckNow()); // off
            session.Actor = new("clerk", "FREETOWN", new HashSet<string> { "business.read", "sales.create" });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => updates.Status());
        }
        finally { await app.StopAsync(); Delete(path); }
    }

    [Fact] public async Task UpgradeKeepsAPreUpgradeBackupThatTheOlderReleaseCanRestore()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sierra-rehearsal-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            using var store = new DesktopStore(directory);
            var factory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite(store.ConnectionString + ";Pooling=False").Options);
            string[] previousRelease;
            await using (var db = factory.CreateDbContext()) // the database as 0.25.0 left it
            {
                previousRelease = db.Database.GetMigrations().TakeWhile(x => !x.EndsWith("_UpdateChecks", StringComparison.Ordinal)).ToArray();
                await db.GetService<IMigrator>().MigrateAsync(previousRelease[^1]);
                await db.Database.ExecuteSqlRawAsync("INSERT INTO AspNetUsers (Id,UserName,NormalizedUserName,EmailConfirmed,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount) VALUES ('owner','owner@example.test','OWNER@EXAMPLE.TEST',0,0,0,1,0)");
                await db.Database.ExecuteSqlRawAsync("INSERT INTO AspNetUserClaims (UserId,ClaimType,ClaimValue) VALUES ('owner','branch','FREETOWN')");
                await db.Database.ExecuteSqlRawAsync("INSERT INTO Products (Id,Sku,Name,Category,Unit,Price,Tracking) VALUES ('5f0b1c4e-0000-4000-8000-000000000001','OLD-1','Before the upgrade','General','piece',100,0)");
            }
            SqliteConnection.ClearAllPools();

            var timer = Stopwatch.StartNew();
            await store.Initialize(factory); // installing 0.26.0 upgrades the database
            output.WriteLine($"Upgrade with backup: {timer.ElapsedMilliseconds} ms");
            var backup = store.ListBackups().Single(x => System.IO.Path.GetFileName(x.Path).StartsWith("before-upgrade", StringComparison.Ordinal));
            await using (var db = factory.CreateDbContext())
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                Assert.True((await HistoryProtection.Verify(db)).Ok);
                Assert.True(await db.Products.AnyAsync(x => x.Sku == "OLD-1"));
            }
            var actor = new Actor("owner", "FREETOWN", BusinessPermissions.All.ToHashSet());
            var business = new BusinessService(factory, Options.Create(new TradingOptions()));
            var widget = await business.AddProduct(actor, new(Guid.NewGuid(), "NEW-1", "After the upgrade", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: 1));
            await business.Post(actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", Today, [new(widget, 5, 50)], null));

            // Rollback: the older release accepts its own pre-upgrade backup and refuses the upgraded database.
            var older = previousRelease.ToHashSet();
            Assert.Equal(backup.Path, await store.ValidateBackup(backup.Path, older, "FREETOWN"));
            var upgraded = await store.CreateBackup();
            Assert.Contains("newer version", (await Assert.ThrowsAsync<BusinessException>(() => store.ValidateBackup(upgraded, older, "FREETOWN"))).Message);
            await using (var check = new SqliteConnection($"Data Source={backup.Path};Mode=ReadOnly;Pooling=False"))
            {
                await check.OpenAsync();
                await using var command = check.CreateCommand(); command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1";
                Assert.Equal(previousRelease[^1], (string?)await command.ExecuteScalarAsync());
            }
            // And restoring it on this release brings the old data forward again, without what was added since.
            await store.Restore(backup.Path, factory.CreateDbContext().Database.GetMigrations().ToHashSet(), "FREETOWN");
            await using (var db = factory.CreateDbContext())
            {
                Assert.True(await db.Products.AnyAsync(x => x.Sku == "OLD-1")); Assert.False(await db.Products.AnyAsync(x => x.Sku == "NEW-1"));
                Assert.True((await HistoryProtection.Verify(db)).Ok);
            }
        }
        finally { SqliteConnection.ClearAllPools(); try { Directory.Delete(directory, true); } catch (IOException) { } }
    }

    /// <summary>Six months of a busy branch: 600 two-line invoices. A sale must post in well under a second on average, and every report within seconds;
    /// the limits are generous so slow build machines pass, and the timings are printed.</summary>
    [Fact] public async Task HalfAYearOfTradingStaysFast()
    {
        var (factory, path) = await NewDb("perf", sync: true);
        var (central, centralPath) = await NewDb("perf-hq");
        try
        {
            var session = new Session(); var options = Options.Create(new TradingOptions());
            var business = new BusinessService(factory, options);
            await TestParties.Register(factory, "Kissy Clinic");
            var products = new List<Guid>();
            for (var i = 0; i < 40; i++) products.Add(await business.AddProduct(session.Actor, new(Guid.NewGuid(), $"P{i:000}", $"Product {i}", i % 2 == 0 ? "Medical" : "General", "piece", 100 + i, TrackingMode.Quantity, MinimumPrice: 1)));
            foreach (var p in products) await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", Today.AddDays(-360), [new(p, 2000, 60)], null));
            var timer = Stopwatch.StartNew();
            for (var i = 0; i < 600; i++)
            {
                var a = products[i % 40]; var b = products[(i * 7 + 3) % 40];
                await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, i % 3 == 0 ? "Kissy Clinic" : "Walk-in", Today.AddDays(-(i % 180)), [new(a, 2, 150), new(b, 1, 150)], i % 3 == 0 ? null : PaymentMethod.Cash, DueDate: i % 3 == 0 ? Today.AddDays(30 - i % 180) : null));
            }
            var posting = timer.Elapsed; output.WriteLine($"600 invoices posted in {posting.TotalSeconds:0.0} s ({posting.TotalMilliseconds / 600:0.0} ms each)");
            Assert.True(posting.TotalMilliseconds / 600 < 1000, $"Posting averaged {posting.TotalMilliseconds / 600:0} ms per invoice.");

            var reports = new ReportService(factory, session, options); var statements = new StatementsService(factory, session, options);
            async Task Measure(string what, Func<Task> work, double seconds)
            {
                var watch = Stopwatch.StartNew(); await work(); watch.Stop();
                output.WriteLine($"{what}: {watch.ElapsedMilliseconds} ms");
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(seconds), $"{what} took {watch.Elapsed}.");
            }
            var from = Today.AddDays(-365);
            await Measure("Dashboard (year)", () => reports.Dashboard(from, Today), 10);
            await Measure("Sales by month", () => reports.Sales(from, Today, SalesGrouping.Month), 10);
            await Measure("Sales by customer", () => reports.Sales(from, Today, SalesGrouping.Customer), 10);
            await Measure("Day book, first page", () => reports.Register(new(from, Today, null, "", null, "", 1, 50)), 10);
            await Measure("Stock valuation", () => reports.Stock(Today), 10);
            await Measure("Receivables ageing", () => reports.Ageing(Today, true), 10);
            await Measure("Balance sheet", () => statements.BalanceSheet(Today), 10);
            await Measure("Cash flow (year)", () => statements.CashFlow(from, Today), 10);
            await Measure("Excel export of the day book", () => statements.Export(new("Day book & ledgers", from, Today)), 20);
            await using (var db = factory.CreateDbContext()) await Measure("Integrity verification", async () => Assert.True((await HistoryProtection.Verify(db)).Ok), 60);

            var server = new SyncServer(central, Options.Create(new SyncOptions { Role = SyncRole.Central }), Options.Create(new TradingOptions { Branch = "HQ" }));
            await Measure("First sync of all history to head office", () => SyncService.Run(factory, options.Value, new SyncOptions { Role = SyncRole.Branch, BatchRows = 500 }, new Direct(server), default), 180);
            await using (var db = central.CreateDbContext()) Assert.Equal(640, await db.Documents.CountAsync(x => x.Branch == "FREETOWN"));
        }
        finally { Delete(path, centralPath); }
    }
    private sealed class Direct(SyncServer server) : ISyncTransport
    {
        public Task<SyncPushResult> Push(SyncPushRequest request, CancellationToken ct = default) => server.Push(request, ct);
        public Task<SyncPullResult> Pull(string branch, long after, int max, CancellationToken ct = default) => server.Pull(branch, after, max, ct);
    }
}
