using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

/// <summary>Where each job stands; one row per branch and job.</summary>
public sealed class JobState { public string Branch { get; set; } = ""; public string Job { get; set; } = ""; public DateTimeOffset? LastRun { get; set; } public DateTimeOffset? LastSuccess { get; set; } public DateTimeOffset? NextDue { get; set; } public int ConsecutiveFailures { get; set; } public bool DeadLettered { get; set; } public string LastMessage { get; set; } = ""; public string Checkpoint { get; set; } = ""; }
// Append-only records (database guards and integrity seals).
public sealed class JobRun { public Guid Id { get; set; } public string Branch { get; set; } = ""; public string Job { get; set; } = ""; public DateTimeOffset Started { get; set; } public DateTimeOffset Finished { get; set; } public JobOutcome Outcome { get; set; } public int Attempt { get; set; } public string Message { get; set; } = ""; }
public sealed class Reminder { public Guid Id { get; set; } public string Branch { get; set; } = ""; public string Key { get; set; } = ""; public ReminderKind Kind { get; set; } public string Title { get; set; } = ""; public string Detail { get; set; } = ""; public DateOnly? Due { get; set; } public string Permission { get; set; } = "business.read"; public string Page { get; set; } = ""; public DateTimeOffset Created { get; set; } }
public sealed class ReminderDismissal { public Guid Id { get; set; } public Guid ReminderId { get; set; } public string Actor { get; set; } = ""; public DateTimeOffset Time { get; set; } }

/// <summary>The identity background jobs act as: the configured branch with every permission. Recorded as "system".</summary>
public sealed class SystemSession(IOptions<TradingOptions> options) : IActorSession
{
    public Task<Actor> CurrentActor() => Task.FromResult(new Actor("system", options.Value.Branch, BusinessPermissions.All.ToHashSet()));
}

/// <summary>A failure that retrying will not fix (for example invalid configuration): the job stops and waits for an administrator.</summary>
public sealed class PermanentJobException(string message) : Exception(message);

public sealed record JobContext(IDbContextFactory<BusinessDb> Factory, IOptions<TradingOptions> Options, IActorSession System, IServiceProvider Services, string Checkpoint)
{
    public string Branch => Options.Value.Branch;
    public string NewCheckpoint { get; set; } = Checkpoint;
}
public interface IBackgroundJob
{
    string Name { get; }
    string Description { get; }
    TimeSpan Interval { get; }
    /// <summary>Does the work; must be safe to run again after a failure part-way. Returns a short summary.</summary>
    Task<string> Run(JobContext context, CancellationToken cancellationToken);
}

/// <summary>Runs due jobs with bounded retry: a failed job is retried after 1, 2, 4… minutes (at most an hour); after 8 failures in a row, or a permanent failure, it stops and raises a reminder for administrators until someone runs it again.</summary>
public sealed class JobRunner(IDbContextFactory<BusinessDb> factory, IOptions<TradingOptions> options, IEnumerable<IBackgroundJob> jobs, IServiceProvider services, ILogger<JobRunner>? logger = null, TimeProvider? clock = null)
{
    public const int MaxFailures = 8;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ILogger log = logger ?? NullLogger<JobRunner>.Instance;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public IReadOnlyList<IBackgroundJob> Jobs { get; } = jobs.ToList();
    private string Branch => options.Value.Branch;

    /// <summary>False until the database exists and is fully upgraded (fresh installs and upgrades run their migrations first).</summary>
    public async Task<bool> Ready(CancellationToken ct = default)
    {
        try { await using var db = await factory.CreateDbContextAsync(ct); return await db.Database.CanConnectAsync(ct) && !(await db.Database.GetPendingMigrationsAsync(ct)).Any(); }
        catch (Exception) { return false; }
    }

    public async Task<int> RunDue(CancellationToken ct = default)
    {
        if (!await Ready(ct)) return 0;
        var ran = 0;
        foreach (var job in Jobs)
        {
            var state = await State(job.Name, ct);
            if (state.DeadLettered || state.NextDue is DateTimeOffset due && due > time.GetUtcNow()) continue;
            await Run(job, ct); ran++;
        }
        return ran;
    }

