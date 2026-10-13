using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure.Imports;
namespace SierraBusiness.Infrastructure;

public sealed partial class PayrollService
{
    // Workforce commands are immutable, sealed operation events. Existing backup/sync transports
    // replicate these records without adding mutable HR history or changing posted payroll schemas.
    private const string AttendanceAction = "payroll.attendance", EntitlementAction = "payroll.leave.entitlement", RequestAction = "payroll.leave.request", DecisionAction = "payroll.leave.decision", HeadAction = "payroll.payhead";
    private sealed record WorkforceState(List<AttendanceView> Attendance, List<LeaveBalance> Balances, List<LeaveRequestView> Requests, List<PayHeadView> Heads, List<PayHeadView> HeadHistory);
    private static async Task<WorkforceState> WorkforceStateFor(BusinessDb db, Actor actor)
    {
        var events = await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && (o.Action == AttendanceAction || o.Action == EntitlementAction || o.Action == RequestAction || o.Action == DecisionAction || o.Action == HeadAction)).ToListAsync();
        var employees = await db.Employees.AsNoTracking().Where(e => e.Branch == actor.Branch).ToDictionaryAsync(e => e.Id, e => e.Code + " · " + e.Name);
        string EmployeeName(Guid id) => employees.GetValueOrDefault(id, "Unavailable employee");
        static T Read<T>(Operation o) => JsonSerializer.Deserialize<T>(o.Payload) ?? throw new BusinessException("A workforce record could not be read. Verify record integrity.");
        var attendance = events.Where(o => o.Action == AttendanceAction).Select(Read<AttendanceCommand>).GroupBy(a => (a.EmployeeId, a.Date)).Select(g => g.MaxBy(a => a.ExpectedVersion)!).Select(a => new AttendanceView(a.EmployeeId, EmployeeName(a.EmployeeId), a.Date, a.ExpectedVersion + 1, a.Status, a.Hours, a.OvertimeHours, a.Note)).ToList();
        var decisions = events.Where(o => o.Action == DecisionAction).Select(Read<LeaveDecisionCommand>).GroupBy(d => d.RequestId).ToDictionary(g => g.Key, g => g.MaxBy(d => d.ExpectedVersion)!);
        var requests = events.Where(o => o.Action == RequestAction).Select(o =>
        {
            var r = Read<LeaveRequestCommand>(o); decisions.TryGetValue(o.ResultId, out var d);
            return new LeaveRequestView(o.ResultId, r.EmployeeId, EmployeeName(r.EmployeeId), r.Kind, r.Days.Order().ToArray(), r.Reason, d is null ? 1 : d.ExpectedVersion + 1, d?.Status ?? LeaveStatus.Pending, d?.Reason ?? "");
        }).ToList();
        var balances = events.Where(o => o.Action == EntitlementAction).Select(Read<LeaveEntitlementCommand>).GroupBy(e => (e.EmployeeId, e.Year, e.Kind)).Select(g => g.MaxBy(e => e.ExpectedVersion)!).Select(e => new LeaveBalance(e.EmployeeId, EmployeeName(e.EmployeeId), e.Year, e.Kind, e.ExpectedVersion + 1, e.Days, requests.Where(r => r.EmployeeId == e.EmployeeId && r.Kind == e.Kind && r.Status == LeaveStatus.Approved).Sum(r => (decimal)r.Days.Count(d => d.Year == e.Year)))).ToList();
        var history = events.Where(o => o.Action == HeadAction).Select(o => (Id: o.ResultId, Command: Read<PayHeadCommand>(o))).Select(x => new PayHeadView(x.Id, x.Command.ExpectedVersion + 1, x.Command.Name, x.Command.Kind, x.Command.Formula, x.Command.From, x.Command.To, x.Command.EmployeeId, x.Command.Active)).ToList();
        var heads = history.GroupBy(h => h.Id).Select(g => g.MaxBy(h => h.Version)!).ToList();
        return new(attendance, balances, requests, heads, history);
    }
    public async Task<WorkforceSnapshot> Workforce(DateOnly month)
    {
        if (month.Year < 2000 || month.Year > Today.Year + 3) throw new BusinessException("Choose a workforce report within the supported planning period.");
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync(); var s = await WorkforceStateFor(db, actor);
        var first = new DateOnly(month.Year, month.Month, 1); var last = first.AddMonths(1).AddDays(-1);
        return new(first, s.Attendance.Where(a => a.Date >= first && a.Date <= last).OrderBy(a => a.Date).ThenBy(a => a.Employee).ToArray(), s.Balances.Where(b => b.Year == first.Year).OrderBy(b => b.Employee).ThenBy(b => b.Kind).ToArray(), s.Requests.Where(r => r.Days.Any(d => d.Year == first.Year)).OrderByDescending(r => r.Days[0]).ToArray(), s.Heads.OrderBy(h => h.Name).ToArray());
    }
    private static async Task<Employee> WorkforceEmployee(BusinessDb db, Actor actor, Guid id) => await db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.Branch == actor.Branch) ?? throw new BusinessException("Choose an employee in this branch.");
    private async Task WorkforceOpenDate(BusinessDb db, Actor actor, Guid employee, DateOnly date)
    {
        if (date <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough)) throw new BusinessException("This workforce date is in a closed period.");
        var first = new DateOnly(date.Year, date.Month, 1);
        if (await db.PayrollItems.AnyAsync(i => i.EmployeeId == employee && i.Month == first && !db.Documents.Any(d => d.Reverses == i.DocumentId))) throw new BusinessException("Payroll is already posted for this employee and month. Reverse it through the authorized workflow before changing its inputs.");
    }
    private static void Employed(Employee e, DateOnly d) { if (d < e.StartDate || e.EndDate is DateOnly end && d > end) throw new BusinessException("The employee was not employed on this date."); }
    public Task<Guid> SaveAttendance(AttendanceCommand input) => Execute(input.OperationId, AttendanceAction, input, async (db, actor) =>
    {
        var e = await WorkforceEmployee(db, actor, input.EmployeeId); Employed(e, input.Date);
        if (input.Date > Today || !Enum.IsDefined(input.Status)) throw new BusinessException("Choose a valid attendance status and a date no later than today.");
        if (input.Hours < 0 || input.OvertimeHours < 0 || input.Hours + input.OvertimeHours > 24 || input.Hours * 4 != decimal.Truncate(input.Hours * 4) || input.OvertimeHours * 4 != decimal.Truncate(input.OvertimeHours * 4)) throw new BusinessException("Hours must be quarter hours, nonnegative and total at most 24 per day.");
        if (input.Status is AttendanceStatus.UnpaidAbsence or AttendanceStatus.RestDay && input.Hours + input.OvertimeHours != 0) throw new BusinessException("An absence or rest day cannot contain worked hours.");
        Optional(input.Note ?? "", "Attendance note", 160); await WorkforceOpenDate(db, actor, e.Id, input.Date);
        var s = await WorkforceStateFor(db, actor); var old = s.Attendance.SingleOrDefault(a => a.EmployeeId == e.Id && a.Date == input.Date);
        if ((old?.Version ?? 0) != input.ExpectedVersion) throw new BusinessException("Attendance changed. Refresh before saving.");
        if (s.Requests.Any(r => r.EmployeeId == e.Id && r.Status == LeaveStatus.Approved && r.Days.Contains(input.Date))) throw new BusinessException("Approved leave already covers this date. Cancel or correct that leave before recording attendance.");
        return old is null ? Guid.NewGuid() : input.OperationId;
    });
    public Task<Guid> SetLeaveEntitlement(LeaveEntitlementCommand input) => Execute(input.OperationId, EntitlementAction, input, async (db, actor) =>
    {
        await WorkforceEmployee(db, actor, input.EmployeeId);
        if (input.Year is < 2000 or > 9998 || !Enum.IsDefined(input.Kind) || input.Kind == LeaveKind.Unpaid || input.Days < 0 || input.Days > 366 || input.Days != decimal.Truncate(input.Days)) throw new BusinessException("Choose a paid leave type, year and a whole-day entitlement between 0 and 366.");
        SierraBusiness.Domain.Rules.Required(input.Reason, "Entitlement reason", 160); var s = await WorkforceStateFor(db, actor);
        var old = s.Balances.SingleOrDefault(b => b.EmployeeId == input.EmployeeId && b.Year == input.Year && b.Kind == input.Kind);
        if ((old?.Version ?? 0) != input.ExpectedVersion) throw new BusinessException("Leave entitlement changed. Refresh before saving.");
        if (input.Days < (old?.Used ?? 0)) throw new BusinessException("Entitlement cannot be lower than approved leave already used.");
        return Guid.NewGuid();
    });
    public Task<Guid> RequestLeave(LeaveRequestCommand input) => Execute(input.OperationId, RequestAction, input, async (db, actor) =>
    {
        var e = await WorkforceEmployee(db, actor, input.EmployeeId);
        if (!Enum.IsDefined(input.Kind) || input.Days is null || input.Days.Count is < 1 or > 92 || input.Days.Distinct().Count() != input.Days.Count) throw new BusinessException("Choose a leave type and 1 to 92 distinct leave dates.");
        SierraBusiness.Domain.Rules.Required(input.Reason, "Leave reason", 160);
        foreach (var d in input.Days) { Employed(e, d); if (d.Year < 2000 || d.Year > Today.Year + 3) throw new BusinessException("Choose leave within the supported planning period."); await WorkforceOpenDate(db, actor, e.Id, d); }
        return Guid.NewGuid();
    });
    public Task<Guid> DecideLeave(LeaveDecisionCommand input) => Execute(input.OperationId, DecisionAction, input, async (db, actor) =>
    {
        SierraBusiness.Domain.Rules.Required(input.Reason, "Decision reason", 160); var s = await WorkforceStateFor(db, actor); var r = s.Requests.SingleOrDefault(r => r.Id == input.RequestId) ?? throw new BusinessException("Leave request not found in this branch.");
        if (r.Version != input.ExpectedVersion) throw new BusinessException("Leave request changed. Refresh before deciding.");
        if (!Enum.IsDefined(input.Status) || input.Status == LeaveStatus.Pending || r.Status is LeaveStatus.Cancelled or LeaveStatus.Rejected || r.Status == LeaveStatus.Approved && input.Status != LeaveStatus.Cancelled) throw new BusinessException("Approve or reject pending leave, or cancel an approved request.");
        foreach (var d in r.Days) await WorkforceOpenDate(db, actor, r.EmployeeId, d);
        if (input.Status == LeaveStatus.Approved)
        {
            var e = await WorkforceEmployee(db, actor, r.EmployeeId); foreach (var d in r.Days) Employed(e, d);
            if (s.Requests.Any(other => other.Id != r.Id && other.EmployeeId == r.EmployeeId && other.Status == LeaveStatus.Approved && other.Days.Intersect(r.Days).Any()) || s.Attendance.Any(a => a.EmployeeId == r.EmployeeId && r.Days.Contains(a.Date))) throw new BusinessException("Leave overlaps approved leave or a recorded attendance day.");
            if (r.Kind != LeaveKind.Unpaid) foreach (var year in r.Days.GroupBy(d => d.Year))
            {
                var balance = s.Balances.SingleOrDefault(b => b.EmployeeId == r.EmployeeId && b.Year == year.Key && b.Kind == r.Kind);
                if ((balance?.Remaining ?? 0) < year.Count()) throw new BusinessException("Not enough granted leave for " + year.Key + ". Review the entitlement first.");
            }
        }
        return r.Id;
    });
    public Task<Guid> SavePayHead(PayHeadCommand input) => Execute(input.OperationId, HeadAction, input, async (db, actor) =>
    {
        SierraBusiness.Domain.Rules.Required(input.Name, "Pay head name", 80);
        if (!Enum.IsDefined(input.Kind) || input.From.Day != 1 || input.To is DateOnly to && (to < input.From || to != new DateOnly(to.Year, to.Month, 1).AddMonths(1).AddDays(-1))) throw new BusinessException("Use a valid pay head, a first-of-month start and an optional month-end finish.");
        if (input.EmployeeId is Guid employee) await WorkforceEmployee(db, actor, employee);
        PayHeadFormula.Validate(input.Formula);
        var s = await WorkforceStateFor(db, actor); var old = input.Id is Guid id ? s.Heads.SingleOrDefault(h => h.Id == id) ?? throw new BusinessException("Pay head not found.") : null;
        if ((old?.Version ?? 0) != input.ExpectedVersion) throw new BusinessException("Pay head changed. Refresh before saving.");
        if (old is not null && input.From < old.From) throw new BusinessException("A pay-head revision cannot start before the previous revision's start month.");
        if (old is null && s.Heads.Count >= 100) throw new BusinessException("This branch already has 100 pay heads. Review existing heads before adding more.");
        if (input.Active && s.HeadHistory.Any(h => h.Id != old?.Id && h.Name.Trim().Equals(input.Name.Trim(), StringComparison.OrdinalIgnoreCase) && (h.EmployeeId == null || input.EmployeeId == null || h.EmployeeId == input.EmployeeId))) throw new BusinessException("A pay head with this name already applies to these employees. Edit its effective-dated revision instead of adding a duplicate.");
        if (await db.PayrollItems.AnyAsync(i => (input.EmployeeId == null || i.EmployeeId == input.EmployeeId) && i.Month >= input.From && (input.To == null || i.Month <= input.To) && db.Documents.Any(d => d.Id == i.DocumentId && d.Branch == actor.Branch) && !db.Documents.Any(d => d.Reverses == i.DocumentId))) throw new BusinessException("That pay-head period already contains posted payroll. Start the change in a future unposted month.");
        return old?.Id ?? Guid.NewGuid();
    });
    private static WorkforcePayrollInput WorkforceInput(WorkforceState s, Employee e, DateOnly month)
    {
        var end = month.AddMonths(1).AddDays(-1);
        var attendance = s.Attendance.Where(a => a.EmployeeId == e.Id && a.Date >= month && a.Date <= end).ToList();
        var leave = s.Requests.Where(r => r.EmployeeId == e.Id && r.Status == LeaveStatus.Approved && r.Kind == LeaveKind.Unpaid).SelectMany(r => r.Days).Count(d => d >= month && d <= end);
        var unpaid = leave + attendance.Sum(a => a.Status == AttendanceStatus.UnpaidAbsence ? 1 : a.Status == AttendanceStatus.HalfDayUnpaid ? .5m : 0);
        var baseCalculation = PayrollMath.Calculate(View(e), null, month, new(e.Id, UnpaidDays: unpaid));
        var values = new Dictionary<string, decimal> { ["BASIC"] = baseCalculation.Basic, ["ALLOWANCES"] = baseCalculation.Allowances, ["DAYS"] = baseCalculation.DaysPaid, ["HOURS"] = attendance.Sum(a => a.Hours), ["OVERTIME_HOURS"] = attendance.Sum(a => a.OvertimeHours) };
        decimal earnings = 0, deductions = 0; var detail = new List<string>();
        var applicable = s.HeadHistory.Where(h => h.From <= month).GroupBy(h => h.Id).Select(g => g.MaxBy(h => h.Version)!).Where(h => h.Active && (h.EmployeeId is null || h.EmployeeId == e.Id) && (h.To is null || h.To >= end));
        foreach (var head in applicable)
        {
            var amount = PayHeadFormula.Evaluate(head.Formula, values);
            if (head.Kind == PayHeadKind.TaxableEarning) earnings += amount; else deductions += amount;
            detail.Add(head.Name + " v" + head.Version + ": " + amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }
        return new(e.Id, unpaid, values["HOURS"], values["OVERTIME_HOURS"], earnings, deductions, "Attendance unpaid days " + unpaid + "; hours " + values["HOURS"] + "; overtime hours " + values["OVERTIME_HOURS"] + "; " + string.Join("; ", detail));
    }
    public async Task<byte[]> ExportWorkforce(DateOnly month)
    {
        var s = await Workforce(month);
        return WorkbookWriter.Write([
            ("Attendance", [["Employee", "Date", "Status", "Hours", "Overtime hours", "Version", "Note"], .. s.Attendance.Select(a => (IReadOnlyList<string>)[a.Employee, a.Date.ToString("yyyy-MM-dd"), a.Status.ToString(), a.Hours.ToString(System.Globalization.CultureInfo.InvariantCulture), a.OvertimeHours.ToString(System.Globalization.CultureInfo.InvariantCulture), a.Version.ToString(), a.Note])]),
            ("Leave balances", [["Employee", "Year", "Type", "Granted", "Approved", "Remaining"], .. s.Balances.Select(b => (IReadOnlyList<string>)[b.Employee, b.Year.ToString(), b.Kind.ToString(), b.Granted.ToString(), b.Used.ToString(), b.Remaining.ToString()])]),
            ("Leave requests", [["Employee", "Type", "Dates", "Status", "Reason", "Decision"], .. s.Requests.Select(r => (IReadOnlyList<string>)[r.Employee, r.Kind.ToString(), string.Join(", ", r.Days.Select(d => d.ToString("yyyy-MM-dd"))), r.Status.ToString(), r.Reason, r.DecisionReason])])]);
    }
}
