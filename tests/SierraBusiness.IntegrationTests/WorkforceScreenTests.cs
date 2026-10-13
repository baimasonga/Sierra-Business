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

public sealed class WorkforceScreenTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "rokela-workforce-ui-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly BunitContext context = new(); private PayrollService payroll = null!; private Guid employee;
    public async Task InitializeAsync()
    {
        var factory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=" + path + ";Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        payroll = new(factory, new Session(), Options.Create(new TradingOptions()));
        employee = await payroll.SaveEmployee(new(Guid.NewGuid(), null, 0, "UI-HR", "Test employee", "Admin", 3000, 0, 0, DateOnly.FromDateTime(DateTime.Today).AddYears(-1), null));
        context.Services.AddSingleton<IPayrollService>(payroll); context.JSInterop.Mode = JSRuntimeMode.Loose;
    }
    public Task DisposeAsync() { context.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private static void Click<T>(IRenderedComponent<T> screen, string text) where T : Microsoft.AspNetCore.Components.IComponent => screen.FindAll("button").First(b => b.TextContent.Trim() == text).Click();
    private static AngleSharp.Dom.IElement Field<T>(IRenderedComponent<T> screen, string label) where T : Microsoft.AspNetCore.Components.IComponent => screen.FindAll("label").First(l => l.TextContent.Trim() == label).QuerySelector("input,select")!;
    [Fact] public void AttendanceLeaveApprovalBalancesAndExportWorkFromTheScreen()
    {
        var screen = context.Render<WorkforceWorkspace>(); screen.WaitForAssertion(() => Assert.Contains("Record a day", screen.Markup));
        Field(screen, "Regular hours").Change("7.5"); Field(screen, "Overtime hours").Change("1.5"); Field(screen, "Attendance note").Change("Warehouse duty");
        Assert.True(screen.Instance.HasDraft); Click(screen, "Save attendance day"); screen.WaitForAssertion(() => Assert.Contains("Attendance saved", screen.Markup)); Assert.False(screen.Instance.HasDraft);
        Click(screen, "Leave balances"); Field(screen, "Total granted days").Change("5"); Field(screen, "Entitlement reason").Change("Annual grant"); Click(screen, "Save leave entitlement"); screen.WaitForAssertion(() => Assert.Contains("Leave entitlement saved", screen.Markup));
        Click(screen, "Leave requests"); var date = DateOnly.FromDateTime(DateTime.Today).AddDays(1); while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(1);
        Field(screen, "First date").Change(date.ToString("yyyy-MM-dd")); Field(screen, "Last date").Change(date.ToString("yyyy-MM-dd")); Field(screen, "Request reason").Change("Annual break"); Click(screen, "Submit leave request"); screen.WaitForAssertion(() => Assert.Contains("Pending", screen.Markup));
        Field(screen, "Decision reason").Change("Cover assigned"); Click(screen, "Approve"); screen.WaitForAssertion(() => Assert.Contains("Approved", screen.Markup));
        Click(screen, "Leave balances"); screen.WaitForAssertion(() => Assert.Contains("<td>4</td>", screen.Markup));
        Click(screen, "Export attendance and leave"); screen.WaitForAssertion(() => Assert.Contains(context.JSInterop.Invocations, i => i.Identifier == "sierraDownload"));
    }
    [Fact] public void PayHeadErrorsKeepFormAndSavedEditorsAreNotDirty()
    {
        var screen = context.Render<WorkforceWorkspace>(); screen.WaitForAssertion(() => Assert.Contains("Record a day", screen.Markup));
        Click(screen, "Pay heads"); Field(screen, "Name").Change("Hourly addition"); Field(screen, "Formula").Change("Process.Start()"); Click(screen, "Save pay head");
        screen.WaitForAssertion(() => Assert.Contains("Unknown pay variable", screen.Markup)); Assert.Equal("Hourly addition", Field(screen, "Name").GetAttribute("value")); Assert.True(screen.Instance.HasDraft);
        Field(screen, "Formula").Change("OVERTIME_HOURS * 20"); Click(screen, "Save pay head"); screen.WaitForAssertion(() => Assert.Contains("Pay head saved", screen.Markup)); Assert.False(screen.Instance.HasDraft);
        Click(screen, "Edit pay head"); Assert.False(screen.Instance.HasDraft);
        Field(screen, "Formula").Change("OVERTIME_HOURS * 25"); Assert.True(screen.Instance.HasDraft);
    }
    [Fact] public async Task PayrollPreviewShowsWorkforceDetailsOnlyWhenSelected()
    {
        var month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        await payroll.SaveAttendance(new(Guid.NewGuid(), employee, month, 0, AttendanceStatus.Present, 8, 2, "Extra hours"));
        await payroll.SavePayHead(new(Guid.NewGuid(), null, 0, "Overtime", PayHeadKind.TaxableEarning, "OVERTIME_HOURS * 25", month, null, employee, true));
        var screen = context.Render<PayrollWorkspace>(); screen.WaitForAssertion(() => Assert.Contains("Attendance &amp; leave", screen.Markup));
        Click(screen, "Run payroll"); screen.Find("input[aria-label='Select all employees']").Change(true); Field(screen, "Use attendance leave and pay heads").Change(true); Click(screen, "Calculate");
        screen.WaitForAssertion(() => Assert.Contains("Overtime v1: 50.00", screen.Markup));
        Click(screen, "Post payroll for " + month.ToString("MMMM yyyy")); screen.WaitForAssertion(() => Assert.Contains("posted. Pay salaries", screen.Markup));
        Assert.Equal(3050, Assert.Single((await payroll.Read()).Payslips).Net);
    }
    private sealed class Session : IActorSession { public Task<Actor> CurrentActor() => Task.FromResult(new Actor("owner", "FREETOWN", BusinessPermissions.All.ToHashSet())); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
