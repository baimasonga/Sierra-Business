using SierraBusiness.Application;
namespace SierraBusiness.Desktop;

/// <summary>Saves generated files (templates, error workbooks) to Documents\Rokela Business Admin, where Excel can open them.</summary>
public sealed class DesktopFileExport : IFileExport
{
    private readonly string folder;
    public DesktopFileExport() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rokela Business Admin")) { }
    public DesktopFileExport(string folder) => this.folder = folder;
    public async Task<string> Save(string fileName, byte[] content)
    {
        Directory.CreateDirectory(folder);
        var name = string.Concat(Path.GetFileName(fileName).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(folder, name);
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
        await File.WriteAllBytesAsync(path, content);
        return path;
    }
}
