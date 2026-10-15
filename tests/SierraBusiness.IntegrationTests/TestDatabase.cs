using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.IntegrationTests;

/// <summary>Migrating a new SQLite file replays every migration and history guard, about 1.5 s of CPU per test (several seconds on CI runners).
/// Tests copy a file migrated once per run instead. Anything other than a new, empty SQLite file, or a context that captures changes for sync, migrates normally.</summary>
internal static class TestDatabase
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<bool, string> Templates = [];

    /// <param name="guarded">True for <see cref="HistoryProtection.MigrateAsync"/>, false for a plain EF migration.</param>
    public static async Task MigrateAsync(BusinessDb db, bool guarded = true)
    {
        var path = NewSqliteFile(db);
        if (path is null)
        {
            if (guarded) await HistoryProtection.MigrateAsync(db); else await db.Database.MigrateAsync();
            return;
        }
        File.Copy(await Template(guarded), path, overwrite: true);
    }

    private static string? NewSqliteFile(BusinessDb db)
    {
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite") return null;
        if (db.Database.GetDbConnection().State != System.Data.ConnectionState.Closed) return null;
        var interceptors = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        if (interceptors.Any(x => x is SyncCaptureInterceptor)) return null;
        var source = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        if (string.IsNullOrEmpty(source) || source == ":memory:" || (File.Exists(source) && new FileInfo(source).Length > 0)) return null;
        return source;
    }

    private static async Task<string> Template(bool guarded)
    {
        await Gate.WaitAsync();
        try
        {
            if (Templates.TryGetValue(guarded, out var existing)) return existing;
            var path = Path.Combine(Path.GetTempPath(), $"sierra-template-{(guarded ? "guarded" : "plain")}-{Guid.NewGuid():N}.db");
            await using (var db = new BusinessDb(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options))
                if (guarded) await HistoryProtection.MigrateAsync(db); else await db.Database.MigrateAsync();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => File.Delete(path);
            return Templates[guarded] = path;
        }
        finally { Gate.Release(); }
    }
}
