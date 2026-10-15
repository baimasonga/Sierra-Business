using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using SierraBusiness.Application;

namespace SierraBusiness.Desktop;

public sealed record LicenseVerificationKey(string Kid, string X);
/// <summary>Only pinned public keys verify leases. Neither transport success nor local plan names grant access.</summary>
public static class LicenseProtocol
{
    public static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] Decode(string value)
    {
        if (value.Length == 0 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new FormatException();
        var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
        if (Encode(bytes) != value) throw new FormatException();
        return bytes;
    }
    public static string Thumbprint(string x) => Encode(SHA256.HashData(Encoding.UTF8.GetBytes($"{{\"crv\":\"Ed25519\",\"kty\":\"OKP\",\"x\":\"{x}\"}}")));
    public static string Proof(string purpose, string nonce, string installation, string activation, string thumbprint) =>
        string.Join('\n', "trust-platform/v1/device-proof", purpose, nonce, installation, activation, thumbprint);

    public static LicenseCheck Verify(string token, IReadOnlyList<LicenseVerificationKey> keys, string product, string installation, string thumbprint,
        DateTimeOffset now, DateTimeOffset? lastSeen = null, string? activation = null)
    {
        try
        {
            if (token.Length > 16384) return new(LicenseState.Invalid);
            var parts = token.Split('.');
            if (parts.Length != 3) return new(LicenseState.Invalid);
            using var headerDoc = JsonDocument.Parse(Decode(parts[0]));
            var h = headerDoc.RootElement;
            if (h.GetProperty("alg").GetString() != "EdDSA" || h.GetProperty("typ").GetString() != "lease+jws") return new(LicenseState.Invalid);
            var key = keys.SingleOrDefault(k => k.Kid == h.GetProperty("kid").GetString());
            if (key is null) return new(LicenseState.Invalid);
            var publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, Decode(key.X), KeyBlobFormat.RawPublicKey);
            if (!SignatureAlgorithm.Ed25519.Verify(publicKey, Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]))) return new(LicenseState.Invalid);
            using var payload = JsonDocument.Parse(Decode(parts[1]));
            var p = payload.RootElement;
            if (p.GetProperty("v").GetInt32() != 1 || p.GetProperty("iss").GetString() != "trust-platform" ||
                p.GetProperty("aud").GetString() != "product:" + product || p.GetProperty("inst").GetString() != installation ||
                p.GetProperty("cnf").GetProperty("jkt").GetString() != thumbprint ||
                (activation is not null && p.GetProperty("act").GetString() != activation)) return new(LicenseState.Invalid);
            var issued = p.GetProperty("iat").GetInt64(); var notBefore = p.GetProperty("nbf").GetInt64();
            var expires = p.GetProperty("exp").GetInt64(); var refresh = p.GetProperty("refresh_after").GetInt64();
            if (expires <= issued || notBefore > expires || refresh < issued || refresh > expires) return new(LicenseState.Invalid);
            var expiry = DateTimeOffset.FromUnixTimeSeconds(expires);
            if ((lastSeen is not null && now < lastSeen.Value.AddSeconds(-60)) || now.ToUnixTimeSeconds() < notBefore - 60) return new(LicenseState.ClockAnomaly, expiry);
            if (now.ToUnixTimeSeconds() >= expires) return new(LicenseState.Expired, expiry);
            var features = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var property in p.GetProperty("ent").EnumerateObject())
            {
                if (!features.TryAdd(property.Name, property.Value.ValueKind == JsonValueKind.True)) return new(LicenseState.Invalid);
                if (property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False and not JsonValueKind.Number) return new(LicenseState.Invalid);
            }
            return new(now.ToUnixTimeSeconds() >= refresh ? LicenseState.RefreshRecommended : LicenseState.Valid, expiry, features);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
        { return new(LicenseState.Invalid); }
    }
}
