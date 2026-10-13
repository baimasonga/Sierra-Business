using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Web;

/// <summary>Invalidation only: clients re-read through permission-checked business services.</summary>
public sealed class WorkspaceChangesHub(IdentityActorValidator validator, IOptions<TradingOptions> options) : Hub
{
    public override async Task OnConnectedAsync()
    {
        try
        {
            var principal = Context.User ?? throw new UnauthorizedAccessException();
            if (!long.TryParse(principal.FindFirst("session.expires")?.Value, out var expires) || DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expires) throw new UnauthorizedAccessException();
            var actor = await validator.Validate(principal); actor.Demand("business.read");
            if (actor.Branch != options.Value.Branch) throw new UnauthorizedAccessException();
            await Groups.AddToGroupAsync(Context.ConnectionId, "branch:" + actor.Branch);
            await Clients.Caller.SendAsync("WorkspaceReady");
            await base.OnConnectedAsync();
        }
        catch (UnauthorizedAccessException) { Context.Abort(); throw new HubException("Sign in to this branch again."); }
    }
}

public sealed class WorkspaceChangesPublisher(IHubContext<WorkspaceChangesHub> hub, IOptions<TradingOptions> options, ILogger<WorkspaceChangesPublisher> logger) : BackgroundService
{
    private readonly Channel<bool> pending = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private void Changed() => pending.Writer.TryWrite(true);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ChangeNotifier.Changed += Changed;
        try
        {
            while (await pending.Reader.WaitToReadAsync(stoppingToken))
            {
                while (pending.Reader.TryRead(out _)) { }
                try { await hub.Clients.Group("branch:" + options.Value.Branch).SendAsync("WorkspaceChanged", cancellationToken: stoppingToken); }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning(e, "Workspace change notification failed; clients retain their refresh fallback."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { ChangeNotifier.Changed -= Changed; }
    }
}
