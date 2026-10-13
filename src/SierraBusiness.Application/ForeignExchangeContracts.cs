namespace SierraBusiness.Application;

public sealed record ForeignCollectionCommand(Guid OperationId, Guid BillId, DateOnly Date, decimal SourceAmount, decimal LocalReceived, int FundsAccount, string Reference);
public sealed record ForeignReceivableView(Guid BillId, string Number, string Customer, DateOnly Date, string Currency, decimal InvoiceRate, decimal OriginalSource, decimal SourceOutstanding, decimal LocalCarrying);
public enum ForeignBankTradeKind { Deposit, Withdrawal, Transfer }
public sealed record ForeignBankCommand(Guid OperationId, int AccountCode, string Currency);
public sealed record ForeignBankView(int AccountCode, string Name, string Currency, decimal SourceBalance, decimal LocalCarrying);
public sealed record ForeignBankTradeCommand(Guid OperationId, ForeignBankTradeKind Kind, DateOnly Date, int BankAccount, int CounterAccount, decimal SourceAmount, decimal LocalAmount, decimal TargetSourceAmount, string Reference, string? ExpectedCalculation = null);
public sealed record ForeignBankTradePreview(IReadOnlyList<VoucherLine> Lines, decimal SourceBalanceAfter, decimal SourceCarryingUsed, decimal TargetSourceBalanceAfter, string CalculationFingerprint);
public sealed record ForeignRevaluationRequest(DateOnly Date, List<string> Currencies);
public sealed record ForeignValuationRow(string Key, string Kind, Guid? BillId, int AccountCode, string Number, string Party, string Currency, decimal SourceBalance, decimal Carrying, decimal ClosingRate, decimal ClosingValue, decimal Gain);
public sealed record ForeignRevaluationPreview(ForeignRevaluationRequest Request, IReadOnlyList<ForeignValuationRow> Rows, decimal NetGain, string CalculationFingerprint);
public sealed record ForeignRevaluationCommand(Guid OperationId, ForeignRevaluationRequest Request, string ExpectedCalculation, string Reason);
public sealed record CancelForeignRevaluationCommand(Guid OperationId, Guid DocumentId, string Reason);
public sealed record ForeignRevaluationView(Guid Id, string Number, DateOnly Date, DateOnly ReversedOn, decimal NetGain, bool Cancelled, string Reason);
