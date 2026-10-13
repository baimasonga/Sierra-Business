using System.Globalization;
using SierraBusiness.Domain;
namespace SierraBusiness.Application;

/// <summary>0.24.0: how a bank's statement CSV is laid out. Columns are numbered from 1; 0 means not used. Several date formats can be separated by "|".</summary>
public sealed record StatementFormat(string Name, string Delimiter = ",", int HeaderRows = 1, int DateColumn = 1, string DateFormat = "yyyy-MM-dd", int ReferenceColumn = 2, int AmountColumn = 3, int DebitColumn = 0, int CreditColumn = 0)
{
    public static IReadOnlyList<StatementFormat> BuiltIn { get; } = [
        new("Rokela Business Admin standard (Date, Reference, Amount)"),
        new("Debit and credit columns (Date, Details, Debit, Credit, Balance)", ",", 1, 1, "dd/MM/yyyy|d/M/yyyy|dd-MMM-yyyy|yyyy-MM-dd", 2, 0, 3, 4),
        new("Signed amount (Date, Details, Amount)", ",", 1, 1, "dd/MM/yyyy|d/M/yyyy|dd-MMM-yyyy|yyyy-MM-dd", 2, 3),
        new("Value date, details, reference, debit, credit", ",", 1, 1, "dd/MM/yyyy|d/M/yyyy|dd-MMM-yyyy|yyyy-MM-dd", 2, 0, 4, 5)
    ];
}
public sealed record SaveStatementFormat(Guid OperationId, string Bank, StatementFormat Format);
public sealed record BankFormatView(Guid Id, string Bank, StatementFormat Format);

public enum IssuedChequeState { Issued, Presented, Cancelled }
/// <param name="ExpenseAccount">Ledger debited when the cheque does not settle supplier bills (for example 5100 operating expenses).</param>
public sealed record IssueChequeCommand(Guid OperationId, int BankAccount, string Payee, string ChequeNumber, DateOnly Date, decimal Amount, List<BillSettlement> Allocations, int? ExpenseAccount = null, string Narration = "",
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? BookId = null);
public sealed record IssuedChequeActionCommand(Guid OperationId, Guid ChequeId, IssuedChequeState NewState, DateOnly Date, string Reason);
public sealed record IssuedChequeView(Guid Id, string Number, int BankAccount, string Payee, DateOnly Date, decimal Amount, IssuedChequeState State, DateOnly? StateDate, string Reason, Guid VoucherId, string VoucherNumber);

public sealed record DrawerAssignment(int AccountCode, string AccountName, IReadOnlyList<string> Cashiers);
public sealed record AssignDrawerCommand(Guid OperationId, int AccountCode, string CashierUserName, bool Assign);

public interface IBankingExtras
{
    Task<IReadOnlyList<ChequeBookView>> ChequeBooks();
    Task<Guid> SaveChequeBook(ChequeBookCommand input);
    Task<IReadOnlyList<ChequeLayoutView>> ChequeLayouts();
    Task<Guid> SaveChequeLayout(ChequeLayoutCommand input);
    Task<ChequePrintPreview> PreviewChequePrint(ChequePrintRequest input);
    Task<Guid> RecordChequePrint(ChequePrintCommand input);
    Task<IReadOnlyList<BankFormatView>> Formats();
    Task<Guid> SaveFormat(SaveStatementFormat input);
    Task<IReadOnlyList<StatementLineInput>> Preview(string csv, StatementFormat format);
    Task<IReadOnlyList<IssuedChequeView>> IssuedCheques();
    Task<Guid> IssueCheque(IssueChequeCommand input);
    Task<Guid> ActOnIssuedCheque(IssuedChequeActionCommand input);
    Task<IReadOnlyList<DrawerAssignment>> Drawers();
    Task<Guid> AssignDrawer(AssignDrawerCommand input);
}

public static class BankStatementFormats
{
    public static IReadOnlyList<StatementLineInput> Parse(string csv, StatementFormat f)
    {
        if (string.IsNullOrWhiteSpace(csv) || csv.Length > 2_000_000) throw new BusinessException("Provide a statement file up to 2 MB.");
        if (f.Delimiter.Length != 1 && f.Delimiter != "\\t") throw new BusinessException("The column separator is one character (for example , or ;).");
        if (f.DateColumn < 1 || f.ReferenceColumn < 1 || f.AmountColumn == 0 && (f.DebitColumn < 1 || f.CreditColumn < 1) || f.HeaderRows is < 0 or > 20) throw new BusinessException("Choose the date, details and amount (or debit and credit) columns.");
        var delimiter = f.Delimiter == "\\t" ? "\t" : f.Delimiter;
        var formats = f.DateFormat.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(new StringReader(csv.TrimStart('﻿'))) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(delimiter);
        var rows = new List<StatementLineInput>(); var line = 0;
        try
        {
            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields(); line++;
                if (line <= f.HeaderRows || fields is null || fields.All(string.IsNullOrWhiteSpace)) continue;
                string Cell(int column) => column >= 1 && column <= fields.Length ? fields[column - 1] : "";
                decimal? amount;
                if (f.AmountColumn > 0) amount = Money(Cell(f.AmountColumn));
                else { var debit = Money(Cell(f.DebitColumn)); var credit = Money(Cell(f.CreditColumn)); amount = debit is null && credit is null ? null : (credit ?? 0) - Math.Abs(debit ?? 0); }
                var dateText = Cell(f.DateColumn);
                if (!DateOnly.TryParseExact(dateText, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    if (amount is null || amount == 0) continue; // opening/closing balance and summary rows
                    throw new BusinessException($"Line {line}: '{dateText}' is not a date in the format {f.DateFormat}.");
                }
                if (amount is null || amount == 0) continue;
                var reference = Cell(f.ReferenceColumn).Trim();
                rows.Add(new(date, reference.Length == 0 ? "(no details)" : reference.Length > 160 ? reference[..160] : reference, amount.Value));
                if (rows.Count > 5000) throw new BusinessException("Import at most 5000 statement rows at a time.");
            }
        }
        catch (Microsoft.VisualBasic.FileIO.MalformedLineException) { throw new BusinessException($"Line {line + 1} has unbalanced quotes. Check the file before importing."); }
        if (rows.Count == 0) throw new BusinessException("No transactions were found. Check the format: the columns and date format must match the bank's file.");
        return rows;
    }

    /// <summary>Reads 1,234.50 / (1,234.50) / -1234.5 / 1234.50 DR / 1234.50 CR; blank is null.</summary>
    public static decimal? Money(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0 || text == "-") return null;
        var negative = false;
        if (text.StartsWith('(') && text.EndsWith(')')) { negative = true; text = text[1..^1]; }
        if (text.EndsWith("DR", StringComparison.OrdinalIgnoreCase)) { negative = true; text = text[..^2]; }
        else if (text.EndsWith("CR", StringComparison.OrdinalIgnoreCase)) text = text[..^2];
        text = text.Replace(",", "").Replace(" ", "").Replace("NLe", "", StringComparison.OrdinalIgnoreCase).Replace("SLE", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) throw new BusinessException($"'{text}' is not an amount.");
        return negative ? -Math.Abs(value) : value;
    }
}
