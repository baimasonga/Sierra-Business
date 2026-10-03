namespace SierraBusiness.Application;

/// <summary>Result of re-checking every posted record against the tamper-evidence chain.</summary>
public sealed record IntegrityReport(bool Ok, int Seals, int Rows, string Head, bool DatabaseGuards, IReadOnlyList<string> Problems, IReadOnlyList<string> Notes, DateTimeOffset CheckedAt);
public interface IIntegrityService { Task<IntegrityReport> Verify(); }

public sealed record BackupSettingsView(bool Automatic, int IntervalHours, int KeepAutomatic, string CopyFolder);
public sealed record BackupFileView(string Id, string Name, string Location, string Kind, DateTimeOffset Created, long Size, string Sha256, string SealHead);
public sealed record BackupResult(string Path, string CopyStatus);
public sealed record RestoreResult(string RestoredFrom, string SafetyBackup, IntegrityReport Integrity);

/// <summary>Local database backups; available in the Windows desktop host only.</summary>
public interface IBackupManager
{
    Task<BackupSettingsView> Settings();
    Task SaveSettings(BackupSettingsView settings);
    Task<IReadOnlyList<BackupFileView>> List();
    Task<BackupResult> Create();
    Task<RestoreResult> Restore(string backupId, string confirmation);
    Task<string> AutomaticStatus();
}
