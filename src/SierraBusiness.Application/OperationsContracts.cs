namespace SierraBusiness.Application;

/// <summary>Specification Skill 25 (0.23.0): background jobs, reminders and diagnostics.</summary>
public enum JobOutcome { Succeeded, Failed, DeadLettered }
public sealed record JobStatusView(string Job, string Description, TimeSpan Interval, DateTimeOffset? LastRun, DateTimeOffset? LastSuccess, DateTimeOffset? NextDue, int ConsecutiveFailures, bool DeadLettered, string LastMessage, string Checkpoint)
{
    public bool Overdue(DateTimeOffset now) => !DeadLettered && NextDue is DateTimeOffset due && now - due > Interval * 3;
}
public sealed record JobRunView(string Job, DateTimeOffset Started, DateTimeOffset Finished, JobOutcome Outcome, int Attempt, string Message);
public sealed record DiagnosticsView(string Version, string Runtime, string OperatingSystem, string Database, long DatabaseBytes, string SchemaVersion, int PendingMigrations, string Branch, string Device,
    DateTimeOffset ServerTime, IReadOnlyList<JobStatusView> Jobs, IReadOnlyList<JobRunView> Runs, string Backup, string Integrity, IReadOnlyDictionary<string, int> Counts)
{
    public string Health => Jobs.Any(x => x.DeadLettered) || PendingMigrations > 0 ? "Attention needed" : Jobs.Any(x => x.ConsecutiveFailures > 0 || x.Overdue(ServerTime)) ? "Degraded" : "Healthy";
}

public enum ReminderKind { TenderClosing, MaintenanceDue, WarrantyExpiring, InvoiceOverdue, ApprovalWaiting, StatutoryDue, LowStock, DocumentDeadline, JobFailing }
/// <param name="Page">The screen where the reminder is dealt with.</param>
public sealed record ReminderView(Guid Id, ReminderKind Kind, string Title, string Detail, DateOnly? Due, DateTimeOffset Created, string Page);

public interface IOperationsService
{
    Task<DiagnosticsView> Diagnostics();
    Task<IReadOnlyList<ReminderView>> Reminders();
    Task Dismiss(Guid reminderId);
    /// <summary>Runs a job now (and clears it from the failed list), returning the result.</summary>
    Task<JobRunView> RunNow(string job);
}
