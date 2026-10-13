using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SierraBusiness.Desktop;
using SierraBusiness.Domain;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class CloudBackupTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(),"rokela-cloud-tests-"+Guid.NewGuid().ToString("N"));
    public CloudBackupTests() => Directory.CreateDirectory(folder);
    public void Dispose() => Directory.Delete(folder,true);
    [Fact] public async Task EncryptionStreamsAcrossChunksAndRejectsWrongKeysTamperingAndTruncation()
    {
        var source = Path.Combine(folder,"source.db"); var bytes = RandomNumberGenerator.GetBytes(140000); await File.WriteAllBytesAsync(source,bytes);
        var manifest = new BackupManifest("manual",DateTimeOffset.UtcNow,Convert.ToHexString(SHA256.HashData(bytes)),bytes.Length,"seal","migration"); var key = BackupEncryption.NewRecoveryKey(); var encrypted = Path.Combine(folder,"encrypted.backup");
        await BackupEncryption.Encrypt(source,encrypted,key,manifest); var plain = Path.Combine(folder,"restored.db"); Assert.Equal(manifest,await BackupEncryption.Decrypt(encrypted,plain,key)); Assert.Equal(bytes,await File.ReadAllBytesAsync(plain));
        var failed = Path.Combine(folder,"failed.db"); await Assert.ThrowsAnyAsync<CryptographicException>(() => BackupEncryption.Decrypt(encrypted,failed,BackupEncryption.NewRecoveryKey())); Assert.False(File.Exists(failed));
        var damaged = await File.ReadAllBytesAsync(encrypted); damaged[damaged.Length-20] ^= 1; var corrupt = Path.Combine(folder,"corrupt.backup"); await File.WriteAllBytesAsync(corrupt,damaged); await Assert.ThrowsAnyAsync<CryptographicException>(() => BackupEncryption.Decrypt(corrupt,failed,key)); Assert.False(File.Exists(failed));
        await File.WriteAllBytesAsync(corrupt,damaged[..^1]); await Assert.ThrowsAnyAsync<CryptographicException>(() => BackupEncryption.Decrypt(corrupt,failed,key));
        await Assert.ThrowsAsync<IOException>(() => BackupEncryption.Decrypt(encrypted,plain,key)); Assert.Equal(bytes,await File.ReadAllBytesAsync(plain));
        var badOutput = Path.Combine(folder,"invalid-source.backup"); await Assert.ThrowsAnyAsync<CryptographicException>(() => BackupEncryption.Encrypt(source,badOutput,key,manifest with { Sha256 = new string('0',64) })); Assert.False(File.Exists(badOutput));
    }
    [Fact] public async Task DriveUploadVerifiesFolderContentAndSeparatesRecoveryKeysWithoutLiveRequests()
    {
        var encrypted = Path.Combine(folder,"payload.backup"); var bytes = RandomNumberGenerator.GetBytes(9*1024*1024); await File.WriteAllBytesAsync(encrypted,bytes);
        var tag = new string('A',32); var keyId = new string('B',32); var hash = new string('C',64); var manifest = new BackupManifest("auto",DateTimeOffset.UtcNow,hash,123,"seal","migration"); var requests = 0; var chunks = 0; var uploaded = new List<byte>();
        using var client = new HttpClient(new Handler(async request =>
        {
            requests++; Assert.Equal("Bearer",request.Headers.Authorization!.Scheme);
            var uri = request.RequestUri!;
            if (request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("/folder_123456",StringComparison.Ordinal)) return Json(new { id = "folder_123456",mimeType = "application/vnd.google-apps.folder",trashed = false,appProperties = new Dictionary<string,string> { ["rokelaCompany"] = tag } });
            if (request.Method == HttpMethod.Get && uri.Query.Contains("pageSize=1",StringComparison.Ordinal)) { Assert.Contains("rokelaKeyId",Uri.UnescapeDataString(uri.Query)); return Json(new { files = Array.Empty<object>() }); }
            if (request.Method == HttpMethod.Post) { var response = Json(new { }); response.Headers.Location = new Uri("https://www.googleapis.com/upload/drive/v3/files?upload_id=synthetic"); return response; }
            if (request.Method == HttpMethod.Put) { chunks++; uploaded.AddRange(await request.Content!.ReadAsByteArrayAsync()); if (chunks == 1) { var response = new HttpResponseMessage((HttpStatusCode)308); response.Headers.Add("Range","bytes=0-"+(uploaded.Count-1)); return response; } return Json(new { id = "file_123456" }); }
            return Json(new { id = "file_123456",size = bytes.Length.ToString(),md5Checksum = Convert.ToHexString(MD5.HashData(bytes)),appProperties = new Dictionary<string,string> { ["rokelaSha256"] = hash,["rokelaKeyId"] = keyId } });
        }));
        Assert.Contains("verified",await new GoogleDriveTransport(client).Upload("synthetic-token","folder_123456",tag,keyId,encrypted,manifest,CancellationToken.None)); Assert.Equal(2,chunks); Assert.Equal(bytes,uploaded.ToArray()); Assert.Equal(6,requests);
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value),System.Text.Encoding.UTF8,"application/json") };
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) => send(request); }
}
