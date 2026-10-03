namespace SierraBusiness.Application;

/// <summary>Specification Skill 13: foreign-currency purchasing, supplier payment and landed cost. NLe (SLE) is the functional currency.</summary>
public sealed record ExchangeRateCommand(Guid OperationId, string Currency, DateOnly EffectiveFrom, decimal Rate, string Source);
public sealed record ExchangeRateView(string Currency, DateOnly EffectiveFrom, decimal Rate, string Source);

public sealed record ForeignBillView(Guid BillId, string Number, string Party, DateOnly Date, string Currency, decimal Rate, decimal SourceAmount, decimal SourceOutstanding, decimal CarryingOutstanding);
/// <summary>Settles part or all of a foreign-currency bill. LocalPaid is the NLe actually paid; the difference from the carrying amount is an exchange gain or loss.</summary>
public sealed record ForeignPaymentCommand(Guid OperationId, Guid BillId, DateOnly Date, decimal SourceAmount, decimal LocalPaid, int FundsAccount, string Reference);

public enum LandedCostBasis { Value, Quantity, Manual }
public enum LandedCostCharge { Freight, Insurance, Clearing, Duty, PortCharges, BankCharges, LocalTransport, Other }
public sealed record LandedCostChargeLine(LandedCostCharge Type, decimal Amount, string Description = "");
/// <summary>A service provider's invoice (or cash payment) for costs of bringing goods in. Payable to Party unless FundsAccount is given.</summary>
public sealed record LandedCostCommand(Guid OperationId, string Party, DateOnly Date, List<Guid> Receipts, List<LandedCostChargeLine> Charges, LandedCostBasis Basis,
    int? FundsAccount = null, string? Currency = null, string Reference = "", Dictionary<Guid, decimal>? ManualShares = null);
public sealed record ReceiptLineView(Guid MovementId, Guid ReceiptId, string ReceiptNumber, string Sku, string Product, string Location, string? Serial, string? Batch, decimal Quantity, decimal Value);
public sealed record LandedReceiptView(Guid Id, string Number, string Party, DateOnly Date, decimal Total, string Currency, IReadOnlyList<ReceiptLineView> Lines);
public sealed record LandedAllocationView(Guid MovementId, string Sku, string Location, decimal Weight, decimal Allocated, decimal ToInventory, decimal ToCostOfSales);
public sealed record LandedCostPreview(decimal LocalTotal, decimal Rate, IReadOnlyList<LandedAllocationView> Lines);

public interface IPurchasingService
{
    Task<IReadOnlyList<ExchangeRateView>> Rates();
    Task<Guid> SetRate(ExchangeRateCommand input);
    Task<IReadOnlyList<ForeignBillView>> ForeignBills();
    Task<Guid> PayForeign(ForeignPaymentCommand input);
    Task<IReadOnlyList<LandedReceiptView>> Receipts();
    Task<LandedCostPreview> PreviewLandedCost(LandedCostCommand input);
    Task<Guid> PostLandedCost(LandedCostCommand input);
}