    public async Task<JobRunView> RunNow(string name, CancellationToken ct = default)
    {
        var job = Jobs.SingleOrDefault(x => x.Name == name) ?? throw new BusinessException("Unknown job.");
        if (!await Ready(ct)) throw new BusinessException("The database is not ready yet.");
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var s = await db.JobStates.SingleOrDefaultAsync(x => x.Branch == Branch && x.Job == name, ct);
            if (s is not null) { s.DeadLettered = false; s.ConsecutiveFailures = 0; await db.SaveChangesAsync(ct); }
        }
        return await Run(job, ct);
    }

    private async Task<JobState> State(string name, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.JobStates.AsNoTracking().SingleOrDefaultAsync(x => x.Branch == Branch && x.Job == name, ct) ?? new() { Branch = Branch, Job = name };
    }

    private async Task<JobRunView> Run(IBackgroundJob job, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var state = await State(job.Name, ct);
            var started = time.GetUtcNow(); var attempt = state.ConsecutiveFailures + 1;
            var context = new JobContext(factory, options, new SystemSession(options), services, state.Checkpoint);
            JobOutcome outcome; string message;
            using (log.BeginScope(new Dictionary<string, object> { ["Branch"] = Branch, ["Job"] = job.Name, ["Attempt"] = attempt }))
            {
                try
                {
                    message = await job.Run(context, ct); outcome = JobOutcome.Succeeded;
                    log.LogInformation("Job {Job} succeeded: {Message}", job.Name, message);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    var permanent = ex is PermanentJobException or BusinessException or UnauthorizedAccessException;
                    message = Clean(ex.Message);
                    outcome = permanent || attempt >= MaxFailures ? JobOutcome.DeadLettered : JobOutcome.Failed;
                    log.LogWarning("Job {Job} {Outcome} on attempt {Attempt}: {Message}", job.Name, outcome, attempt, message);
                }
            }
            var finished = time.GetUtcNow();
            await using var db = await factory.CreateDbContextAsync(ct);
            var s = await db.JobStates.SingleOrDefaultAsync(x => x.Branch == Branch && x.Job == job.Name, ct);
            if (s is null) { s = new() { Branch = Branch, Job = job.Name }; db.JobStates.Add(s); }
            s.LastRun = finished; s.LastMessage = message;
            if (outcome == JobOutcome.Succeeded) { s.LastSuccess = finished; s.ConsecutiveFailures = 0; s.DeadLettered = false; s.NextDue = finished + job.Interval; s.Checkpoint = context.NewCheckpoint; }
            else
            {
                s.ConsecutiveFailures = attempt;
                s.DeadLettered = outcome == JobOutcome.DeadLettered;
                s.NextDue = finished + TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, attempt - 1)));
                if (s.DeadLettered && !await db.Reminders.AnyAsync(x => x.Branch == Branch && x.Key == $"job:{job.Name}:{finished:yyyyMMddHH}", ct))
                    db.Reminders.Add(new() { Id = Guid.NewGuid(), Branch = Branch, Key = $"job:{job.Name}:{finished:yyyyMMddHH}", Kind = ReminderKind.JobFailing, Title = $"Background task stopped: {job.Description}",
                        Detail = $"{message} Open Administration → System health and choose Run now once the cause is fixed.", Permission = "users.manage", Page = "System health", Created = finished });
            }
            db.JobRuns.Add(new() { Id = Guid.NewGuid(), Branch = Branch, Job = job.Name, Started = started, Finished = finished, Outcome = outcome, Attempt = attempt, Message = message });
            await db.SaveChangesAsync(ct);
            return new(job.Name, started, finished, outcome, attempt, message);
        }
        finally { gate.Release(); }
    }

    /// <summary>Error text without connection strings, paths or tokens.</summary>
    private static string Clean(string message)
    {
        message = System.Text.RegularExpressions.Regex.Replace(message, @"(?i)(password|pwd|token|secret|key)\s*=\s*[^;\s]+", "$1=***");
        return message.Length > 400 ? message[..400] : message;
    }
}

