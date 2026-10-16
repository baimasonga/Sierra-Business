namespace SierraBusiness.Application;
public sealed record CompanySearchEntry(string Kind, string Key, string Name, DateOnly? Date, decimal? Amount);
public sealed record CompanySearchIndex(int Version, Guid SourceId, string Company, string Branch, DateTimeOffset Exported, IReadOnlyList<CompanySearchEntry> Entries);
public sealed record CompanySearchRequest(string Query, IReadOnlyList<CompanySearchIndex> Companies, int Limit = 100);
public sealed record CompanySearchHit(Guid SourceId, string Company, string Branch, DateTimeOffset IndexDate, CompanySearchEntry Entry);
/// <summary>Searches the company databases on this computer directly. A company is searched only when the signed-in user has an owner
/// account there (same user name, with user-administration and business-read permissions); others are listed as skipped.</summary>
public sealed record LiveCompanySearchResult(IReadOnlyList<CompanySearchHit> Hits, IReadOnlyList<string> Searched, IReadOnlyList<string> Skipped);
public interface ILiveCompanySearch { Task<LiveCompanySearchResult> Search(string query, int limit = 200); }
