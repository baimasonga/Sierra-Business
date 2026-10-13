using SierraBusiness.Domain;
using System.Text.Json.Serialization;
namespace SierraBusiness.Application;

/// <param name="Tin">Employee's taxpayer identification number (for the PAYE schedule).</param>
/// <param name="NassitNumber">Employee's social security number (for the NASSIT schedule).</param>
/// <param name="Resident">Residents pay PAYE on the bands; non-residents at the flat non-resident rate. Default: resident.</param>
/// <param name="NassitExempt">Not a NASSIT contributor (for example a pensioner re-employed). Default: contributes.</param>
/// <param name="BirthDate">Used only to stop NASSIT contributions after the age limit.</param>
public sealed record EmployeeCommand(Guid OperationId, Guid? EmployeeId, int ExpectedVersion, string Code, string Name, string Department, decimal BasicSalary, decimal Allowances, decimal Deductions, DateOnly StartDate, DateOnly? EndDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tin = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NassitNumber = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Resident = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? NassitExempt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? BirthDate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BankName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BankAccount = null);
public sealed record EmployeeView(Guid Id, int Version, string Code, string Name, string Department, decimal BasicSalary, decimal Allowances, decimal Deductions, DateOnly StartDate, DateOnly? EndDate,
    string Tin = "", string NassitNumber = "", bool Resident = true, bool NassitExempt = false, DateOnly? BirthDate = null, string BankName = "", string BankAccount = "");

/// <summary>One-off changes for one employee in one payroll run.</summary>
/// <param name="UnpaidDays">Calendar days of unpaid absence deducted from basic salary and allowances.</param>
/// <param name="Overtime">Taxable overtime for the month.</param>
/// <param name="Bonus">Taxable bonus or other one-off earning.</param>
/// <param name="ExtraDeduction">One-off deduction after tax (for example a salary advance repaid).</param>
/// <param name="LeaveAllowance">Leave allowance paid this month (taxed by the leave rules).</param>
/// <param name="EndOfService">End-of-service or redundancy benefit (tax-free when the rules say so; no NASSIT).</param>
/// <param name="SkipLoan">Do not deduct the loan instalment this month.</param>
public sealed record PayrollAdjustment(Guid EmployeeId, decimal UnpaidDays = 0, decimal Overtime = 0, decimal Bonus = 0, decimal ExtraDeduction = 0, string Note = "", decimal LeaveAllowance = 0, decimal EndOfService = 0, bool SkipLoan = false);
public sealed record PayrollCommand(Guid OperationId, DateOnly Month, DateOnly PostingDate, List<Guid> EmployeeIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<PayrollAdjustment>? Adjustments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? UseWorkforceInputs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpectedCalculation = null);

/// <summary>How one employee's pay was worked out. Stored with the payslip so later rule or salary changes never alter it.</summary>
public sealed record PayCalculation(decimal DaysInMonth, decimal DaysPaid, decimal Basic, decimal Allowances, decimal Overtime, decimal Bonus, decimal Gross,
    decimal TaxFreeAllowances, decimal NassitBase, decimal NassitEmployee, decimal Chargeable, decimal Paye, decimal OtherDeductions, decimal Net, decimal NassitEmployer,
    bool Statutory, int RulesVersion, string Note = "", decimal LeaveAllowance = 0, decimal LeaveTaxFree = 0, decimal LeaveExcessTax = 0, decimal EndOfService = 0, decimal LoanRepayment = 0);
public sealed record PayslipView(Guid DocumentId, string Number, DateOnly Month, DateOnly Posted, string EmployeeCode, string EmployeeName, string Department, decimal Basic, decimal Allowances, decimal Deductions, decimal Net, bool Reversed,
    Guid ItemId = default, Guid EmployeeId = default, PayCalculation? Calculation = null, string Tin = "", string NassitNumber = "", string PaymentNumber = "", DateOnly? PaidOn = null)
{
    public bool Paid => PaymentNumber.Length > 0;
}
public sealed record PayrollPreviewLine(Guid EmployeeId, string Code, string Name, string Department, PayCalculation Calculation);
public sealed record PayrollPreview(DateOnly Month, IReadOnlyList<PayrollPreviewLine> Lines, decimal Gross, decimal Paye, decimal NassitEmployee, decimal NassitEmployer, decimal OtherDeductions, decimal Net, bool Statutory, string RulesNote, string CalculationFingerprint = "");

// ---------------- statutory rules ----------------

