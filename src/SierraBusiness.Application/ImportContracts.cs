namespace SierraBusiness.Application;

/// <summary>Excel onboarding (specification Skill 10). Every row passes the same domain rules as manual entry.</summary>
public enum ImportType { Products, OpeningStock, SerialStock, BatchStock, PriceUpdate, StockCount, Parties, Ledgers, Vouchers, OpeningBalances }

public sealed record ImportField(string Key, string Label, bool Required, string Hint);
public sealed record ImportTemplate(ImportType Type, string Title, string Description, string Permission, IReadOnlyList<ImportField> Fields);
public sealed record SheetInfo(string Name, int Rows, IReadOnlyList<string> Headers);
public sealed record ImportValidateCommand(ImportType Type, string FileName, byte[] Content, string Sheet, IReadOnlyDictionary<string, string> Mapping, DateOnly? AsAt = null, string SessionName = "");
public sealed record ImportRowView(int Row, string Status, IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> Messages);
public sealed record ImportPreview(Guid BatchId, ImportType Type, int Total, int Valid, int Warnings, int Errors, IReadOnlyList<ImportRowView> Rows, string AlreadyImported);
public sealed record ImportCommitCommand(Guid OperationId, Guid BatchId);
public sealed record ExchangeImportCommand(ImportType Type, string FileName, byte[] Content);
public sealed record ImportCommitResult(Guid BatchId, int Rows, IReadOnlyList<Guid> Documents, bool Replayed, string Summary);
public sealed record ImportHistoryView(Guid Id, ImportType Type, string FileName, string Status, DateTimeOffset Created, string CreatedBy, int Rows, int Errors, DateTimeOffset? Committed, string Summary);

public interface IImportService
{
    Task<byte[]> ConvertExchange(ExchangeImportCommand input);
    /// <summary>Tally-compatible XML of the ledger masters and every posted voucher's ledger entries in the period, for external reporting tools.</summary>
    Task<byte[]> ExportTally(DateOnly from, DateOnly to);
    IReadOnlyList<ImportTemplate> Templates();
    Task<byte[]> Template(ImportType type);
    Task<IReadOnlyList<SheetInfo>> Inspect(string fileName, byte[] content);
    IReadOnlyDictionary<string, string> SuggestMapping(ImportType type, IReadOnlyList<string> headers);
    Task<ImportPreview> Validate(ImportValidateCommand input);
    Task<byte[]> ErrorWorkbook(Guid batchId);
    Task<ImportCommitResult> Commit(ImportCommitCommand input);
    Task<IReadOnlyList<ImportHistoryView>> History();
}

/// <summary>Hands a generated file to the user. The desktop host saves it to Documents; the browser host downloads it.</summary>
public interface IFileExport { Task<string> Save(string fileName, byte[] content); }
