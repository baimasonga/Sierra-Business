using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace SierraBusiness.Desktop;

public interface ILicenseStore { Task<string?> Read(CancellationToken cancellationToken = default); Task Write(string state, CancellationToken cancellationToken = default); }

/// <summary>A single atomic DPAPI CurrentUser checkpoint; never written into a business database or backup.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsLicenseStore(string directory, string product) : ILicenseStore
{
    private readonly string path = Path.Combine(directory, "license.dpapi");
    private readonly byte[] entropy = SHA256.HashData(Encoding.UTF8.GetBytes("SierraBusiness/License/v1/" + product));
    public async Task<string?> Read(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length > 65536) throw new CryptographicException("Protected license state is invalid.");
        var plain = ProtectedData.Unprotect(bytes, entropy, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public async Task Write(string state, CancellationToken cancellationToken = default)
    {
        var plain = Encoding.UTF8.GetBytes(state);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { await file.WriteAsync(encrypted, cancellationToken); file.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
