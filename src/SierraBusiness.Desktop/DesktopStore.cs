using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

public sealed record BackupManifest(string Kind, DateTimeOffset Created, string Sha256, long Size, string SealHead, string Migration);

/// <summary>Per-Windows-user durable data, independent of the installation directory.</summary>
public sealed class DesktopStore(string directory) : IDisposable
{
    private const string SettingsFile = "backup-settings.json", ManifestSuffix = ".manifest.json", CopySubfolder = "SierraBusiness-Backups";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private FileStream? instanceLock;
    private IDbContextFactory<BusinessDb>? factory;
    private bool ready;
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string DatabasePath => Path.Combine(DirectoryPath, "sierra.db");
    public string BackupDirectory => Path.Combine(DirectoryPath, "Backups");
    public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true }.ToString();
    /// <summary>Outcome of the most recent automatic backup attempt, shown on the Backups screen.</summary>
    public string LastAutomaticStatus { get; private set; } = "No automatic backup has run since the application started.";

    public BackupSettingsView ReadSettings()
    {
        var path = Path.Combine(DirectoryPath, SettingsFile);
        try { return File.Exists(path) ? JsonSerializer.Deserialize<BackupSettingsView>(File.ReadAllText(path)) ?? Defaults : Defaults; }
        catch (JsonException) { return Defaults; } // A damaged settings file must never stop backups; defaults keep them running.
    }
    private static BackupSettingsView Defaults => new(true, 24, 14, "");

    public void SaveSettings(BackupSettingsView settings)
    {
        if (settings.IntervalHours is < 1 or > 168) throw new BusinessException("Automatic backups must run every 1 to 168 hours.");
        if (settings.KeepAutomatic is < 1 or > 365) throw new BusinessException("Keep between 1 and 365 automatic backups.");
        var folder = (settings.CopyFolder ?? "").Trim();
        if (folder.Length > 0)
        {
            if (!Path.IsPathFullyQualified(folder)) throw new BusinessException("Enter the full path of the copy folder, for example E:\\SierraBackups.");
            folder = Path.GetFullPath(folder);
            if (folder.StartsWith(DirectoryPath, StringComparison.OrdinalIgnoreCase)) throw new BusinessException("The copy folder must be outside the application's data folder, preferably on another drive.");
            if (!Directory.Exists(folder)) throw new BusinessException("The copy folder does not exist or the drive is not connected.");
            var probe = Path.Combine(folder, $".sierra-write-test-{Guid.NewGuid():N}");
            try { File.WriteAllText(probe, "test"); File.Delete(probe); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new BusinessException("Sierra Business cannot write to the copy folder. Choose a folder you can save files in."); }
        }
        File.WriteAllText(Path.Combine(DirectoryPath, SettingsFile), JsonSerializer.Serialize(settings with { CopyFolder = folder }, Indented));
    }

    private string? CopyDirectory(BackupSettingsView settings) => settings.CopyFolder.Length == 0 ? null : Path.Combine(settings.CopyFolder, CopySubfolder, Path.GetFileName(DirectoryPath));

    public async Task<string> CreateBackup() => (await CreateBackupWithCopy()).Path;

    public async Task<BackupResult> CreateBackupWithCopy(string kind = "manual")
    {
        await gate.WaitAsync();
        try
        {
            if (!ready) throw new InvalidOperationException("Open the local workspace before creating a backup.");
            return await WriteBackup(kind);
        }
        finally { gate.Release(); }
    }

    private async Task<BackupResult> WriteBackup(string prefix)
    {
        Directory.CreateDirectory(BackupDirectory);
        var path = Path.Combine(BackupDirectory, $"{prefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        var temporary = path + ".partial";
        BackupManifest manifest;
        try
        {
            await using (var source = new SqliteConnection(ConnectionString))
            await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            {
                await source.OpenAsync();
                await destination.OpenAsync();
                source.BackupDatabase(destination);
                if (await Scalar(destination, "PRAGMA integrity_check") != "ok") throw new InvalidOperationException("Backup verification failed. Try again before relying on this backup.");
                manifest = new(prefix, DateTimeOffset.UtcNow, "", 0, await TryScalar(destination, "SELECT Hash FROM IntegritySeals ORDER BY Sequence DESC LIMIT 1"), await TryScalar(destination, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1"));
            }
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        manifest = manifest with { Sha256 = await FileHash(path), Size = new FileInfo(path).Length };
        await File.WriteAllTextAsync(path + ManifestSuffix, JsonSerializer.Serialize(manifest, Indented));
        return new(path, await CopyOffMachine(path, manifest));
    }

    /// <summary>Copies the verified backup to the configured second location. A failed copy never invalidates the local backup.</summary>
    private async Task<string> CopyOffMachine(string path, BackupManifest manifest)
    {
        var target = CopyDirectory(ReadSettings());
        if (target is null) return "No copy folder is set. Set one under Backups so a copy survives if this computer fails.";
        var destination = Path.Combine(target, Path.GetFileName(path)); var partial = destination + ".partial";
        try
        {
            Directory.CreateDirectory(target);
            File.Copy(path, partial, true);
            if (await FileHash(partial) != manifest.Sha256) throw new IOException("Copied file does not match.");
            File.Move(partial, destination, true);
            await File.WriteAllTextAsync(destination + ManifestSuffix, JsonSerializer.Serialize(manifest, Indented));
            return "Copied and verified in " + target + ".";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { }
            return "The local backup is safe, but copying to " + target + " failed. Check the drive is connected.";
        }
    }

    public IReadOnlyList<(string Path, string Location, BackupManifest Manifest)> ListBackups()
    {
        var result = new List<(string, string, BackupManifest)>();
        void Scan(string? folder, string location)
        {
            if (folder is null || !Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, "*.db"))
            {
                var info = new FileInfo(file); var name = info.Name;
                BackupManifest? manifest = null;
                try { if (File.Exists(file + ManifestSuffix)) manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(file + ManifestSuffix)); } catch (JsonException) { }
                result.Add((file, location, manifest ?? new(name.Split('-')[0], new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), "", info.Length, "", "")));
            }
        }
        Scan(BackupDirectory, "This computer");
        try { Scan(CopyDirectory(ReadSettings()), "Copy folder"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return result.OrderByDescending(x => x.Item3.Created).ToArray();
    }

    /// <summary>Runs a backup when the newest automatic one is older than the configured interval, then trims old automatic backups.</summary>
    public async Task<bool> RunAutomaticIfDue(CancellationToken ct = default)
    {
        var settings = ReadSettings();
        if (!settings.Automatic) return false;
        await gate.WaitAsync(ct);
        try
        {
            if (!ready || factory is null) return false;
            await using (var db = await factory.CreateDbContextAsync(ct))
                if (!await db.Users.AnyAsync(ct)) return false; // Nothing worth protecting before the owner account exists.
            var newest = ListBackups().Where(x => x.Manifest.Kind == "auto").Select(x => x.Manifest.Created).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
            if (DateTimeOffset.UtcNow - newest < TimeSpan.FromHours(settings.IntervalHours)) return false;
            var result = await WriteBackup("auto");
            Prune(settings);
            LastAutomaticStatus = $"Automatic backup completed {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC. {result.CopyStatus}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException)
        {
            LastAutomaticStatus = $"Automatic backup failed {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC. Check disk space, then use Back up now.";
            return false;
        }
        finally { gate.Release(); }
    }

    // Only automatic backups are trimmed; manual, pre-upgrade and pre-restore backups are kept until the owner removes them.
    private void Prune(BackupSettingsView settings)
    {
        foreach (var folder in new[] { BackupDirectory, CopyDirectory(settings) })
        {
            if (folder is null || !Directory.Exists(folder)) continue;
            try
            {
                foreach (var old in Directory.EnumerateFiles(folder, "auto-*.db").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(settings.KeepAutomatic))
                { File.Delete(old); File.Delete(old + ManifestSuffix); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private async Task AutomaticLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        try { do await RunAutomaticIfDue(stopping.Token); while (await timer.WaitForNextTickAsync(stopping.Token)); }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { } // Application closing.
    }

    /// <summary>Checks a backup file before anything is replaced: integrity, checksum, version and branch.</summary>
    public async Task<string> ValidateBackup(string path, IReadOnlySet<string> knownMigrations, string branch)
    {
        if (!File.Exists(path)) throw new BusinessException("The backup file is no longer available.");
        var manifestPath = path + ManifestSuffix;
        if (File.Exists(manifestPath))
        {
            var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath));
            if (manifest is not null && manifest.Sha256.Length > 0 && manifest.Sha256 != await FileHash(path)) throw new BusinessException("This backup file has been changed or damaged since it was made. Choose another backup.");
        }
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        try { await connection.OpenAsync(); if (await Scalar(connection, "PRAGMA integrity_check") != "ok") throw new BusinessException("This backup failed its integrity check. Choose another backup."); }
        catch (SqliteException) { throw new BusinessException("This file is not a readable Sierra Business backup."); }
        var migrations = await Column(connection, "SELECT MigrationId FROM __EFMigrationsHistory");
        if (migrations.Count == 0) throw new BusinessException("This file is not a Sierra Business backup.");
        if (migrations.Any(x => !knownMigrations.Contains(x))) throw new BusinessException("This backup was made by a newer version of Sierra Business. Install that version before restoring it.");
        if ((await Column(connection, "SELECT COUNT(*) FROM AspNetUsers")).Single() == "0") throw new BusinessException("This backup contains no user accounts, so nobody could sign in after restoring it.");
        var branches = (await Column(connection, "SELECT DISTINCT Branch FROM Documents")).Concat(await Column(connection, "SELECT DISTINCT ClaimValue FROM AspNetUserClaims WHERE ClaimType = 'branch'")).Distinct().ToArray();
        if (branches.Any(x => x != branch)) throw new BusinessException("This backup belongs to a different branch or company and cannot be restored here.");
        return path;
    }

    /// <summary>Replaces the live database with a validated backup. A safety backup of the current data is taken first.</summary>
    public async Task<string> Restore(string path, IReadOnlySet<string> knownMigrations, string branch)
    {
        await gate.WaitAsync();
        try
        {
            if (!ready || factory is null) throw new InvalidOperationException("Open the local workspace before restoring.");
            await ValidateBackup(path, knownMigrations, branch);
            var safety = await WriteBackup("before-restore");
            SqliteConnection.ClearAllPools();
            await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            await using (var live = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString()))
            {
                await source.OpenAsync(); await live.OpenAsync();
                source.BackupDatabase(live); // Page-level copy into the live file, applied atomically by SQLite.
            }
            SqliteConnection.ClearAllPools();
            await using (var db = await factory.CreateDbContextAsync()) await HistoryProtection.MigrateAsync(db); // Brings older backups up to date and reinstates guards.
            return safety.Path;
        }
        finally { gate.Release(); }
    }

    public async Task Initialize(IDbContextFactory<BusinessDb> contextFactory)
    {
        await gate.WaitAsync();
        try
        {
            if (ready) return;
            Directory.CreateDirectory(DirectoryPath);
            try { instanceLock ??= new FileStream(Path.Combine(DirectoryPath, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new InvalidOperationException("Sierra Business is already open for this Windows account. Close the other window and try again."); }
            var existed = File.Exists(DatabasePath);
            await using var db = await contextFactory.CreateDbContextAsync();
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
            if (existed && pending.Length > 0)
            {
                await WriteBackup("before-upgrade");
            }
            // EF's pending-model check remains enabled: never silently accept an incompatible model.
            await HistoryProtection.MigrateAsync(db);
            var settings = DesktopTradingConfiguration.Load(DirectoryPath);
            if (await db.Documents.AnyAsync(x => x.Branch != settings.Branch) || await db.UserClaims.AnyAsync(x => x.ClaimType == "branch" && x.ClaimValue != settings.Branch))
                throw new InvalidOperationException("The configured branch differs from this database's records or accounts. Restore the original branch setting; do not repurpose an existing database for a different branch.");
            factory = contextFactory;
            ready = true;
        }
        finally { gate.Release(); }
        _ = AutomaticLoop();
    }

    private static async Task<string> FileHash(string path) { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream)); }
    private static async Task<string> Scalar(SqliteConnection connection, string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? ""; }
    private static async Task<string> TryScalar(SqliteConnection connection, string sql) { try { return await Scalar(connection, sql); } catch (SqliteException) { return ""; } }
    private static async Task<List<string>> Column(SqliteConnection connection, string sql)
    {
        var values = new List<string>();
        try { await using var command = connection.CreateCommand(); command.CommandText = sql; await using var reader = await command.ExecuteReaderAsync(); while (await reader.ReadAsync()) values.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? ""); }
        catch (SqliteException) { }
        return values;
    }

    public void Dispose() { stopping.Cancel(); instanceLock?.Dispose(); gate.Dispose(); stopping.Dispose(); }
}
