namespace SierraBusiness.Application;
public sealed record CloudBackupStatus(bool Configured, bool Connected, bool Enabled, bool RecoveryConfirmed, string FolderId, string Message);
public interface ICloudBackupManager
{
    Task<CloudBackupStatus> Status();
    Task Configure(string desktopCredentialsJson);
    Task<string> RecoveryKey();
    Task ConfirmRecoveryKey();
    Task Connect();
    Task SetEnabled(bool enabled);
    Task Disconnect();
    Task<string> UploadLatest();
    Task<string> ImportEncrypted(Stream encryptedStream,string recoveryKey);
}
