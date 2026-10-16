using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed partial class InventoryService
{
    private static DateOnly UtcToday => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<(decimal Quantity,decimal Book,List<(InventoryCost.Identity Key,decimal Quantity,decimal Value)> Identities)> StockOf(BusinessDb db,string branch,Guid product)
    {
        var rows = await db.Movements.AsNoTracking().Where(m => m.Branch == branch && m.ProductId == product).ToListAsync();
        var identities = rows.GroupBy(m => new InventoryCost.Identity(m.ProductId,m.Location,m.Serial,m.Batch,m.Expiry)).Select(g => (g.Key,g.Sum(m => m.Quantity),g.Sum(m => m.Value))).Where(x => x.Item2 != 0 || x.Item3 != 0).ToList();
        return (identities.Sum(i => i.Item2),identities.Sum(i => i.Item3),identities);
    }

    public async Task<StandardCostMigrationPreview> PreviewStandardMigration(Guid productId,decimal standardUnitCost)
    {
        var actor = await Authorize(); actor.Demand("accounting.read"); Rules.Positive(standardUnitCost,"Standard cost");
        await using var db = await factory.CreateDbContextAsync(); if (!await db.Products.AnyAsync(p => p.Id == productId)) throw new BusinessException("Product not found.");
        var policy = await InventoryCost.Policy(db,actor.Branch,productId); var stock = await StockOf(db,actor.Branch,productId);
        return new(productId,policy.Method,policy.Version,stock.Quantity,stock.Book,stock.Identities.Sum(i => AccountingEngine.Money(i.Quantity*standardUnitCost)));
    }

    public Task<Guid> MigrateToStandard(StandardCostMigrationCommand input) => Execute(input.OperationId,InventoryCost.StandardMigrationAction,input,async (db,actor) =>
    {
        actor.Demand("accounting.read"); actor.Demand("accounting.write"); Rules.Required(input.Reason,"Migration review reason",200); Rules.Positive(input.StandardUnitCost,"Standard cost");
        if (decimal.Round(input.StandardUnitCost,4) != input.StandardUnitCost || input.StandardUnitCost > 1_000_000_000) throw new BusinessException("Use a standard unit cost of at most one billion with at most four decimal places.");
        if (input.From != UtcToday) throw new BusinessException("Standard-cost migrations are prospective and dated today.");
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == input.ProductId) ?? throw new BusinessException("Product not found.");
        var current = await InventoryCost.Policy(db,actor.Branch,product.Id); if (current.Version != input.ExpectedVersion) throw new BusinessException("The costing policy changed. Refresh the migration preview.");
        if (current.Method == InventoryCostMethod.Standard && current.StandardUnitCost == input.StandardUnitCost) throw new BusinessException("The product already uses this standard cost.");
        if (await db.Movements.AnyAsync(m => m.Branch == actor.Branch && m.ProductId == product.Id && db.Documents.Any(d => d.Id == m.DocumentId && d.BusinessDate > input.From))) throw new BusinessException("Resolve future stock activity before migrating to standard cost.");
        if (Controls.Backdated(await Controls.Current(db,actor.Branch),input.From) is not null) throw new BusinessException("Today's date requires backdating approval under the current controls.");
        var stock = await StockOf(db,actor.Branch,product.Id);
        if (stock.Book != input.ExpectedBookValue) throw new BusinessException("Stock book value changed. Refresh the migration preview before posting.");
        if (stock.Identities.Any(i => i.Quantity < 0 || i.Value < 0)) throw new BusinessException("Stock history needs reconciliation before migrating to standard cost.");
        var changes = stock.Identities.Select(i => (i.Key,Change: AccountingEngine.Money(i.Quantity*input.StandardUnitCost)-i.Value)).Where(c => c.Change != 0).ToList();
        var difference = changes.Sum(c => c.Change);
        if (changes.Count == 0) return product.Id; // No stock, or already at the standard: only the policy changes.
        var account = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.DifferenceAccount);
        if (account is null || account.Control || account.CashOrBank || account.Group is not (LedgerGroup.Expense or LedgerGroup.Income)) throw new BusinessException("Choose a custom income or expense ledger for the standard-cost revaluation difference.");
        var doc = new BusinessDocument { Id = Guid.NewGuid(),Branch = actor.Branch,Number = $"{actor.Branch}-{options.Value.Device}-STD-{input.OperationId:N}",Kind = DocumentKind.StockCount,BusinessDate = input.From,Party = "Standard-cost migration",Total = difference,Settlement = "Reviewed standard-cost migration",Detail = JsonSerializer.Serialize(input) };
        db.Documents.Add(doc);
        foreach (var (key,change) in changes) db.Movements.Add(new Movement { Id = Guid.NewGuid(),DocumentId = doc.Id,Branch = actor.Branch,ProductId = key.Product,Location = key.Location,Serial = key.Serial,Batch = key.Batch,Expiry = key.Expiry,Quantity = 0,Value = change });
        var amount = Math.Abs(difference);
        if (amount != 0)
            db.Journal.AddRange(new JournalLine { Id = Guid.NewGuid(),Branch = actor.Branch,DocumentId = doc.Id,Account = difference > 0 ? Account.Inventory : (Account)account.Id,Debit = amount },
                new JournalLine { Id = Guid.NewGuid(),Branch = actor.Branch,DocumentId = doc.Id,Account = difference > 0 ? (Account)account.Id : Account.Inventory,Credit = amount });
        return doc.Id;
    });

    /// <summary>Allowance per product from active (unreversed) allowance reviews.</summary>
    private static async Task<Dictionary<Guid,decimal>> StandardAllowanceBalances(BusinessDb db,string branch)
    {
        var ids = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == InventoryCost.StandardAllowanceAction).Select(o => o.ResultId).ToListAsync();
        var docs = await db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id) && !db.Documents.Any(r => r.Reverses == d.Id)).Select(d => d.Detail).ToListAsync();
        var result = new Dictionary<Guid,decimal>();
        foreach (var detail in docs)
        {
            using var json = JsonDocument.Parse(detail);
            foreach (var change in json.RootElement.GetProperty("Changes").EnumerateObject()) result[Guid.Parse(change.Name)] = result.GetValueOrDefault(Guid.Parse(change.Name))+change.Value.GetDecimal();
        }
        return result;
    }
    private static async Task<int?> StandardAllowanceAccount(BusinessDb db,string branch)
    {
        var ids = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == InventoryCost.StandardAllowanceAction).Select(o => o.ResultId).ToListAsync();
        var detail = await db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id) && !db.Documents.Any(r => r.Reverses == d.Id)).OrderByDescending(d => d.BusinessDate).Select(d => d.Detail).FirstOrDefaultAsync();
        if (detail is null) return null; using var json = JsonDocument.Parse(detail); return json.RootElement.GetProperty("AllowanceAccount").GetInt32();
    }

    public async Task<IReadOnlyList<StandardAllowanceView>> StandardAllowances()
    {
        var actor = await Authorize(); actor.Demand("accounting.read"); await using var db = await factory.CreateDbContextAsync();
        var policies = await InventoryCost.PolicyMap(db,actor.Branch); var balances = await StandardAllowanceBalances(db,actor.Branch);
        var ids = policies.Values.Where(p => p.Method == InventoryCostMethod.Standard).Select(p => p.ProductId).Union(balances.Where(b => b.Value != 0).Select(b => b.Key)).ToArray();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var rows = new List<StandardAllowanceView>();
        foreach (var id in ids.Where(products.ContainsKey)) { var stock = await StockOf(db,actor.Branch,id); rows.Add(new(id,products[id].Sku,products[id].Name,stock.Quantity,stock.Book,balances.GetValueOrDefault(id))); }
        return rows.OrderBy(r => r.Sku).ToArray();
    }

    public Task<Guid> ReviewStandardAllowance(StandardAllowanceCommand input) => Execute(input.OperationId,InventoryCost.StandardAllowanceAction,input,async (db,actor) =>
    {
        actor.Demand("accounting.read"); actor.Demand("accounting.write"); Rules.Required(input.Reason,"Allowance review reason",200);
        if (Controls.Backdated(await Controls.Current(db,actor.Branch),UtcToday) is not null) throw new BusinessException("Today's date requires backdating approval under the current controls.");
        if (input.UnitValues is null || input.UnitValues.Count > 10000 || input.UnitValues.Any(p => p.Value < 0 || p.Value > 1_000_000_000 || decimal.Round(p.Value,4) != p.Value)) throw new BusinessException("Enter reviewed non-negative unit values with at most four decimal places.");
        var expense = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.ExpenseAccount); if (expense?.Group != LedgerGroup.Expense || expense.Control) throw new BusinessException("Choose a non-control expense ledger for the allowance.");
        var allowance = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.AllowanceAccount); if (allowance?.Group != LedgerGroup.Asset || allowance.Control || allowance.CashOrBank) throw new BusinessException("Choose a separate custom non-bank asset ledger as the inventory allowance (contra-asset).");
        if (await StandardAllowanceAccount(db,actor.Branch) is int used && used != allowance.Id) throw new BusinessException($"Existing allowances are held in ledger {used}. Keep using it, or release them first.");
        var policies = await InventoryCost.PolicyMap(db,actor.Branch); var balances = await StandardAllowanceBalances(db,actor.Branch);
        var standard = policies.Values.Where(p => p.Method == InventoryCostMethod.Standard).Select(p => p.ProductId).ToHashSet();
        var changes = new Dictionary<Guid,decimal>(); decimal standardValue = 0;
        foreach (var id in standard.Union(balances.Where(b => b.Value != 0).Select(b => b.Key)))
        {
            var stock = await StockOf(db,actor.Branch,id); var current = balances.GetValueOrDefault(id); decimal target = 0;
            if (standard.Contains(id)) standardValue += stock.Book;
            if (standard.Contains(id) && stock.Quantity > 0)
            {
                if (!input.UnitValues.TryGetValue(id,out var unit)) throw new BusinessException("Provide a reviewed unit value for every standard-cost product in stock.");
                target = Math.Max(0,stock.Book-AccountingEngine.Money(stock.Quantity*unit));
            }
            if (target != current) changes[id] = target-current;
        }
        if (standardValue != input.ExpectedStandardValue) throw new BusinessException("Standard-cost stock value changed. Refresh the allowance review before posting.");
        if (changes.Count == 0) throw new BusinessException("The reviewed values do not change any allowance.");
        decimal raised = changes.Values.Where(v => v > 0).Sum(), released = -changes.Values.Where(v => v < 0).Sum();
        if (raised > 1_000_000_000 || released > 1_000_000_000) throw new BusinessException("An allowance review may change at most one billion in each direction.");
        var doc = new BusinessDocument { Id = Guid.NewGuid(),Branch = actor.Branch,Number = $"{actor.Branch}-{options.Value.Device}-ALW-{input.OperationId:N}",Kind = DocumentKind.Journal,BusinessDate = UtcToday,Party = "Standard-cost allowance",Total = raised-released,Settlement = "Reviewed standard-cost allowance",
            Detail = JsonSerializer.Serialize(new { input.Reason,input.ExpenseAccount,input.AllowanceAccount,UnitValues = input.UnitValues,Changes = changes.ToDictionary(c => c.Key.ToString(),c => c.Value) }) };
        db.Documents.Add(doc);
        void Line(int account,decimal debit,decimal credit) { if (debit != 0 || credit != 0) db.Journal.Add(new JournalLine { Id = Guid.NewGuid(),Branch = actor.Branch,DocumentId = doc.Id,Account = (Account)account,Debit = debit,Credit = credit }); }
        Line(expense.Id,raised,0); Line(allowance.Id,0,raised); Line(allowance.Id,released,0); Line(expense.Id,0,released);
        return doc.Id;
    });
}
