namespace SierraBusiness.Application;
public enum InventoryCostMethod { WeightedAverage, Fifo, Standard }
public sealed record InventoryCostCommand(Guid OperationId, Guid ProductId, int ExpectedVersion, InventoryCostMethod Method, decimal StandardUnitCost, DateOnly From, string Reason);
public sealed record InventoryCostPolicy(Guid ProductId, int Version, InventoryCostMethod Method, decimal StandardUnitCost, DateOnly From);
public enum InventoryValuationMethod { Book, Fifo, Lifo, Standard, Market }
public sealed record InventoryValuationRequest(DateOnly AsAt, InventoryValuationMethod Method, IReadOnlyDictionary<Guid,decimal>? ReferencePrices = null);
/// <summary>Moves existing stock to standard cost, or revises an existing standard, today: every stock identity is revalued to quantity × standard and the difference goes to a reviewed ledger.</summary>
public sealed record StandardCostMigrationCommand(Guid OperationId, Guid ProductId, int ExpectedVersion, decimal StandardUnitCost, DateOnly From, decimal ExpectedBookValue, int DifferenceAccount, string Reason);
public sealed record StandardCostMigrationPreview(Guid ProductId, InventoryCostMethod CurrentMethod, int Version, decimal Quantity, decimal BookValue, decimal StandardValue)
{
    public decimal Difference => StandardValue-BookValue;
}
/// <summary>Impairment of standard-cost stock through a contra-asset allowance; standard carrying values stay unchanged. Raising and releasing both follow the reviewed unit values.</summary>
public sealed record StandardAllowanceCommand(Guid OperationId, decimal ExpectedStandardValue, IReadOnlyDictionary<Guid,decimal> UnitValues, int ExpenseAccount, int AllowanceAccount, string Reason);
public sealed record StandardAllowanceView(Guid ProductId, string Sku, string Name, decimal Quantity, decimal StandardValue, decimal Allowance)
{
    public decimal NetValue => StandardValue-Allowance;
}
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
