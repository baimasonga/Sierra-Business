using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed partial class StatementsService
{
    public async Task<CompanySearchIndex> ExportSearchIndex(string company)
    {
        var actor = await Authorize(); actor.Demand("users.manage"); actor.Demand("business.read"); company = Rules.Required(company,"Company name",120);
        await using var db = await factory.CreateDbContextAsync(); await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var origin = await db.IntegritySeals.AsNoTracking().OrderBy(s => s.Sequence).Select(s => s.Hash).FirstOrDefaultAsync() ?? throw new BusinessException("Initialize protected history before exporting.");
        var source = new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(origin + "\n" + actor.Branch)).AsSpan(0,16));
        var docs = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch).OrderByDescending(d => d.BusinessDate).Take(100001).ToListAsync();
        var products = await db.Products.AsNoTracking().Take(100001).ToListAsync(); var parties = await db.Parties.AsNoTracking().Where(p => p.Branch == actor.Branch).Take(100001).ToListAsync(); var accounts = await db.LedgerAccounts.AsNoTracking().Take(100001).ToListAsync();
        if (docs.Count + products.Count + parties.Count + accounts.Count > 100000) throw new BusinessException("The portable search index supports at most 100,000 entries; use the live company reports for larger histories.");
        var entries = docs.Select(d => new CompanySearchEntry("Document",d.Number,d.Party,d.BusinessDate,d.Total)).Concat(products.Select(p => new CompanySearchEntry("Product",p.Sku,p.Name,null,null))).Concat(parties.Select(p => new CompanySearchEntry("Party",p.Code,p.Name,null,null))).Concat(accounts.Select(a => new CompanySearchEntry("Ledger",a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),a.Name,null,null))).ToArray();
        await transaction.CommitAsync(); return new(1,source,company,actor.Branch,DateTimeOffset.UtcNow,entries);
    }
    public async Task<IReadOnlyList<CompanySearchHit>> SearchCompanies(CompanySearchRequest input)
    {
        var actor = await Authorize(); actor.Demand("users.manage"); actor.Demand("business.read");
        if (input is null || input.Companies is null || input.Companies.Count is < 1 or > 20 || input.Companies.Any(c => c is null) || input.Limit is < 1 or > 500) throw new BusinessException("Search one to twenty reviewed indexes, returning at most 500 results.");
        var query = Rules.Required(input.Query,"Search text",100); if (query.Length < 2) throw new BusinessException("Enter at least two search characters.");
        if (input.Companies.Select(c => c.SourceId).Distinct().Count() != input.Companies.Count) throw new BusinessException("Load each company lineage only once; replace older indexes explicitly.");
        foreach (var company in input.Companies)
        {
            Rules.Required(company.Company,"Company name",120); Rules.Required(company.Branch,"Branch",120);
            if (company.Version != 1 || company.SourceId == Guid.Empty || company.Exported > DateTimeOffset.UtcNow.AddMinutes(5) || company.Entries is null || company.Entries.Count > 100000 || company.Entries.Any(e => e is null || e.Kind is not ("Document" or "Product" or "Party" or "Ledger") || string.IsNullOrWhiteSpace(e.Key) || e.Key.Length > 200 || e.Name is null || e.Name.Length > 200 || e.Amount is < -1_000_000_000_000m or > 1_000_000_000_000m)) throw new BusinessException("Use a valid Version 1 search index exported from an authorized company session.");
        }
        return input.Companies.SelectMany(c => c.Entries.Where(e => (e.Key + " " + e.Name).Contains(query,StringComparison.OrdinalIgnoreCase)).Select(e => new CompanySearchHit(c.SourceId,c.Company,c.Branch,c.Exported,e))).OrderBy(h => h.Company).ThenByDescending(h => h.Entry.Date).ThenBy(h => h.Entry.Key).Take(input.Limit).ToArray();
    }
}
