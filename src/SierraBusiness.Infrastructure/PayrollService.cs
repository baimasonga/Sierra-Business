using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure.Imports;
namespace SierraBusiness.Infrastructure;
public sealed class Employee
{
    public Guid Id { get; set; }
    public string Branch { get; set; } = "";
    public int Version { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
    public decimal BasicSalary { get; set; }
    public decimal Allowances { get; set; }
    public decimal Deductions { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Tin { get; set; } = "";
    public string NassitNumber { get; set; } = "";
    public bool NonResident { get; set; }
    public bool NassitExempt { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string BankName { get; set; } = "";
    public string BankAccount { get; set; } = "";
}
public sealed class PayrollItem
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid DocumentId { get; set; }
    public DateOnly Month { get; set; }
    public string Detail { get; set; } = "";
}
// Append-only records (database guards and integrity seals).
public sealed class PayrollRuleVersion { public Guid Id { get; set; } public string Branch { get; set; } = ""; public int Version { get; set; } public DateOnly EffectiveFrom { get; set; } public string RulesJson { get; set; } = "{}"; public string CreatedBy { get; set; } = ""; public DateTimeOffset Time { get; set; } }
public sealed class PayrollPaymentLine { public Guid Id { get; set; } public Guid DocumentId { get; set; } public Guid PayrollItemId { get; set; } public decimal Amount { get; set; } }
public sealed class EmployeeLoan { public Guid Id { get; set; } public Guid DocumentId { get; set; } public Guid EmployeeId { get; set; } public decimal Principal { get; set; } public decimal Installment { get; set; } public string Reason { get; set; } = ""; }
public sealed class StatutoryRemittance { public Guid Id { get; set; } public Guid DocumentId { get; set; } public string Branch { get; set; } = ""; public DateOnly Month { get; set; } public StatutoryKind Kind { get; set; } public decimal Amount { get; set; } public string Reference { get; set; } = ""; }

/// <summary>Pay calculation: proration by calendar days, then NASSIT and PAYE from the rules in force for the month.</summary>
public static class PayrollMath
{
    public const int Salaries = 5200, EmployerNassit = 5210, NetPay = 2200, OtherDeductions = 2300, PayePayable = 2310, NassitPayable = 2320, Loans = 1310;

    /// <summary>What each employee still owes on staff loans: loans given less instalments deducted in payroll (both unreversed).</summary>
    public static async Task<Dictionary<Guid, (decimal Outstanding, decimal Installment)>> LoanBalances(BusinessDb db, string branch)
    {
        var loans = await db.EmployeeLoans.AsNoTracking().Where(l => db.Documents.Any(d => d.Id == l.DocumentId && d.Branch == branch) && !db.Documents.Any(r => r.Reverses == l.DocumentId)).ToListAsync();
        var items = await db.PayrollItems.AsNoTracking().Where(i => db.Documents.Any(d => d.Id == i.DocumentId && d.Branch == branch) && !db.Documents.Any(r => r.Reverses == i.DocumentId)).ToListAsync();
        var repaid = items.GroupBy(i => i.EmployeeId).ToDictionary(g => g.Key, g => g.Sum(i => Read(i).Calculation.LoanRepayment));
        return loans.GroupBy(l => l.EmployeeId).ToDictionary(g => g.Key, g => (g.Sum(l => l.Principal) - repaid.GetValueOrDefault(g.Key), g.OrderBy(l => l.Id).Last().Installment));
    }

    public static PayCalculation Calculate(EmployeeView e, PayrollRules? rules, DateOnly month, PayrollAdjustment? adjustment, decimal loanInstallment = 0)
    {
        var first = new DateOnly(month.Year, month.Month, 1); var last = first.AddMonths(1).AddDays(-1);
        var daysInMonth = (decimal)DateTime.DaysInMonth(first.Year, first.Month);
        var from = e.StartDate > first ? e.StartDate : first; var to = e.EndDate is DateOnly end && end < last ? end : last;
        if (to < from) throw new BusinessException($"{e.Code} was not employed during {first:MMMM yyyy}.");
        var employed = (decimal)(to.DayNumber - from.DayNumber + 1);
        var a = adjustment ?? new(e.Id);
        foreach (var v in new[] { a.UnpaidDays, a.Overtime, a.Bonus, a.ExtraDeduction, a.LeaveAllowance, a.EndOfService })
            if (v < 0 || v > 1_000_000_000 || AccountingEngine.Money(v) != v) throw new BusinessException($"Check the adjustments for {e.Code}: amounts are positive with up to two decimals.");
        if (a.UnpaidDays > employed || a.UnpaidDays * 2 != Math.Floor(a.UnpaidDays * 2)) throw new BusinessException($"Unpaid days for {e.Code} must be in half days and at most the {employed:0} days employed this month.");
        var daysPaid = employed - a.UnpaidDays;
        var basic = daysPaid == daysInMonth ? e.BasicSalary : AccountingEngine.Money(e.BasicSalary * daysPaid / daysInMonth);
        var allowances = daysPaid == daysInMonth ? e.Allowances : AccountingEngine.Money(e.Allowances * daysPaid / daysInMonth);
        var gross = basic + allowances + a.Overtime + a.Bonus + a.LeaveAllowance + a.EndOfService;
        decimal leaveFree = 0, leaveExcessTax = 0;
        decimal taxFree = 0, nassitBase = 0, nassitEmployee = 0, nassitEmployer = 0, chargeable = 0, paye = 0;
        if (rules is not null)
        {
            var aged = rules.NassitAgeLimit > 0 && e.BirthDate is DateOnly born && born.AddYears(rules.NassitAgeLimit) < first; // stops from the month after the birthday
            if (!e.NassitExempt && !aged)
            {
                nassitBase = rules.NassitBase == NassitBase.BasicSalary ? basic : gross - a.EndOfService - a.LeaveAllowance;
                nassitEmployee = AccountingEngine.Money(nassitBase * rules.NassitEmployee);
                nassitEmployer = AccountingEngine.Money(nassitBase * rules.NassitEmployer);
            }
            taxFree = Math.Min(allowances, rules.TaxFreeAllowances);
            var banded = gross;
            if (rules.EndOfServiceTaxFree) banded -= a.EndOfService; // Finance Act 2026: end-of-service benefits are exempt
            if (rules.LeaveUpToBasicTaxFree && a.LeaveAllowance > 0)
            {
                leaveFree = Math.Min(a.LeaveAllowance, e.BasicSalary);
                leaveExcessTax = AccountingEngine.Money((a.LeaveAllowance - leaveFree) * rules.LeaveExcessRate);
                banded -= a.LeaveAllowance;
            }
            chargeable = Math.Max(0, banded - taxFree - nassitEmployee);
            paye = (e.Resident ? Paye(chargeable, rules.PayeBands) : AccountingEngine.Money(chargeable * rules.NonResidentRate)) + leaveExcessTax;
        }
        var other = e.Deductions + a.ExtraDeduction;
        var loan = a.SkipLoan ? 0 : Math.Min(loanInstallment, Math.Max(0, gross - paye - nassitEmployee - other));
        var net = gross - paye - nassitEmployee - other - loan;
        if (net < 0) throw new BusinessException($"Deductions for {e.Code} ({other:N2} plus statutory {paye + nassitEmployee:N2}) are more than the pay of {gross:N2} this month. Reduce the one-off deduction.");
        return new(daysInMonth, daysPaid, basic, allowances, a.Overtime, a.Bonus, gross, taxFree, nassitBase, nassitEmployee, chargeable, paye, other, net, nassitEmployer, rules is not null, rules?.Version ?? 0, (a.Note ?? "").Trim(), a.LeaveAllowance, leaveFree, leaveExcessTax, a.EndOfService, loan);
    }

    public static decimal Paye(decimal chargeable, IReadOnlyList<PayeBand> bands)
    {
        decimal tax = 0, lower = 0;
        foreach (var b in bands)
        {
            var upper = b.UpTo ?? decimal.MaxValue;
            if (chargeable > lower) tax += (Math.Min(chargeable, upper) - lower) * b.Rate;
            if (b.UpTo is null || chargeable <= upper) break;
            lower = upper;
        }
        return AccountingEngine.Money(tax);
    }

    public static async Task<PayrollRules?> RulesFor(BusinessDb db, string branch, DateOnly month)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var row = await db.PayrollRules.AsNoTracking().Where(x => x.Branch == branch && x.EffectiveFrom <= first).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version).FirstOrDefaultAsync();
        return row is null ? null : JsonSerializer.Deserialize<PayrollRules>(row.RulesJson)! with { Version = row.Version, EffectiveFrom = row.EffectiveFrom };
    }

