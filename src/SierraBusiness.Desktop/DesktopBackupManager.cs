using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

/// <summary>Backup settings, listing, creation and restore for the signed-in desktop user. Every call re-checks current permissions.</summary>
public sealed class DesktopBackupManager(LocalAccountService session, DesktopStore store, IDbContextFactory<BusinessDb> factory, IOptions<TradingOptions> options) : IBackupManager
{
    public const string RestoreConfirmation = "RESTORE";
    private async Task<Actor> Authorize(bool restore = false)
    {
        var actor = await session.CurrentActor();
        actor.Demand("backups.create");
        if (restore) actor.Demand("users.manage"); // Restoring replaces every record and account: owner-level only.
        if (actor.Branch != options.Value.Branch) throw new UnauthorizedAccessException("Wrong branch.");
        return actor;
    }
    private static string IdFor(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path))))[..20];

    public async Task<BackupSettingsView> Settings() { await Authorize(); return store.ReadSettings(); }
    public async Task SaveSettings(BackupSettingsView settings) { await Authorize(); store.SaveSettings(settings); }
    public async Task<BackupResult> Create() { await Authorize(); return await store.CreateBackupWithCopy(); }
    public async Task<IReadOnlyList<BackupFileView>> List()
    {
        await Authorize();
        return store.ListBackups().Select(x => new BackupFileView(IdFor(x.Path), Path.GetFileName(x.Path), x.Location, x.Manifest.Kind, x.Manifest.Created, x.Manifest.Size, x.Manifest.Sha256, x.Manifest.SealHead.Length >= 16 ? x.Manifest.SealHead[..16] : x.Manifest.SealHead)).ToArray();
    }
    public async Task<RestoreResult> Restore(string backupId, string confirmation)
    {
        var actor = await Authorize(restore: true);
        if (!string.Equals(confirmation?.Trim(), RestoreConfirmation, StringComparison.Ordinal)) throw new BusinessException($"Type {RestoreConfirmation} to confirm. Restoring replaces all current records with the backup's records.");
        var chosen = store.ListBackups().FirstOrDefault(x => IdFor(x.Path) == backupId);
        if (chosen.Path is null) throw new BusinessException("That backup is no longer available. Refresh the list.");
        IReadOnlySet<string> known;
        await using (var db = await factory.CreateDbContextAsync()) known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var safety = await store.Restore(chosen.Path, known, actor.Branch);
        IntegrityReport report;
        await using (var db = await factory.CreateDbContextAsync()) report = await HistoryProtection.Verify(db);
        session.SignOut(); // Accounts and permissions now come from the restored data.
        return new(Path.GetFileName(chosen.Path), safety, report);
    }
    public async Task<string> AutomaticStatus() { await Authorize(); return store.LastAutomaticStatus; }
}