/// <summary>Checks for due jobs every minute while the application runs.</summary>
public sealed class JobScheduler(JobRunner runner, ILogger<JobScheduler>? logger = null) : IAsyncDisposable
{
    private CancellationTokenSource? stop; private Task? loop;
    public void Start(TimeSpan? period = null)
    {
        if (loop is not null) return;
        stop = new();
        loop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(period ?? TimeSpan.FromMinutes(1));
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), stop.Token); // let the application finish starting
                do
                {
                    try { await runner.RunDue(stop.Token); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { (logger ?? (ILogger)NullLogger.Instance).LogError(ex, "Background job check failed."); }
                } while (await timer.WaitForNextTickAsync(stop.Token));
            }
            catch (OperationCanceledException) { }
        });
    }
    public async ValueTask DisposeAsync()
    {
        if (stop is null || loop is null) return;
        await stop.CancelAsync(); try { await loop; } catch (OperationCanceledException) { }
        stop.Dispose(); loop = null;
    }
}

// ---------------- jobs ----------------

/// <summary>Creates reminders: tenders closing, maintenance due, warranties expiring, overdue customers, approvals waiting, PAYE/NASSIT outstanding, low stock and document deadlines. Each reminder is created once (by key).</summary>
public sealed class RemindersJob : IBackgroundJob
{
    public string Name => "reminders"; public string Description => "Reminders"; public TimeSpan Interval => TimeSpan.FromHours(6);
    public async Task<string> Run(JobContext c, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow); var now = DateTimeOffset.UtcNow;
        var found = new List<Reminder>(); var problems = new List<string>();
        Reminder R(string key, ReminderKind kind, string title, string detail, DateOnly? due, string permission, string page) => new() { Id = Guid.NewGuid(), Branch = c.Branch, Key = key, Kind = kind, Title = title, Detail = detail, Due = due, Permission = permission, Page = page, Created = now };
        async Task Source(string name, Func<Task> work) { try { await work(); } catch (Exception ex) when (ex is not OperationCanceledException) { problems.Add($"{name}: {ex.Message}"); } }
        var week = System.Globalization.ISOWeek.GetWeekOfYear(DateTime.UtcNow);

