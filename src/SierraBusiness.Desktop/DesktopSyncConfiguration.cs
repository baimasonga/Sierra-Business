using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

/// <summary>sync-settings.json in the company's data folder (inside the user's own profile). A head-office address makes this installation a branch.</summary>
public sealed class DesktopSyncConfiguration(string directory) : ISyncSettings
{
    private sealed record Stored(string Server = "", string Key = "", string ProtectedKey = "");
    private string warning = "";
    private string PathName => Path.Combine(directory, "sync-settings.json");
    private Stored Raw() { try { return File.Exists(PathName) ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(PathName)) ?? new() : new(); } catch (JsonException) { warning = "The saved sync settings are unreadable. Enter the head-office address and branch key again. Local records are unaffected."; return new(); } }

    public SyncOptions Load()
    {
        warning = ""; var s = Raw();
        s = s with { Server = s.Server ?? "", Key = s.Key ?? "", ProtectedKey = s.ProtectedKey ?? "" };
        var key = s.Key;
        try
        {
            if (s.ProtectedKey.Length > 0) key = Decode(s.ProtectedKey);
            else if (key.Length > 0) Write(s.Server, key); // migrate older plaintext settings atomically
        }
        catch (Exception e) when (e is CryptographicException or FormatException or PlatformNotSupportedException)
        {
            key = ""; warning = "The saved sync key cannot be unlocked for this Windows account. Enter the branch key again under Branch sync. Local records are unaffected.";
        }
        // A period archive never syncs: its continuation is the branch's connection to head office.
        if (CompanyPeriodService.Marker(directory) is { Archive: true }) return new SyncOptions { Role = SyncRole.Off };
        return new SyncOptions { Role = string.IsNullOrWhiteSpace(s.Server) ? SyncRole.Off : SyncRole.Branch, Server = s.Server.Trim(), Key = key };
    }
    public SyncSettingsView Read() { var o = Load(); return new(o.Role, o.Server, o.Key.Length > 0, true, warning); }
    public void Save(string server, string? key)
    {
        server = (server ?? "").Trim();
        if (server.Length > 0 && CompanyPeriodService.Marker(directory) is { } marker && !(marker is { Archive: false, SyncContinuation: true })) throw new BusinessException("Period-split datasets are standalone. Only the continuation of a coordinated branch split may connect to head office.");
        if (server.Length > 0 && (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)) throw new BusinessException("Enter head office's address starting with https://.");
        var newKey = server.Length == 0 ? "" : key is null ? Load().Key : key.Trim();
        if (server.Length > 0 && newKey.Length < 16) throw new BusinessException("Enter this branch's sync key from head office (at least 16 characters).");
        Write(server, newKey);
    }
    private void Write(string server, string key)
    {
        var protectedKey = key.Length == 0 ? "" : Encode(key);
        Directory.CreateDirectory(directory);
        var temp = PathName + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Stored(server, "", protectedKey), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, PathName, overwrite: true);
    }
    private static string Encode(string key)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Desktop sync key storage requires Windows.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    }
    private static string Decode(string key)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Desktop sync key storage requires Windows.");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(key), null, DataProtectionScope.CurrentUser));
    }
}
