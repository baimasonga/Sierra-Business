using Microsoft.EntityFrameworkCore;
using SierraBusiness.Desktop;
using SierraBusiness.Infrastructure;

if (args.Length != 2 || args[0] != "--data-directory" || !Path.IsPathFullyQualified(args[1]))
{
    Console.Error.WriteLine("Usage: SierraBusiness.BackupWorker --data-directory <existing absolute company folder>"); return 2;
}
try
{
    await using var store = new DesktopStore(args[1]);
    var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = store.DatabasePath, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
    var factory = new ReadFactory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite(connection).Options);
    var created = await store.RunClosedAppBackup(factory);
    Console.WriteLine(created ? store.LastAutomaticStatus : "No backup due, automatic backup disabled, or desktop open.");
    return store.LastAutomaticStatus.StartsWith("Automatic backup failed", StringComparison.Ordinal) ? 1 : 0;
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or SierraBusiness.Domain.BusinessException)
{
    Console.Error.WriteLine("Scheduled backup failed: " + e.Message); return 1;
}
sealed class ReadFactory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb>
{
    public BusinessDb CreateDbContext() => new(options);
}
