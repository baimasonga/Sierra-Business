using System.Text.RegularExpressions;
namespace SierraBusiness.Application;

/// <summary>Document library (0.15.0): originals of tenders, cheques, invoices, contracts and other business papers, with versions, links and history.</summary>
public enum LibraryDocumentType { TenderDocument, Cheque, SupplierInvoice, Receipt, Contract, DeliveryNote, Proforma, Certificate, Correspondence, Other }
public enum DocumentLinkTarget { Transaction, Party, Cheque, SerialUnit, Installation, Tender, ServiceJob }

public sealed record DocumentDetails(LibraryDocumentType Type, string Title, string Reference = "", string Party = "", DateOnly? DocumentDate = null, decimal? Amount = null, string Currency = "",
    DateOnly? DueDate = null, string Tags = "", string Notes = "", bool Confidential = false);
/// <summary>A file as received. Text, pages and confidence come from on-device OCR when available; files without readable text (photos, handwriting) are accepted.</summary>
public sealed record DocumentFileInput(string FileName, byte[] Content, string Text = "", int Pages = 0, float Confidence = 0);
public sealed record DocumentLinkInput(DocumentLinkTarget Target, string Key);
public sealed record AddDocumentCommand(Guid OperationId, DocumentDetails Details, DocumentFileInput File, List<DocumentLinkInput>? Links = null);
public sealed record AddDocumentVersionCommand(Guid OperationId, Guid DocumentId, DocumentFileInput File, string Reason);
public sealed record UpdateDocumentCommand(Guid OperationId, Guid DocumentId, int ExpectedVersion, DocumentDetails Details, string Reason = "");
public sealed record VoidDocumentCommand(Guid OperationId, Guid DocumentId, int ExpectedVersion, string Reason);
public sealed record LinkDocumentCommand(Guid OperationId, Guid DocumentId, DocumentLinkTarget Target, string Key);
public sealed record UnlinkDocumentCommand(Guid OperationId, Guid LinkId, string Reason);
public sealed record DocumentQuery(string Text = "", LibraryDocumentType? Type = null, bool IncludeVoided = false, DocumentLinkTarget? Target = null, string? Key = null);

public sealed record DocumentFileView(Guid Id, int Version, string FileName, string ContentType, long Size, string Sha256, int Pages, DateTimeOffset Created, string CreatedBy, string Reason);
public sealed record DocumentLinkView(Guid Id, DocumentLinkTarget Target, string Key, string Label);
public sealed record DocumentRevisionView(int Version, string Action, string Actor, DateTimeOffset Time, DocumentDetails Details, string Reason);
public sealed record LibraryItemView(Guid Id, string Number, int Version, DocumentDetails Details, bool Voided, DateTimeOffset Created, string CreatedBy, int Files, int Links, Guid LatestFileId, string LatestFileName, string LatestContentType);
public sealed record LibraryDocumentView(LibraryItemView Item, IReadOnlyList<DocumentFileView> Files, IReadOnlyList<DocumentLinkView> Links, IReadOnlyList<DocumentRevisionView> History, string Text, string VoidReason);
public sealed record DocumentContent(string FileName, string ContentType, byte[] Content);
/// <summary>Native: the viewer script renders Content (PDF, images, TIFF, text). Html: a formatted read-only rendering made on this computer (Word, Excel, PowerPoint, CSV). None: Message explains why.</summary>
public enum PreviewKind { Native, Html, None }
public sealed record DocumentPreview(PreviewKind Kind, string FileName, string ContentType, byte[] Content, string Html, string Message);
public sealed record DocumentDeadline(Guid Id, string Number, string Title, LibraryDocumentType Type, string Party, DateOnly DueDate, int DaysLeft);
public sealed record LinkTargetOption(string Key, string Label);

