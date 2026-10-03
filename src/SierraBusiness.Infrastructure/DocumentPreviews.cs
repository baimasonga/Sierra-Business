using System.Net;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using SierraBusiness.Application;
using SierraBusiness.Infrastructure.Imports;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;
namespace SierraBusiness.Infrastructure;

/// <summary>Cached first-page picture of a stored file, keyed by its fingerprint. Derived data: it can be rebuilt at any time, so it is not part of the sealed history.</summary>
public sealed class DocumentThumbnail { public Guid Id { get; set; } public string Sha256 { get; set; } = ""; public byte[] Content { get; set; } = []; public DateTimeOffset Created { get; set; } }

/// <summary>
/// Builds read-only formatted previews of Office and text files on this computer (nothing is sent anywhere).
/// All text from the file is HTML-encoded; only tags generated here appear in the output.
/// </summary>
public static class DocumentPreviews
{
    public const int MaxBlocks = 600, MaxRows = 200, MaxColumns = 30, MaxSheets = 6, MaxSlides = 60, MaxImageBytes = 3 * 1024 * 1024;
    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    /// <summary>Returns a preview for a stored file. Browser-native formats come back as Kind Native for the viewer script.</summary>
    public static DocumentPreview For(string fileName, string contentType, byte[] content)
    {
        try
        {
            return contentType switch
            {
                "application/pdf" or "image/png" or "image/jpeg" or "image/gif" or "image/bmp" or "image/webp" or "image/tiff" or "text/plain" => new(PreviewKind.Native, fileName, contentType, content, "", ""),
                "text/csv" => Html(fileName, contentType, Csv(content)),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => Html(fileName, contentType, Word(content)),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => Html(fileName, contentType, Excel(content)),
                "application/vnd.openxmlformats-officedocument.presentationml.presentation" => Html(fileName, contentType, Slides(content)),
                _ => new(PreviewKind.None, fileName, contentType, [], "", "Older Office formats (.doc, .xls) cannot be shown here. Download the original to open it, or save it as .docx/.xlsx.")
            };
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException or FormatException or InvalidOperationException or ArgumentException or Domain.BusinessException or System.Xml.XmlException)
        {
            return new(PreviewKind.None, fileName, contentType, [], "", "This file could not be read for a preview. Download the original to open it.");
        }
    }
    private static DocumentPreview Html(string name, string type, string html) => new(PreviewKind.Html, name, type, [], html, "");

    // ---------------- Word ----------------

    public static string Word(byte[] content)
    {
        using var stream = new MemoryStream(content, false);
        using var doc = WordprocessingDocument.Open(stream, false);
        var main = doc.MainDocumentPart ?? throw new InvalidDataException("No document body.");
        var body = main.Document?.Body ?? throw new InvalidDataException("No document body.");
        var html = new StringBuilder("<article class=\"doc-word\">");
        var blocks = 0; string? openList = null;
        void CloseList() { if (openList is not null) { html.Append($"</{openList}>"); openList = null; } }
        foreach (var element in body.ChildElements)
        {
            if (++blocks > MaxBlocks) { CloseList(); html.Append("<p class=\"doc-more\">The preview shows the first part of this document. Download the original to read it all.</p>"); break; }
            if (element is W.Paragraph p)
            {
                var style = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                var inner = Runs(main, p);
                var numbered = p.ParagraphProperties?.NumberingProperties is not null || style.StartsWith("List", StringComparison.OrdinalIgnoreCase);
                if (numbered)
                {
                    var tag = style.Contains("Number", StringComparison.OrdinalIgnoreCase) ? "ol" : "ul";
                    if (openList != tag) { CloseList(); html.Append($"<{tag}>"); openList = tag; }
                    html.Append($"<li>{inner}</li>"); continue;
                }
                CloseList();
                var heading = style switch { "Title" => "h1", "Heading1" or "heading1" => "h2", "Heading2" or "heading2" => "h3", "Heading3" or "heading3" or "Heading4" => "h4", "Subtitle" => "h3", _ => null };
                var align = p.ParagraphProperties?.Justification?.Val?.Value;
                var css = align == W.JustificationValues.Center ? " style=\"text-align:center\"" : align == W.JustificationValues.Right ? " style=\"text-align:right\"" : "";
                html.Append(heading is null ? (inner.Length == 0 ? "<p class=\"doc-gap\"></p>" : $"<p{css}>{inner}</p>") : $"<{heading}{css}>{inner}</{heading}>");
            }
            else if (element is W.Table table) { CloseList(); html.Append(Table(main, table)); }
        }
        CloseList();
        return html.Append("</article>").ToString();
    }

