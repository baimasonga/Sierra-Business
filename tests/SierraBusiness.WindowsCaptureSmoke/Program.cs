using System.Text;
using SierraBusiness.Desktop;
using SierraBusiness.Domain;
using SierraBusiness.Maui;

// Synthetic PDF exercises Windows PDF rendering and the same bundled native OCR used in MAUI.
static byte[] Pdf(int pages)
{
    var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", $"<< /Type /Pages /Count {pages} /Kids [{string.Join(" ", Enumerable.Range(0, pages).Select(i => $"{4 + i * 2} 0 R"))}] >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
    for (var i = 0; i < pages; i++)
    {
        var content = "BT /F1 22 Tf 60 730 Td (INVOICE) Tj 0 -45 Td (Invoice No: TEST123) Tj 0 -45 Td (Supplier: Sierra Test Supplies) Tj 0 -45 Td (Total: SLE 125.00) Tj ET";
        objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + i * 2} 0 R >>");
        objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
    }
    var pdf = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
    for (var i = 0; i < objects.Count; i++) { offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
    var xref = pdf.Length; pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
    foreach (var offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
    pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    return Encoding.ASCII.GetBytes(pdf.ToString());
}
var reader = new WindowsDocumentReader();
var pdf = Pdf(2);
var result = await reader.Read(pdf, ".pdf", new Progress<string>(Console.WriteLine), default);
if (result.Pages != 2 || !result.Text.Contains("TEST123") || !result.Text.Contains("125.00")) throw new Exception("Real offline PDF OCR failed.");
if (OfflineCaptureParser.Parse(result.Text).Kind != "Invoice") throw new Exception("Document classification failed.");
using var input = new MemoryStream(Pdf(1));
using var random = input.AsRandomAccessStream();
var document = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(random);
using var page = document.GetPage(0);
using var image = new Windows.Storage.Streams.InMemoryRandomAccessStream();
await page.RenderToStreamAsync(image, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = 1800 });
image.Seek(0); using var png = new MemoryStream(); await image.AsStreamForRead().CopyToAsync(png);
var imageResult = await reader.Read(png.ToArray(), ".png", null, default);
if (imageResult.Pages != 1 || !imageResult.Text.Contains("TEST123")) throw new Exception("Real offline image OCR failed.");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await reader.Read(pdf, ".pdf", null, cancelled.Token); throw new Exception("Cancellation failed."); } catch (OperationCanceledException) { }
try { await reader.Read(Pdf(101), ".pdf", null, default); throw new Exception("Page limit failed."); } catch (BusinessException) { }
Console.WriteLine($"PASS: PDF (2 pages), PNG, classification, cancellation and 101-page rejection. OCR score: {result.Confidence:P0}.");
