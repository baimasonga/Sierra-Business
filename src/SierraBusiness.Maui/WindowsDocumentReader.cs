using System.Text;
using SierraBusiness.Desktop;
using SierraBusiness.Domain;
using Tesseract;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
namespace SierraBusiness.Maui;

public sealed class WindowsDocumentReader : IOfflineDocumentReader
{
    public Task<OcrResult> Read(byte[] original, string extension, IProgress<string>? progress, CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            if (original.Length is 0 or > DocumentCaptureService.MaxBytes) throw new BusinessException("Choose a document up to 20 MiB.");
            using var engine = new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "OcrData"), "eng", EngineMode.LstmOnly);
            using var source = new MemoryStream(original);
            using var input = source.AsRandomAccessStream();
            var text = new StringBuilder();
            var confidence = 0f;
            var pages = 0;
            void Recognize(byte[] png, int page, int total)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Reading page {page} of {total} offline…");
                using var pix = Pix.LoadFromMemory(png);
                using var result = engine.Process(pix, PageSegMode.Auto);
                var content = result.GetText();
                if (!string.IsNullOrWhiteSpace(content)) text.AppendLine($"--- Page {page} ---").AppendLine(content);
                confidence += result.GetMeanConfidence(); pages++;
                if (text.Length > DocumentCaptureService.MaxText) throw new BusinessException("Too much text in one document. Split it into smaller files; nothing has been imported.");
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var pdf = await PdfDocument.LoadFromStreamAsync(input).AsTask(cancellationToken);
                if (pdf.PageCount is 0 or > 100) throw new BusinessException("PDFs may contain up to 100 pages. Split larger tender documents into volumes.");
                for (uint i = 0; i < pdf.PageCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var page = pdf.GetPage(i);
                    using var output = new InMemoryRandomAccessStream();
                    var scale = 2500d / Math.Max(page.Size.Width, page.Size.Height);
                    await page.RenderToStreamAsync(output, new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, page.Size.Width * scale), DestinationHeight = (uint)Math.Max(1, page.Size.Height * scale) }).AsTask(cancellationToken);
                    Recognize(await Bytes(output), (int)i + 1, (int)pdf.PageCount);
                }
            }
            else
            {
                var decoder = await BitmapDecoder.CreateAsync(input).AsTask(cancellationToken);
                if (decoder.FrameCount is 0 or > 100) throw new BusinessException("Images may contain up to 100 pages.");
                for (uint i = 0; i < decoder.FrameCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var frame = await decoder.GetFrameAsync(i).AsTask(cancellationToken);
                    if ((long)frame.PixelWidth * frame.PixelHeight > 40_000_000) throw new BusinessException("An image exceeds 40 megapixels. Resize it before importing.");
                    var scale = Math.Min(1d, 3000d / Math.Max(frame.PixelWidth, frame.PixelHeight));
                    var width = (uint)Math.Max(1, frame.OrientedPixelWidth * scale); var height = (uint)Math.Max(1, frame.OrientedPixelHeight * scale);
                    var pixels = await frame.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                        new BitmapTransform { ScaledWidth = (uint)Math.Max(1, frame.PixelWidth * scale), ScaledHeight = (uint)Math.Max(1, frame.PixelHeight * scale) }, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(cancellationToken);
                    using var output = new InMemoryRandomAccessStream();
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output).AsTask(cancellationToken);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 300, 300, pixels.DetachPixelData());
                    await encoder.FlushAsync().AsTask(cancellationToken);
                    Recognize(await Bytes(output), (int)i + 1, (int)decoder.FrameCount);
                }
            }
            return new OcrResult(text.ToString(), pages, confidence / Math.Max(1, pages));
        }, cancellationToken);

    private static async Task<byte[]> Bytes(IRandomAccessStream stream)
    {
        stream.Seek(0);
        using var memory = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(memory);
        return memory.ToArray();
    }
}
