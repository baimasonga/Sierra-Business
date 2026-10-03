using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class PayrollTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-payroll-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private PayrollService payroll = null!; private BusinessService business = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using var db = factory.CreateDbContext(); await db.Database.MigrateAsync(); payroll = new(factory, session, Options.Create(new TradingOptions())); business = new(factory, Options.Create(new TradingOptions()));
    }
    public Task DisposeAsync() { File.Delete(path); return Task.CompletedTask; }
    private EmployeeCommand Employee() => new(Guid.NewGuid(), null, 0, "E001", "Test Employee", "Sales", 1000, 200, 150, today.AddYears(-1), null);
    [Fact] public async Task PayrollPostsSnapshotAndBalancedJournalWithDuplicateProtection()
    {
        var employee = Employee(); var id = await payroll.SaveEmployee(employee);
        var run = new PayrollCommand(Guid.NewGuid(), today.AddMonths(-1), today, [id]);
        var document = await payroll.Post(run); Assert.Equal(document, await payroll.Post(run));
        var slip = (await payroll.Read()).Payslips.Single(); Assert.Equal(1050, slip.Net);
        var trading = await business.Read(session.Actor); Assert.Equal(trading.Ledger.Sum(x => x.Debit), trading.Ledger.Sum(x => x.Credit));
        Assert.Equal(1200, trading.Ledger.Single(x => (int)x.Account == 5200).Debit);
        Assert.Equal(1050, trading.Ledger.Single(x => (int)x.Account == 2200).Credit);
        Assert.Equal(150, trading.Ledger.Single(x => (int)x.Account == 2300).Credit);
        await Assert.ThrowsAsync<BusinessException>(() => payroll.Post(run with { OperationId = Guid.NewGuid() }));
        await payroll.SaveEmployee(employee with { OperationId = Guid.NewGuid(), EmployeeId = id, ExpectedVersion = 1, BasicSalary = 1500 });
        Assert.Equal(1000, (await payroll.Read()).Payslips.Single().Basic);
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SaveEmployee(employee with { OperationId = Guid.NewGuid(), EmployeeId = id, ExpectedVersion = 1 }));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), document, "Incorrect payroll"));
        Assert.True((await payroll.Read()).Payslips.Single().Reversed);
        await payroll.Post(run with { OperationId = Guid.NewGuid() }); Assert.Equal(1550, (await payroll.Read()).Payslips.Single(x => !x.Reversed).Net);
    }
    [Fact] public async Task InvalidDeductionsAndPartialMonthAreRejectedAtomically()
    {
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SaveEmployee(Employee() with { Deductions = 1500 }));
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var id = await payroll.SaveEmployee(Employee() with { StartDate = month.AddDays(10) });
        await Assert.ThrowsAsync<BusinessException>(() => payroll.Post(new(Guid.NewGuid(), month, today, [id])));
        Assert.Empty((await payroll.Read()).Payslips); Assert.Empty((await business.Read(session.Actor)).Documents);
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", new HashSet<string> { "business.read", "accounting.read", "accounting.write", "documents.reverse" }); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}

