using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Desktop;

internal sealed record GoogleBackupConfiguration(string ClientId,string ClientSecret,string RefreshToken,string RecoveryKey,bool RecoveryConfirmed,bool Enabled,string FolderId);
internal static class GoogleDriveBackup
{
    private const string FileName = "google-backup.dpapi";
    internal static string Tag(string directory) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToUpperInvariant())))[..32];
    internal static GoogleBackupConfiguration? Load(string directory)
    {
        var file = Path.Combine(directory,FileName); if (!File.Exists(file)) return null;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Encrypted Google credentials require Windows.");
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(file),Encoding.UTF8.GetBytes(Tag(directory)),DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<GoogleBackupConfiguration>(bytes) ?? throw new BusinessException("Google backup settings are damaged. Reconnect the account."); } finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    internal static void Save(string directory,GoogleBackupConfiguration configuration)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Encrypted Google credentials require Windows.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(configuration); var temporary = Path.Combine(directory,FileName+"."+Guid.NewGuid().ToString("N")+".partial");
        try { File.WriteAllBytes(temporary,ProtectedData.Protect(bytes,Encoding.UTF8.GetBytes(Tag(directory)),DataProtectionScope.CurrentUser)); File.Move(temporary,Path.Combine(directory,FileName),true); }
        finally { CryptographicOperations.ZeroMemory(bytes); if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(2) };
    internal static async Task<string> Upload(string directory,string path,BackupManifest manifest,CancellationToken ct = default)
    {
        var configuration = Load(directory); if (configuration is null || !configuration.Enabled) return "Google Drive backup is off.";
        if (!configuration.RecoveryConfirmed || configuration.RefreshToken.Length == 0 || configuration.FolderId.Length == 0) throw new BusinessException("Save the recovery key and reconnect Google Drive before enabling uploads.");
        using var client = Client(); var fields = new Dictionary<string,string> { ["client_id"] = configuration.ClientId,["refresh_token"] = configuration.RefreshToken,["grant_type"] = "refresh_token" }; if (configuration.ClientSecret.Length > 0) fields["client_secret"] = configuration.ClientSecret;
        var token = await GoogleBackupOAuth.Token(client,fields,"access_token",ct); var encrypted = Path.Combine(Path.GetDirectoryName(path)!,"encrypted-"+Guid.NewGuid().ToString("N")+".partial");
        var keyId = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(configuration.RecoveryKey)))[..32];
        try { await BackupEncryption.Encrypt(path,encrypted,configuration.RecoveryKey,manifest,ct); return await new GoogleDriveTransport(client).Upload(token,configuration.FolderId,Tag(directory),keyId,encrypted,manifest,ct); }
        finally { if (File.Exists(encrypted)) File.Delete(encrypted); }
    }
}

