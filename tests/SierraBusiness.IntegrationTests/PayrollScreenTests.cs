using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Drives the Payroll screen: fill in the Sierra Leone rates, hire, calculate and post a month, pay the salary and print the payslip.</summary>
public sealed class PayrollScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-payroll-ui-{Guid.NewGuid():N}.db");
    private readonly BunitContext context = new();
    private Factory factory = null!;
    private readonly Session session = new();
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        context.Services.AddSingleton<IPayrollService>(new PayrollService(factory, session, Options.Create(new TradingOptions())));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private static void Click<T>(IRenderedComponent<T> screen, string text) where T : Microsoft.AspNetCore.Components.IComponent => screen.FindAll("button").First(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal)).Click();
    private static AngleSharp.Dom.IElement Field<T>(IRenderedComponent<T> screen, string label) where T : Microsoft.AspNetCore.Components.IComponent =>
        screen.FindAll("label").First(l => l.TextContent.Trim().StartsWith(label, StringComparison.Ordinal)).QuerySelector("input,select")!;

    [Fact] public void OwnerSetsRatesRunsPayrollPaysAndPrintsThePayslip()
    {
        var screen = context.Render<PayrollWorkspace>();
        screen.WaitForAssertion(() => Assert.Contains("Statutory deductions are off", screen.Markup));

        Click(screen, "PAYE & NASSIT");
        screen.WaitForAssertion(() => Assert.Contains("Statutory rules", screen.Markup));
        var previous = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-1);
        Field(screen, "Applies from payroll month").Change(previous.ToString("yyyy-MM"));
        Click(screen, "Fill in current Sierra Leone rates");
        Click(screen, "Save rules");
        screen.WaitForAssertion(() => Assert.Contains("Payroll rules saved", screen.Markup));

        Click(screen, "Employees");
        screen.WaitForAssertion(() => Assert.Contains("New employee", screen.Markup));
        Field(screen, "Employee code").Change("E001"); Field(screen, "Name").Change("Aminata Kamara"); Field(screen, "Department").Change("Sales");
        Field(screen, "Basic monthly salary").Change("3000"); Field(screen, "Monthly allowances").Change("800"); Field(screen, "Other monthly deductions").Change("100");
        Field(screen, "Employment start").Change(DateTime.Now.AddYears(-1).ToString("yyyy-MM-dd")); Field(screen, "TIN").Change("1000555");
        Click(screen, "Save employee");
        screen.WaitForAssertion(() => Assert.Contains("Employee saved", screen.Markup));

        Click(screen, "Run payroll");
        screen.WaitForAssertion(() => Assert.Contains("E001 · Aminata Kamara", screen.Markup));
        screen.Find("input[aria-label='Select Aminata Kamara for payroll']").Change(true);
        Click(screen, "Calculate");
        screen.WaitForAssertion(() => Assert.Contains("2,965.00", screen.Markup)); // net after PAYE 585, NASSIT 150 and 100 other
        Assert.Contains("585.00", screen.Markup);
        Click(screen, "Post payroll for");
        screen.WaitForAssertion(() => Assert.Contains("Pay salaries from Payslips", screen.Markup));

        Click(screen, "Payslips & payments");
        screen.WaitForAssertion(() => Assert.Contains("Unpaid", screen.Markup));
        screen.Find("input[aria-label='Pay Aminata Kamara']").Change(true);
        Field(screen, "Transfer reference").Change("RCB-001");
        Click(screen, "Pay 1 salary");
        screen.WaitForAssertion(() => Assert.Contains("1 salary paid", screen.Markup));
        screen.FindAll("button").First(b => b.TextContent.Trim() == "Payslip").Click();
        screen.WaitForAssertion(() => Assert.Contains("TIN: 1000555", screen.Find("#payslip-print").TextContent));
        Assert.Contains("Paid on", screen.Find("#payslip-print").TextContent);
        Assert.Contains("Two thousand nine hundred and sixty-five Leones only", screen.Find("#payslip-print").TextContent);
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
