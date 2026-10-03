using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure.Imports;

public sealed record WorkbookRow(int Number, IReadOnlyList<string> Cells);
public sealed record WorkbookSheet(string Name, IReadOnlyList<string> Headers, IReadOnlyList<WorkbookRow> Rows);

/// <summary>Reads .xlsx with the Open XML SDK only: no Office, no macros, no formula evaluation (saved results are used).</summary>
public static partial class WorkbookReader
{
    public const int MaxBytes = 10 * 1024 * 1024, MaxRows = 10_000, MaxColumns = 60, MaxCell = 2_000;
    public const string FormulaWithoutValue = "\u0000formula";
    private static readonly HashSet<uint> BuiltInDateFormats = [14, 15, 16, 17, 18, 19, 20, 21, 22, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 45, 46, 47, 50, 51, 52, 53, 54, 55, 56, 57, 58];

    public static IReadOnlyList<WorkbookSheet> Read(byte[] content, string? onlySheet = null)
    {
        if (content is null || content.Length == 0) throw new BusinessException("The file is empty.");
        if (content.Length > MaxBytes) throw new BusinessException("The file is larger than 10 MB. Split it into smaller workbooks.");
        try
        {
            using var stream = new MemoryStream(content, false);
            using var document = SpreadsheetDocument.Open(stream, false);
            if (document.DocumentType is SpreadsheetDocumentType.MacroEnabledWorkbook or SpreadsheetDocumentType.MacroEnabledTemplate || document.WorkbookPart?.VbaProjectPart is not null)
                throw new BusinessException("Macro-enabled workbooks (.xlsm) are not accepted. Save the sheet as a normal Excel workbook (.xlsx).");
            var workbook = document.WorkbookPart ?? throw new BusinessException("The file does not contain a workbook.");
            var shared = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(x => x.InnerText).ToArray() ?? [];
            var dateStyles = DateStyles(workbook);
            var result = new List<WorkbookSheet>();
            foreach (var sheet in workbook.Workbook?.Sheets?.Elements<Sheet>() ?? [])
            {
                var name = sheet.Name?.Value ?? "Sheet";
                if (onlySheet is not null && name != onlySheet) continue;
                if (sheet.Id?.Value is not { } id || workbook.GetPartById(id) is not WorksheetPart part) continue;
                result.Add(ReadSheet(name, part, shared, dateStyles));
            }
            if (onlySheet is not null && result.Count == 0) throw new BusinessException($"The sheet \"{onlySheet}\" was not found in this workbook.");
            if (result.Count == 0) throw new BusinessException("The workbook contains no worksheets.");
            return result;
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or FileFormatException or XmlException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            throw new BusinessException("This file is not a readable Excel workbook (.xlsx). Open it in Excel, choose Save As → Excel Workbook, and try again.");
        }
    }