public interface IDocumentLibrary
{
    Task<IReadOnlyList<LibraryItemView>> Search(DocumentQuery query);
    Task<LibraryDocumentView> Get(Guid id);
    Task<DocumentContent> Download(Guid fileId);
    Task<DocumentPreview> Preview(Guid fileId);
    /// <summary>Cached first-page pictures (JPEG) for the given files, where one exists and the caller may see the file.</summary>
    Task<IReadOnlyDictionary<Guid, byte[]>> Thumbnails(IReadOnlyCollection<Guid> fileIds);
    Task SaveThumbnail(Guid fileId, byte[] jpeg);
    Task<IReadOnlyList<DocumentDeadline>> Deadlines(int days = 60);
    Task<LibraryItemView?> FindDuplicate(byte[] content);
    Task<IReadOnlyList<LinkTargetOption>> Targets(DocumentLinkTarget target, string text);
    Task<Guid> Add(AddDocumentCommand input);
    Task<Guid> AddVersion(AddDocumentVersionCommand input);
    Task<Guid> Update(UpdateDocumentCommand input);
    Task<Guid> Void(VoidDocumentCommand input);
    Task<Guid> Link(LinkDocumentCommand input);
    Task<Guid> Unlink(UnlinkDocumentCommand input);
}

/// <summary>Optional device capabilities, registered only where available (the Windows desktop app).</summary>
public sealed record DocumentText(string Text, int Pages, float Confidence);
public interface IDocumentTextReader { Task<DocumentText> Read(byte[] content, string extension, IProgress<string>? progress, CancellationToken cancellationToken); }
public interface IDocumentScanner { Task<(string FileName, byte[] Content)?> Scan(); }
public interface IDocumentAssistant { Task<DocumentHintsView> Suggest(string text, string apiKey, string model, CancellationToken cancellationToken); }

public sealed record DocumentHintsView(LibraryDocumentType Type, string Reference, string Party, string Date, string Total, string Currency, string Deadline);
/// <summary>Conservative suggestions from extracted text. Ambiguous values stay blank; the user confirms everything.</summary>
public static class DocumentHints
{
    public static readonly string[] Extensions = [".pdf", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".docx", ".xlsx", ".pptx", ".doc", ".xls", ".txt", ".csv"];
    public static readonly string[] OcrExtensions = [".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"];
    public static string Name(LibraryDocumentType type) => type switch
    {
        LibraryDocumentType.TenderDocument => "Tender document", LibraryDocumentType.SupplierInvoice => "Supplier invoice", LibraryDocumentType.Contract => "Contract / agreement",
        LibraryDocumentType.DeliveryNote => "Delivery note (signed)", LibraryDocumentType.Proforma => "Proforma / quotation", LibraryDocumentType.Certificate => "Certificate / licence", _ => type.ToString()
    };
    public static DocumentHintsView Parse(string text)
    {
        text ??= "";
        string Find(string pattern) => Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromSeconds(1)).Groups[1].Value.Trim();
        bool Has(string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var type = Has(@"\b(tender|invitation to bid|request for (quotation|proposal)|bid document)\b") ? LibraryDocumentType.TenderDocument
            : Has(@"\bor bearer\b|\bpay\b.{0,80}\b(the sum of|leones)\b|\bcheque\b") ? LibraryDocumentType.Cheque
            : Has(@"\b(contract|agreement|memorandum of understanding)\b") ? LibraryDocumentType.Contract
            : Has(@"\bdelivery note\b|\bgoods received note\b") ? LibraryDocumentType.DeliveryNote
            : Has(@"\b(pro\s?forma|quotation)\b") ? LibraryDocumentType.Proforma
            : Has(@"\b(certificate|licen[cs]e)\b") ? LibraryDocumentType.Certificate
            : Has(@"\binvoice\b") ? LibraryDocumentType.SupplierInvoice
            : Has(@"\breceipt\b") ? LibraryDocumentType.Receipt : LibraryDocumentType.Other;
        return new(type,
            Find(@"^\s*(?:invoice|receipt|proforma|quotation|tender|contract|cheque|reference)\s*(?:no\.?|number|#|ref\.?)?\s*[:#-]\s*([^\r\n]{1,120})"),
            Find(@"^\s*(?:supplier|seller|vendor|buyer|customer|procuring entity|payee|drawer)\s*:\s*([^\r\n]{1,160})"),
            Find(@"^\s*(?:invoice date|receipt date|document date|date)\s*:\s*([^\r\n]{1,60})"),
            Find(@"^\s*(?:grand total|total amount|amount due|total|amount)\s*[:=]\s*([^\r\n]{1,60})"),
            Find(@"\b(SLE|NLe|USD|GBP|EUR)\b"),
            Find(@"^\s*(?:submission deadline|closing date|deadline|expiry date|expires)\s*:\s*([^\r\n]{1,80})"));
    }
}