        await Source("tenders", async () =>
        {
            foreach (var t in (await new TenderService(c.Factory, c.System, c.Options).List()).Where(t => t.Status is TenderStatus.Draft or TenderStatus.BidPreparation or TenderStatus.Approved or TenderStatus.Clarification && t.DaysLeft is >= 0 and <= 3))
                found.Add(R($"tender:{t.Id}:{t.Closing:yyyyMMdd}", ReminderKind.TenderClosing, $"Tender {t.Reference} closes {(t.DaysLeft == 0 ? "today" : $"in {t.DaysLeft} day{(t.DaysLeft == 1 ? "" : "s")}")}", $"{t.Institution} · {t.Title} · {t.Status}", t.Closing, "sales.create", "Tenders"));
        });
        await Source("maintenance", async () =>
        {
            foreach (var p in (await new ServiceDesk(c.Factory, c.System, c.Options).Read()).Plans.Where(p => p.Active && p.NextDue is DateOnly d && d <= today.AddDays(7)))
                found.Add(R($"plan:{p.Id}:{p.NextDue:yyyyMMdd}", ReminderKind.MaintenanceDue, $"Maintenance visit due {p.NextDue:dd MMM}: {p.Customer}", $"{p.Sku} · {p.Name}{(p.Serial.Length > 0 ? " · serial " + p.Serial : "")} · plan {p.Number}", p.NextDue, "business.read", "Service jobs"));
        });
        await Source("warranties", async () =>
        {
            foreach (var w in (await new EquipmentService(c.Factory, c.System, c.Options).Warranties()).Where(w => w.Status == "Active" && w.End >= today && w.End <= today.AddDays(30)))
                found.Add(R($"warranty:{w.Id}:{w.End:yyyyMMdd}", ReminderKind.WarrantyExpiring, $"Warranty ends {w.End:dd MMM yyyy}: {w.Customer}", $"{w.Sku} serial {w.Serial}. Offer a maintenance contract.", w.End, "business.read", "Equipment lookup"));
        });
        await Source("receivables", async () =>
        {
            foreach (var r in (await new ReportService(c.Factory, c.System, c.Options).Ageing(today, true)).Rows.Where(r => r.Total - r.Current > 0))
                found.Add(R($"overdue:{r.Party.ToUpperInvariant()}:{today.Year}-{week}", ReminderKind.InvoiceOverdue, $"{r.Party} owes NLe {r.Total - r.Current:N2} overdue", $"Total balance NLe {r.Total:N2}; over 90 days NLe {r.Over90:N2}. Send a statement and follow up.", null, "accounting.read", "Customers & suppliers"));
        });
        await Source("approvals", async () =>
        {
            foreach (var a in (await new ControlService(c.Factory, c.System, c.Options).Approvals()).Where(a => a.State == ApprovalState.Pending && a.RequestedAt < now.AddHours(-4)))
                found.Add(R($"approval:{a.Id}", ReminderKind.ApprovalWaiting, $"Approval {a.Number} is still waiting", $"{a.Summary} · requested by {a.RequestedBy}", null, "business.read", "Approvals"));
        });
        await Source("payroll", async () =>
        {
            var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
            var s = await new PayrollService(c.Factory, c.System, c.Options).Schedule(month);
            if (s.PayeOutstanding > 0) found.Add(R($"paye:{month:yyyy-MM}", ReminderKind.StatutoryDue, $"PAYE for {month:MMMM yyyy} not yet remitted: NLe {s.PayeOutstanding:N2}", "Remit to the NRA and record it under Payroll → PAYE & NASSIT. Check the NRA due date.", null, "accounting.write", "Payroll"));
            if (s.NassitOutstanding > 0) found.Add(R($"nassit:{month:yyyy-MM}", ReminderKind.StatutoryDue, $"NASSIT for {month:MMMM yyyy} not yet remitted: NLe {s.NassitOutstanding:N2}", "Remit to NASSIT and record it under Payroll → PAYE & NASSIT. Check the NASSIT due date.", null, "accounting.write", "Payroll"));
        });
        await Source("stock", async () =>
        {
            await using var db = await c.Factory.CreateDbContextAsync(ct);
            var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
            foreach (var r in (await new InventoryManagement(c.Factory, c.System, c.Options).Reorder()).Where(r => r.SuggestedQuantity > 0 && products.ContainsKey(r.Rule.ProductId)))
            {
                var p = products[r.Rule.ProductId];
                found.Add(R($"stock:{p.Id}:{r.Rule.Location}:{today.Year}-{week}", ReminderKind.LowStock, $"Reorder {p.Sku} · {p.Name}", $"{r.OnHand:0.##} {p.Unit} on hand at {r.Rule.Location}; suggested order {r.SuggestedQuantity:0.##}.", null, "business.read", "Warehouse controls"));
            }
        });
        await Source("documents", async () =>
        {
            if (c.Services.GetService<IFileStore>() is not { } files) return;
            foreach (var d in await new DocumentLibrary(c.Factory, c.System, c.Options, files).Deadlines(14))
                found.Add(R($"document:{d.Id}:{d.DueDate:yyyyMMdd}", ReminderKind.DocumentDeadline, $"{d.Title} {(d.DaysLeft < 0 ? "is past its date" : $"due {d.DueDate:dd MMM}")}", $"{d.Number} · {d.Type}{(d.Party.Length > 0 ? " · " + d.Party : "")}", d.DueDate, "business.read", "Document library"));
        });

        await using (var db = await c.Factory.CreateDbContextAsync(ct))
        {
            var keys = found.Select(x => x.Key).ToList();
            var existing = (await db.Reminders.Where(x => x.Branch == c.Branch && keys.Contains(x.Key)).Select(x => x.Key).ToListAsync(ct)).ToHashSet();
            var added = found.Where(x => existing.Add(x.Key)).ToList();
            db.Reminders.AddRange(added); await db.SaveChangesAsync(ct);
            c.NewCheckpoint = today.ToString("yyyy-MM-dd");
            if (problems.Count > 0) throw new InvalidOperationException($"{added.Count} reminders added, but some sources failed: {string.Join("; ", problems)}");
            return $"{added.Count} new reminder{(added.Count == 1 ? "" : "s")} ({found.Count} current).";
        }
    }
}

