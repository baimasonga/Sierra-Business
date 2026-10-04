using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Desktop;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Drives the Branch sync screen: sync now, see the result, and save the head-office connection.</summary>
public sealed class SyncScreenTests : IAsyncLifetime
{
    private readonly string central = Path.Combine(Path.GetTempPath(), $"sierra-syncui-hq-{Guid.NewGuid():N}.db"), branch = Path.Combine(Path.GetTempPath(), $"sierra-syncui-br-{Guid.NewGuid():N}.db");
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"sierra-syncui-{Guid.NewGuid():N}");
    private readonly BunitContext context = new();
    private readonly Actor owner = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet());
    public async Task InitializeAsync()
    {
        var hq = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={central};Pooling=False").AddInterceptors(new SyncCaptureInterceptor(SyncRole.Central, "HQ")).Options);
        var local = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={branch};Pooling=False").AddInterceptors(new SyncCaptureInterceptor(SyncRole.Branch, "FREETOWN")).Options);
        await using (var db = hq.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        await using (var db = local.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        await new BusinessService(hq, Options.Create(new TradingOptions { Branch = "HQ" })).AddProduct(new("owner", "HQ", BusinessPermissions.All.ToHashSet()), new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 200, TrackingMode.Quantity, MinimumPrice: 1));
        var server = new SyncServer(hq, Options.Create(new SyncOptions { Role = SyncRole.Central }), Options.Create(new TradingOptions { Branch = "HQ" }));
        var services = new ServiceCollection().AddSingleton<ISyncTransport>(new Direct(server)).BuildServiceProvider();
        context.Services.AddSingleton<ISyncService>(new SyncService(local, new Session(owner), Options.Create(new TradingOptions()), Options.Create(new SyncOptions { Role = SyncRole.Branch, Server = "https://hq.example" }), services));
        context.Services.AddSingleton<ISyncSettings>(new DesktopSyncConfiguration(folder));
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(central); File.Delete(branch); if (Directory.Exists(folder)) Directory.Delete(folder, true); return Task.CompletedTask; }

    [Fact] public void SyncNowAndSaveTheConnection()
    {
        var page = context.Render<SyncWorkspace>(p => p.Add(x => x.Actor, owner));
        page.WaitForAssertion(() => Assert.Contains("Branch sync", page.Markup));
        Assert.Contains("Never", page.Markup);
        page.FindAll("button").First(b => b.TextContent.Trim() == "Sync now").Click();
        page.WaitForAssertion(() => Assert.Contains("Received", page.Markup));
        Assert.Contains("No conflicts to review.", page.Markup);

        page.FindAll("input").First(i => i.GetAttribute("placeholder")?.StartsWith("https://") == true).Change("http://insecure.example");
        page.FindAll("input[type=password]").Single().Change("branch-key-0123456789");
        page.FindAll("button").First(b => b.TextContent == "Save connection").Click();
        page.WaitForAssertion(() => Assert.Contains("https://", page.Find("[role=alert]").TextContent));
        page.FindAll("input").First(i => i.GetAttribute("placeholder")?.StartsWith("https://") == true).Change("https://hq.example");
        page.FindAll("input[type=password]").Single().Change("branch-key-0123456789");
        page.FindAll("button").First(b => b.TextContent == "Save connection").Click();
        page.WaitForAssertion(() => Assert.Contains("Restart Sierra Business", page.Markup));
        var saved = new DesktopSyncConfiguration(folder).Load();
        Assert.Equal((SyncRole.Branch, "https://hq.example", "branch-key-0123456789"), (saved.Role, saved.Server, saved.Key));
    }

    private sealed class Direct(SyncServer server) : ISyncTransport
    {
        public Task<SyncPushResult> Push(SyncPushRequest request, CancellationToken ct = default) => server.Push(request, ct);
        public Task<SyncPullResult> Pull(string b, long after, int max, CancellationToken ct = default) => server.Pull(b, after, max, ct);
    }
    private sealed class Session(Actor actor) : IActorSession { public Task<Actor> CurrentActor() => Task.FromResult(actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
