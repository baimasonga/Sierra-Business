using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed partial class FinanceService
{
    private static async Task<IReadOnlyList<CostCategoryView>> CategoriesFor(BusinessDb db, string branch)
    {
        var events = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == "accounts.category").ToListAsync();
        return events.Select(o => (o.ResultId, C: JsonSerializer.Deserialize<CostCategoryCommand>(o.Payload)!)).GroupBy(x => x.ResultId).Select(g => g.MaxBy(x => x.C.ExpectedVersion)).Select(x => new CostCategoryView(x.ResultId, x.C.ExpectedVersion + 1, x.C.Name.Trim(), x.C.Active)).OrderBy(c => c.Name).ToArray();
    }
    public async Task<IReadOnlyList<CostCategoryView>> CostCategories() { var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync(); return await CategoriesFor(db, actor.Branch); }
    public Task<Guid> SaveCostCategory(CostCategoryCommand input) => Execute(input.OperationId, "accounts.category", input, async (db, actor) =>
    {
        var name = Rules.Required(input.Name, "Cost category name", 100); var current = await CategoriesFor(db, actor.Branch); var old = input.Id is Guid id ? current.SingleOrDefault(c => c.Id == id) ?? throw new BusinessException("Cost category not found.") : null;
        if ((old?.Version ?? 0) != input.ExpectedVersion) throw new BusinessException("Cost category changed. Refresh before saving.");
        if (current.Any(c => c.Id != input.Id && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new BusinessException("Cost category name already exists.");
        return old?.Id ?? Guid.NewGuid();
    });
    public Task<Guid> AllocateCategory(CategoryAllocationCommand input) => Execute(input.OperationId, "accounts.category.allocate", input, async (db, actor) =>
    {
        var category = (await CategoriesFor(db, actor.Branch)).SingleOrDefault(c => c.Id == input.CategoryId) ?? throw new BusinessException("Cost category not found.");
        if (!category.Active || category.Version != input.ExpectedCategoryVersion) throw new BusinessException("Cost category changed or is inactive. Review again.");
        CheckShares(input.Shares, await StructureFor(db, actor.Branch));
        var doc = await db.Documents.SingleOrDefaultAsync(d => d.Id == input.DocumentId && d.Branch == actor.Branch && d.Reverses == null && !db.Documents.Any(r => r.Reverses == d.Id)) ?? throw new BusinessException("Choose an active source document.");
        await OpenDate(db, actor.Branch, doc.BusinessDate);
        if (!await db.Journal.AnyAsync(j => j.DocumentId == doc.Id && db.LedgerAccounts.Any(a => a.Id == (int)j.Account && (a.Group == LedgerGroup.Income || a.Group == LedgerGroup.Expense)))) throw new BusinessException("The document has no revenue or expense to allocate.");
        var previous = await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == "accounts.category.allocate").ToListAsync();
        if (previous.Select(o => JsonSerializer.Deserialize<CategoryAllocationCommand>(o.Payload)!).Any(c => c.CategoryId == input.CategoryId && c.DocumentId == input.DocumentId)) throw new BusinessException("This document already has an allocation in this category. Category allocations are immutable.");
        return Guid.NewGuid();
    });
    public async Task<CategoryReport> CategoryAnalysis(DateOnly from, DateOnly to)
    {
        var actor = await Authorize(); var finance = await Read(from, to); await using var db = await factory.CreateDbContextAsync(); var categories = await CategoriesFor(db, actor.Branch); var structure = await StructureFor(db, actor.Branch);
        var commands = (await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == "accounts.category.allocate").ToListAsync()).Select(o => JsonSerializer.Deserialize<CategoryAllocationCommand>(o.Payload)!).ToArray();
        var entries = finance.Entries.Where(e => finance.Accounts.Single(a => a.Code == e.AccountCode).Group is LedgerGroup.Income or LedgerGroup.Expense).ToArray();
        var reverseSources = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && d.BusinessDate >= from && d.BusinessDate <= to && d.Reverses != null).ToDictionaryAsync(d => d.Id, d => d.Reverses!.Value);
        var rows = new List<CategoryBalance>();
        foreach (var category in categories)
        {
            foreach (var document in entries.GroupBy(e => e.DocumentId))
            {
                var source = reverseSources.GetValueOrDefault(document.Key, document.Key);
                var allocation = commands.SingleOrDefault(c => c.CategoryId == category.Id && c.DocumentId == source);
                var revenue = document.Where(e => finance.Accounts.Single(a => a.Code == e.AccountCode).Group == LedgerGroup.Income).Sum(e => e.Credit - e.Debit);
                var expense = document.Where(e => finance.Accounts.Single(a => a.Code == e.AccountCode).Group == LedgerGroup.Expense).Sum(e => e.Debit - e.Credit);
                if (allocation is null) { rows.Add(new(category.Id, category.Name, "Unallocated", revenue, expense)); continue; }
                decimal usedRevenue = 0, usedExpense = 0;
                for (var i = 0; i < allocation.Shares.Count; i++)
                {
                    var share = allocation.Shares[i]; var r = i == allocation.Shares.Count - 1 ? revenue - usedRevenue : AccountingEngine.Money(revenue * share.Percent / 100); var e = i == allocation.Shares.Count - 1 ? expense - usedExpense : AccountingEngine.Money(expense * share.Percent / 100); usedRevenue += r; usedExpense += e;
                    rows.Add(new(category.Id, category.Name, structure.Centres.Single(c => c.Id == share.CentreId).Code, r, e));
                }
            }
        }
        return new(from, to, rows.GroupBy(r => (r.CategoryId, r.Category, r.Centre)).Select(g => new CategoryBalance(g.Key.CategoryId, g.Key.Category, g.Key.Centre, g.Sum(r => r.Revenue), g.Sum(r => r.Expenses))).ToArray());
    }
}
