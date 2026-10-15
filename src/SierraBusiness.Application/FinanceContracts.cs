using SierraBusiness.Domain;
namespace SierraBusiness.Application;

public interface IActorSession { Task<Actor> CurrentActor(); }
public enum LedgerGroup { Asset, Liability, Equity, Income, Expense }
public enum VoucherType { Journal, Payment, Receipt, Contra }
public sealed record LedgerInput(Guid OperationId, int Code, string Name, LedgerGroup Group, bool CashOrBank = false);
public sealed record LedgerDefinition(int Code, string Name, LedgerGroup Group, bool CashOrBank, bool Control);
public sealed record VoucherLine(int AccountCode, decimal Debit, decimal Credit, string CostCentre = "");
public sealed record BillSettlement(Guid DocumentId, decimal Amount);
public sealed record VoucherCommand(Guid OperationId, VoucherType Type, DateOnly Date, string Narration, List<VoucherLine> Lines, List<BillSettlement> Allocations, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? ApprovalId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? AllocationSource = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? StatementRowId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IssueChequeCommand? ChequeIssue = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ExternalReference = null);
public sealed record BankMatchCommand(Guid OperationId, Guid JournalLineId, DateOnly ClearedDate, string StatementReference, Guid? StatementRowId = null);
public sealed record CancelBankMatchCommand(Guid OperationId, Guid MatchId, string Reason);
public sealed record BankMatchHistory(Guid Id, Guid JournalLineId, int AccountCode, string Number, DateOnly ClearedDate, string Reference, string? CancellationReason);
public sealed record CashFlowEntry(Guid DocumentId, string Number, DateOnly Date, string Narration, decimal Received, decimal Paid);
public sealed record CashFlowReport(decimal Opening, decimal Received, decimal Paid, decimal Closing, IReadOnlyList<CashFlowEntry> Entries);
public sealed record BudgetCommand(Guid OperationId, int AccountCode, DateOnly From, DateOnly To, decimal Amount, string CostCentre);
public sealed record LedgerBalance(int Code, string Name, LedgerGroup Group, decimal Opening, decimal Debit, decimal Credit, decimal Closing);
public sealed record FinanceEntry(Guid Id, Guid DocumentId, string Number, DateOnly Date, string Party, int AccountCode, string AccountName, decimal Debit, decimal Credit, string CostCentre, bool Reversed, DateOnly? ClearedDate, string StatementReference);
public sealed record OutstandingBill(Guid Id, string Number, string Party, bool Receivable, DateOnly Date, DateOnly DueDate, decimal Original, decimal Settled, decimal Outstanding, int OverdueDays);
public sealed record BudgetView(int AccountCode, string AccountName, DateOnly From, DateOnly To, string CostCentre, decimal Budget, decimal Actual);
public sealed record BankBalance(int AccountCode, decimal Book, decimal Uncleared, decimal Cleared);
public sealed record FinanceSnapshot(IReadOnlyList<LedgerDefinition> Accounts, IReadOnlyList<LedgerBalance> Balances, IReadOnlyList<FinanceEntry> Entries, IReadOnlyList<OutstandingBill> Bills, IReadOnlyList<BudgetView> Budgets, decimal Profit, decimal Assets, decimal Liabilities, decimal Equity, decimal RetainedProfit, IReadOnlyList<BankBalance> BankBalances);
public interface IFinanceService
{
    Task<AccountingProductivitySnapshot> Productivity();
    Task<IReadOnlyList<CostCategoryView>> CostCategories();
    Task<Guid> SaveCostCategory(CostCategoryCommand input);
    Task<Guid> AllocateCategory(CategoryAllocationCommand input);
    Task<CategoryReport> CategoryAnalysis(DateOnly from, DateOnly to);
    Task<Guid> SaveAllocationClass(AllocationClassCommand input);
    Task<CentreAllocationPreview> PreviewAllocationClass(UseAllocationClassCommand input);
    Task<Guid> PostAllocationClass(UseAllocationClassCommand input);
    Task<Guid> SaveProject(ProjectCommand input);
    Task<Guid> AssignProjectDocument(ProjectDocumentCommand input);
    Task<ProjectReport> ProjectProfitability(DateOnly from, DateOnly to);
    Task<Guid> ScheduleJournal(ScheduledJournalCommand input);
    Task<Guid> PostScheduledJournal(PostScheduledJournalCommand input);
    Task<AccountingStructure> Structure();
    Task<Guid> SaveAccountGroup(AccountGroupCommand input);
    Task<Guid> AssignLedgerGroup(LedgerGroupingCommand input);
    Task<Guid> SaveCostCentre(CostCentreCommand input);
    Task<StructuredReport> StructureReport(DateOnly from, DateOnly to);
    Task<CentreAllocationPreview> PreviewCentreAllocation(AllocateCentreCommand input);
    Task<Guid> PostCentreAllocation(AllocateCentreCommand input);
    Task<IReadOnlyList<ScenarioView>> Scenarios();
    Task<Guid> SaveScenario(ScenarioCommand input);
    Task<ScenarioReport> ProjectScenarios(DateOnly from, DateOnly to, IReadOnlyList<Guid> ids);
    Task<InterestReport> Interest(InterestRequest input);
    Task<Guid> ChargeInterest(ChargeInterestCommand input);
    Task<byte[]> ExportAdvanced(DateOnly from, DateOnly to, IReadOnlyList<Guid> scenarioIds, InterestRequest? interest = null);
    Task<FinanceSnapshot> Read(DateOnly from, DateOnly to);
    Task<Guid> CreateLedger(LedgerInput input);
    Task<Guid> PostVoucher(VoucherCommand input);
    Task<Guid> Reconcile(BankMatchCommand input);
    Task<Guid> CancelMatch(CancelBankMatchCommand input);
    Task<IReadOnlyList<BankMatchHistory>> MatchHistory();
    Task<CashFlowReport> CashFlow(DateOnly from, DateOnly to);
    Task<Guid> SetBudget(BudgetCommand input);
}

