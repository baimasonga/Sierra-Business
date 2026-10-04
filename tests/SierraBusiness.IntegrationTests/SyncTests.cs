using Microsoft.AspNetCore.Hosting;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>0.25.0: head office and two branches on separate databases. Masters flow down, each branch's records flow up, retries are harmless,
/// conflicts are recorded, branches never receive each other's records, attachments travel, and existing history is sent when sync is switched on.</summary>
public sealed class SyncTests : IAsyncLifetime
{
    private const string FreetownKey = "freetown-key-0123456789", BoKey = "bo-branch-key-0123456789";
    private readonly List<string> paths = [];
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Site hq = null!, freetown = null!, bo = null!;
    private SyncServer server = null!;

    private sealed class Site
    {
        public required string Branch; public required IDbContextFactory<BusinessDb> Factory; public required Session Session; public required IOptions<TradingOptions> Options; public required SyncOptions Sync;
        public BusinessService Business => new(Factory, Options);
        public Actor Owner => Session.Actor;
    }
    private async Task<Site> Open(string branch, SyncRole role, bool capture = true, string? path = null)
    {
        path ??= Path.Combine(Path.GetTempPath(), $"sierra-sync-{branch}-{Guid.NewGuid():N}.db"); paths.Add(path);
        var builder = new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False");
        if (capture) builder.AddInterceptors(new SyncCaptureInterceptor(role, branch));
        var factory = new Factory(builder.Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var sync = new SyncOptions { Role = role, BatchRows = 7 };
        if (role == SyncRole.Central) { sync.Keys["FREETOWN"] = FreetownKey; sync.Keys["BO"] = BoKey; }
        return new() { Branch = branch, Factory = factory, Session = new() { Actor = new("owner", branch, BusinessPermissions.All.ToHashSet()) }, Options = Options.Create(new TradingOptions { Branch = branch, Device = "PC1" }), Sync = sync };
    }
    public async Task InitializeAsync()
    {
        hq = await Open("HQ", SyncRole.Central); freetown = await Open("FREETOWN", SyncRole.Branch); bo = await Open("BO", SyncRole.Branch);
        server = new SyncServer(hq.Factory, Options.Create(hq.Sync), hq.Options);
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (var p in paths) File.Delete(p); return Task.CompletedTask; }

    private Task<SyncRunResult> Sync(Site site, ISyncTransport? transport = null) => SyncService.Run(site.Factory, site.Options.Value, site.Sync, transport ?? new Wire(server, site.Branch), default);
    private static async Task Verified(Site site) { await using var db = site.Factory.CreateDbContext(); var report = await HistoryProtection.Verify(db); Assert.True(report.Ok, string.Join("; ", report.Problems)); }
    private static async Task<T> In<T>(Site site, Func<BusinessDb, Task<T>> read) { await using var db = site.Factory.CreateDbContext(); return await read(db); }

    [Fact] public async Task BranchRecordsReachHeadOfficeAndMastersReachEveryBranch()
    {
        var widget = await hq.Business.AddProduct(hq.Owner, new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 200, TrackingMode.Quantity, MinimumPrice: 150));
        await new PricingService(hq.Factory, hq.Session, hq.Options).Save(new(Guid.NewGuid(), null, 0, "BULK", "Bulk", PriceRuleKind.FixedPrice, 160, widget, "", "", 10, today.AddDays(-1), null));

        var first = await Sync(freetown);
        Assert.True(first.Pulled > 0);
        Assert.Equal("WID-1", await In(freetown, db => db.Products.Where(x => x.Id == widget).Select(x => x.Sku).SingleAsync()));
        Assert.True(await In(freetown, db => db.PriceRules.AnyAsync(x => x.Code == "BULK")));

        await TestParties.Register(freetown.Factory, "Kissy Clinic");
        await freetown.Business.Post(freetown.Owner, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(widget, 20, 100)], null));
        var sale = await freetown.Business.Post(freetown.Owner, new(Guid.NewGuid(), DocumentKind.Sale, "Kissy Clinic", today, [new(widget, 12, 160)], PaymentMethod.Cash)); // bulk price from head office's rule
        var run = await Sync(freetown);
        Assert.True(run.Pushed > 0); Assert.Equal(0, run.Conflicts);

        var atHq = await In(hq, db => db.Documents.AsNoTracking().Where(x => x.Branch == "FREETOWN").ToListAsync());
        Assert.Equal(2, atHq.Count); Assert.Contains(atHq, x => x.Id == sale);
        var lines = await In(hq, db => db.Journal.AsNoTracking().Where(x => x.Branch == "FREETOWN").ToListAsync());
        Assert.Equal(lines.Sum(x => x.Debit), lines.Sum(x => x.Credit));
        Assert.Equal(12m, await In(hq, async db => (await db.Movements.AsNoTracking().Where(x => x.DocumentId == sale).ToListAsync()).Sum(x => Math.Abs(x.Quantity))));
        await Verified(hq); await Verified(freetown);

        Assert.Equal(0, (await Sync(freetown)).Pushed); // nothing new
        Assert.Equal(0, await In(freetown, db => db.SyncChanges.CountAsync(x => x.Seq < db.SyncChanges.Max(y => y.Seq)))); // sent changes are cleared

        await Sync(bo);
        Assert.True(await In(bo, db => db.Products.AnyAsync(x => x.Id == widget)));
        Assert.False(await In(bo, db => db.Documents.AnyAsync())); // never another branch's records

        var status = await new SyncService(freetown.Factory, freetown.Session, freetown.Options, Options.Create(freetown.Sync), new ServiceCollection().BuildServiceProvider()).Status();
        Assert.Equal((0, SyncRole.Branch), (status.PendingChanges, status.Role));
        Assert.NotNull(status.Central!.LastSuccess);
        var central = await new SyncService(hq.Factory, hq.Session, hq.Options, Options.Create(hq.Sync), new ServiceCollection().BuildServiceProvider()).Status();
        Assert.Contains(central.Peers, x => x.Name == "FREETOWN" && x.Pushed > 0);
        Assert.Contains(central.Peers, x => x.Name == "BO");
    }

    [Fact] public async Task RepeatedPushesAreHarmlessAndConflictsAreRecorded()
    {
        var pricing = new PricingService(hq.Factory, hq.Session, hq.Options);
        var rule = await pricing.Save(new(Guid.NewGuid(), null, 0, "GOV", "Government", PriceRuleKind.PercentOff, 5, null, "", "Government", 0, today, null));
        await Sync(freetown);

        await freetown.Business.AddProduct(freetown.Owner, new(Guid.NewGuid(), "LOCAL-1", "Local item", "General", "piece", 10, TrackingMode.Quantity, MinimumPrice: 1));
        List<SyncChangeDto> batch;
        await using (var db = freetown.Factory.CreateDbContext()) batch = (await db.SyncChanges.AsNoTracking().Where(x => x.Origin == "").OrderBy(x => x.Seq).ToListAsync()).Select(x => SyncEngine.Dto(x, "FREETOWN")).ToList();
        var once = await server.Push(new("FREETOWN", batch));
        var twice = await server.Push(new("FREETOWN", batch)); // a retry after a lost reply
        Assert.True(once.Applied > 0); Assert.Equal(0, twice.Applied);
        Assert.Equal(1, await In(hq, db => db.Products.CountAsync(x => x.Sku == "LOCAL-1")));

        // Both sides edit the same shared rule from version 1.
        await pricing.Save(new(Guid.NewGuid(), rule, 1, "GOV", "Government (head office)", PriceRuleKind.PercentOff, 7, null, "", "Government", 0, today, null));
        await new PricingService(freetown.Factory, freetown.Session, freetown.Options).Save(new(Guid.NewGuid(), rule, 1, "GOV", "Government (Freetown)", PriceRuleKind.PercentOff, 9, null, "", "Government", 0, today, null));
        var run = await Sync(freetown);
        Assert.True(run.Conflicts > 0);
        Assert.Equal(7m, await In(hq, db => db.PriceRules.Where(x => x.Id == rule).Select(x => x.Value).SingleAsync())); // head office kept its version
        Assert.Equal(7m, await In(freetown, db => db.PriceRules.Where(x => x.Id == rule).Select(x => x.Value).SingleAsync())); // and the branch now has it
        Assert.Contains(await In(freetown, db => db.SyncConflicts.ToListAsync()), x => x.Reason.Contains("head office's version replaced"));
        Assert.Contains(await In(hq, db => db.SyncConflicts.ToListAsync()), x => x.Reason.Contains("kept head office's"));

        // A branch cannot send another branch's records.
        var forged = new SyncChangeDto(1, Guid.NewGuid(), nameof(BusinessParty), JsonSerializer.Serialize(new object[] { Guid.NewGuid() }), SyncOperation.Upsert, "{}", "BO", "FREETOWN");
        Assert.Equal(1, (await server.Push(new("FREETOWN", [forged with { Seq = 1_000_000 }]))).Conflicts);

        var conflicts = new SyncService(hq.Factory, hq.Session, hq.Options, Options.Create(hq.Sync), new ServiceCollection().BuildServiceProvider());
        var first = (await conflicts.Status()).Conflicts.First();
        await conflicts.MarkReviewed(first.Id);
        Assert.True((await conflicts.Status()).Conflicts.Single(x => x.Id == first.Id).Reviewed);
        hq.Session.Actor = new("clerk", "HQ", new HashSet<string> { "business.read", "sales.create" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => conflicts.Status());
    }

    [Fact] public async Task KeysAreCheckedAndAnOfflineBranchKeepsItsChanges()
    {
        Assert.True(server.Authorized("FREETOWN", FreetownKey));
        Assert.True(server.Authorized("freetown", FreetownKey));
        Assert.False(server.Authorized("FREETOWN", BoKey));
        Assert.False(server.Authorized("HQ", FreetownKey)); // head office's own code is never a branch
        Assert.False(server.Authorized("KENEMA", FreetownKey));
        Assert.False(server.Authorized("../x", FreetownKey));
        Assert.False(new SyncServer(hq.Factory, Options.Create(new SyncOptions { Role = SyncRole.Branch, Keys = hq.Sync.Keys }), hq.Options).Authorized("FREETOWN", FreetownKey));

        await freetown.Business.AddProduct(freetown.Owner, new(Guid.NewGuid(), "OFF-1", "Made offline", "General", "piece", 10, TrackingMode.Quantity, MinimumPrice: 1));
        await Assert.ThrowsAsync<HttpRequestException>(() => Sync(freetown, new Offline()));
        var peer = await In(freetown, db => db.SyncPeers.SingleAsync(x => x.Name == "central"));
        Assert.Contains("unreachable", peer.LastError); Assert.Null(peer.LastSuccess);
        Assert.True(await In(freetown, db => db.SyncChanges.AnyAsync(x => x.Entity == nameof(Product))));
        await Sync(freetown);
        Assert.True(await In(hq, db => db.Products.AnyAsync(x => x.Sku == "OFF-1")));
        Assert.Throws<PermanentJobException>(() => HttpSyncTransport.Create(new SyncOptions { Role = SyncRole.Branch, Server = "http://head-office.example", Key = FreetownKey }, "FREETOWN")); // plain http is refused
        Assert.NotNull(HttpSyncTransport.Create(new SyncOptions { Role = SyncRole.Branch, Server = "https://head-office.example", Key = FreetownKey }, "FREETOWN"));
    }

    [Fact] public async Task HistoryFromBeforeSyncAndAttachmentsAreSent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sierra-sync-legacy-{Guid.NewGuid():N}.db");
        var legacy = await Open("FREETOWN", SyncRole.Branch, capture: false, path); // used on its own before 0.25
        var item = await legacy.Business.AddProduct(legacy.Owner, new(Guid.NewGuid(), "OLD-1", "Old item", "General", "piece", 50, TrackingMode.Quantity, MinimumPrice: 1));
        await legacy.Business.Post(legacy.Owner, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(item, 5, 20)], null));
        var library = new DocumentLibrary(legacy.Factory, legacy.Session, legacy.Options, new DatabaseFileStore());
        var pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% contract\n1 0 obj <<>> endobj\ntrailer <<>>\n%%EOF");
        await library.Add(new(Guid.NewGuid(), new(LibraryDocumentType.Contract, "Supply contract", "C-1", "MedEquip Ltd", today, 1000, "SLE", null, "", "", false), new("contract.pdf", pdf, "", 0, 1f), null));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var joined = await Open("FREETOWN", SyncRole.Branch, capture: true, path); // sync switched on
        var run = await Sync(joined);
        Assert.True(run.Pushed > 0); Assert.Equal(0, run.Conflicts);
        Assert.True(await In(hq, db => db.Documents.AnyAsync(x => x.Branch == "FREETOWN")));
        Assert.Equal(pdf, await In(hq, db => db.Set<FileBlob>().Select(x => x.Content).SingleAsync()));
        Assert.Equal(0, (await Sync(joined)).Pushed); // history is queued once
        await Sync(bo);
        Assert.False(await In(bo, db => db.Set<FileBlob>().AnyAsync())); // another branch's files stay with it and head office
        Assert.True(await In(bo, db => db.Products.AnyAsync(x => x.Sku == "OLD-1")));
        await Verified(hq); await Verified(bo);
    }

    [Fact] public async Task BranchesSyncOverHttpWithTheirKeys()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(server);
        await using var app = builder.Build();
        SierraBusiness.Web.SyncEndpoints.MapSyncEndpoints(app);
        await app.StartAsync();
        var address = app.Urls.First();
        await hq.Business.AddProduct(hq.Owner, new(Guid.NewGuid(), "NET-1", "Over the wire", "General", "piece", 20, TrackingMode.Quantity, MinimumPrice: 1));
        await freetown.Business.AddProduct(freetown.Owner, new(Guid.NewGuid(), "NET-2", "From Freetown", "General", "piece", 20, TrackingMode.Quantity, MinimumPrice: 1));
        var transport = HttpSyncTransport.Create(new SyncOptions { Role = SyncRole.Branch, Server = address, Key = FreetownKey }, "FREETOWN");
        var run = await Sync(freetown, transport);
        Assert.True(run.Pulled > 0 && run.Pushed > 0);
        Assert.True(await In(freetown, db => db.Products.AnyAsync(x => x.Sku == "NET-1")));
        Assert.True(await In(hq, db => db.Products.AnyAsync(x => x.Sku == "NET-2")));
        var wrong = HttpSyncTransport.Create(new SyncOptions { Role = SyncRole.Branch, Server = address, Key = BoKey }, "FREETOWN");
        await Assert.ThrowsAsync<PermanentJobException>(() => wrong.Pull("FREETOWN", 0, 10));
        using var anonymous = new HttpClient();
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await anonymous.GetAsync(address + "/api/sync/pull?after=0")).StatusCode);
        await app.StopAsync();
    }

    /// <summary>Sends everything through JSON, as HTTP would.</summary>
    private sealed class Wire(SyncServer server, string branch) : ISyncTransport
    {
        private static T Trip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
        public async Task<SyncPushResult> Push(SyncPushRequest request, CancellationToken ct = default) => Trip(await server.Push(Trip(request), ct));
        public async Task<SyncPullResult> Pull(string b, long after, int max, CancellationToken ct = default) => Trip(await server.Pull(branch, after, max, ct));
    }
    private sealed class Offline : ISyncTransport
    {
        public Task<SyncPushResult> Push(SyncPushRequest request, CancellationToken ct = default) => throw new HttpRequestException("Network unreachable");
        public Task<SyncPullResult> Pull(string branch, long after, int max, CancellationToken ct = default) => throw new HttpRequestException("Network unreachable");
    }
    private sealed class Session : IActorSession { public Actor Actor = null!; public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
