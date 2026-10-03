using SierraBusiness.Application;
namespace SierraBusiness.Desktop;

/// <summary>Connects the shared document library to on-device OCR.</summary>
public sealed class OfflineTextReader(IOfflineDocumentReader reader) : IDocumentTextReader
{
    public async Task<DocumentText> Read(byte[] content, string extension, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var result = await reader.Read(content, extension, progress, cancellationToken);
        return new(result.Text, result.Pages, result.Confidence);
    }
}

/// <summary>Optional, explicit online field suggestions for the document library (text only, never the original file).</summary>
public sealed class OnlineDocumentAssistant(OnlineCaptureAssistant assistant) : IDocumentAssistant
{
    public async Task<DocumentHintsView> Suggest(string text, string apiKey, string model, CancellationToken cancellationToken)
    {
        var f = await assistant.Suggest(text, apiKey, model, true, cancellationToken);
        var type = f.Kind switch { "Tender" => LibraryDocumentType.TenderDocument, "Invoice" => LibraryDocumentType.SupplierInvoice, "Receipt" => LibraryDocumentType.Receipt, "Proforma" => LibraryDocumentType.Proforma, _ => DocumentHints.Parse(text).Type };
        return new(type, f.Reference, f.Party, f.Date, f.Total, f.Currency, f.Deadline);
    }
}
