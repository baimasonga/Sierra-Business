namespace SierraBusiness.Application;
public sealed record CompanySearchEntry(string Kind, string Key, string Name, DateOnly? Date, decimal? Amount);
public sealed record CompanySearchIndex(int Version, Guid SourceId, string Company, string Branch, DateTimeOffset Exported, IReadOnlyList<CompanySearchEntry> Entries);
public sealed record CompanySearchRequest(string Query, IReadOnlyList<CompanySearchIndex> Companies, int Limit = 100);
public sealed record CompanySearchHit(Guid SourceId, string Company, string Branch, DateTimeOffset IndexDate, CompanySearchEntry Entry);
