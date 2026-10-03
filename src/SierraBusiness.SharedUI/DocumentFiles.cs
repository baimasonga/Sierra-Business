using Microsoft.JSInterop;
using SierraBusiness.Application;
namespace SierraBusiness.SharedUI;

/// <summary>Saving and previewing stored originals in either host (desktop: Documents\Sierra Business; browser: download).</summary>
public static class DocumentFiles
{
    public static async Task<string> Save(IServiceProvider services, IJSRuntime js, DocumentContent file)
    {
        if (services.GetService(typeof(IFileExport)) is IFileExport export) return "Saved to " + await export.Save(file.FileName, file.Content);
        await js.InvokeVoidAsync("sierraDownload", file.FileName, Convert.ToBase64String(file.Content), file.ContentType);
        return "Download started.";
    }
    public static bool CanPreview(string contentType) => contentType is "application/pdf" or "image/png" or "image/jpeg" or "image/gif" or "image/bmp" or "image/webp" or "text/plain" or "text/csv";
    public static ValueTask Preview(IJSRuntime js, string elementId, DocumentContent file) => js.InvokeVoidAsync("sierraPreview", elementId, Convert.ToBase64String(file.Content), file.ContentType);
}
