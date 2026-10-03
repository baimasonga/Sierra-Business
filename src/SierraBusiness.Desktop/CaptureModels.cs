using System.Text.RegularExpressions;
namespace SierraBusiness.Desktop;

public sealed class CaptureFields
{
    public string Kind { get; set; } = "Other";
    public string Reference { get; set; } = "";
    public string Party { get; set; } = "";
    public string Date { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Total { get; set; } = "";
    public string Items { get; set; } = "";
    public string Deadline { get; set; } = "";
    public string Requirements { get; set; } = "";
    public static readonly string[] Kinds = ["Receipt", "Invoice", "Proforma", "Tender", "Other"];
}

public sealed record OcrResult(string Text, int Pages, float Confidence);
public interface IOfflineDocumentReader
{
    Task<OcrResult> Read(byte[] original, string extension, IProgress<string>? progress, CancellationToken cancellationToken);
}

/// <summary>Conservative suggestions only. Ambiguous dates, amounts and requirements remain source text.</summary>
public static class OfflineCaptureParser
{
    public static CaptureFields Parse(string text)
    {
        string Find(string pattern) => Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromSeconds(1)).Groups[1].Value.Trim();
        var kind = Regex.IsMatch(text, @"\b(pro\s?forma)\b", RegexOptions.IgnoreCase) ? "Proforma"
            : Regex.IsMatch(text, @"\b(tender|invitation to bid|request for quotation)\b", RegexOptions.IgnoreCase) ? "Tender"
            : Regex.IsMatch(text, @"\binvoice\b", RegexOptions.IgnoreCase) ? "Invoice"
            : Regex.IsMatch(text, @"\breceipt\b", RegexOptions.IgnoreCase) ? "Receipt" : "Other";
        return new CaptureFields
        {
            Kind = kind,
            Reference = Find(@"^\s*(?:invoice|receipt|proforma|tender|reference)\s*(?:no\.?|number|#|ref\.?)?\s*[:#-]\s*([^\r\n]+)"),
            Party = Find(@"^\s*(?:supplier|seller|vendor|buyer|procuring entity)\s*:\s*([^\r\n]+)"),
            Date = Find(@"^\s*(?:invoice date|receipt date|date)\s*:\s*([^\r\n]+)"),
            Currency = Find(@"\b(SLE|NLe|USD|GBP|EUR|SLL)\b"),
            Total = Find(@"^\s*(?:grand total|total amount|amount due|total)\s*[:=]\s*([^\r\n]+)"),
            Deadline = Find(@"^\s*(?:submission deadline|closing date|deadline)\s*:\s*([^\r\n]+)")
        };
    }
}
