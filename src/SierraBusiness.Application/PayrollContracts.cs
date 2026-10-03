namespace SierraBusiness.Application;
public sealed record EmployeeCommand(Guid OperationId, Guid? EmployeeId, int ExpectedVersion, string Code, string Name, string Department, decimal BasicSalary, decimal Allowances, decimal Deductions, DateOnly StartDate, DateOnly? EndDate);
public sealed record EmployeeView(Guid Id, int Version, string Code, string Name, string Department, decimal BasicSalary, decimal Allowances, decimal Deductions, DateOnly StartDate, DateOnly? EndDate);
public sealed record PayrollCommand(Guid OperationId, DateOnly Month, DateOnly PostingDate, List<Guid> EmployeeIds);
public sealed record PayslipView(Guid DocumentId, string Number, DateOnly Month, DateOnly Posted, string EmployeeCode, string EmployeeName, string Department, decimal Basic, decimal Allowances, decimal Deductions, decimal Net, bool Reversed);
public sealed record PayrollSnapshot(IReadOnlyList<EmployeeView> Employees, IReadOnlyList<PayslipView> Payslips);
public interface IPayrollService
{
    Task<PayrollSnapshot> Read();
    Task<Guid> SaveEmployee(EmployeeCommand input);
    Task<Guid> Post(PayrollCommand input);
}
