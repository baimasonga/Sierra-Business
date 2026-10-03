namespace SierraBusiness.Application;
public sealed record LocationCommand(Guid OperationId, string Code, string Name);
public sealed record LocationView(string Code, string Name);
public sealed record BatchPick(string Batch, DateOnly? Expiry, decimal Quantity);
public sealed record BatchSuggestionCommand(Guid ProductId, string Location, DateOnly Date, decimal Quantity, IReadOnlyList<BatchPick>? DraftAllocations = null);
public sealed record TransferCommand(Guid OperationId, Guid ProductId, string From, string To, decimal Quantity, string? Serial, string? Batch, DateOnly? Expiry, string Reason);
public sealed record StockCountCommand(Guid OperationId, Guid ProductId, string Location, decimal ExpectedQuantity, decimal CountedQuantity, decimal SurplusUnitCost, string Reason,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Batch = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] DateOnly? Expiry = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Serial = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? ApprovalId = null);
public interface IInventoryService
{
    Task<IReadOnlyList<LocationView>> Locations();
    Task<Guid> CreateLocation(LocationCommand input);
    Task<Guid> Transfer(TransferCommand input);
    Task<Guid> Count(StockCountCommand input);
    Task<IReadOnlyList<BatchPick>> SuggestBatches(BatchSuggestionCommand input);
}