public enum NassitBase { BasicSalary, GrossPay }
/// <summary>A PAYE band on monthly chargeable income: income up to <see cref="UpTo"/> (null = everything above the previous band) is taxed at <see cref="Rate"/>.</summary>
public sealed record PayeBand(decimal? UpTo, decimal Rate);
/// <summary>PAYE and NASSIT settings from a month. Statutory changes are new versions with a start month, never code changes.</summary>
/// <param name="TaxFreeAllowances">Monthly allowances (rent, transport, medical and similar) that are not taxed, up to this amount.</param>
/// <param name="LeaveUpToBasicTaxFree">Leave allowance up to one month's basic salary is not taxed; any excess is taxed at <see cref="LeaveExcessRate"/>. Otherwise leave allowance is taxed with other pay.</param>
/// <param name="EndOfServiceTaxFree">End-of-service and redundancy benefits are exempt (Finance Act 2026).</param>
/// <param name="NassitAgeLimit">Contributions stop from the month after the employee reaches this age. 0: no limit.</param>
public sealed record PayrollRules(int Version = 0, DateOnly EffectiveFrom = default, IReadOnlyList<PayeBand>? Bands = null, decimal TaxFreeAllowances = 0, decimal NonResidentRate = 0,
    decimal NassitEmployee = 0, decimal NassitEmployer = 0, NassitBase NassitBase = NassitBase.BasicSalary, int NassitAgeLimit = 0, string Source = "", string SavedBy = "", DateTimeOffset? Saved = null,
    bool LeaveUpToBasicTaxFree = true, decimal LeaveExcessRate = .30m, bool EndOfServiceTaxFree = true)
{
    public IReadOnlyList<PayeBand> PayeBands => Bands ?? [];
    /// <summary>Sierra Leone monthly PAYE bands of the Finance Act 2021 (unchanged by the Finance Acts 2025 and 2026), SLE 500 tax-free allowances, 25% non-resident rate, NASSIT 5% employee and 10% employer on basic salary, stopping at 60. Verify with the NRA and NASSIT before use.</summary>
    public static PayrollRules Recommended(DateOnly from) => new(0, from, [new(600, 0), new(1200, .15m), new(1800, .20m), new(2400, .25m), new(null, .30m)], 500, .25m, .05m, .10m, NassitBase.BasicSalary, 60,
        "Finance Act 2021 PAYE bands (unchanged by the Finance Acts 2025 and 2026); NASSIT 5% employee, 10% employer on basic salary. Verify with the NRA and NASSIT before use.");
}
public sealed record SavePayrollRules(Guid OperationId, PayrollRules Rules);
public sealed record PayrollRulesView(PayrollRules? Current, IReadOnlyList<PayrollRules> History);

// ---------------- payments and remittances ----------------

public sealed record PaySalariesCommand(Guid OperationId, DateOnly Date, PaymentMethod Method, string Reference, List<Guid> PayrollItemIds);
public enum StatutoryKind { Paye, Nassit }
public sealed record RemitCommand(Guid OperationId, DateOnly Month, StatutoryKind Kind, DateOnly Date, PaymentMethod Method, string Reference);
public sealed record ScheduleLine(string Code, string Name, string Tin, string NassitNumber, decimal Gross, decimal TaxFree, decimal NassitEmployee, decimal Chargeable, decimal Paye, decimal NassitBase, decimal NassitEmployer);
public sealed record RemittanceView(Guid DocumentId, string Number, DateOnly Date, StatutoryKind Kind, decimal Amount, string Reference);
public sealed record StatutorySchedule(DateOnly Month, IReadOnlyList<ScheduleLine> Lines, decimal Paye, decimal NassitEmployee, decimal NassitEmployer, decimal PayeRemitted, decimal NassitRemitted, IReadOnlyList<RemittanceView> Remittances)
{
    public decimal PayeOutstanding => Paye - PayeRemitted;
    public decimal NassitOutstanding => NassitEmployee + NassitEmployer - NassitRemitted;
}

public sealed record LoanCommand(Guid OperationId, Guid EmployeeId, decimal Principal, decimal Installment, DateOnly Date, PaymentMethod Method, string Reference, string Reason);
public sealed record LoanView(Guid DocumentId, string Number, Guid EmployeeId, string Employee, DateOnly Date, decimal Principal, decimal Installment, decimal Repaid, decimal Outstanding, string Reason, bool Reversed);
public sealed record PayrollSnapshot(IReadOnlyList<EmployeeView> Employees, IReadOnlyList<PayslipView> Payslips);
public interface IPayrollService
{
    Task<WorkforceSnapshot> Workforce(DateOnly month);
    Task<Guid> SaveAttendance(AttendanceCommand input);
    Task<Guid> SetLeaveEntitlement(LeaveEntitlementCommand input);
    Task<Guid> RequestLeave(LeaveRequestCommand input);
    Task<Guid> DecideLeave(LeaveDecisionCommand input);
    Task<Guid> SavePayHead(PayHeadCommand input);
    Task<byte[]> ExportWorkforce(DateOnly month);
    Task<PayrollSnapshot> Read();
    Task<Guid> SaveEmployee(EmployeeCommand input);
    Task<PayrollPreview> Preview(PayrollCommand input);
    Task<Guid> Post(PayrollCommand input);
    Task<PayrollRulesView> Rules();
    Task<Guid> SaveRules(SavePayrollRules input);
    Task<Guid> PaySalaries(PaySalariesCommand input);
    Task<StatutorySchedule> Schedule(DateOnly month);
    Task<Guid> Remit(RemitCommand input);
    Task<byte[]> ExportSchedule(DateOnly month);
    Task<IReadOnlyList<LoanView>> Loans();
    Task<Guid> GiveLoan(LoanCommand input);
    /// <summary>A CSV file for the bank listing each selected payslip's net pay, bank and account number.</summary>
    Task<byte[]> BankFile(IReadOnlyList<Guid> payrollItemIds);
}
