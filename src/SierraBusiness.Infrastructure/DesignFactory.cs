using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace SierraBusiness.Infrastructure;

public sealed class DesignFactory : IDesignTimeDbContextFactory<BusinessDb>
{
    public BusinessDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=sierra-design.db").Options);
}