/// <summary>Sends invoices waiting for fiscal registration whenever the fiscal device or service is available.</summary>
public sealed class FiscalRetryJob : IBackgroundJob
{
    public string Name => "fiscal"; public string Description => "Fiscal registration of pending invoices"; public TimeSpan Interval => TimeSpan.FromMinutes(15);
    public async Task<string> Run(JobContext c, CancellationToken ct)
    {
        var fiscal = c.Services.GetService<IFiscalIntegration>() ?? new NoFiscalDevice();
        var service = new ComplianceService(c.Factory, c.System, c.Options, fiscal);
        var status = await service.Fiscal();
        if (!status.Enabled || status.Pending == 0) return status.Enabled ? "Nothing pending." : "Fiscal registration is off.";
        if (!fiscal.Available) throw new InvalidOperationException($"{status.Pending} invoice(s) pending; the fiscal device ({fiscal.Name}) is not available. Will retry.");
        var result = await service.RegisterPending();
        if (result.Registered == 0 && result.StillPending > 0) throw new InvalidOperationException(result.Message);
        return result.Message;
    }
}

/// <summary>Verifies the history seals every day so tampering or disk damage is noticed early.</summary>
public sealed class IntegrityJob : IBackgroundJob
{
    public string Name => "integrity"; public string Description => "Daily integrity check of posted history"; public TimeSpan Interval => TimeSpan.FromHours(24);
    public async Task<string> Run(JobContext c, CancellationToken ct)
    {
        await using var db = await c.Factory.CreateDbContextAsync(ct);
        var report = await HistoryProtection.Verify(db);
        if (!report.Ok) throw new PermanentJobException("Integrity check found problems: " + string.Join("; ", report.Problems.Take(5)));
        return "Posted history verified.";
    }
}