    internal sealed record Stored(EmployeeView Employee, PayCalculation Calculation);
    internal static (EmployeeView Employee, PayCalculation Calculation) Read(PayrollItem item)
    {
        using var json = JsonDocument.Parse(item.Detail);
        if (json.RootElement.TryGetProperty("Calculation", out _)) { var s = json.RootElement.Deserialize<Stored>()!; return (s.Employee, s.Calculation); }
        var e = json.RootElement.Deserialize<EmployeeView>()!; // payslips from before 0.21.0: fixed salary, no statutory deductions
        var gross = e.BasicSalary + e.Allowances;
        return (e, new(30, 30, e.BasicSalary, e.Allowances, 0, 0, gross, 0, 0, 0, 0, 0, e.Deductions, gross - e.Deductions, 0, false, 0));
    }
}

public sealed class PayrollService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IPayrollService
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private async Task<Actor> Authorize()
    {
        var actor = await session.CurrentActor(); actor.Demand("accounting.read"); actor.Demand("accounting.write");
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch."); return actor;
    }
    private static string Short(string? number) { var parts = (number ?? "").Split('-'); return parts.Length >= 3 && parts[^1].Length > 8 ? $"{parts[^2]}-{parts[^1][^8..].ToUpperInvariant()}" : number ?? ""; }
    private static EmployeeView View(Employee e) => new(e.Id, e.Version, e.Code, e.Name, e.Department, e.BasicSalary, e.Allowances, e.Deductions, e.StartDate, e.EndDate, e.Tin, e.NassitNumber, !e.NonResident, e.NassitExempt, e.BirthDate, e.BankName, e.BankAccount);

    public async Task<PayrollSnapshot> Read()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var employees = await db.Employees.AsNoTracking().Where(x => x.Branch == actor.Branch).OrderBy(x => x.Code).ToListAsync();
        return new(employees.Select(View).ToArray(), await Payslips(db, actor.Branch, null));
    }

    private static async Task<PayslipView[]> Payslips(BusinessDb db, string branch, DateOnly? month)
    {
        var docs = await db.Documents.AsNoTracking().Where(x => x.Branch == branch && (x.Kind == DocumentKind.Payroll || x.Kind == DocumentKind.PayrollPayment || x.Reverses != null)).ToListAsync();
        var byId = docs.ToDictionary(x => x.Id);
        var reversed = docs.Where(x => x.Reverses != null).Select(x => x.Reverses!.Value).ToHashSet();
        var items = await db.PayrollItems.AsNoTracking().Where(x => db.Documents.Any(d => d.Id == x.DocumentId && d.Branch == branch) && (month == null || x.Month == month)).ToListAsync();
        var ids = items.Select(x => x.Id).ToList();
        var payments = (await db.PayrollPaymentLines.AsNoTracking().Where(x => ids.Contains(x.PayrollItemId)).ToListAsync()).Where(x => !reversed.Contains(x.DocumentId)).ToDictionary(x => x.PayrollItemId);
        return items.Select(x =>
        {
            var (e, c) = PayrollMath.Read(x); var d = byId[x.DocumentId];
            payments.TryGetValue(x.Id, out var paid); var payDoc = paid is null ? null : byId.GetValueOrDefault(paid.DocumentId);
            return new PayslipView(d.Id, d.Number, x.Month, d.BusinessDate, e.Code, e.Name, e.Department, c.Basic, c.Allowances, c.OtherDeductions + c.Paye + c.NassitEmployee + c.LoanRepayment, c.Net, reversed.Contains(d.Id),
                x.Id, x.EmployeeId, c, e.Tin, e.NassitNumber, payDoc is null ? "" : Short(payDoc.Number), payDoc?.BusinessDate);
        }).OrderByDescending(x => x.Month).ThenBy(x => x.EmployeeCode).ToArray();
    }

    public Task<Guid> SaveEmployee(EmployeeCommand input) => Execute(input.OperationId, "employee.save", input, async (db, actor) =>
    {
        foreach (var amount in new[] { input.BasicSalary, input.Allowances, input.Deductions }) if (amount < 0 || amount > 1_000_000_000 || AccountingEngine.Money(amount) != amount) throw new BusinessException("Salary amounts must be non-negative, at most one billion and have up to two decimals.");
        if (input.BasicSalary + input.Allowances <= 0 || input.Deductions > input.BasicSalary + input.Allowances) throw new BusinessException("Gross salary must be positive and deductions cannot exceed it.");
        if (input.EndDate < input.StartDate) throw new BusinessException("Employment end cannot precede its start.");
        if (input.BirthDate is DateOnly born && (born > Today.AddYears(-14) || born < Today.AddYears(-100))) throw new BusinessException("Check the date of birth.");
        var code = SierraBusiness.Domain.Rules.Required(input.Code, "Employee code", 40).ToUpperInvariant();
        if (await db.Employees.AnyAsync(x => x.Branch == actor.Branch && x.Code == code && x.Id != input.EmployeeId)) throw new BusinessException("Employee code already exists.");
        Employee employee;
        if (input.EmployeeId is null) { employee = new() { Id = Guid.NewGuid(), Branch = actor.Branch }; db.Employees.Add(employee); }
        else { employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == input.EmployeeId && x.Branch == actor.Branch) ?? throw new BusinessException("Employee not found."); if (employee.Version != input.ExpectedVersion) throw new BusinessException("Employee changed. Refresh before saving."); }
        employee.Version++; employee.Code = code; employee.Name = SierraBusiness.Domain.Rules.Required(input.Name, "Employee name"); employee.Department = SierraBusiness.Domain.Rules.Required(input.Department, "Department", 100); employee.BasicSalary = input.BasicSalary; employee.Allowances = input.Allowances; employee.Deductions = input.Deductions; employee.StartDate = input.StartDate; employee.EndDate = input.EndDate;
        if (input.Tin is not null) employee.Tin = Optional(input.Tin, "TIN", 30);
        if (input.NassitNumber is not null) employee.NassitNumber = Optional(input.NassitNumber, "NASSIT number", 30);
        if (input.Resident is bool resident) employee.NonResident = !resident;
        if (input.NassitExempt is bool exempt) employee.NassitExempt = exempt;
        if (input.BirthDate is not null || input.EmployeeId is null) employee.BirthDate = input.BirthDate;
        if (input.BankName is not null) employee.BankName = Optional(input.BankName, "Bank name", 60);
        if (input.BankAccount is not null) employee.BankAccount = Optional(input.BankAccount, "Bank account number", 40);
        return employee.Id;
    });
    private static string Optional(string value, string field, int max) { value = value.Trim().ToUpperInvariant(); if (value.Length > max) throw new BusinessException($"{field} is limited to {max} characters."); return value; }

    // ---------------- payroll runs ----------------

    private sealed record RunLine(Employee Employee, PayCalculation Calculation);
    private async Task<(DateOnly First, PayrollRules? Rules, List<RunLine> Lines)> Build(BusinessDb db, Actor actor, PayrollCommand input)
    {
        var first = new DateOnly(input.Month.Year, input.Month.Month, 1);
        if (input.EmployeeIds is null || input.EmployeeIds.Count is < 1 or > 500 || input.EmployeeIds.Distinct().Count() != input.EmployeeIds.Count) throw new BusinessException("Select 1–500 different employees.");
        var adjustments = input.Adjustments ?? [];
        if (adjustments.GroupBy(x => x.EmployeeId).Any(g => g.Count() > 1) || adjustments.Any(x => !input.EmployeeIds.Contains(x.EmployeeId))) throw new BusinessException("Give at most one adjustment per selected employee.");
        if (adjustments.Any(x => (x.Note ?? "").Length > 100)) throw new BusinessException("Adjustment notes are limited to 100 characters.");
        var rules = await PayrollMath.RulesFor(db, actor.Branch, first);
        var loans = await PayrollMath.LoanBalances(db, actor.Branch);
        var lines = new List<RunLine>();
        foreach (var id in input.EmployeeIds)
        {
            var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == id && x.Branch == actor.Branch) ?? throw new BusinessException("Employee not found in this branch.");
            var adjustment = adjustments.SingleOrDefault(x => x.EmployeeId == id);
            var owed = loans.TryGetValue(employee.Id, out var l) && l.Outstanding > 0 ? Math.Min(l.Installment, l.Outstanding) : 0;
            lines.Add(new(employee, PayrollMath.Calculate(View(employee), rules, first, adjustment is null ? null : adjustment with { Note = adjustment.Note ?? "" }, owed)));
        }
        return (first, rules, lines);
    }

    public async Task<PayrollPreview> Preview(PayrollCommand input)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var (first, rules, lines) = await Build(db, actor, input);
        var c = lines.Select(x => x.Calculation).ToList();
        return new(first, lines.Select(x => new PayrollPreviewLine(x.Employee.Id, x.Employee.Code, x.Employee.Name, x.Employee.Department, x.Calculation)).ToArray(),
            c.Sum(x => x.Gross), c.Sum(x => x.Paye), c.Sum(x => x.NassitEmployee), c.Sum(x => x.NassitEmployer), c.Sum(x => x.OtherDeductions), c.Sum(x => x.Net), rules is not null,
            rules is null ? "Statutory deductions are off: set the PAYE and NASSIT rules under Payroll → Statutory rules." : $"PAYE and NASSIT rules version {rules.Version}, in force from {rules.EffectiveFrom:MMMM yyyy}.");
    }

    public Task<Guid> Post(PayrollCommand input) => Execute(input.OperationId, "payroll.post", input, async (db, actor) =>
    {
        var first = new DateOnly(input.Month.Year, input.Month.Month, 1); var last = first.AddMonths(1).AddDays(-1);
        if (input.PostingDate <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough) || input.PostingDate > Today || input.PostingDate < last) throw new BusinessException("Post monthly payroll on or after month-end, within an open period and no later than today.");
        var (_, _, lines) = await Build(db, actor, input);
        var doc = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-PAY-{input.OperationId:N}", Kind = DocumentKind.Payroll, Party = $"Payroll {first:yyyy-MM}", BusinessDate = input.PostingDate, Settlement = "Payroll payable", Detail = JsonSerializer.Serialize(input) };
        foreach (var (employee, c) in lines)
        {
            if (await db.PayrollItems.AnyAsync(x => x.EmployeeId == employee.Id && x.Month == first && !db.Documents.Any(d => d.Reverses == x.DocumentId))) throw new BusinessException($"Payroll is already posted for {employee.Code} in this month.");
            db.PayrollItems.Add(new() { Id = Guid.NewGuid(), EmployeeId = employee.Id, DocumentId = doc.Id, Month = first, Detail = JsonSerializer.Serialize(new PayrollMath.Stored(View(employee), c)) });
            Line(db, doc, PayrollMath.Salaries, c.Gross, 0, employee.Department);
            if (c.NassitEmployer > 0) Line(db, doc, PayrollMath.EmployerNassit, c.NassitEmployer, 0, employee.Department);
        }
        var calc = lines.Select(x => x.Calculation).ToList();
        doc.Total = calc.Sum(x => x.Gross); db.Documents.Add(doc);
        Line(db, doc, PayrollMath.NetPay, 0, calc.Sum(x => x.Net));
        Line(db, doc, PayrollMath.PayePayable, 0, calc.Sum(x => x.Paye));
        Line(db, doc, PayrollMath.NassitPayable, 0, calc.Sum(x => x.NassitEmployee + x.NassitEmployer));
        Line(db, doc, PayrollMath.OtherDeductions, 0, calc.Sum(x => x.OtherDeductions));
        Line(db, doc, PayrollMath.Loans, 0, calc.Sum(x => x.LoanRepayment));
        return doc.Id;
    });
    private static void Line(BusinessDb db, BusinessDocument doc, int account, decimal debit, decimal credit, string centre = "")
    {
        if (debit == 0 && credit == 0) return;
        db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = doc.Branch, Account = (Account)account, Debit = debit, Credit = credit, CostCentre = centre });
    }

    // ---------------- statutory rules ----------------

    public async Task<PayrollRulesView> Rules()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var rows = await db.PayrollRules.AsNoTracking().Where(x => x.Branch == actor.Branch).OrderByDescending(x => x.Version).ToListAsync();
        var names = await db.Users.AsNoTracking().Where(x => x.UserName != null).ToDictionaryAsync(x => x.Id, x => x.UserName!);
        var history = rows.Select(r => JsonSerializer.Deserialize<PayrollRules>(r.RulesJson)! with { Version = r.Version, EffectiveFrom = r.EffectiveFrom }).Select(r => r with { SavedBy = names.GetValueOrDefault(r.SavedBy, r.SavedBy) }).ToArray();
        return new(await PayrollMath.RulesFor(db, actor.Branch, Today), history);
    }

    public Task<Guid> SaveRules(SavePayrollRules input) => Execute(input.OperationId, "payroll.rules", input, async (db, actor) =>
    {
        var r = input.Rules ?? throw new BusinessException("Enter the PAYE and NASSIT rules.");
        var from = new DateOnly(r.EffectiveFrom.Year, r.EffectiveFrom.Month, 1);
        if (r.EffectiveFrom == default) throw new BusinessException("Choose the first payroll month the rules apply to.");
        var bands = r.PayeBands.ToList();
        if (bands.Count is < 1 or > 10 || bands[^1].UpTo is not null || bands.Take(bands.Count - 1).Any(b => b.UpTo is null)) throw new BusinessException("Enter 1 to 10 PAYE bands; only the last band is open-ended.");
        decimal previous = 0;
        foreach (var b in bands)
        {
            if (b.Rate is < 0 or > 1 || decimal.Round(b.Rate, 4) != b.Rate) throw new BusinessException("PAYE rates are between 0% and 100%.");
            if (b.UpTo is decimal upTo) { if (upTo <= previous || AccountingEngine.Money(upTo) != upTo) throw new BusinessException("Each PAYE band must end above the previous one."); previous = upTo; }
        }
        foreach (var rate in new[] { r.NonResidentRate, r.NassitEmployee, r.NassitEmployer }) if (rate is < 0 or > 1 || decimal.Round(rate, 4) != rate) throw new BusinessException("Rates are between 0% and 100%.");
        if (r.TaxFreeAllowances < 0 || AccountingEngine.Money(r.TaxFreeAllowances) != r.TaxFreeAllowances) throw new BusinessException("Tax-free allowances cannot be negative.");
        if (r.NassitAgeLimit is < 0 or > 100) throw new BusinessException("The NASSIT age limit is 0 (none) to 100.");
        if (!Enum.IsDefined(r.NassitBase)) throw new BusinessException("Choose basic salary or gross pay for NASSIT.");
        var source = SierraBusiness.Domain.Rules.Required(r.Source, "Source (Finance Act, NRA or NASSIT notice)", 300);
        var posted = await db.PayrollItems.Where(x => db.Documents.Any(d => d.Id == x.DocumentId && d.Branch == actor.Branch) && !db.Documents.Any(d => d.Reverses == x.DocumentId)).MaxAsync(x => (DateOnly?)x.Month);
        if (posted is DateOnly p && from <= p) throw new BusinessException($"Payroll is already posted for {p:MMMM yyyy}. New rules start from a later month; posted payslips keep the rules they were calculated with.");
        var current = await db.PayrollRules.Where(x => x.Branch == actor.Branch).MaxAsync(x => (int?)x.Version) ?? 0;
        if (current != r.Version) throw new BusinessException("The payroll rules were changed by someone else. Refresh before saving.");
        var clean = r with { Version = current + 1, EffectiveFrom = from, Bands = bands, Source = source, SavedBy = actor.Id, Saved = DateTimeOffset.UtcNow };
        var row = new PayrollRuleVersion { Id = Guid.NewGuid(), Branch = actor.Branch, Version = current + 1, EffectiveFrom = from, RulesJson = JsonSerializer.Serialize(clean), CreatedBy = actor.Id, Time = DateTimeOffset.UtcNow };
        db.PayrollRules.Add(row); return row.Id;
    });

    // ---------------- salary payments and remittances ----------------

    private static Account Paid(PaymentMethod method) => method switch { PaymentMethod.Cash => Account.Cash, PaymentMethod.BankTransfer => Account.Bank, _ => throw new BusinessException("Pay by cash or bank transfer.") };
    private async Task CheckDate(BusinessDb db, Actor actor, DateOnly date)
    {
        if (date > Today || date <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough)) throw new BusinessException("The payment date must be in an open period and not in the future.");
    }

    public Task<Guid> PaySalaries(PaySalariesCommand input) => Execute(input.OperationId, "payroll.pay", input, async (db, actor) =>
    {
        await CheckDate(db, actor, input.Date);
        var account = Paid(input.Method);
        var reference = (input.Reference ?? "").Trim();
        if (reference.Length > 100 || input.Method == PaymentMethod.BankTransfer && reference.Length == 0) throw new BusinessException("Enter the bank transfer reference (up to 100 characters).");
        if (input.PayrollItemIds is null || input.PayrollItemIds.Count is < 1 or > 500 || input.PayrollItemIds.Distinct().Count() != input.PayrollItemIds.Count) throw new BusinessException("Select 1–500 different payslips to pay.");
        var items = await db.PayrollItems.Where(x => input.PayrollItemIds.Contains(x.Id) && db.Documents.Any(d => d.Id == x.DocumentId && d.Branch == actor.Branch)).ToListAsync();
        if (items.Count != input.PayrollItemIds.Count) throw new BusinessException("A selected payslip was not found.");
        var doc = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-SPY-{input.OperationId:N}", Kind = DocumentKind.PayrollPayment, Party = $"Salaries ({items.Count} employee{(items.Count == 1 ? "" : "s")})", BusinessDate = input.Date, Settlement = input.Method.ToString(), Detail = JsonSerializer.Serialize(input) };
        decimal total = 0;
        foreach (var item in items)
        {
            var run = await db.Documents.SingleAsync(x => x.Id == item.DocumentId);
            if (await db.Documents.AnyAsync(x => x.Reverses == run.Id)) throw new BusinessException("A selected payslip belongs to a reversed payroll.");
            if (input.Date < run.BusinessDate) throw new BusinessException("Salaries are paid on or after the payroll posting date.");
            if (await db.PayrollPaymentLines.AnyAsync(x => x.PayrollItemId == item.Id && !db.Documents.Any(r => r.Reverses == x.DocumentId))) throw new BusinessException($"{PayrollMath.Read(item).Employee.Code} is already paid for {item.Month:MMMM yyyy}.");
            var net = PayrollMath.Read(item).Calculation.Net;
            if (net <= 0) continue;
            db.PayrollPaymentLines.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, PayrollItemId = item.Id, Amount = net });
            total += net;
        }
        if (total == 0) throw new BusinessException("Nothing to pay: the selected payslips have no net pay.");
        doc.Total = total; db.Documents.Add(doc);
        Line(db, doc, PayrollMath.NetPay, total, 0); Line(db, doc, (int)account, 0, total);
        return doc.Id;
    });

    public async Task<StatutorySchedule> Schedule(DateOnly month)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        return await BuildSchedule(db, actor.Branch, new DateOnly(month.Year, month.Month, 1));
    }
    private static async Task<StatutorySchedule> BuildSchedule(BusinessDb db, string branch, DateOnly first)
    {
        var slips = (await Payslips(db, branch, first)).Where(x => !x.Reversed).OrderBy(x => x.EmployeeCode).ToList();
        var lines = slips.Select(s => { var c = s.Calculation!; return new ScheduleLine(s.EmployeeCode, s.EmployeeName, s.Tin, s.NassitNumber, c.Gross, c.TaxFreeAllowances, c.NassitEmployee, c.Chargeable, c.Paye, c.NassitBase, c.NassitEmployer); }).ToArray();
        var remittances = (await db.StatutoryRemittances.AsNoTracking().Where(x => x.Branch == branch && x.Month == first && !db.Documents.Any(r => r.Reverses == x.DocumentId)).ToListAsync());
        var docIds = remittances.Select(x => x.DocumentId).ToList();
        var docs = await db.Documents.AsNoTracking().Where(x => docIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        return new(first, lines, lines.Sum(x => x.Paye), lines.Sum(x => x.NassitEmployee), lines.Sum(x => x.NassitEmployer),
            remittances.Where(x => x.Kind == StatutoryKind.Paye).Sum(x => x.Amount), remittances.Where(x => x.Kind == StatutoryKind.Nassit).Sum(x => x.Amount),
            remittances.Select(x => new RemittanceView(x.DocumentId, Short(docs[x.DocumentId].Number), docs[x.DocumentId].BusinessDate, x.Kind, x.Amount, x.Reference)).OrderBy(x => x.Date).ToArray());
    }

    public Task<Guid> Remit(RemitCommand input) => Execute(input.OperationId, "payroll.remit", input, async (db, actor) =>
    {
        await CheckDate(db, actor, input.Date);
        if (!Enum.IsDefined(input.Kind)) throw new BusinessException("Choose PAYE or NASSIT.");
        var account = Paid(input.Method);
        var reference = SierraBusiness.Domain.Rules.Required(input.Reference, "Receipt or transfer reference", 100);
        var first = new DateOnly(input.Month.Year, input.Month.Month, 1);
        if (input.Date < first.AddMonths(1).AddDays(-1)) throw new BusinessException("Remit a month's deductions on or after its last day.");
        var schedule = await BuildSchedule(db, actor.Branch, first);
        var amount = input.Kind == StatutoryKind.Paye ? schedule.PayeOutstanding : schedule.NassitOutstanding;
        if (amount <= 0) throw new BusinessException($"Nothing is outstanding for {(input.Kind == StatutoryKind.Paye ? "PAYE" : "NASSIT")} in {first:MMMM yyyy}.");
        var doc = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-REM-{input.OperationId:N}", Kind = DocumentKind.StatutoryRemittance,
            Party = input.Kind == StatutoryKind.Paye ? "National Revenue Authority (PAYE)" : "NASSIT", BusinessDate = input.Date, Total = amount, Settlement = input.Method.ToString(), Detail = JsonSerializer.Serialize(input) };
        db.Documents.Add(doc);
        db.StatutoryRemittances.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, Month = first, Kind = input.Kind, Amount = amount, Reference = reference });
        Line(db, doc, input.Kind == StatutoryKind.Paye ? PayrollMath.PayePayable : PayrollMath.NassitPayable, amount, 0); Line(db, doc, (int)account, 0, amount);
        return doc.Id;
    });

    public async Task<byte[]> ExportSchedule(DateOnly month)
    {
        var s = await Schedule(month);
        static string M(decimal v) => v.ToString("0.00");
        return WorkbookWriter.Write([
            ("PAYE schedule", [["Code", "Employee", "TIN", "Gross pay", "Tax-free allowances", "Employee NASSIT", "Chargeable income", "PAYE"], .. s.Lines.Select(l => (IReadOnlyList<string>)[l.Code, l.Name, l.Tin, M(l.Gross), M(l.TaxFree), M(l.NassitEmployee), M(l.Chargeable), M(l.Paye)]), ["", "Total", "", M(s.Lines.Sum(x => x.Gross)), "", "", M(s.Lines.Sum(x => x.Chargeable)), M(s.Paye)]]),
            ("NASSIT schedule", [["Code", "Employee", "NASSIT number", "Contribution base", "Employee 5%", "Employer 10%", "Total"], .. s.Lines.Select(l => (IReadOnlyList<string>)[l.Code, l.Name, l.NassitNumber, M(l.NassitBase), M(l.NassitEmployee), M(l.NassitEmployer), M(l.NassitEmployee + l.NassitEmployer)]), ["", "Total", "", M(s.Lines.Sum(x => x.NassitBase)), M(s.NassitEmployee), M(s.NassitEmployer), M(s.NassitEmployee + s.NassitEmployer)]]),
            ("Remittances", [["Date", "Kind", "Document", "Reference", "Amount"], .. s.Remittances.Select(r => (IReadOnlyList<string>)[r.Date.ToString("yyyy-MM-dd"), r.Kind == StatutoryKind.Paye ? "PAYE" : "NASSIT", r.Number, r.Reference, M(r.Amount)]),
                ["", "PAYE outstanding", "", "", M(s.PayeOutstanding)], ["", "NASSIT outstanding", "", "", M(s.NassitOutstanding)]])
        ]);
    }

    // ---------------- staff loans and bank payment files ----------------

    public async Task<IReadOnlyList<LoanView>> Loans()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var employees = await db.Employees.AsNoTracking().Where(x => x.Branch == actor.Branch).ToDictionaryAsync(x => x.Id);
        var loans = await db.EmployeeLoans.AsNoTracking().Where(l => db.Documents.Any(d => d.Id == l.DocumentId && d.Branch == actor.Branch)).ToListAsync();
        var docIds = loans.Select(x => x.DocumentId).ToList();
        var docs = await db.Documents.AsNoTracking().Where(x => docIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var reversed = (await db.Documents.AsNoTracking().Where(x => x.Reverses != null && docIds.Contains(x.Reverses.Value)).Select(x => x.Reverses!.Value).ToListAsync()).ToHashSet();
        var balances = await PayrollMath.LoanBalances(db, actor.Branch);
        // Repayments are applied to an employee's loans oldest first.
        var views = new List<LoanView>();
        foreach (var g in loans.GroupBy(x => x.EmployeeId))
        {
            var active = g.Where(l => !reversed.Contains(l.DocumentId)).OrderBy(l => docs[l.DocumentId].BusinessDate).ToList();
            var repaid = active.Sum(l => l.Principal) - balances.GetValueOrDefault(g.Key).Outstanding;
            foreach (var l in g.OrderBy(l => docs[l.DocumentId].BusinessDate))
            {
                var isReversed = reversed.Contains(l.DocumentId);
                var part = isReversed ? 0 : Math.Min(l.Principal, repaid); if (!isReversed) repaid -= part;
                var e = employees.GetValueOrDefault(l.EmployeeId);
                views.Add(new(l.DocumentId, Short(docs[l.DocumentId].Number), l.EmployeeId, e is null ? "" : $"{e.Code} · {e.Name}", docs[l.DocumentId].BusinessDate, l.Principal, l.Installment, part, isReversed ? 0 : l.Principal - part, l.Reason, isReversed));
            }
        }
        return views.OrderByDescending(x => x.Date).ToArray();
    }

    public Task<Guid> GiveLoan(LoanCommand input) => Execute(input.OperationId, "payroll.loan", input, async (db, actor) =>
    {
        await CheckDate(db, actor, input.Date);
        var account = Paid(input.Method);
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == input.EmployeeId && x.Branch == actor.Branch) ?? throw new BusinessException("Employee not found.");
        SierraBusiness.Domain.Rules.Positive(input.Principal, "Loan amount"); SierraBusiness.Domain.Rules.Positive(input.Installment, "Monthly instalment");
        if (AccountingEngine.Money(input.Principal) != input.Principal || AccountingEngine.Money(input.Installment) != input.Installment || input.Installment > input.Principal) throw new BusinessException("The instalment is at most the loan amount, both with up to two decimals.");
        var reason = SierraBusiness.Domain.Rules.Required(input.Reason, "Reason", 200);
        if (input.Method == PaymentMethod.BankTransfer) SierraBusiness.Domain.Rules.Required(input.Reference, "Transfer reference", 100);
        if ((await PayrollMath.LoanBalances(db, actor.Branch)).TryGetValue(employee.Id, out var b) && b.Outstanding > 0) throw new BusinessException($"{employee.Code} still owes NLe {b.Outstanding:N2} on an earlier loan.");
        var doc = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-LOAN-{input.OperationId:N}", Kind = DocumentKind.EmployeeLoan, Party = $"{employee.Code} {employee.Name}",
            BusinessDate = input.Date, Total = input.Principal, Settlement = input.Method.ToString(), Detail = JsonSerializer.Serialize(input) };
        db.Documents.Add(doc);
        db.EmployeeLoans.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, EmployeeId = employee.Id, Principal = input.Principal, Installment = input.Installment, Reason = reason });
        Line(db, doc, PayrollMath.Loans, input.Principal, 0); Line(db, doc, (int)account, 0, input.Principal);
        return doc.Id;
    });

    public async Task<byte[]> BankFile(IReadOnlyList<Guid> payrollItemIds)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        if (payrollItemIds is null || payrollItemIds.Count == 0) throw new BusinessException("Select the payslips to pay.");
        var slips = (await Payslips(db, actor.Branch, null)).Where(x => payrollItemIds.Contains(x.ItemId)).ToList();
        if (slips.Count != payrollItemIds.Count || slips.Any(x => x.Reversed)) throw new BusinessException("A selected payslip was not found or has been reversed.");
        var employees = await db.Employees.AsNoTracking().Where(x => x.Branch == actor.Branch).ToDictionaryAsync(x => x.Id);
        var missing = slips.Where(s => employees.GetValueOrDefault(s.EmployeeId) is not { } e || e.BankName.Length == 0 || e.BankAccount.Length == 0).Select(s => s.EmployeeCode).ToList();
        if (missing.Count > 0) throw new BusinessException($"Add bank details for: {string.Join(", ", missing)}.");
        static string C(string v) => v.Contains(',') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        var sb = new StringBuilder("Employee code,Employee name,Bank,Account number,Amount,Reference\r\n");
        foreach (var s in slips.OrderBy(x => x.EmployeeCode))
        {
            var e = employees[s.EmployeeId];
            sb.Append($"{C(s.EmployeeCode)},{C(s.EmployeeName)},{C(e.BankName)},{C(e.BankAccount)},{s.Net.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},{C($"Salary {s.Month:yyyy-MM}")}\r\n");
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private async Task<Guid> Execute<T>(Guid operation, string action, T input, Func<BusinessDb, Actor, Task<Guid>> work)
    {
        var actor = await Authorize(); if (operation == Guid.Empty) throw new BusinessException("Operation ID is required.");
        var payload = JsonSerializer.Serialize(input); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + action + payload)));
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.Operations.SingleOrDefaultAsync(x => x.Id == operation); if (previous is not null) { if (hash != previous.Fingerprint) throw new BusinessException("Operation ID already used with different data or identity."); return previous.ResultId; }
        var id = await work(db, actor); db.Operations.Add(new() { Id = operation, ResultId = id, Branch = actor.Branch, Actor = actor.Id, Action = action, Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = hash }); await db.SaveChangesAsync(); await tx.CommitAsync(); return id;
    }
}
