using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
namespace SierraBusiness.Infrastructure.Postgres;

/// <summary>How a head-office server uses PostgreSQL (for example Supabase): its own migrations, the migration history in the "sierra" schema,
/// and retries for the brief disconnections a hosted database can have.</summary>
public static class PostgresSetup
{
    public const string Assembly = "SierraBusiness.Infrastructure.Postgres";
    public static DbContextOptionsBuilder UsePostgres(this DbContextOptionsBuilder options, string connection) =>
        options.UseNpgsql(connection, Configure);
    public static DbContextOptionsBuilder<BusinessDb> UsePostgres(this DbContextOptionsBuilder<BusinessDb> options, string connection) =>
        options.UseNpgsql(connection, Configure);
    public static void Configure(NpgsqlDbContextOptionsBuilder x)
    {
        x.MigrationsAssembly(Assembly);
        x.MigrationsHistoryTable("__EFMigrationsHistory", HistoryProtection.PostgresSchema);
    }
}

/// <summary>dotnet ef migrations add Name --project src/SierraBusiness.Infrastructure.Postgres --startup-project src/SierraBusiness.Infrastructure.Postgres</summary>
public sealed class PostgresDesignFactory : IDesignTimeDbContextFactory<BusinessDb>
{
    public BusinessDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<BusinessDb>().UsePostgres("Host=localhost;Database=sierra_design;Username=postgres").Options);
}
