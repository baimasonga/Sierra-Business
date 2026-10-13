namespace SierraBusiness.Application;
public sealed record ForeignAdvanceCommand(Guid OperationId,TradeAdvanceKind Kind,Guid PartyId,DateOnly Date,string Currency,decimal SourceAmount,int FundsAccount,decimal LocalAmount,string Reference);
public sealed record ForeignAdvanceView(Guid CreditId,Guid DocumentId,TradeAdvanceKind Kind,string Number,string Party,DateOnly Date,string Currency,decimal OriginalSource,decimal RemainingSource,decimal LocalCarrying);
public sealed record ForeignAdvanceUseCommand(Guid OperationId,Guid CreditId,DateOnly Date,decimal SourceAmount,Guid? BillId,int? RefundAccount,decimal LocalRefundAmount,string Reference,string? ExpectedCalculation = null);
public sealed record ForeignAdvanceUsePreview(IReadOnlyList<VoucherLine> Lines,decimal SourceRemaining,decimal CreditCarryingUsed,decimal BillCarryingUsed,string CalculationFingerprint);
