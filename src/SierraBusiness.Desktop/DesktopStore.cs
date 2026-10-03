using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

/// <summary>Per-Windows-user durable data, independent of the installation directory.</summary>
public sealed class DesktopStore(string directory) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private FileStream? instanceLock;
    private bool ready;
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string DatabasePath => Path.Combine(DirectoryPath, "sierra.db");
    public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true }.ToString();

    public async Task<string> CreateBackup()
    {
        await gate.WaitAsync();
        try
        {
            if (!ready) throw new InvalidOperationException("Open the local workspace before creating a backup.");
            return await WriteBackup("manual");
        }
        finally { gate.Release(); }
    }

    private async Task<string> WriteBackup(string prefix)
    {
        var backups = Path.Combine(DirectoryPath, "Backups");
        Directory.CreateDirectory(backups);
        var path = Path.Combine(backups, $"{prefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        var temporary = path + ".partial";
        try
        {
            await using (var source = new SqliteConnection(ConnectionString))
            await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            {
                await source.OpenAsync();
                await destination.OpenAsync();
                source.BackupDatabase(destination);
                await using var check = destination.CreateCommand();
                check.CommandText = "PRAGMA integrity_check";
                if (!string.Equals(await check.ExecuteScalarAsync() as string, "ok", StringComparison.Ordinal))
                    throw new InvalidOperationException("Backup verification failed. Try again before relying on this backup.");
            }
            File.Move(temporary, path);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task Initialize(IDbContextFactory<BusinessDb> factory)
    {
        await gate.WaitAsync();
        try
        {
            if (ready) return;
            Directory.CreateDirectory(DirectoryPath);
            try { instanceLock ??= new FileStream(Path.Combine(DirectoryPath, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new InvalidOperationException("Sierra Business is already open for this Windows account. Close the other window and try again."); }
            var existed = File.Exists(DatabasePath);
            await using var db = await factory.CreateDbContextAsync();
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
            if (existed && pending.Length > 0)
            {
                await WriteBackup("before-upgrade");
            }
            // EF's pending-model check remains enabled: never silently accept an incompatible model.
            await db.Database.MigrateAsync();
            var settings = DesktopTradingConfiguration.Load(DirectoryPath);
            if (await db.Documents.AnyAsync(x => x.Branch != settings.Branch) || await db.UserClaims.AnyAsync(x => x.ClaimType == "branch" && x.ClaimValue != settings.Branch))
                throw new InvalidOperationException("The configured branch differs from this database's records or accounts. Restore the original branch setting; do not repurpose an existing database for a different branch.");
            ready = true;
        }
        finally { gate.Release(); }
    }

    public void Dispose() { instanceLock?.Dispose(); gate.Dispose(); }
}
