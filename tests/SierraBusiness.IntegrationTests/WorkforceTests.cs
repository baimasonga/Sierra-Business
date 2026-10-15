using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class WorkforceTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "rokela-workforce-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly Session session = new(); private Factory factory = null!; private PayrollService payroll = null!; private BusinessService business = null!; private Guid employee;
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private DateOnly Month => new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=" + path + ";Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions()); payroll = new(factory, session, options); business = new(factory, options);
        employee = await payroll.SaveEmployee(new(Guid.NewGuid(), null, 0, "HR001", "Workforce employee", "Operations", DateTime.DaysInMonth(Month.Year, Month.Month) * 100, 0, 0, today.AddYears(-2), null));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    [Fact] public async Task AttendanceRetainsRevisionsRejectsStaleEditsAndReplaysOnce()
    {
        var first = new AttendanceCommand(Guid.NewGuid(), employee, Month, 0, AttendanceStatus.Present, 8, 2, "Site visit");
        var id = await payroll.SaveAttendance(first); Assert.Equal(id, await payroll.SaveAttendance(first));
        await payroll.SaveAttendance(first with { OperationId = Guid.NewGuid(), ExpectedVersion = 1, Hours = 7, Note = "Corrected hours" });
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SaveAttendance(first with { OperationId = Guid.NewGuid() }));
        var a = Assert.Single((await payroll.Workforce(Month)).Attendance); Assert.Equal(2, a.Version); Assert.Equal(7, a.Hours);
        await using var db = factory.CreateDbContext(); Assert.Equal(2, await db.Operations.CountAsync(o => o.Action == "payroll.attendance")); Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task LeaveBalancesOverlapDecisionVersionsAndCancellationAreControlled()
    {
        await payroll.SetLeaveEntitlement(new(Guid.NewGuid(), employee, Month.Year, LeaveKind.Annual, 0, 2, "Approved annual entitlement"));
        var id = await payroll.RequestLeave(new(Guid.NewGuid(), employee, LeaveKind.Annual, [Month.AddDays(1), Month.AddDays(2)], "Family visit"));
        var approve = new LeaveDecisionCommand(Guid.NewGuid(), id, 1, LeaveStatus.Approved, "Cover arranged"); await payroll.DecideLeave(approve); Assert.Equal(id, await payroll.DecideLeave(approve));
        var balance = Assert.Single((await payroll.Workforce(Month)).Balances); Assert.Equal(0, balance.Remaining);
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SetLeaveEntitlement(new(Guid.NewGuid(), employee, Month.Year, LeaveKind.Annual, 1, 1, "Too low")));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SaveAttendance(new(Guid.NewGuid(), employee, Month.AddDays(1), 0, AttendanceStatus.Present, 8, 0, "Conflicting time")));
        var overlap = await payroll.RequestLeave(new(Guid.NewGuid(), employee, LeaveKind.Unpaid, [Month.AddDays(1)], "Duplicate date"));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.DecideLeave(new(Guid.NewGuid(), overlap, 1, LeaveStatus.Approved, "Review")));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.DecideLeave(new(Guid.NewGuid(), id, 1, LeaveStatus.Cancelled, "Stale")));
        await payroll.DecideLeave(new(Guid.NewGuid(), id, 2, LeaveStatus.Cancelled, "Employee stayed at work"));
        Assert.Equal(2, Assert.Single((await payroll.Workforce(Month)).Balances).Remaining);
        await payroll.SaveAttendance(new(Guid.NewGuid(), employee, Month.AddDays(1), 0, AttendanceStatus.Present, 8, 0, "Attended"));
    }
    [Fact] public async Task WorkforceIsOptInAndSnapshotsItsInputsWithoutDoubleDeduction()
    {
        await payroll.SaveAttendance(new(Guid.NewGuid(), employee, Month, 0, AttendanceStatus.HalfDayUnpaid, 4, 2, "Half day"));
        await payroll.SaveAttendance(new(Guid.NewGuid(), employee, Month.AddDays(1), 0, AttendanceStatus.UnpaidAbsence, 0, 0, "Absent"));
        var leave = await payroll.RequestLeave(new(Guid.NewGuid(), employee, LeaveKind.Unpaid, [Month.AddDays(2)], "Unpaid leave"));
        await payroll.DecideLeave(new(Guid.NewGuid(), leave, 1, LeaveStatus.Approved, "Approved"));
        await payroll.SavePayHead(new(Guid.NewGuid(), null, 0, "Overtime", PayHeadKind.TaxableEarning, "OVERTIME_HOURS * 20", Month, null, null, true));
        await payroll.SavePayHead(new(Guid.NewGuid(), null, 0, "Meal contribution", PayHeadKind.AfterTaxDeduction, "10", Month, null, employee, true));
        var old = new PayrollCommand(Guid.NewGuid(), Month, today, [employee], [new(employee, Bonus: 5)]);
        var full = DateTime.DaysInMonth(Month.Year, Month.Month) * 100;
        Assert.Equal(full + 5, (await payroll.Preview(old)).Net);
        var command = old with { UseWorkforceInputs = true };
        var preview = await payroll.Preview(command); Assert.Equal(full - 250 + 40 + 5 - 10, preview.Net);
        Assert.Contains("Overtime v1: 40.00", preview.Lines[0].Calculation.Note);
        await Assert.ThrowsAsync<BusinessException>(() => payroll.Preview(command with { Adjustments = [new(employee, UnpaidDays: 1)] }));
        var posted = await payroll.Post(command); Assert.Equal(posted, await payroll.Post(command));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SaveAttendance(new(Guid.NewGuid(), employee, Month.AddDays(3), 0, AttendanceStatus.Present, 8, 0, "Later edit")));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.DecideLeave(new(Guid.NewGuid(), leave, 2, LeaveStatus.Cancelled, "After payroll")));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SavePayHead(new(Guid.NewGuid(), null, 0, "Retroactive", PayHeadKind.TaxableEarning, "25", Month, null, null, true)));
        var snapshot = await business.Read(session.Actor); Assert.Equal(snapshot.Ledger.Sum(l => l.Debit), snapshot.Ledger.Sum(l => l.Credit));
        var payslip = Assert.Single((await payroll.Read()).Payslips); Assert.Equal(preview.Net, payslip.Net); Assert.Equal(preview.Lines[0].Calculation.Note, payslip.Calculation!.Note);
    }
    [Fact] public async Task CrossYearLeaveNeedsEachEntitlementAndPermissionsAreFresh()
    {
        var year = today.Year; var days = new List<DateOnly> { new(year, 12, 31), new(year + 1, 1, 1) };
        await payroll.SetLeaveEntitlement(new(Guid.NewGuid(), employee, year, LeaveKind.Annual, 0, 1, "Grant"));
        var id = await payroll.RequestLeave(new(Guid.NewGuid(), employee, LeaveKind.Annual, days, "Cross year"));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.DecideLeave(new(Guid.NewGuid(), id, 1, LeaveStatus.Approved, "Approve")));
        await payroll.SetLeaveEntitlement(new(Guid.NewGuid(), employee, year + 1, LeaveKind.Annual, 0, 1, "Next year grant"));
        await payroll.DecideLeave(new(Guid.NewGuid(), id, 1, LeaveStatus.Approved, "Both granted"));
        Assert.Equal(1, Assert.Single((await payroll.Workforce(new(year, 12, 1))).Balances).Used);
        Assert.Equal(1, Assert.Single((await payroll.Workforce(new(year + 1, 1, 1))).Balances).Used);
        session.Actor = new("reader", "FREETOWN", new HashSet<string> { "accounting.read" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => payroll.Workforce(Month));
        session.Actor = new("owner", "OTHER", BusinessPermissions.All.ToHashSet());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => payroll.Workforce(Month));
    }
    [Fact] public async Task InvalidTimeAndFormulaFailureDoNotSaveOrPost()
    {
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SaveAttendance(new(Guid.NewGuid(), employee, Month, 0, AttendanceStatus.Present, 23, 2, "Too long")));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SaveAttendance(new(Guid.NewGuid(), employee, Month, 0, AttendanceStatus.UnpaidAbsence, 8, 0, "Wrong status")));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SavePayHead(new(Guid.NewGuid(), null, 0, "Unsafe", PayHeadKind.TaxableEarning, "System.IO.File()", Month, null, null, true)));
        await payroll.SavePayHead(new(Guid.NewGuid(), null, 0, "Hourly ratio", PayHeadKind.TaxableEarning, "BASIC / HOURS", Month, null, null, true));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.Post(new(Guid.NewGuid(), Month, today, [employee], UseWorkforceInputs: true)));
        Assert.Empty((await payroll.Read()).Payslips);
        Assert.Empty((await payroll.Workforce(Month)).Attendance);
    }
    [Fact] public async Task PayHeadsRejectDuplicatesAndStaleEditsAndExportRealRecords()
    {
        var command = new PayHeadCommand(Guid.NewGuid(), null, 0, "Travel", PayHeadKind.TaxableEarning, "25", Month, null, null, true);
        var id = await payroll.SavePayHead(command); Assert.Equal(id, await payroll.SavePayHead(command));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SavePayHead(command with { OperationId = Guid.NewGuid() }));
        await payroll.SavePayHead(command with { OperationId = Guid.NewGuid(), Id = id, ExpectedVersion = 1, Formula = "30" });
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SavePayHead(command with { OperationId = Guid.NewGuid(), Id = id, ExpectedVersion = 1 }));
        await payroll.SaveAttendance(new(Guid.NewGuid(), employee, Month, 0, AttendanceStatus.Present, 8, 0, "Export row"));
        var bytes = await payroll.ExportWorkforce(Month); using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        Assert.NotNull(zip.GetEntry("xl/workbook.xml")); Assert.Contains(zip.Entries, e => e.FullName.StartsWith("xl/worksheets/"));
    }
    [Fact] public async Task FuturePayHeadRevisionsKeepEarlierRatesAndRejectChangedPreviews()
    {
        var head = new PayHeadCommand(Guid.NewGuid(), null, 0, "Fixed allowance", PayHeadKind.TaxableEarning, "25", Month, null, null, true);
        var id = await payroll.SavePayHead(head);
        var run = new PayrollCommand(Guid.NewGuid(), Month, today, [employee], UseWorkforceInputs: true);
        var preview = await payroll.Preview(run);
        await payroll.SavePayHead(head with { OperationId = Guid.NewGuid(), Id = id, ExpectedVersion = 1, From = Month.AddMonths(1), Formula = "50" });
        Assert.Equal(preview.Net, (await payroll.Preview(run)).Net);
        var current = await payroll.Preview(run with { Month = Month.AddMonths(1) });
        Assert.Contains("Fixed allowance v2: 50.00", current.Lines[0].Calculation.Note);
        await payroll.SaveAttendance(new(Guid.NewGuid(), employee, Month, 0, AttendanceStatus.UnpaidAbsence, 0, 0, "Late correction"));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.Post(run with { ExpectedCalculation = preview.CalculationFingerprint }));
        Assert.Empty((await payroll.Read()).Payslips);
        var updated = await payroll.Preview(run);
        await payroll.Post(run with { ExpectedCalculation = updated.CalculationFingerprint });
    }
    [Theory]
    [InlineData("BASIC * 0.05", 50)]
    [InlineData("(HOURS + OVERTIME_HOURS) * 2", 324)]
    [InlineData("basic / 3", 333.33)]
    public void FormulasUseBoundedArithmeticAndRoundOnce(string formula, double expected)
    {
        var values = new Dictionary<string, decimal> { ["BASIC"] = 1000, ["HOURS"] = 160, ["OVERTIME_HOURS"] = 2 };
        PayHeadFormula.Validate(formula); Assert.Equal((decimal)expected, PayHeadFormula.Evaluate(formula, values));
    }
    [Theory]
    [InlineData("BASIC / 0")]
    [InlineData("BASIC - 2000")]
    [InlineData("BONUS * 2")]
    [InlineData("BASIC.ToString()")]
    [InlineData("((((((((((((((((((1))))))))))))))))))")]
    public void FormulaErrorsAreBusinessErrors(string formula) => Assert.Throws<BusinessException>(() => PayHeadFormula.Evaluate(formula, new Dictionary<string, decimal> { ["BASIC"] = 1000 }));
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
