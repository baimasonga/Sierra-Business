namespace SierraBusiness.Application;

public sealed record AccountGroupCommand(Guid OperationId, Guid? Id, int ExpectedVersion, string Name, LedgerGroup Nature, Guid? ParentId);
public sealed record AccountGroupView(Guid Id, int Version, string Name, LedgerGroup Nature, Guid? ParentId);
public sealed record LedgerGroupingCommand(Guid OperationId, int AccountCode, int ExpectedVersion, Guid? GroupId);
public sealed record LedgerGroupingView(int AccountCode, int Version, Guid? GroupId);
public sealed record CostCentreCommand(Guid OperationId, Guid? Id, int ExpectedVersion, string Code, string Name, Guid? ParentId, bool Active);
public sealed record CostCentreView(Guid Id, int Version, string Code, string Name, Guid? ParentId, bool Active);
public sealed record AccountingStructure(IReadOnlyList<AccountGroupView> Groups, IReadOnlyList<LedgerGroupingView> Ledgers, IReadOnlyList<CostCentreView> Centres);
public sealed record GroupBalance(Guid? Id, string Name, LedgerGroup Nature, int Depth, decimal Amount);
public sealed record CostCentreBalance(string Code, string Name, decimal Income, decimal Expenses) { public decimal Profit => Income - Expenses; }
public sealed record StructuredReport(DateOnly From, DateOnly To, IReadOnlyList<GroupBalance> Groups, IReadOnlyList<CostCentreBalance> Centres);
public sealed record CentreShare(Guid CentreId, decimal Percent);
public sealed record AllocateCentreCommand(Guid OperationId, DateOnly Date, string Narration, int AccountCode, int FundsAccount, bool Income, decimal Amount, List<CentreShare> Shares, Guid? ApprovalId = null);
public sealed record CentreAllocationPreview(IReadOnlyList<VoucherLine> Lines, decimal Amount);
public enum ScenarioKind { Adjustment, Memorandum }
public sealed record ScenarioCommand(Guid OperationId, Guid? Id, int ExpectedVersion, string Name, ScenarioKind Kind, DateOnly Date, string Note, List<VoucherLine> Lines, bool Active);
public sealed record ScenarioView(Guid Id, int Version, string Name, ScenarioKind Kind, DateOnly Date, string Note, IReadOnlyList<VoucherLine> Lines, bool Active);
public sealed record ScenarioBalance(int Code, string Name, LedgerGroup Nature, decimal Actual, decimal Change, decimal Projected);
public sealed record ScenarioReport(DateOnly From, DateOnly To, IReadOnlyList<ScenarioBalance> Balances, decimal ActualProfit, decimal ProjectedProfit);
public sealed record ScenarioProjectionRequest(DateOnly From, DateOnly To, List<Guid> Ids);
public enum InterestBasis { Actual365, ActualYear, Actual360 }
public sealed record InterestRequest(DateOnly From, DateOnly To, decimal AnnualPercent, int GraceDays, InterestBasis Basis, bool Receivable = true, string Party = "");
public sealed record InterestRow(Guid DocumentId, string Number, string Party, DateOnly Due, int ChargedDays, decimal StartBalance, decimal EndBalance, decimal Interest);
public sealed record InterestReport(InterestRequest Request, IReadOnlyList<InterestRow> Rows, decimal Total);
/// <summary>Bills reviewed customer interest from a worksheet. The server recalculates it; <paramref name="ExpectedTotal"/> must still match.</summary>
/// <summary>What an interest charge document records: the worksheet settings, payment due date and the interest per bill.</summary>
public sealed record InterestChargeDetail(DateOnly From, DateOnly To, decimal AnnualPercent, int GraceDays, InterestBasis Basis, DateOnly PaymentDue, IReadOnlyList<InterestRow> Bills);
public sealed record ChargeInterestCommand(Guid OperationId, InterestRequest Request, DateOnly Date, IReadOnlyList<Guid> DocumentIds, decimal ExpectedTotal, int DueDays = 0);
