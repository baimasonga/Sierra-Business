using System.Runtime.Versioning;
using System.Security.Cryptography;
using SierraBusiness.Desktop;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class WindowsLicenseStoreTests
{
    [WindowsFact, SupportedOSPlatform("windows")]
    public async Task DpapiCheckpointRoundTripsOverwritesAndRejectsTamperingAndWrongScope()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sierra-license-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WindowsLicenseStore(directory, "sierra-business");
            Assert.Null(await store.Read());
            await store.Write("synthetic secret 日本語");
            var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, "license.dpapi"));
            Assert.DoesNotContain("synthetic secret", System.Text.Encoding.UTF8.GetString(bytes));
            Assert.Equal("synthetic secret 日本語", await new WindowsLicenseStore(directory, "sierra-business").Read());
            await store.Write("replacement"); Assert.Equal("replacement", await store.Read());
            await Assert.ThrowsAsync<CryptographicException>(() => new WindowsLicenseStore(directory, "other-product").Read());
            await File.WriteAllBytesAsync(Path.Combine(directory, "license.dpapi"), new byte[64]);
            await Assert.ThrowsAsync<CryptographicException>(() => store.Read());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
