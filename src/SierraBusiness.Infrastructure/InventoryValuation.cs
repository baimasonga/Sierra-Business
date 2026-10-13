using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed partial class InventoryService
{
    public async Task<IReadOnlyList<InventoryCostPolicy>> CostPolicies() { var actor = await Authorize(); actor.Demand("accounting.read"); await using var db = await factory.CreateDbContextAsync(); return (await InventoryCost.PolicyMap(db,actor.Branch)).Values.ToArray(); }
    public Task<Guid> SetCostPolicy(InventoryCostCommand input) => Execute(input.OperationId,"inventory.cost-method",input,async (db,actor) =>
    {
        actor.Demand("accounting.write"); Rules.Required(input.Reason,"Costing policy reason",160);
        if (!Enum.IsDefined(input.Method) || input.From.Year < 2000 || input.From > DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("Choose a valid costing method and policy date.");
        if (input.Method == InventoryCostMethod.Standard) Rules.Positive(input.StandardUnitCost,"Standard cost"); else if (input.StandardUnitCost != 0) throw new BusinessException("Only standard costing uses a standard unit cost.");
        if (!await db.Products.AnyAsync(p => p.Id == input.ProductId)) throw new BusinessException("Product not found.");
        var current = await InventoryCost.Policy(db,actor.Branch,input.ProductId); if (current.Version != input.ExpectedVersion) throw new BusinessException("The costing policy changed. Refresh before saving.");
        if (await db.Movements.AnyAsync(m => m.Branch == actor.Branch && m.ProductId == input.ProductId))
        {
            if (input.From != DateOnly.FromDateTime(DateTime.UtcNow) || input.Method == InventoryCostMethod.Standard || current.Method == InventoryCostMethod.Standard) throw new BusinessException("Existing-stock changes are prospective weighted-average/FIFO transitions dated today. Standard-cost transitions require a separately reviewed stock migration.");
            if (input.Method == current.Method) throw new BusinessException("The product already uses this method.");
            if (await db.Movements.AnyAsync(m => m.Branch == actor.Branch && m.ProductId == input.ProductId && db.Documents.Any(d => d.Id == m.DocumentId && d.BusinessDate > input.From))) throw new BusinessException("Resolve future stock activity before changing the method.");
        }
        return input.ProductId;
    });
    public async Task<InventoryValuationReport> Valuation(InventoryValuationRequest input)
    {
        var actor = await Authorize(); actor.Demand("accounting.read");
        if (!Enum.IsDefined(input.Method) || input.AsAt.Year < 2000 || input.AsAt > DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("Choose a past or current stock valuation date and method.");
        await using var db = await factory.CreateDbContextAsync();
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id);
        var movements = await db.Movements.AsNoTracking().Where(m => m.Branch == actor.Branch && db.Documents.Any(d => d.Id == m.DocumentId && d.BusinessDate <= input.AsAt)).ToListAsync();
        Dictionary<InventoryCost.Identity,decimal>? alternatives = input.Method is InventoryValuationMethod.Fifo or InventoryValuationMethod.Lifo ? await InventoryCost.Values(db,actor.Branch,input.AsAt,input.Method == InventoryValuationMethod.Lifo) : null;
        var rows = new List<InventoryValuationRow>();
        foreach (var group in movements.GroupBy(m => new InventoryCost.Identity(m.ProductId,m.Location,m.Serial,m.Batch,m.Expiry)))
        {
            var key = group.Key; var quantity = group.Sum(m => m.Quantity); var book = group.Sum(m => m.Value); if (quantity == 0 && book == 0) continue; var p = products[key.Product]; var selected = book;
            if (alternatives is not null) selected = alternatives.GetValueOrDefault(key);
            if (input.Method is InventoryValuationMethod.Standard or InventoryValuationMethod.Market)
            {
                if (input.ReferencePrices is null || !input.ReferencePrices.TryGetValue(key.Product,out var reference)) throw new BusinessException($"Enter the reviewed reference unit price for {p.Sku}.");
                Rules.Positive(reference,"Reference unit price"); selected = AccountingEngine.Money(quantity*reference);
            }
            rows.Add(new(key.Product,p.Sku,p.Name,p.Unit,key.Location,key.Serial,key.Batch,key.Expiry,quantity,book,selected));
        }
        return new(input,rows.OrderBy(r => r.Sku).ThenBy(r => r.Location).ToArray());
    }
    public async Task<byte[]> ExportValuation(InventoryValuationRequest input)
    {
        var report = await Valuation(input);
        string M(decimal value) => value.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);
        return Imports.WorkbookWriter.Write([("Valuation",[[input.Method.ToString(),input.AsAt.ToString("yyyy-MM-dd"),"NLe; alternative valuations do not change posted books"],["SKU","Product","Location","Serial","Batch","Expiry","Quantity","Unit","Book value","Selected value","Difference"],..report.Rows.Select(r => (IReadOnlyList<string>)[r.Sku,r.Name,r.Location,r.Serial ?? "",r.Batch ?? "",r.Expiry?.ToString("yyyy-MM-dd") ?? "",r.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),r.Unit,M(r.BookValue),M(r.SelectedValue),M(r.Difference)]),["Total","","","","","","","",M(report.BookValue),M(report.SelectedValue),M(report.SelectedValue-report.BookValue)]])]);
    }
}