public sealed class OperationsService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options, JobRunner runner, IServiceProvider services) : IOperationsService
{
    private async Task<Actor> Authorize(bool admin)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        if (admin && !actor.Permissions.Contains("audit.read") && !actor.Permissions.Contains("users.manage")) throw new UnauthorizedAccessException("System health needs audit.read or users.manage.");
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }

    public async Task<DiagnosticsView> Diagnostics()
    {
        var actor = await Authorize(true); await using var db = await factory.CreateDbContextAsync();
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? typeof(OperationsService).Assembly.GetName().Version?.ToString() ?? "";
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList(); var pending = (await db.Database.GetPendingMigrationsAsync()).Count();
        var provider = db.Database.ProviderName ?? ""; long bytes = 0; var database = provider.Split('.').Last();
        if (provider.Contains("Sqlite", StringComparison.Ordinal))
        {
            var path = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
            if (File.Exists(path)) bytes = new FileInfo(path).Length;
            database = "SQLite (local file)";
        }
        var states = await db.JobStates.AsNoTracking().Where(x => x.Branch == actor.Branch).ToDictionaryAsync(x => x.Job);
        var jobs = runner.Jobs.Select(j => { states.TryGetValue(j.Name, out var s); return new JobStatusView(j.Name, j.Description, j.Interval, s?.LastRun, s?.LastSuccess, s?.NextDue, s?.ConsecutiveFailures ?? 0, s?.DeadLettered ?? false, s?.LastMessage ?? "Not run yet.", s?.Checkpoint ?? ""); }).ToArray();
        var runs = (await db.JobRuns.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync()).OrderByDescending(x => x.Started).Take(50).Select(r => new JobRunView(r.Job, r.Started, r.Finished, r.Outcome, r.Attempt, r.Message)).ToArray();
        var backup = "Managed by the central server.";
        if (services.GetService<IBackupManager>() is { } backups) try { backup = await backups.AutomaticStatus(); } catch (Exception ex) when (ex is UnauthorizedAccessException or BusinessException) { backup = "Backup status needs backups.create."; }
        var integrity = states.TryGetValue("integrity", out var i) ? $"{(i.ConsecutiveFailures == 0 ? "OK" : "Problem")} · {i.LastMessage} ({i.LastRun?.ToLocalTime():dd MMM yyyy HH:mm})" : "Not checked yet.";
        var counts = new Dictionary<string, int>
        {
            ["Documents"] = await db.Documents.CountAsync(x => x.Branch == actor.Branch), ["Products"] = await db.Products.CountAsync(),
            ["Customers & suppliers"] = await db.Parties.CountAsync(x => x.Branch == actor.Branch), ["Employees"] = await db.Employees.CountAsync(x => x.Branch == actor.Branch),
            ["Library documents"] = await db.StoredDocuments.CountAsync(x => x.Branch == actor.Branch), ["Users"] = await db.Users.CountAsync()
        };
        return new(version, RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription, database, bytes, applied.LastOrDefault() ?? "none", pending, actor.Branch, options.Value.Device,
            DateTimeOffset.UtcNow, jobs, runs, backup, integrity, counts);
    }

    public async Task<IReadOnlyList<ReminderView>> Reminders()
    {
        var actor = await Authorize(false); await using var db = await factory.CreateDbContextAsync();
        var since = DateTimeOffset.UtcNow.AddDays(-60); var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dismissed = (await db.ReminderDismissals.AsNoTracking().Where(x => x.Actor == actor.Id).Select(x => x.ReminderId).ToListAsync()).ToHashSet();
        var rows = (await db.Reminders.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync()).Where(x => x.Created >= since && (x.Due is null || x.Due >= today.AddDays(-30)));
        return rows.Where(x => !dismissed.Contains(x.Id) && actor.Permissions.Contains(x.Permission))
            .OrderBy(x => x.Kind == ReminderKind.JobFailing ? 0 : 1).ThenBy(x => x.Due ?? DateOnly.MaxValue).ThenByDescending(x => x.Created)
            .Select(x => new ReminderView(x.Id, x.Kind, x.Title, x.Detail, x.Due, x.Created, x.Page)).ToArray();
    }

    public async Task Dismiss(Guid reminderId)
    {
        var actor = await Authorize(false); await using var db = await factory.CreateDbContextAsync();
        if (!await db.Reminders.AnyAsync(x => x.Id == reminderId && x.Branch == actor.Branch)) throw new BusinessException("Reminder not found.");
        if (await db.ReminderDismissals.AnyAsync(x => x.ReminderId == reminderId && x.Actor == actor.Id)) return;
        db.ReminderDismissals.Add(new() { Id = Guid.NewGuid(), ReminderId = reminderId, Actor = actor.Id, Time = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task<JobRunView> RunNow(string job) { await Authorize(true); return await runner.RunNow(job); }
}

/// <summary>The health of background jobs, shared by the web health check and the diagnostics screen.</summary>
public static class JobHealth
{
    public static async Task<string?> Problem(IDbContextFactory<BusinessDb> factory, string branch, JobRunner runner, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var states = await db.JobStates.AsNoTracking().Where(x => x.Branch == branch).ToDictionaryAsync(x => x.Job, ct);
        var stopped = states.Values.Where(x => x.DeadLettered).Select(x => x.Job).ToList();
        if (stopped.Count > 0) return $"Stopped after failures: {string.Join(", ", stopped)}.";
        var late = runner.Jobs.Where(j => states.TryGetValue(j.Name, out var s) && s.NextDue is DateTimeOffset due && now - due > j.Interval * 3).Select(j => j.Name).ToList();
        return late.Count > 0 ? $"Not run on time: {string.Join(", ", late)}." : null;
    }
}

public static class OperationsRegistration
{
    /// <summary>Background jobs, their runner and scheduler, and the diagnostics service.</summary>
    public static IServiceCollection AddSierraOperations(this IServiceCollection services)
    {
        services.AddSingleton<IBackgroundJob, RemindersJob>();
        services.AddSingleton<IBackgroundJob, FiscalRetryJob>();
        services.AddSingleton<IBackgroundJob, IntegrityJob>();
        services.AddSingleton<JobRunner>();
        services.AddSingleton<JobScheduler>();
        services.AddTransient<IOperationsService, OperationsService>();
        return services;
    }
}
