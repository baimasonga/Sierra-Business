using System.Text.Json;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

/// <summary>sync-settings.json in the company's data folder (inside the user's own profile). A head-office address makes this installation a branch.</summary>
public sealed class DesktopSyncConfiguration(string directory) : ISyncSettings
{
    private sealed record Stored(string Server = "", string Key = "");
    private string PathName => Path.Combine(directory, "sync-settings.json");
    private Stored Raw() { try { return File.Exists(PathName) ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(PathName)) ?? new() : new(); } catch (JsonException) { return new(); } }

    public SyncOptions Load()
    {
        var s = Raw();
        return new SyncOptions { Role = string.IsNullOrWhiteSpace(s.Server) ? SyncRole.Off : SyncRole.Branch, Server = s.Server.Trim(), Key = s.Key };
    }
    public SyncSettingsView Read() { var o = Load(); return new(o.Role, o.Server, o.Key.Length > 0, true); }
    public void Save(string server, string? key)
    {
        server = (server ?? "").Trim(); var current = Raw();
        if (server.Length > 0 && (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)) throw new BusinessException("Enter head office's address starting with https://.");
        var newKey = key is null ? current.Key : key.Trim();
        if (server.Length > 0 && newKey.Length < 16) throw new BusinessException("Enter this branch's sync key from head office (at least 16 characters).");
        Directory.CreateDirectory(directory);
        var temp = PathName + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Stored(server, server.Length == 0 ? "" : newKey), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, PathName, overwrite: true);
    }
}
