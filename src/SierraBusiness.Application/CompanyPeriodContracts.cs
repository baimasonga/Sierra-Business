namespace SierraBusiness.Application;
public sealed record CompanyPeriodPreview(DateOnly Cutoff, string HistoryHead, int Documents, int Movements, int JournalLines, decimal Debit, decimal Credit);
public sealed record SplitCompanyPeriodCommand(Guid OperationId, DateOnly Cutoff, string ArchiveName, string ContinuationName, string ExpectedHistoryHead);
public sealed record CompanyPeriodResult(Guid ArchiveId, Guid ContinuationId, string ArchiveName, string ContinuationName, DateOnly Cutoff, string BackupPath, string Sha256, CompanyPeriodPreview Verification);
public interface ICompanyPeriodService
{
    Task<CompanyPeriodPreview> Preview(DateOnly cutoff);
    Task<CompanyPeriodResult> Split(SplitCompanyPeriodCommand input);
}
