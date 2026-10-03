using SierraBusiness.Application;
namespace SierraBusiness.Maui;

/// <summary>Windows Image Acquisition scanning for the shared document library.</summary>
public sealed class WiaDocumentScanner : IDocumentScanner
{
    public async Task<(string FileName, byte[] Content)?> Scan()
    {
        var page = await DocumentInput.Scan();
        return page is null ? null : (page.Value.Name, page.Value.Bytes);
    }
}
