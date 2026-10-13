using System.Globalization;
using Microsoft.VisualBasic.FileIO;
using SierraBusiness.Domain;
namespace SierraBusiness.Application;
public sealed record StatementLineInput(DateOnly Date, string Reference, decimal Amount);
public sealed record ImportStatementCommand(Guid OperationId, int AccountCode, string Reference, decimal OpeningBalance, decimal ClosingBalance, string Csv, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StatementFormat? Format = null);
public sealed record VoidStatementCommand(Guid OperationId, Guid StatementId, string Reason);
public sealed record StatementView(Guid Id, string Reference, int AccountCode, decimal Opening, decimal Closing, bool Voided);
public sealed record StatementCandidate(Guid JournalLineId, string Number, DateOnly Date, string Narration);
public sealed record StatementRowView(Guid Id, Guid StatementId, int Line, DateOnly Date, string Reference, decimal Amount, Guid? MatchId, IReadOnlyList<StatementCandidate> Candidates);
public enum ChequeState { Received, Deposited, Cleared, Bounced }
public enum ChequeAction { Deposit, Clear, Bounce, Undo }
public sealed record ReceiveChequeCommand(Guid OperationId, Guid InvoiceId, decimal Amount, string Reference, string Bank, DateOnly ChequeDate);
public sealed record RegisterChequeCommand(Guid OperationId, Guid SourceDocumentId);
public sealed record ChequeActionCommand(Guid OperationId, Guid ChequeId, int ExpectedVersion, ChequeAction Action, DateOnly Date, int BankAccount, string Reason);
public sealed record ChequeEventView(int Version, ChequeAction Action, ChequeState State, DateOnly Date, string Reason, Guid DocumentId);
public sealed record ChequeView(Guid Id, string Party, string Reference, string Bank, DateOnly ChequeDate, decimal Amount, ChequeState State, int Version, IReadOnlyList<ChequeEventView> History, Guid SourceDocumentId);
public sealed record BankingSnapshot(IReadOnlyList<StatementView> Statements, IReadOnlyList<StatementRowView> Rows, IReadOnlyList<ChequeView> Cheques, IReadOnlyList<CreditView> SupplierCredits);
public sealed record OpenTillCommand(Guid OperationId, int AccountCode, decimal OpeningCount);
public sealed record CloseTillCommand(Guid OperationId, Guid ShiftId, decimal ExpectedMovement, decimal Counted, string Note);
public sealed record TillMovementView(string Number, string Actor, decimal Received, decimal Paid);
public sealed record TillView(Guid Id, string Cashier, int AccountCode, DateTimeOffset Opened, decimal OpeningCount, decimal OpeningBook, decimal Movement, decimal Expected, DateTimeOffset? Closed, decimal? Counted, decimal? Variance, string Note, IReadOnlyList<TillMovementView> Movements);
public enum TradeAdvanceKind { CustomerDeposit, SupplierAdvance }
public sealed record AdvanceCheque(string Number,string Bank,DateOnly Date,Guid? BookId = null);
public sealed record TradeAdvanceCommand(Guid OperationId, TradeAdvanceKind Kind, Guid PartyId, int FundsAccount, decimal Amount, string Reference,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] AdvanceCheque? Cheque = null);
public sealed record TradeAdvanceView(Guid CreditId, Guid DocumentId, TradeAdvanceKind Kind, string Number, string Party, DateOnly Date, decimal Amount, decimal Remaining,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ChequeState? ChequeStatus = null);
public interface IBankingService
{
    Task<Guid> PostForeignAdvance(ForeignAdvanceCommand input);
    Task<IReadOnlyList<ForeignAdvanceView>> ForeignAdvances();
    Task<ForeignAdvanceUsePreview> PreviewForeignAdvanceUse(ForeignAdvanceUseCommand input);
    Task<Guid> UseForeignAdvance(ForeignAdvanceUseCommand input);
    Task<BankingSnapshot> Read();
    Task<Guid> PostAdvance(TradeAdvanceCommand input);
    Task<IReadOnlyList<TradeAdvanceView>> Advances();
    Task<Guid> Import(ImportStatementCommand input);
    Task<Guid> VoidStatement(VoidStatementCommand input);
    Task<Guid> RegisterCheque(RegisterChequeCommand input);
    Task<Guid> ReceiveCheque(ReceiveChequeCommand input);
    Task<Guid> ActOnCheque(ChequeActionCommand input);
    Task<Guid> UseSupplierCredit(UseCreditCommand input);
    Task<IReadOnlyList<TillView>> Tills();
    Task<Guid> OpenTill(OpenTillCommand input);
    Task<Guid> CloseTill(CloseTillCommand input);
}
public static class BankStatementCsv
{
    public static IReadOnlyList<StatementLineInput> Parse(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv) || csv.Length > 1_000_000) throw new BusinessException("Provide a CSV up to 1 MB with Date,Reference,Amount columns.");
        using var parser = new TextFieldParser(new StringReader(csv.TrimStart('\uFEFF'))) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(","); var rows = new List<StatementLineInput>();
        try
        {
            var header = parser.ReadFields();
            if (header is null || !header.SequenceEqual(new[] { "Date", "Reference", "Amount" }, StringComparer.OrdinalIgnoreCase)) throw new BusinessException("CSV header must be Date,Reference,Amount in that order.");
            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields();
                if (fields is null || fields.Length != 3 || !DateOnly.TryParseExact(fields[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || !decimal.TryParse(fields[2], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount == 0 || Math.Abs(amount) > 1_000_000_000 || AccountingEngine.Money(amount) != amount) throw new BusinessException($"Invalid statement row {rows.Count + 2}. Use yyyy-MM-dd and signed amounts with at most two decimals (money in positive, money out negative).");
                rows.Add(new(date, Rules.Required(fields[1], "Statement reference", 160), amount));
                if (rows.Count > 5000) throw new BusinessException("Import at most 5000 statement rows at a time.");
            }
        }
        catch (MalformedLineException) { throw new BusinessException("Malformed quoted CSV row. Check the file before importing."); }
        if (rows.Count == 0) throw new BusinessException("The statement has no transactions.");
        return rows;
    }
}