    private static string Table(MainDocumentPart main, W.Table table)
    {
        var html = new StringBuilder("<div class=\"doc-table\"><table>");
        var first = true;
        foreach (var row in table.Elements<W.TableRow>().Take(MaxRows))
        {
            html.Append("<tr>");
            foreach (var cell in row.Elements<W.TableCell>())
            {
                var span = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                var tag = first ? "th" : "td";
                html.Append($"<{tag}{(span > 1 ? $" colspan=\"{span}\"" : "")}>{string.Join("<br>", cell.Elements<W.Paragraph>().Select(p => Runs(main, p)).Where(x => x.Length > 0))}</{tag}>");
            }
            html.Append("</tr>"); first = false;
        }
        return html.Append("</table></div>").ToString();
    }

    private static string Runs(MainDocumentPart main, OpenXmlElement paragraph)
    {
        var html = new StringBuilder();
        foreach (var run in paragraph.Descendants<W.Run>())
        {
            var props = run.RunProperties;
            var text = new StringBuilder();
            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case W.Text t: text.Append(E(t.Text)); break;
                    case W.TabChar: text.Append("&emsp;"); break;
                    case W.Break: case W.CarriageReturn: text.Append("<br>"); break;
                    case W.Drawing d: text.Append(Image(main, d)); break;
                }
            }
            if (text.Length == 0) continue;
            var s = text.ToString();
            if (props?.Bold is { } b && (b.Val is null || b.Val.Value)) s = $"<strong>{s}</strong>";
            if (props?.Italic is { } i && (i.Val is null || i.Val.Value)) s = $"<em>{s}</em>";
            if (props?.Underline?.Val is { } u && u.Value != W.UnderlineValues.None) s = $"<u>{s}</u>";
            html.Append(s);
        }
        return html.ToString();
    }

    private static string Image(MainDocumentPart main, W.Drawing drawing)
    {
        var id = drawing.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value;
        if (id is null || main.GetPartById(id) is not ImagePart part) return "";
        if (part.ContentType is not ("image/png" or "image/jpeg" or "image/gif" or "image/bmp")) return "<span class=\"doc-image-missing\">[picture]</span>";
        using var stream = part.GetStream();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        if (copy.Length > MaxImageBytes) return "<span class=\"doc-image-missing\">[large picture]</span>";
        return $"<img class=\"doc-image\" alt=\"Picture in the document\" src=\"data:{part.ContentType};base64,{Convert.ToBase64String(copy.ToArray())}\">";
    }

    // ---------------- Excel and CSV ----------------

    public static string Excel(byte[] content)
    {
        var sheets = WorkbookReader.Read(content);
        var html = new StringBuilder("<article class=\"doc-sheets\">");
        foreach (var sheet in sheets.Take(MaxSheets))
        {
            html.Append($"<h3>{E(sheet.Name)}</h3>");
            if (sheet.Headers.Count == 0 && sheet.Rows.Count == 0) { html.Append("<p class=\"doc-more\">This sheet is empty.</p>"); continue; }
            html.Append(Grid(sheet.Headers, sheet.Rows.Select(r => r.Cells).ToList(), true));
        }
        if (sheets.Count > MaxSheets) html.Append($"<p class=\"doc-more\">{sheets.Count - MaxSheets} more sheets are in the original.</p>");
        return html.Append("</article>").ToString();
    }

    public static string Csv(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content).TrimStart('﻿');
        var rows = new List<IReadOnlyList<string>>();
        foreach (var line in text.Split('\n').Take(MaxRows + 1)) { var row = Split(line.TrimEnd('\r')); if (row.Count > 0 && !(row.Count == 1 && row[0].Length == 0)) rows.Add(row); }
        return "<article class=\"doc-sheets\">" + (rows.Count == 0 ? "<p class=\"doc-more\">This file is empty.</p>" : Grid(rows[0], rows.Skip(1).ToList(), true)) + "</article>";
    }
    private static List<string> Split(string line)
    {
        var cells = new List<string>(); var cell = new StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted) { if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; } else if (c == '"') quoted = false; else cell.Append(c); }
            else if (c == '"') quoted = true;
            else if (c is ',' or ';' or '\t') { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString()); return cells;
    }

    private static string Grid(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, bool headerRow)
    {
        var width = Math.Min(MaxColumns, Math.Max(headers.Count, rows.Count == 0 ? 0 : rows.Max(r => r.Count)));
        var html = new StringBuilder("<div class=\"doc-table\"><table><thead><tr><th class=\"doc-rownum\"></th>");
        for (var c = 0; c < width; c++) html.Append($"<th>{(headerRow && c < headers.Count && headers[c].Length > 0 ? E(Cell(headers[c])) : WorkbookReader.ColumnName(c))}</th>");
        html.Append("</tr></thead><tbody>");
        var n = 1;
        foreach (var row in rows.Take(MaxRows))
        {
            html.Append($"<tr><td class=\"doc-rownum\">{++n}</td>");
            for (var c = 0; c < width; c++)
            {
                var value = c < row.Count ? Cell(row[c]) : "";
                var numeric = decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _);
                html.Append(numeric ? $"<td class=\"numeric\">{E(value)}</td>" : $"<td>{E(value)}</td>");
            }
            html.Append("</tr>");
        }
        html.Append("</tbody></table></div>");
        if (rows.Count > MaxRows) html.Append($"<p class=\"doc-more\">Showing the first {MaxRows} rows of {rows.Count}.</p>");
        return html.ToString();
    }
    private static string Cell(string value) => value == WorkbookReader.FormulaWithoutValue ? "(formula)" : value;

    // ---------------- PowerPoint ----------------

    public static string Slides(byte[] content)
    {
        using var stream = new MemoryStream(content, false);
        using var deck = PresentationDocument.Open(stream, false);
        var part = deck.PresentationPart ?? throw new InvalidDataException("No slides.");
        var ids = part.Presentation?.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().ToList() ?? [];
        var html = new StringBuilder("<article class=\"doc-slides\">");
        var number = 0;
        foreach (var id in ids.Take(MaxSlides))
        {
            if (id.RelationshipId?.Value is not string rel || part.GetPartById(rel) is not SlidePart slide) continue;
            number++;
            var paragraphs = slide.Slide?.Descendants<A.Paragraph>().Select(p => string.Concat(p.Descendants<A.Text>().Select(t => t.Text))).Where(t => t.Trim().Length > 0).ToList() ?? [];
            html.Append($"<section class=\"doc-slide\"><span class=\"doc-slide-number\">Slide {number}</span>");
            if (paragraphs.Count == 0) html.Append("<p class=\"doc-more\">(pictures or diagrams only)</p>");
            else { html.Append($"<h3>{E(paragraphs[0])}</h3>"); if (paragraphs.Count > 1) html.Append("<ul>").Append(string.Concat(paragraphs.Skip(1).Select(t => $"<li>{E(t)}</li>"))).Append("</ul>"); }
            html.Append("</section>");
        }
        if (ids.Count > MaxSlides) html.Append($"<p class=\"doc-more\">{ids.Count - MaxSlides} more slides are in the original.</p>");
        if (number == 0) html.Append("<p class=\"doc-more\">No slides found.</p>");
        return html.Append("</article>").ToString();
    }
}
