using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

/// <summary>Configuration section "Updates" (desktop: update-settings.json). Feed is an https address of the release feed; blank turns checks off.</summary>
public sealed class UpdateOptions
{
    public string Feed { get; set; } = "";
    /// <summary>Overrides the running version (tests and side-by-side servers).</summary>
    public string? CurrentVersion { get; set; }
}

/// <summary>The latest result of the update check (one row per branch).</summary>
public sealed class UpdateStatus
{
    public string Branch { get; set; } = "";
    public DateTimeOffset Checked { get; set; }
    public string Latest { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string NotesJson { get; set; } = "";
    public string Problem { get; set; } = "";
}

public static class UpdateFeed
{
    public sealed record Entry([property: JsonPropertyName("version")] string Version, [property: JsonPropertyName("url")] string Url, [property: JsonPropertyName("sha256")] string Sha256, [property: JsonPropertyName("notes")] string? Notes);

    public static string RunningVersion(UpdateOptions options) =>
        options.CurrentVersion ?? Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

    public static bool Newer(string candidate, string current) =>
        Version.TryParse(candidate, out var c) && (!Version.TryParse(current, out var r) || c > r);

    public static bool Secure(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>Reads and validates the feed. Anything malformed is rejected rather than shown.</summary>
    public static async Task<Entry> Read(HttpClient http, string feed, CancellationToken ct)
    {
        if (!Secure(feed)) throw new PermanentJobException("The update feed must be an https:// address.");
        var entry = await http.GetFromJsonAsync<Entry>(feed, ct) ?? throw new InvalidOperationException("The update feed was empty.");
        if (!Version.TryParse(entry.Version, out _)) throw new InvalidOperationException("The update feed has no valid version number.");
        if (!Secure(entry.Url ?? "")) throw new InvalidOperationException("The update feed's download address is not https.");
        if (!Regex.IsMatch(entry.Sha256 ?? "", "^[0-9A-Fa-f]{64}$")) throw new InvalidOperationException("The update feed has no valid SHA-256 for the installer.");
        return entry with { Sha256 = entry.Sha256!.ToUpperInvariant(), Notes = (entry.Notes ?? "").Length > 2000 ? entry.Notes![..2000] : entry.Notes ?? "" };
    }
}

/// <summary>Checks the release feed once a day.</summary>
public sealed class UpdateCheckJob(HttpClient? http = null) : IBackgroundJob
{
    public string Name => "updates"; public string Description => "Check for a newer version"; public TimeSpan Interval => TimeSpan.FromHours(24);
    public async Task<string> Run(JobContext c, CancellationToken ct)
    {
        var options = c.Services.GetService<IOptions<UpdateOptions>>()?.Value ?? new();
        if (string.IsNullOrWhiteSpace(options.Feed)) return "Update checks are off.";
        var current = UpdateFeed.RunningVersion(options);
        await using var db = await c.Factory.CreateDbContextAsync(ct);
        var row = await db.UpdateStatuses.SingleOrDefaultAsync(x => x.Branch == c.Branch, ct);
        if (row is null) { row = new() { Branch = c.Branch }; db.UpdateStatuses.Add(row); }
        row.Checked = DateTimeOffset.UtcNow;
        try
        {
            using var client = http is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : null;
            var entry = await UpdateFeed.Read(http ?? client!, options.Feed, ct);
            (row.Latest, row.Url, row.Sha256, row.NotesJson, row.Problem) = (entry.Version, entry.Url, entry.Sha256, entry.Notes ?? "", "");
            await db.SaveChangesAsync(ct);
            return UpdateFeed.Newer(entry.Version, current) ? $"Version {entry.Version} is available (running {current})." : $"Up to date ({current}).";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException or NotSupportedException)
        {
            row.Problem = SyncEngine.Clean(ex.Message); await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("Could not check for updates: " + row.Problem);
        }
    }
}

public sealed class UpdateService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> trading, IOptions<UpdateOptions> updates, JobRunner runner) : IUpdateService
{
    private async Task Authorize()
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        if (!actor.Permissions.Contains("audit.read") && !actor.Permissions.Contains("users.manage")) throw new UnauthorizedAccessException("Updates need audit.read or users.manage.");
    }
    public async Task<UpdateInfo> Status()
    {
        await Authorize();
        var current = UpdateFeed.RunningVersion(updates.Value); var enabled = !string.IsNullOrWhiteSpace(updates.Value.Feed);
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.UpdateStatuses.AsNoTracking().SingleOrDefaultAsync(x => x.Branch == trading.Value.Branch);
        if (row is null) return new(current, null, false, "", "", "", null, enabled ? "" : "Update checks are off.", enabled);
        var latest = row.Latest.Length > 0 ? row.Latest : null;
        return new(current, latest, latest is not null && UpdateFeed.Newer(latest, current), row.Url, row.Sha256, row.NotesJson, row.Checked, row.Problem, enabled);
    }
    public async Task<UpdateInfo> CheckNow()
    {
        await Authorize();
        if (string.IsNullOrWhiteSpace(updates.Value.Feed)) throw new BusinessException("Update checks are off. Set the update feed address first.");
        await runner.RunNow("updates");
        return await Status();
    }
}
