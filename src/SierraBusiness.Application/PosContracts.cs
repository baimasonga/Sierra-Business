namespace SierraBusiness.Application;
public sealed record BarcodeCommand(Guid OperationId, Guid ProductId, string Code, decimal Conversion = 1);
public sealed record BarcodeView(Guid Id, Guid ProductId, string Code, decimal Conversion);
public sealed record HoldSaleCommand(Guid OperationId, string Label, TradeCommand Draft, Guid? ReplaceId = null);
public sealed record CancelHeldSaleCommand(Guid OperationId, Guid HeldSaleId, string Reason);
public sealed record HeldSaleView(Guid Id, string Label, string Cashier, DateTimeOffset Time, TradeCommand Draft);
public interface IPosService
{
    Task<IReadOnlyList<BarcodeView>> Barcodes();
    Task<Guid> AddBarcode(BarcodeCommand input);
    Task<IReadOnlyList<HeldSaleView>> HeldSales();
    Task<Guid> Hold(HoldSaleCommand input);
    Task<Guid> Cancel(CancelHeldSaleCommand input);
}
