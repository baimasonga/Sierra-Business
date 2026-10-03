using System.Runtime.InteropServices;
using SierraBusiness.Desktop;
using SierraBusiness.Domain;
namespace SierraBusiness.Maui;

public static class DocumentInput
{
    public static async Task<(string Name, byte[] Bytes)?> Pick()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Import a document for offline capture", FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.WinUI] = [".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"] }) });
        if (file is null) return null;
        await using var stream = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + count > DocumentCaptureService.MaxBytes) throw new BusinessException("Choose a file up to 20 MiB.");
            buffer.Write(chunk, 0, count);
        }
        return (file.FileName, buffer.ToArray());
    }
    public static Task<(string Name, byte[] Bytes)?> Scan() => MainThread.InvokeOnMainThreadAsync<(string Name, byte[] Bytes)?>(() =>
    {
        object? dialog = null, image = null;
        var path = Path.Combine(FileSystem.CacheDirectory, "scan-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var type = Type.GetTypeFromProgID("WIA.CommonDialog") ?? throw new BusinessException("Windows Image Acquisition is unavailable. Scan using your scanner software and import the saved image or PDF.");
            dialog = Activator.CreateInstance(type)!;
            image = ((dynamic)dialog).ShowAcquireImage(1, 4, 131072, "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}", true, true, false);
            if (image is null) return null;
            ((dynamic)image).SaveFile(path);
            if (new FileInfo(path).Length > DocumentCaptureService.MaxBytes) throw new BusinessException("The scan exceeds 20 MiB. Reduce scanner resolution.");
            return ("Scan-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", File.ReadAllBytes(path));
        }
        catch (COMException) { throw new BusinessException("The scanner could not complete this scan. Check its connection and WIA driver, or import a scan from its own software."); }
        finally
        {
            if (image is not null && Marshal.IsComObject(image)) Marshal.FinalReleaseComObject(image);
            if (dialog is not null && Marshal.IsComObject(dialog)) Marshal.FinalReleaseComObject(dialog);
            if (File.Exists(path)) File.Delete(path);
        }
    });
}
