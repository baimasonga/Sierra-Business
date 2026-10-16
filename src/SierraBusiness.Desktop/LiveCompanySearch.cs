using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

public sealed class LiveCompanySearch(CompanyDirectory companies, IDbContextFactory<BusinessDb> factory, IActorSession session) : ILiveCompanySearch
{
    public async Task<LiveCompanySearchResult> Search(string query, int limit = 200)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read"); actor.Demand("audit.read"); // read-only: also available from an archive
        query = Rules.Required(query, "Search text", 100); if (query.Length < 2) throw new BusinessException("Enter at least two search characters.");
        if (limit is < 1 or > 500) throw new BusinessException("Return between 1 and 500 results.");
        string userName;
        await using (var current = await factory.CreateDbContextAsync()) userName = await current.Users.AsNoTracking().Where(u => u.Id == actor.Id).Select(u => u.NormalizedUserName).SingleOrDefaultAsync() ?? throw new UnauthorizedAccessException("Sign in again.");
        var pattern = "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var hits = new List<CompanySearchHit>(); var searched = new List<string>(); var skipped = new List<string>();
        foreach (var company in companies.List())
        {
            var path = Path.Combine(company.DataDirectory, "sierra.db"); if (!File.Exists(path)) continue;
            var options = new DbContextOptionsBuilder<BusinessDb>().UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString()).Options;
            try
            {
                await using var db = new BusinessDb(options);
                var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedUserName == userName);
                var claims = user is null ? [] : await db.UserClaims.AsNoTracking().Where(c => c.UserId == user.Id).ToListAsync();
                var branch = claims.FirstOrDefault(c => c.ClaimType == "branch")?.ClaimValue ?? "";
                if (user is null || branch.Length == 0 || !new[] { "users.manage", "business.read" }.All(p => claims.Any(c => c.ClaimType == "permission" && c.ClaimValue == p))) { skipped.Add(company.Name + " (no owner account for you)"); continue; }
                var origin = await db.IntegritySeals.AsNoTracking().OrderBy(s => s.Sequence).Select(s => s.Hash).FirstOrDefaultAsync() ?? "";
                var source = new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(origin + "\n" + branch)).AsSpan(0, 16));
                var found = new List<CompanySearchEntry>();
                found.AddRange((await db.Documents.AsNoTracking().Where(d => d.Branch == branch && (EF.Functions.Like(d.Number, pattern, "\\") || EF.Functions.Like(d.Party, pattern, "\\"))).OrderByDescending(d => d.BusinessDate).Take(limit).ToListAsync()).Select(d => new CompanySearchEntry("Document", d.Number, d.Party, d.BusinessDate, d.Total)));
                found.AddRange((await db.Products.AsNoTracking().Where(p => EF.Functions.Like(p.Sku, pattern, "\\") || EF.Functions.Like(p.Name, pattern, "\\")).Take(limit).ToListAsync()).Select(p => new CompanySearchEntry("Product", p.Sku, p.Name, null, null)));
                found.AddRange((await db.Parties.AsNoTracking().Where(p => p.Branch == branch && (EF.Functions.Like(p.Code, pattern, "\\") || EF.Functions.Like(p.Name, pattern, "\\"))).Take(limit).ToListAsync()).Select(p => new CompanySearchEntry("Party", p.Code, p.Name, null, null)));
                found.AddRange((await db.LedgerAccounts.AsNoTracking().Where(a => EF.Functions.Like(a.Name, pattern, "\\")).Take(limit).ToListAsync()).Select(a => new CompanySearchEntry("Ledger", a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), a.Name, null, null)));
                var now = DateTimeOffset.UtcNow; hits.AddRange(found.Select(e => new CompanySearchHit(source, company.Name, branch, now, e))); searched.Add(company.Name);
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException) { skipped.Add(company.Name + " (could not be opened)"); }
        }
        return new(hits.OrderBy(h => h.Company).ThenByDescending(h => h.Entry.Date).ThenBy(h => h.Entry.Key).Take(limit).ToArray(), searched, skipped);
    }
}