    private static WorkbookSheet ReadSheet(string name, WorksheetPart part, string[] shared, HashSet<uint> dateStyles)
    {
        var raw = new List<(int Number, Dictionary<int, string> Cells)>();
        foreach (var row in part.Worksheet?.Descendants<Row>() ?? [])
        {
            var cells = new Dictionary<int, string>();
            var next = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                var column = cell.CellReference?.Value is { } reference ? ColumnIndex(reference) : next;
                next = column + 1;
                if (column >= MaxColumns) throw new BusinessException($"Sheet \"{name}\" has more than {MaxColumns} columns. Remove unused columns.");
                var value = Value(cell, shared, dateStyles);
                if (value.Length > MaxCell) throw new BusinessException($"A cell in row {row.RowIndex?.Value} of \"{name}\" is longer than {MaxCell} characters.");
                if (value.Length > 0) cells[column] = value;
            }
            if (cells.Count > 0) raw.Add(((int)(row.RowIndex?.Value ?? (uint)(raw.Count + 1)), cells));
            if (raw.Count > MaxRows + 1) throw new BusinessException($"Sheet \"{name}\" has more than {MaxRows:N0} rows. Split the data into several workbooks.");
        }
        if (raw.Count == 0) return new(name, [], []);
        var header = raw[0].Cells; var width = header.Keys.Concat(raw.SelectMany(x => x.Cells.Keys)).Max() + 1;
        var headers = new List<string>();
        for (var i = 0; i < width; i++)
        {
            var text = header.TryGetValue(i, out var h) && h.Trim().Length > 0 ? h.Trim() : $"Column {ColumnName(i)}";
            var unique = text; var n = 2; while (headers.Contains(unique, StringComparer.OrdinalIgnoreCase)) unique = $"{text} ({n++})";
            headers.Add(unique);
        }
        var rows = raw.Skip(1).Select(r => new WorkbookRow(r.Number, Enumerable.Range(0, width).Select(i => r.Cells.TryGetValue(i, out var v) ? v.Trim() : "").ToArray())).ToArray();
        return new(name, headers, rows);
    }

    private static string Value(Cell cell, string[] shared, HashSet<uint> dateStyles)
    {
        var text = cell.CellValue?.Text ?? "";
        if (cell.CellFormula is not null && text.Length == 0 && cell.DataType?.Value != CellValues.InlineString) return FormulaWithoutValue;
        var type = cell.DataType?.Value;
        if (type == CellValues.SharedString) return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < shared.Length ? shared[index] : "";
        if (type == CellValues.InlineString) return cell.InlineString?.InnerText ?? "";
        if (type == CellValues.Boolean) return text == "1" ? "TRUE" : "FALSE";
        if (type == CellValues.Error) return "#ERROR " + text;
        if (type == CellValues.String || type == CellValues.Date) return text;
        if (cell.StyleIndex?.Value is { } style && dateStyles.Contains(style) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 0 and < 2958466)
            return DateTime.FromOADate(serial).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number.ToString(CultureInfo.InvariantCulture) : text;
    }

    private static HashSet<uint> DateStyles(WorkbookPart workbook)
    {
        var result = new HashSet<uint>();
        var stylesheet = workbook.WorkbookStylesPart?.Stylesheet;
        if (stylesheet?.CellFormats is null) return result;
        var custom = stylesheet.NumberingFormats?.Elements<NumberingFormat>().Where(x => x.NumberFormatId is not null).ToDictionary(x => x.NumberFormatId!.Value, x => x.FormatCode?.Value ?? "") ?? [];
        uint index = 0;
        foreach (var format in stylesheet.CellFormats.Elements<CellFormat>())
        {
            var id = format.NumberFormatId?.Value ?? 0;
            if (BuiltInDateFormats.Contains(id) || custom.TryGetValue(id, out var code) && IsDateCode(code)) result.Add(index);
            index++;
        }
        return result;
    }
    private static bool IsDateCode(string code) { var plain = Literals().Replace(code, ""); return plain.Contains('y', StringComparison.OrdinalIgnoreCase) || plain.Contains('d', StringComparison.OrdinalIgnoreCase); }
    [GeneratedRegex("\"[^\"]*\"|\\[[^\\]]*\\]|\\\\.")] private static partial Regex Literals();

    public static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var c in reference) { if (!char.IsLetter(c)) break; index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1); }
        return index - 1;
    }
    public static string ColumnName(int index) { var name = ""; for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name; return name; }
}

/// <summary>Writes simple text workbooks: generated templates and error reports.</summary>
public static class WorkbookWriter
{
    public static byte[] Write(IEnumerable<(string Name, IReadOnlyList<IReadOnlyList<string>> Rows)> sheets)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart(); workbook.Workbook = new Workbook();
            var styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new Stylesheet(
                new Fonts(new Font(), new Font(new Bold())),
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })),
                new Borders(new Border()),
                new CellFormats(new CellFormat(), new CellFormat { FontId = 1, ApplyFont = true }, new CellFormat { NumberFormatId = 49, ApplyNumberFormat = true }));
            var list = workbook.Workbook.AppendChild(new Sheets());
            uint id = 1;
            foreach (var (name, rows) in sheets)
            {
                var part = workbook.AddNewPart<WorksheetPart>();
                var data = new SheetData();
                var width = rows.Count == 0 ? 1 : rows.Max(x => x.Count);
                part.Worksheet = new Worksheet(new Columns(new Column { Min = 1, Max = (uint)Math.Max(width, 1), Width = 24, CustomWidth = true }), data);
                for (var r = 0; r < rows.Count; r++)
                {
                    var row = new Row { RowIndex = (uint)(r + 1) };
                    for (var c = 0; c < rows[r].Count; c++)
                        row.Append(new Cell { CellReference = WorkbookReader.ColumnName(c) + (r + 1), DataType = CellValues.InlineString, StyleIndex = r == 0 ? 1u : 2u, InlineString = new InlineString(new Text(rows[r][c] ?? "") { Space = SpaceProcessingModeValues.Preserve }) });
                    data.Append(row);
                }
                list.Append(new Sheet { Name = name, SheetId = id++, Id = workbook.GetIdOfPart(part) });
            }
            workbook.Workbook.Save();
        }
        return stream.ToArray();
    }
}
