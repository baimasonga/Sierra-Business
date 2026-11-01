using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace SierraBusiness.Desktop;

/// <summary>Lets a connection wait for another connection's lock instead of failing at once. Backups read the live database while
/// the workspace writes to it; without a busy timeout SQLite refuses a commit that meets the backup's read lock with
/// "database is locked", and a sign-in or posting fails.</summary>
public sealed class SqliteBusyTimeout : DbConnectionInterceptor
{
    public const int Milliseconds = 30_000;
    public static readonly SqliteBusyTimeout Instance = new();
    private const string Pragma = "PRAGMA busy_timeout = 30000;";

    public static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragma;
        command.ExecuteNonQuery();
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Apply(connection);

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragma;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
