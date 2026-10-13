using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed partial class InventoryService
{
    public Task<Guid> WriteDown(InventoryWriteDownCommand input) => Execute(input.OperationId,"inventory.write-down",input,async (db,actor) =>
    {
        actor.Demand("accounting.read"); actor.Demand("accounting.write"); Rules.Required(input.Reason,"Write-down review reason",200);
        var today = DateOnly.FromDateTime(DateTime.UtcNow); if (today <= await PeriodControls.ClosedThrough(db,actor.Branch,options.Value.ClosedThrough)) throw new BusinessException("The current date is locked.");
        if (input.UnitValues is null || input.UnitValues.Count is < 1 or > 10000 || input.UnitValues.Any(p => p.Value < 0 || p.Value > 1_000_000_000 || decimal.Round(p.Value,4) != p.Value)) throw new BusinessException("Enter reviewed non-negative unit values with at most four decimal places.");
        var expense = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.ExpenseAccount); if (expense?.Group != LedgerGroup.Expense || expense.Control) throw new BusinessException("Choose a non-control expense ledger for the write-down.");
        var movements = await db.Movements.AsNoTracking().Where(m => m.Branch == actor.Branch).ToListAsync();
        if (movements.Sum(m => m.Value) != input.ExpectedBookValue) throw new BusinessException("Stock book value changed. Refresh the valuation before posting a write-down.");
        var changes = new List<Movement>(); decimal reduction = 0;
        foreach (var group in movements.GroupBy(m => new InventoryCost.Identity(m.ProductId,m.Location,m.Serial,m.Batch,m.Expiry)))
        {
            var quantity = group.Sum(m => m.Quantity); var book = group.Sum(m => m.Value); if (quantity == 0 && book == 0) continue;
            if (quantity <= 0 || book < 0) throw new BusinessException("Stock history needs reconciliation before a write-down.");
            if (!input.UnitValues.TryGetValue(group.Key.Product,out var unit)) throw new BusinessException("Provide a reviewed unit value for every stock product.");
            var target = Math.Min(book,AccountingEngine.Money(quantity*unit)); if (target == book) continue;
            if ((await InventoryCost.Policy(db,actor.Branch,group.Key.Product)).Method == InventoryCostMethod.Standard) throw new BusinessException("Standard-cost impairment needs a separate allowance policy; this workflow cannot change configured standard values.");
            await InventoryCost.GuardDate(db,actor.Branch,group.Key.Product,today);
            var amount = book-target; reduction += amount; changes.Add(new Movement { Id = Guid.NewGuid(),Branch = actor.Branch,ProductId = group.Key.Product,Location = group.Key.Location,Serial = group.Key.Serial,Batch = group.Key.Batch,Expiry = group.Key.Expiry,Quantity = 0,Value = -amount });
        }
        if (reduction <= 0 || reduction > 1_000_000_000) throw new BusinessException("The reviewed values must produce a write-down of at most one billion; upward revaluation is not permitted.");
        var doc = new BusinessDocument { Id = Guid.NewGuid(),Branch = actor.Branch,Number = $"{actor.Branch}-{options.Value.Device}-WD-{input.OperationId:N}",Kind = DocumentKind.StockCount,BusinessDate = today,Party = "Inventory write-down",Total = reduction,Settlement = "Reviewed impairment",Detail = JsonSerializer.Serialize(input) };
        db.Documents.Add(doc); foreach (var change in changes) { change.DocumentId = doc.Id; db.Movements.Add(change); }
        db.Journal.AddRange(new JournalLine { Id = Guid.NewGuid(),Branch = actor.Branch,DocumentId = doc.Id,Account = (Account)input.ExpenseAccount,Debit = reduction },new JournalLine { Id = Guid.NewGuid(),Branch = actor.Branch,DocumentId = doc.Id,Account = Account.Inventory,Credit = reduction });
        return doc.Id;
    });
}
