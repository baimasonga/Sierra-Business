using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Web;

/// <summary>Head-office sync endpoints (0.25.0). Each branch authenticates with its own key in the Authorization header; nothing is served unless Sync:Role is Central.</summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sync").DisableAntiforgery().RequireRateLimiting("sync");
        group.MapPost("/push", async (HttpContext context, SyncServer server, SyncPushRequest body, CancellationToken ct) =>
        {
            var branch = Branch(context);
            if (!server.Authorized(branch, Key(context)) || !string.Equals(body.Branch, branch, StringComparison.OrdinalIgnoreCase)) return Results.Unauthorized();
            if (body.Changes is null || body.Changes.Count > 20_000) return Results.BadRequest();
            return Results.Ok(await server.Push(body with { Branch = branch! }, ct));
        });
        group.MapGet("/pull", async (HttpContext context, SyncServer server, long after, int? max, CancellationToken ct) =>
        {
            var branch = Branch(context);
            if (!server.Authorized(branch, Key(context))) return Results.Unauthorized();
            return Results.Ok(await server.Pull(branch!, Math.Max(0, after), max ?? 500, ct));
        });
    }
    private static string? Branch(HttpContext context) => context.Request.Headers["X-Sierra-Branch"].FirstOrDefault();
    private static string? Key(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.FirstOrDefault();
        return header is not null && header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..].Trim() : null;
    }

    /// <summary>At most 240 sync calls a minute per branch and address; a normal branch makes a handful every five minutes.</summary>
    public static void AddSyncRateLimiting(IServiceCollection services) => services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy("sync", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(context.Request.Headers["X-Sierra-Branch"].ToString() + "|" + context.Connection.RemoteIpAddress, _ => new() { PermitLimit = 240, Window = TimeSpan.FromMinutes(1) }));
    });

    public static IServiceCollection AddSierraSync(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SyncOptions>(configuration.GetSection("Sync"));
        services.AddSingleton<SyncServer>();
        AddSyncRateLimiting(services);
        services.AddScoped<ISyncService, SyncService>();
        services.AddSingleton<ISyncSettings, ConfiguredSyncSettings>();
        if (configuration.GetValue<SyncRole>("Sync:Role") == SyncRole.Branch) services.AddSingleton<IBackgroundJob, SyncJob>();
        return services;
    }
}

/// <summary>On the server the connection is part of the deployment configuration (Sync section or environment variables), not edited on screen.</summary>
public sealed class ConfiguredSyncSettings(IOptions<SyncOptions> options) : ISyncSettings
{
    public SyncSettingsView Read() => new(options.Value.Role, options.Value.Server, options.Value.Key.Length > 0, false);
    public void Save(string server, string? key) => throw new SierraBusiness.Domain.BusinessException("On the server, set the Sync section of the configuration and restart.");
}
