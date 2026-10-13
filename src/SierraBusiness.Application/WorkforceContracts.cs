namespace SierraBusiness.Application;

public enum AttendanceStatus { Present, HalfDayUnpaid, UnpaidAbsence, RestDay }
public enum LeaveKind { Annual, Sick, Other, Unpaid }
public enum LeaveStatus { Pending, Approved, Rejected, Cancelled }
public enum PayHeadKind { TaxableEarning, AfterTaxDeduction }
public sealed record AttendanceCommand(Guid OperationId, Guid EmployeeId, DateOnly Date, int ExpectedVersion, AttendanceStatus Status, decimal Hours, decimal OvertimeHours, string Note);
public sealed record AttendanceView(Guid EmployeeId, string Employee, DateOnly Date, int Version, AttendanceStatus Status, decimal Hours, decimal OvertimeHours, string Note);
public sealed record LeaveEntitlementCommand(Guid OperationId, Guid EmployeeId, int Year, LeaveKind Kind, int ExpectedVersion, decimal Days, string Reason);
public sealed record LeaveBalance(Guid EmployeeId, string Employee, int Year, LeaveKind Kind, int Version, decimal Granted, decimal Used)
{ public decimal Remaining => Granted - Used; }
public sealed record LeaveRequestCommand(Guid OperationId, Guid EmployeeId, LeaveKind Kind, List<DateOnly> Days, string Reason);
public sealed record LeaveDecisionCommand(Guid OperationId, Guid RequestId, int ExpectedVersion, LeaveStatus Status, string Reason);
public sealed record LeaveRequestView(Guid Id, Guid EmployeeId, string Employee, LeaveKind Kind, IReadOnlyList<DateOnly> Days, string Reason, int Version, LeaveStatus Status, string DecisionReason);
public sealed record PayHeadCommand(Guid OperationId, Guid? Id, int ExpectedVersion, string Name, PayHeadKind Kind, string Formula, DateOnly From, DateOnly? To, Guid? EmployeeId, bool Active);
public sealed record PayHeadView(Guid Id, int Version, string Name, PayHeadKind Kind, string Formula, DateOnly From, DateOnly? To, Guid? EmployeeId, bool Active);
public sealed record WorkforceSnapshot(DateOnly Month, IReadOnlyList<AttendanceView> Attendance, IReadOnlyList<LeaveBalance> Balances, IReadOnlyList<LeaveRequestView> Requests, IReadOnlyList<PayHeadView> PayHeads);
public sealed record WorkforcePayrollInput(Guid EmployeeId, decimal UnpaidDays, decimal Hours, decimal OvertimeHours, decimal Earnings, decimal Deductions, string Detail);
