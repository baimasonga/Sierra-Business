using SierraBusiness.Infrastructure;
namespace SierraBusiness.Web;

/// <summary>Runs the background job scheduler while the web server is up.</summary>
public sealed class JobHostedService(JobScheduler scheduler) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) { scheduler.Start(); return Task.CompletedTask; }
    public async Task StopAsync(CancellationToken cancellationToken) => await scheduler.DisposeAsync();
}

/// <summary>Degraded when a background job has stopped after repeated failures or has not run for three intervals.</summary>
public sealed class JobsHealthCheck(JobRunner runner, Microsoft.EntityFrameworkCore.IDbContextFactory<BusinessDb> factory, Microsoft.Extensions.Options.IOptions<TradingOptions> options) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!await runner.Ready(cancellationToken)) return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded("Background jobs wait for the database upgrade.");
        var problem = await JobHealth.Problem(factory, options.Value.Branch, runner, DateTimeOffset.UtcNow, cancellationToken);
        return problem is null ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy() : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded(problem);
    }
}
