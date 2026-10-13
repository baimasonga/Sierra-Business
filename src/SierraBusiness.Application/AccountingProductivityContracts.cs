namespace SierraBusiness.Application;

public sealed record AllocationClassCommand(Guid OperationId, Guid? Id, int ExpectedVersion, string Name, List<CentreShare> Shares, bool Active);
public sealed record AllocationClassView(Guid Id, int Version, string Name, IReadOnlyList<CentreShare> Shares, bool Active);
public sealed record UseAllocationClassCommand(Guid OperationId, Guid ClassId, int ExpectedVersion, DateOnly Date, string Narration, int AccountCode, int FundsAccount, bool Income, decimal Amount, Guid? ApprovalId = null);
public sealed record ProjectCommand(Guid OperationId, Guid? Id, int ExpectedVersion, string Code, string Name, Guid CentreId, DateOnly Start, DateOnly? End, decimal RevenueBudget, decimal ExpenseBudget, bool Active);
public sealed record ProjectView(Guid Id, int Version, string Code, string Name, Guid CentreId, DateOnly Start, DateOnly? End, decimal RevenueBudget, decimal ExpenseBudget, bool Active);
public sealed record ProjectDocumentCommand(Guid OperationId, Guid ProjectId, int ExpectedProjectVersion, Guid DocumentId, string Reason);
public sealed record ProjectBalance(ProjectView Project, decimal Revenue, decimal Expenses, decimal RevenueVariance, decimal ExpenseVariance) { public decimal Profit => Revenue - Expenses; }
public sealed record ProjectReport(DateOnly From, DateOnly To, IReadOnlyList<ProjectBalance> Projects);
public sealed record ScheduledJournalCommand(Guid OperationId, Guid? Id, int ExpectedVersion, string Name, DateOnly Date, DateOnly? ReverseOn, List<VoucherLine> Lines, bool Active);
public sealed record ScheduledJournalView(Guid Id, int Version, string Name, DateOnly Date, DateOnly? ReverseOn, IReadOnlyList<VoucherLine> Lines, bool Active, Guid? PostedDocument, Guid? ReversalDocument);
public sealed record PostScheduledJournalCommand(Guid OperationId, Guid ScheduleId, int ExpectedVersion, bool Reversal, Guid? ApprovalId = null);
public sealed record AccountingProductivitySnapshot(IReadOnlyList<AllocationClassView> Classes, IReadOnlyList<ProjectView> Projects, IReadOnlyList<ScheduledJournalView> Journals);
public sealed record CostCategoryCommand(Guid OperationId, Guid? Id, int ExpectedVersion, string Name, bool Active);
public sealed record CostCategoryView(Guid Id, int Version, string Name, bool Active);
public sealed record CategoryAllocationCommand(Guid OperationId, Guid CategoryId, int ExpectedCategoryVersion, Guid DocumentId, List<CentreShare> Shares);
public sealed record CategoryBalance(Guid CategoryId, string Category, string Centre, decimal Revenue, decimal Expenses) { public decimal Profit => Revenue - Expenses; }
public sealed record CategoryReport(DateOnly From, DateOnly To, IReadOnlyList<CategoryBalance> Rows);
