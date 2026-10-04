using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace SierraBusiness.Infrastructure.SqlServer;

/// <summary>Design-time factory for SQL Server migrations: dotnet ef migrations add Name --project src/SierraBusiness.Infrastructure.SqlServer --startup-project src/SierraBusiness.Infrastructure.SqlServer</summary>
public sealed class SqlServerDesignFactory : IDesignTimeDbContextFactory<BusinessDb>
{
    public const string Assembly = "SierraBusiness.Infrastructure.SqlServer";
    public BusinessDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BusinessDb>().UseSqlServer("Server=localhost;Database=SierraBusinessDesign;Trusted_Connection=True;TrustServerCertificate=True", x => x.MigrationsAssembly(Assembly)).Options);
}