public sealed class GoogleDriveBackupManager(LocalAccountService session,DesktopStore store) : ICloudBackupManager
{
    private async Task Authorize() { var actor = await session.CurrentActor(); actor.Demand("users.manage"); actor.Demand("backups.create"); }
    public async Task<CloudBackupStatus> Status()
    {
        await Authorize(); var config = GoogleDriveBackup.Load(store.DirectoryPath); return new(config is not null,config?.RefreshToken.Length > 0,config?.Enabled ?? false,config?.RecoveryConfirmed ?? false,config?.FolderId ?? "",config is null ? "Supply the administrator's Google desktop credentials file to configure backup sign-in." : "Uploads are encrypted. Keep the recovery key separately from this computer and Google Drive.");
    }
    public async Task Configure(string desktopCredentialsJson)
    {
        await Authorize(); if (desktopCredentialsJson.Length > 16384) throw new BusinessException("The Google desktop credentials file is too large."); using var json = JsonDocument.Parse(desktopCredentialsJson);
        if (!json.RootElement.TryGetProperty("installed",out var installed)) throw new BusinessException("Use Google's Desktop app credentials, not web-server credentials.");
        var id = installed.GetProperty("client_id").GetString() ?? ""; var secret = installed.TryGetProperty("client_secret",out var s) ? s.GetString() ?? "" : "";
        if (id.Length > 200 || !id.EndsWith(".apps.googleusercontent.com",StringComparison.Ordinal) || secret.Length > 500) throw new BusinessException("Invalid Google desktop credentials.");
        var old = GoogleDriveBackup.Load(store.DirectoryPath); GoogleDriveBackup.Save(store.DirectoryPath,new(id,secret,"",old?.RecoveryKey ?? BackupEncryption.NewRecoveryKey(),old?.RecoveryConfirmed ?? false,false,""));
    }
    public async Task<string> RecoveryKey() { await Authorize(); return (GoogleDriveBackup.Load(store.DirectoryPath) ?? throw new BusinessException("Configure the backup credentials first.")).RecoveryKey; }
    public async Task ConfirmRecoveryKey() { await Authorize(); var config = GoogleDriveBackup.Load(store.DirectoryPath) ?? throw new BusinessException("Configure backup credentials first."); GoogleDriveBackup.Save(store.DirectoryPath,config with { RecoveryConfirmed = true }); }
    public async Task Connect()
    {
        await Authorize(); var config = GoogleDriveBackup.Load(store.DirectoryPath) ?? throw new BusinessException("Configure backup credentials first."); if (!config.RecoveryConfirmed) throw new BusinessException("Save and confirm the recovery key before connecting.");
        using var client = GoogleDriveBackup.Client(); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var refresh = await GoogleBackupOAuth.Connect(client,config.ClientId,config.ClientSecret,timeout.Token);
        var fields = new Dictionary<string,string> { ["client_id"] = config.ClientId,["refresh_token"] = refresh,["grant_type"] = "refresh_token" }; if (config.ClientSecret.Length > 0) fields["client_secret"] = config.ClientSecret;
        var token = await GoogleBackupOAuth.Token(client,fields,"access_token",timeout.Token); var folder = await new GoogleDriveTransport(client).Folder(token,GoogleDriveBackup.Tag(store.DirectoryPath),timeout.Token);
        await Authorize(); GoogleDriveBackup.Save(store.DirectoryPath,config with { RefreshToken = refresh,FolderId = folder,Enabled = false });
    }
    public async Task SetEnabled(bool enabled) { await Authorize(); var config = GoogleDriveBackup.Load(store.DirectoryPath) ?? throw new BusinessException("Configure backup credentials first."); if (enabled && (!config.RecoveryConfirmed || config.RefreshToken.Length == 0)) throw new BusinessException("Confirm the recovery key and connect Google Drive before enabling uploads."); GoogleDriveBackup.Save(store.DirectoryPath,config with { Enabled = enabled }); }
    public async Task Disconnect() { await Authorize(); var config = GoogleDriveBackup.Load(store.DirectoryPath); if (config is not null) GoogleDriveBackup.Save(store.DirectoryPath,config with { Enabled = false,RefreshToken = "",FolderId = "" }); }
    public async Task<string> UploadLatest()
    {
        await Authorize(); var backup = store.ListBackups().Where(b => b.Location == "This computer").OrderByDescending(b => b.Manifest.Created).FirstOrDefault(); if (backup.Path is null) throw new BusinessException("Create a verified local backup first.");
        var config = GoogleDriveBackup.Load(store.DirectoryPath); if (config?.Enabled != true) throw new BusinessException("Enable the connected Google backup destination first."); return await GoogleDriveBackup.Upload(store.DirectoryPath,backup.Path,backup.Manifest);
    }
    public async Task<string> ImportEncrypted(Stream encryptedStream,string recoveryKey)
    {
        await Authorize(); Directory.CreateDirectory(store.BackupDirectory); var encrypted = Path.Combine(store.BackupDirectory,"cloud-import-"+Guid.NewGuid().ToString("N")+".partial"); var database = Path.Combine(store.BackupDirectory,"cloud-"+Guid.NewGuid().ToString("N")+".db"); var complete = false;
        try
        {
            await using (var output = new FileStream(encrypted,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { var buffer = new byte[65536]; long total = 0; int count; while ((count = await encryptedStream.ReadAsync(buffer)) > 0) { total += count; if (total > 2L*1024*1024*1024) throw new BusinessException("Import encrypted backups up to 2 GB through this screen."); await output.WriteAsync(buffer.AsMemory(0,count)); } }
            var manifest = await BackupEncryption.Decrypt(encrypted,database,recoveryKey); manifest = manifest with { Kind = "cloud-import" }; await File.WriteAllTextAsync(database+".manifest.json",JsonSerializer.Serialize(manifest));
            var options = new DbContextOptionsBuilder<SierraBusiness.Infrastructure.BusinessDb>().UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = database,Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling = false }.ToString()).Options;
            await using var db = new SierraBusiness.Infrastructure.BusinessDb(options); var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal); var branch = (await session.CurrentActor()).Branch; await store.ValidateBackup(database,known,branch);
            var verified = await SierraBusiness.Infrastructure.HistoryProtection.Verify(db); if (!verified.Ok) throw new BusinessException("The decrypted backup's protected history failed verification."); complete = true; return Path.GetFileName(database);
        }
        finally { if (File.Exists(encrypted)) File.Delete(encrypted); if (!complete) { if (File.Exists(database)) File.Delete(database); if (File.Exists(database+".manifest.json")) File.Delete(database+".manifest.json"); } }
    }
}
