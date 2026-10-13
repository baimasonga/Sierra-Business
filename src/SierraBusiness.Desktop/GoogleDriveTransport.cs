using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using SierraBusiness.Domain;
namespace SierraBusiness.Desktop;

public sealed class GoogleDriveTransport(HttpClient client)
{
    private const string Files = "https://www.googleapis.com/drive/v3/files";
    private static HttpRequestMessage Request(HttpMethod method,string url,string token)
    {
        var request = new HttpRequestMessage(method,url); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",token); return request;
    }
    private static async Task<JsonDocument> Read(HttpResponseMessage response,CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode) throw new BusinessException($"Google Drive returned HTTP {(int)response.StatusCode}. Reconnect or check storage and network access.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    public async Task<string> Folder(string token,string companyTag,CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(companyTag,"^[A-F0-9]{32}$")) throw new BusinessException("Invalid company backup identifier.");
        var query = $"trashed=false and mimeType='application/vnd.google-apps.folder' and appProperties has {{ key='rokelaCompany' and value='{companyTag}' }}";
        using var list = Request(HttpMethod.Get,Files+"?pageSize=100&fields=files(id)&q="+Uri.EscapeDataString(query),token); using var response = await client.SendAsync(list,ct); using var found = await Read(response,ct);
        var folders = found.RootElement.GetProperty("files").EnumerateArray().ToArray(); if (folders.Length > 1) throw new BusinessException("More than one app backup folder matches this company. Resolve the duplicate folders before reconnecting.");
        if (folders.Length == 1) return folders[0].GetProperty("id").GetString()!;
        using var create = Request(HttpMethod.Post,Files+"?fields=id",token); create.Content = JsonContent.Create(new { name = "Rokela encrypted backups",mimeType = "application/vnd.google-apps.folder",appProperties = new Dictionary<string,string> { ["rokelaCompany"] = companyTag } });
        using var created = await client.SendAsync(create,ct); using var metadata = await Read(created,ct); return metadata.RootElement.GetProperty("id").GetString()!;
    }
    public async Task<string> Upload(string token,string folder,string companyTag,string keyId,string encryptedPath,BackupManifest manifest,CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(folder,"^[A-Za-z0-9_-]{10,200}$") || !System.Text.RegularExpressions.Regex.IsMatch(companyTag,"^[A-F0-9]{32}$") || !System.Text.RegularExpressions.Regex.IsMatch(keyId,"^[A-F0-9]{32}$") || !System.Text.RegularExpressions.Regex.IsMatch(manifest.Sha256,"^[A-F0-9]{64}$")) throw new BusinessException("Invalid backup destination or checksum.");
        using (var folderRequest = Request(HttpMethod.Get,Files+"/"+folder+"?fields=id,mimeType,trashed,appProperties",token))
        using (var folderResponse = await client.SendAsync(folderRequest,ct))
        using (var folderJson = await Read(folderResponse,ct))
        {
            var root = folderJson.RootElement;
            if (root.GetProperty("mimeType").GetString() != "application/vnd.google-apps.folder" || root.TryGetProperty("trashed",out var trashed) && trashed.GetBoolean() || !root.TryGetProperty("appProperties",out var properties) || !properties.TryGetProperty("rokelaCompany",out var tag) || tag.GetString() != companyTag) throw new BusinessException("The Drive backup folder was changed. Reconnect before uploading.");
        }
        var query = $"trashed=false and '{folder}' in parents and appProperties has {{ key='rokelaSha256' and value='{manifest.Sha256}' }} and appProperties has {{ key='rokelaKeyId' and value='{keyId}' }}";
        using (var duplicate = Request(HttpMethod.Get,Files+"?pageSize=1&fields=files(id)&q="+Uri.EscapeDataString(query),token))
        using (var duplicateResponse = await client.SendAsync(duplicate,ct))
        using (var duplicateJson = await Read(duplicateResponse,ct)) if (duplicateJson.RootElement.GetProperty("files").GetArrayLength() > 0) return "An encrypted backup with this verified database checksum is already in Google Drive.";
        var encryptedLength = new FileInfo(encryptedPath).Length;
        using var start = Request(HttpMethod.Post,"https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id,size",token);
        start.Headers.Add("X-Upload-Content-Type","application/octet-stream"); start.Headers.Add("X-Upload-Content-Length",encryptedLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.Content = JsonContent.Create(new { name = $"rokela-{manifest.Created:yyyyMMdd-HHmmss}-{manifest.Sha256[..12]}.rokela-backup",parents = new[] { folder },appProperties = new Dictionary<string,string> { ["rokelaSha256"] = manifest.Sha256,["rokelaCompany"] = companyTag,["rokelaKeyId"] = keyId,["rokelaBackupVersion"] = "1" } });
        using var started = await client.SendAsync(start,ct); if (!started.IsSuccessStatusCode || started.Headers.Location is not Uri location || location.Scheme != "https" || location.Host != "www.googleapis.com" || !location.AbsolutePath.StartsWith("/upload/drive/",StringComparison.Ordinal)) throw new BusinessException("Google Drive could not start a secure upload session.");
        await using var file = File.OpenRead(encryptedPath); using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.MD5); var buffer = new byte[8*1024*1024]; long offset = 0; string? id = null;
        while (offset < encryptedLength)
        {
            var count = (int)Math.Min(buffer.Length,encryptedLength-offset); file.Position = offset; await file.ReadExactlyAsync(buffer.AsMemory(0,count),ct);
            using var chunk = Request(HttpMethod.Put,location.AbsoluteUri,token); chunk.Content = new ByteArrayContent(buffer,0,count); chunk.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); chunk.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset,offset+count-1,encryptedLength);
            using var sent = await client.SendAsync(chunk,ct);
            if ((int)sent.StatusCode == 308)
            {
                if (!sent.Headers.TryGetValues("Range",out var rangeValues)) throw new BusinessException("Drive did not acknowledge the uploaded bytes. Retry the backup.");
                var range = rangeValues.Single(); if (!range.StartsWith("bytes=0-",StringComparison.Ordinal) || !long.TryParse(range[8..],out var last) || last+1 <= offset || last+1 > offset+count) throw new BusinessException("Drive returned an invalid upload acknowledgement."); checksum.AppendData(buffer,0,(int)(last+1-offset)); offset = last+1;
            }
            else { using var complete = await Read(sent,ct); if (offset+count != encryptedLength) throw new BusinessException("Drive ended the upload before all encrypted bytes were sent."); checksum.AppendData(buffer,0,count); id = complete.RootElement.GetProperty("id").GetString(); offset = encryptedLength; }
        }
        if (string.IsNullOrEmpty(id)) throw new BusinessException("Drive did not confirm the completed backup.");
        using var verify = Request(HttpMethod.Get,Files+"/"+Uri.EscapeDataString(id)+"?fields=id,size,md5Checksum,appProperties",token); using var verified = await client.SendAsync(verify,ct); using var readback = await Read(verified,ct);
        if (readback.RootElement.GetProperty("size").GetString() != encryptedLength.ToString(System.Globalization.CultureInfo.InvariantCulture) || !readback.RootElement.GetProperty("md5Checksum").GetString()!.Equals(Convert.ToHexString(checksum.GetHashAndReset()),StringComparison.OrdinalIgnoreCase) || readback.RootElement.GetProperty("appProperties").GetProperty("rokelaSha256").GetString() != manifest.Sha256 || readback.RootElement.GetProperty("appProperties").GetProperty("rokelaKeyId").GetString() != keyId) throw new BusinessException("Drive backup readback did not match the encrypted content, key identifier and database checksum.");
        return "Encrypted backup uploaded and verified in Google Drive.";
    }
}
