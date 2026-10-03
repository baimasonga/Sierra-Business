using SierraBusiness.Domain;
namespace SierraBusiness.Application;

public interface IActorSession { Task<Actor> CurrentActor(); }
public enum LedgerGroup { Asset, Liability, Equity, Income, Expense }
public enum VoucherType { Journal, Payment, Receipt, Contra }
public sealed record LedgerInput(Guid OperationId, int Code, string Name, LedgerGroup Group, bool CashOrBank = false);
public sealed record LedgerDefinition(int Code, string Name, LedgerGroup Group, bool CashOrBank, bool Control);
public sealed record VoucherLine(int AccountCode, decimal Debit, decimal Credit, string CostCentre = "");
public sealed record BillSettlement(Guid DocumentId, decimal Amount);
public sealed record VoucherCommand(Guid OperationId, VoucherType Type, DateOnly Date, string Narration, List<VoucherLine> Lines, List<BillSettlement> Allocations, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? ApprovalId = null);
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
    Task<FinanceSnapshot> Read(DateOnly from, DateOnly to);
    Task<Guid> CreateLedger(LedgerInput input);
    Task<Guid> PostVoucher(VoucherCommand input);
    Task<Guid> Reconcile(BankMatchCommand input);
    Task<Guid> CancelMatch(CancelBankMatchCommand input);
    Task<IReadOnlyList<BankMatchHistory>> MatchHistory();
    Task<CashFlowReport> CashFlow(DateOnly from, DateOnly to);
    Task<Guid> SetBudget(BudgetCommand input);
}

