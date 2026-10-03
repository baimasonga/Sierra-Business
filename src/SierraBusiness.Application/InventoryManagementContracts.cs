namespace SierraBusiness.Application;

public sealed record ProductSettings(Guid ProductId, int Version = 0, bool Active = true, string Brand = "", string Model = "", string Family = "", string Attributes = "", string Compatibility = "", string PurchaseUnit = "", decimal PurchaseConversion = 1, string SalesUnit = "", decimal SalesConversion = 1);
public sealed record SaveProductSettings(Guid OperationId, ProductSettings Settings);
public sealed record ReorderRule(Guid ProductId, string Location, int Version = 0, decimal Minimum = 0, decimal Target = 0, Guid? SupplierId = null);
public sealed record SaveReorderRule(Guid OperationId, ReorderRule Rule);
public sealed record ReorderView(ReorderRule Rule, decimal OnHand, decimal Reserved, decimal Expired, decimal SuggestedQuantity);
public sealed record CountSessionCommand(Guid OperationId, string Name, IReadOnlyList<StockCountCommand> Lines);
public sealed record CountDecision(Guid OperationId, Guid SessionId, bool Approve, string Reason);
public sealed record CountSessionView(Guid Id, string Name, string PreparedBy, DateTimeOffset Time, string Status, IReadOnlyList<StockCountCommand> Lines, IReadOnlyList<Guid> Documents, string DecisionReason);
public sealed record BarcodeChange(Guid OperationId, Guid BarcodeId, int ExpectedVersion, Guid ProductId, decimal Conversion, bool Retired, string Reason);
public sealed record SerialServiceCommand(Guid OperationId, Guid ProductId, string Serial, bool InService, int ExpectedVersion, string Reason);
public sealed record SerialServiceState(Guid ProductId, string Serial, bool InService, int Version);
public sealed record SerialRegisterView(Guid ProductId, string Serial, string Location, string Status, decimal Value, int ServiceVersion);
public sealed record BatchMetadata(Guid ProductId, string Batch, DateOnly? Expiry, int Version = 0, DateOnly? Manufactured = null, string SupplierLot = "", string Notes = "");
public sealed record SaveBatchMetadata(Guid OperationId, BatchMetadata Metadata);
public sealed record BatchRegisterView(BatchMetadata Metadata, string Location, decimal Quantity, decimal Reserved, decimal Value, DateOnly? FirstReceived, bool Expired);
public sealed record ManagedBarcode(Guid Id, string Code, Guid ProductId, decimal Conversion, int Version, bool Retired);
public sealed record InventoryTrace(Guid DocumentId, string Document, DateOnly Date, string Kind, Guid ProductId, string Location, string? Serial, string? Batch, DateOnly? Expiry, decimal Quantity, decimal Value, string User, DateTimeOffset? Time);
public interface IInventoryManagement
{
    Task<IReadOnlyList<ProductSettings>> Products();
    Task<Guid> SaveProduct(SaveProductSettings input);
    Task<IReadOnlyList<ReorderView>> Reorder();
    Task<Guid> SaveReorder(SaveReorderRule input);
    Task<IReadOnlyList<CountSessionView>> Counts();
    Task<Guid> SubmitCount(CountSessionCommand input);
    Task<Guid> DecideCount(CountDecision input);
    Task<IReadOnlyList<ManagedBarcode>> Barcodes();
    Task<Guid> ChangeBarcode(BarcodeChange input);
    Task<IReadOnlyList<InventoryTrace>> Trace(Guid productId);
    Task<IReadOnlyList<SerialRegisterView>> Serials();
    Task<Guid> SetSerialService(SerialServiceCommand input);
    Task<IReadOnlyList<BatchRegisterView>> Batches();
    Task<Guid> SaveBatch(SaveBatchMetadata input);
}
