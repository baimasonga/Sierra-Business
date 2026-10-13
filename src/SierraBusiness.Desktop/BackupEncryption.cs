using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SierraBusiness.Desktop;

/// <summary>Versioned authenticated streaming encryption; the recovery key stays outside Drive.</summary>
public static class BackupEncryption
{
    private const int ChunkSize = 65536;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ROKELABK");
    public static string NewRecoveryKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static byte[] Key(string recoveryKey)
    {
        byte[] key; try { key = Convert.FromBase64String(recoveryKey.Trim()); } catch (FormatException) { throw new CryptographicException("Use the saved 256-bit backup recovery key."); }
        if (key.Length != 32) throw new CryptographicException("Use the saved 256-bit backup recovery key."); return key;
    }
    public static async Task Encrypt(string source, string destination, string recoveryKey, BackupManifest manifest, CancellationToken ct = default)
    {
        var key = Key(recoveryKey); var created = false;
        try
        {
            await using var input = File.OpenRead(source); if (input.Length <= 0 || input.Length > 512L*1024*1024*1024 || manifest.Size != input.Length) throw new IOException("Backup size does not match its manifest or exceeds the supported limit.");
            var metadata = JsonSerializer.SerializeToUtf8Bytes(manifest); if (metadata.Length > 8192) throw new IOException("Backup metadata is too large.");
            var header = new byte[29 + metadata.Length]; Magic.CopyTo(header,0); header[8] = 1; RandomNumberGenerator.Fill(header.AsSpan(9,8)); BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(17,8),input.Length); BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(25,4),metadata.Length); metadata.CopyTo(header,29);
            await using var output = new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None); created = true; await output.WriteAsync(header,ct);
            using var aes = new AesGcm(key,16); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var plain = new byte[ChunkSize]; var encrypted = new byte[ChunkSize]; var tag = new byte[16]; var nonce = new byte[12]; header.AsSpan(9,8).CopyTo(nonce); var aad = new byte[40]; SHA256.HashData(header).CopyTo(aad,0);
            long remaining = input.Length; uint index = 0;
            while (remaining > 0)
            {
                var length = (int)Math.Min(ChunkSize,remaining); await input.ReadExactlyAsync(plain.AsMemory(0,length),ct); BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8),index); BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(32),index); BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(36),length);
                hash.AppendData(plain,0,length); aes.Encrypt(nonce,plain.AsSpan(0,length),encrypted.AsSpan(0,length),tag,aad); await output.WriteAsync(encrypted.AsMemory(0,length),ct); await output.WriteAsync(tag,ct); remaining -= length; index = checked(index+1);
            }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase)) throw new CryptographicException("Source backup checksum differs from its verified manifest."); await output.FlushAsync(ct);
        }
        catch { if (created && File.Exists(destination)) File.Delete(destination); throw; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static async Task<BackupManifest> Decrypt(string source, string destination, string recoveryKey, CancellationToken ct = default)
    {
        var key = Key(recoveryKey); var created = false;
        try
        {
            await using var input = File.OpenRead(source); var prefix = new byte[29]; await input.ReadExactlyAsync(prefix,ct);
            if (!prefix.AsSpan(0,8).SequenceEqual(Magic) || prefix[8] != 1) throw new CryptographicException("Unsupported encrypted backup format.");
            var length = BinaryPrimitives.ReadInt64LittleEndian(prefix.AsSpan(17,8)); var metadataLength = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(25,4)); if (length <= 0 || length > 512L*1024*1024*1024 || metadataLength is < 1 or > 8192) throw new CryptographicException("Invalid encrypted backup header.");
            var chunks = (length+ChunkSize-1)/ChunkSize; if (input.Length != 29+metadataLength+length+chunks*16) throw new CryptographicException("The encrypted backup is truncated or contains extra data.");
            var header = new byte[29+metadataLength]; prefix.CopyTo(header,0); await input.ReadExactlyAsync(header.AsMemory(29),ct);
            BackupManifest manifest; try { manifest = JsonSerializer.Deserialize<BackupManifest>(header.AsSpan(29)) ?? throw new CryptographicException("Invalid backup metadata."); } catch (JsonException) { throw new CryptographicException("Invalid backup metadata."); }
            if (manifest.Size != length) throw new CryptographicException("Backup metadata size differs from its content.");
            await using var output = new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None); created = true;
            using var aes = new AesGcm(key,16); var plain = new byte[ChunkSize]; var encrypted = new byte[ChunkSize]; var tag = new byte[16]; var nonce = new byte[12]; header.AsSpan(9,8).CopyTo(nonce); var aad = new byte[40]; SHA256.HashData(header).CopyTo(aad,0); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); uint index = 0;
            for (long remaining = length; remaining > 0; index = checked(index+1))
            {
                var count = (int)Math.Min(ChunkSize,remaining); await input.ReadExactlyAsync(encrypted.AsMemory(0,count),ct); await input.ReadExactlyAsync(tag,ct); BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8),index); BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(32),index); BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(36),count);
                aes.Decrypt(nonce,encrypted.AsSpan(0,count),tag,plain.AsSpan(0,count),aad); hash.AppendData(plain,0,count); await output.WriteAsync(plain.AsMemory(0,count),ct); remaining -= count;
            }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase)) throw new CryptographicException("Decrypted backup checksum differs from its manifest."); await output.FlushAsync(ct); return manifest;
        }
        catch { if (created && File.Exists(destination)) File.Delete(destination); throw; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
