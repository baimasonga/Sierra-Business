using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>0.23.0 (Skill 25): retry then recovery, permanent failure stops a job with an administrator reminder, restart keeps state, health degradation, reminders created once per subject and dismissed per user, fiscal retries, diagnostics.</summary>
public sealed class OperationsTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-ops-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private BusinessService business = null!; private IOptions<TradingOptions> options = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly Clock clock = new(DateTimeOffset.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        options = Options.Create(new TradingOptions()); business = new(factory, options);
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private JobRunner Runner(params IBackgroundJob[] jobs) => new(factory, options, jobs, new ServiceCollection().BuildServiceProvider(), null, clock);
    private async Task<JobState> State(string job) { await using var db = factory.CreateDbContext(); return await db.JobStates.AsNoTracking().SingleAsync(x => x.Job == job); }
    private async Task<int> Runs(string job) { await using var db = factory.CreateDbContext(); return await db.JobRuns.CountAsync(x => x.Job == job); }

    [Fact] public async Task FailedJobsAreRetriedWithBackoffThenRecover()
    {
        var job = new Flaky(failures: 2);
        var runner = Runner(job);
        Assert.Equal(1, await runner.RunDue());
        var s = await State("flaky");
        Assert.Equal((1, false), (s.ConsecutiveFailures, s.DeadLettered));
        Assert.Equal(0, await runner.RunDue()); // not due yet: waits a minute
        clock.Advance(TimeSpan.FromMinutes(1)); await runner.RunDue();
        Assert.Equal(2, (await State("flaky")).ConsecutiveFailures);
        clock.Advance(TimeSpan.FromMinutes(1)); Assert.Equal(0, await runner.RunDue()); // second retry waits two minutes
        clock.Advance(TimeSpan.FromMinutes(1)); await runner.RunDue();
        s = await State("flaky");
        Assert.Equal((0, false, "done 3"), (s.ConsecutiveFailures, s.DeadLettered, s.Checkpoint)); // recovered, checkpoint saved
        Assert.Equal(3, await Runs("flaky"));

        var restarted = Runner(new Flaky(failures: 0)); // a new process picks up the saved schedule
        Assert.Equal(0, await restarted.RunDue());
        clock.Advance(TimeSpan.FromHours(1)); Assert.Equal(1, await restarted.RunDue());
    }

    [Fact] public async Task PermanentOrRepeatedFailuresStopTheJobAndAlertAdministrators()
    {
        var runner = Runner(new Broken(permanent: true), new Flaky(failures: 100));
        await runner.RunDue();
        Assert.True((await State("broken")).DeadLettered); // a configuration fault is not retried
        for (var i = 0; i < JobRunner.MaxFailures; i++) { await runner.RunDue(); clock.Advance(TimeSpan.FromHours(1)); }
        var flaky = await State("flaky");
        Assert.True(flaky.DeadLettered); Assert.Equal(JobRunner.MaxFailures, flaky.ConsecutiveFailures);
        clock.Advance(TimeSpan.FromHours(2)); Assert.Equal(0, await runner.RunDue()); // stopped jobs wait for an administrator
        Assert.Contains("Stopped after failures", await JobHealth.Problem(factory, "FREETOWN", runner, clock.GetUtcNow()));

        var ops = new OperationsService(factory, session, options, runner, new ServiceCollection().BuildServiceProvider());
        var reminders = await ops.Reminders();
        Assert.Equal(2, reminders.Count(x => x.Kind == ReminderKind.JobFailing));
        Assert.DoesNotContain("password=hunter2", (await ops.Diagnostics()).Jobs.Single(x => x.Job == "broken").LastMessage); // secrets are masked
        session.Actor = new("clerk", "FREETOWN", new HashSet<string> { "business.read", "sales.create" });
        Assert.Empty(await ops.Reminders()); // only administrators see job failures
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ops.Diagnostics());
        session.Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet());
        var rerun = await ops.RunNow("broken");
        Assert.Equal(JobOutcome.DeadLettered, rerun.Outcome);
    }

    [Fact] public async Task HealthIsDegradedWhenAJobHasNotRunOnTime()
    {
        var runner = Runner(new Flaky(failures: 0));
        await runner.RunDue();
        Assert.Null(await JobHealth.Problem(factory, "FREETOWN", runner, clock.GetUtcNow()));
        Assert.Contains("Not run on time", await JobHealth.Problem(factory, "FREETOWN", runner, clock.GetUtcNow().AddHours(5))); // interval 1 h: three missed intervals
    }

    [Fact] public async Task RemindersAreRaisedOnceAndDismissedPerUser()
    {
        await TestParties.Register(factory, "Kissy Clinic");
        var widget = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today.AddDays(-90), [new(widget, 5, 40)], null));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Kissy Clinic", today.AddDays(-80), [new(widget, 2, 100)], null, DueDate: today.AddDays(-50)));
        await new InventoryManagement(factory, session, options).SaveReorder(new(Guid.NewGuid(), new(widget, "MAIN", 0, 10, 20)));
        await new TenderService(factory, session, options).Create(new(Guid.NewGuid(), "MOH/2026/14", "Ministry of Health", "Oxygen concentrators", today.AddDays(2)));

        var runner = Runner(new RemindersJob());
        var first = await runner.RunNow("reminders");
        Assert.Equal(JobOutcome.Succeeded, first.Outcome);
        await runner.RunNow("reminders"); // running again (or after a restart) adds nothing new
        var ops = new OperationsService(factory, session, options, runner, new ServiceCollection().BuildServiceProvider());
        var list = await ops.Reminders();
        Assert.Single(list, x => x.Kind == ReminderKind.TenderClosing && x.Title.Contains("closes in 2 days"));
        Assert.Single(list, x => x.Kind == ReminderKind.InvoiceOverdue && x.Title.StartsWith("Kissy Clinic owes NLe 200.00"));
        Assert.Single(list, x => x.Kind == ReminderKind.LowStock);

        await ops.Dismiss(list.First(x => x.Kind == ReminderKind.TenderClosing).Id);
        Assert.DoesNotContain(await ops.Reminders(), x => x.Kind == ReminderKind.TenderClosing);
        session.Actor = new("clerk", "FREETOWN", new HashSet<string> { "business.read", "sales.create" });
        var clerk = await ops.Reminders();
        Assert.Contains(clerk, x => x.Kind == ReminderKind.TenderClosing); // dismissed for the owner only
        Assert.DoesNotContain(clerk, x => x.Kind == ReminderKind.InvoiceOverdue); // receivables need accounting.read
    }

    [Fact] public async Task PendingFiscalInvoicesAreSentWhenTheDeviceComesBack()
    {
        var widget = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(widget, 5, 40)], null));
        await new ComplianceService(factory, session, options, new NoFiscalDevice()).SaveProfile(new(Guid.NewGuid(), new CompanyProfile(LegalName: "Index Ltd", FiscalFrom: today)));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(widget, 1, 100)], PaymentMethod.Cash));
        var device = new Device();
        var services = new ServiceCollection().AddSingleton<IFiscalIntegration>(device).BuildServiceProvider();
        var runner = new JobRunner(factory, options, [new FiscalRetryJob()], services, null, clock);
        await runner.RunDue();
        Assert.Equal(1, (await State("fiscal")).ConsecutiveFailures); // device offline: retried later, nothing lost
        device.Online = true; clock.Advance(TimeSpan.FromMinutes(1));
        await runner.RunDue();
        var s = await State("fiscal");
        Assert.Equal(0, s.ConsecutiveFailures); Assert.Contains("1 registered", s.LastMessage);
    }

    private sealed class Flaky(int failures) : IBackgroundJob
    {
        private int calls;
        public string Name => "flaky"; public string Description => "Flaky test job"; public TimeSpan Interval => TimeSpan.FromHours(1);
        public Task<string> Run(JobContext context, CancellationToken ct) { calls++; if (calls <= failures) throw new IOException("Network unreachable"); context.NewCheckpoint = $"done {calls}"; return Task.FromResult("ok"); }
    }
    private sealed class Broken(bool permanent) : IBackgroundJob
    {
        public string Name => "broken"; public string Description => "Misconfigured job"; public TimeSpan Interval => TimeSpan.FromHours(1);
        public Task<string> Run(JobContext context, CancellationToken ct) => permanent ? throw new PermanentJobException("Server rejected login password=hunter2") : throw new IOException("down");
    }
    private sealed class Device : IFiscalIntegration
    {
        public bool Online; public string Name => "Test device"; public bool Available => Online;
        public Task<FiscalResult> Register(FiscalInvoice invoice, CancellationToken cancellationToken = default) => Task.FromResult(new FiscalResult(true, "FC-1"));
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider { private DateTimeOffset current = now; public override DateTimeOffset GetUtcNow() => current; public void Advance(TimeSpan by) => current += by; }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
