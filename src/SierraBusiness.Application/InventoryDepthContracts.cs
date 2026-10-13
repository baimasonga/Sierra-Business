namespace SierraBusiness.Application;
public enum InventoryCostMethod { WeightedAverage, Fifo, Standard }
public sealed record InventoryCostCommand(Guid OperationId, Guid ProductId, int ExpectedVersion, InventoryCostMethod Method, decimal StandardUnitCost, DateOnly From, string Reason);
public sealed record InventoryCostPolicy(Guid ProductId, int Version, InventoryCostMethod Method, decimal StandardUnitCost, DateOnly From);
public enum InventoryValuationMethod { Book, Fifo, Lifo, Standard, Market }
public sealed record InventoryValuationRequest(DateOnly AsAt, InventoryValuationMethod Method, IReadOnlyDictionary<Guid,decimal>? ReferencePrices = null);
public sealed record InventoryWriteDownCommand(Guid OperationId, decimal ExpectedBookValue, IReadOnlyDictionary<Guid,decimal> UnitValues, int ExpenseAccount, string Reason);
public sealed record InventoryValuationRow(Guid ProductId,string Sku,string Name,string Unit,string Location,string? Serial,string? Batch,DateOnly? Expiry,decimal Quantity,decimal BookValue,decimal SelectedValue)
{
    public decimal Difference => SelectedValue-BookValue;
}
public sealed record InventoryValuationReport(InventoryValuationRequest Request,IReadOnlyList<InventoryValuationRow> Rows)
{
    public decimal BookValue => Rows.Sum(r => r.BookValue);
    public decimal SelectedValue => Rows.Sum(r => r.SelectedValue);
}
