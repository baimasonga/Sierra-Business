namespace SierraBusiness.Application;
/// <param name="LaterDocuments">Documents dated after the cutoff. Zero unless later activity was explicitly allowed for a retrospective split.</param>
/// <param name="Synced">The company is a synced branch; the split is the coordinated branch migration.</param>
public sealed record CompanyPeriodPreview(DateOnly Cutoff, string HistoryHead, int Documents, int Movements, int JournalLines, decimal Debit, decimal Credit, int LaterDocuments = 0, bool Synced = false);
public sealed record SplitCompanyPeriodCommand(Guid OperationId, DateOnly Cutoff, string ArchiveName, string ContinuationName, string ExpectedHistoryHead,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool AllowLaterActivity = false);
public sealed record CompanyPeriodResult(Guid ArchiveId, Guid ContinuationId, string ArchiveName, string ContinuationName, DateOnly Cutoff, string BackupPath, string Sha256, CompanyPeriodPreview Verification);
/// <summary>Verified physical compaction: backup, SQLite VACUUM, then the integrity check, protected-history head and record counts must match.</summary>
public sealed record CompanyCompactionResult(long BytesBefore, long BytesAfter, string HistoryHead, int Documents, int JournalLines, string BackupPath);
public interface ICompanyPeriodService
{
    Task<CompanyPeriodPreview> Preview(DateOnly cutoff, bool allowLaterActivity = false);
    Task<CompanyPeriodResult> Split(SplitCompanyPeriodCommand input);
    Task<CompanyCompactionResult> Compact();
}
