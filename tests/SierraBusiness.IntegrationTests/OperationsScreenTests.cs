using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Drives the System health and Reminders screens: run a task now, see its result, then see and dismiss the reminder it raised.</summary>
public sealed class OperationsScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-ops-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private Factory factory = null!;
    private readonly Session session = new();
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        await new TenderService(factory, session, options).Create(new(Guid.NewGuid(), "NPPA/26/3", "National Procurement", "Laptops", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)));
        var runner = new JobRunner(factory, options, [new RemindersJob(), new IntegrityJob()], new ServiceCollection().BuildServiceProvider());
        context.Services.AddSingleton<IOperationsService>(new OperationsService(factory, session, options, runner, new ServiceCollection().BuildServiceProvider()));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    [Fact] public void RunATaskAndActOnItsReminder()
    {
        var health = context.Render<DiagnosticsWorkspace>();
        health.WaitForAssertion(() => Assert.Contains("Background tasks", health.Markup));
        Assert.Contains("Not run yet.", health.Markup);
        health.FindAll("button").Where(b => b.TextContent.Trim() == "Run now").ToList()[1].Click(); // integrity
        health.WaitForAssertion(() => Assert.Contains("Posted history verified.", health.Markup));
        health.FindAll("button").First(b => b.TextContent.Trim() == "Run now").Click(); // reminders
        health.WaitForAssertion(() => Assert.Contains("new reminder", health.Markup));

        var reminders = context.Render<RemindersWorkspace>();
        reminders.WaitForAssertion(() => Assert.Contains("Tender NPPA/26/3 closes in 1 day", reminders.Markup));
        reminders.FindAll("button").First(b => b.TextContent.Trim() == "Dismiss").Click();
        reminders.WaitForAssertion(() => Assert.Contains("Nothing needs attention", reminders.Markup));
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
