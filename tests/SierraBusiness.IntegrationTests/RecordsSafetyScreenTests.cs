using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SierraBusiness.Application;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Renders the Backups &amp; integrity screen with stand-in services and drives it like a user.</summary>
public sealed class RecordsSafetyScreenTests : IDisposable
{
    private readonly BunitContext context = new();
    private readonly FakeBackups backups = new();
    private static readonly Actor Owner = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet());
    public RecordsSafetyScreenTests() { context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true); context.JSInterop.SetupVoid("alert", _ => true).SetVoidResult(); }
    public void Dispose() => context.Dispose();

    [Fact] public void OwnerSeesIntegrityBackupsAndCanRestoreOnlyAfterTypingConfirmation()
    {
        context.Services.AddSingleton<IBackupManager>(backups); context.Services.AddSingleton<IIntegrityService>(new FakeIntegrity());
        var restored = false;
        var screen = context.Render<RecordsSafetyWorkspace>(p => p.Add(x => x.Actor, Owner).Add(x => x.OnRestored, () => restored = true));

        screen.Find("button.secondary").Click(); // Check records now
        Assert.Contains("All 42 posted records match", screen.Markup); Assert.Contains("ABCDEF0123456789", screen.Markup);

        Assert.Contains("Automatic", screen.Markup); Assert.Contains("Copy folder", screen.Markup);
        screen.FindAll("button.text-button").First(x => x.TextContent.Contains("Restore")).Click();
        var restore = screen.Find("button.danger");
        Assert.True(restore.HasAttribute("disabled"));
        screen.Find("input[autocomplete=off]").Input("RESTORE");
        screen.Find("button.danger").Click();
        Assert.Equal("RESTORE", backups.Confirmation); Assert.True(restored);
    }

    [Fact] public void ClerkCannotSeeRestoreAndWebHostExplainsBackupsAreDesktopOnly()
    {
        context.Services.AddSingleton<IBackupManager>(backups);
        var clerk = Owner with { Permissions = new HashSet<string> { "business.read", "backups.create" } };
        var screen = context.Render<RecordsSafetyWorkspace>(p => p.Add(x => x.Actor, clerk));
        Assert.DoesNotContain("Restore…", screen.Markup); Assert.Contains("Back up now", screen.Markup);

        using var web = new BunitContext();
        web.Services.AddSingleton<IIntegrityService>(new FakeIntegrity());
        var webScreen = web.Render<RecordsSafetyWorkspace>(p => p.Add(x => x.Actor, Owner));
        Assert.Contains("Windows desktop application", webScreen.Markup); Assert.Contains("Check records now", webScreen.Markup);
    }

    [Fact] public void SettingsErrorsAreShownWithoutLosingInput()
    {
        backups.FailSave = true; context.Services.AddSingleton<IBackupManager>(backups);
        var screen = context.Render<RecordsSafetyWorkspace>(p => p.Add(x => x.Actor, Owner));
        screen.Find("input[placeholder]").Change("relative");
        screen.FindAll("button.primary").Single().Click();
        Assert.Contains("full path", screen.Markup); Assert.Equal("relative", screen.Find("input[placeholder]").GetAttribute("value"));
    }

    private sealed class FakeIntegrity : IIntegrityService
    {
        public Task<IntegrityReport> Verify() => Task.FromResult(new IntegrityReport(true, 7, 42, "ABCDEF0123456789", true, [], ["Protection started on 2026-10-03 covering 20 existing rows."], DateTimeOffset.UtcNow));
    }
    private sealed class FakeBackups : IBackupManager
    {
        public string Confirmation = ""; public bool FailSave;
        public Task<BackupSettingsView> Settings() => Task.FromResult(new BackupSettingsView(true, 24, 14, ""));
        public Task SaveSettings(BackupSettingsView settings) => FailSave ? throw new SierraBusiness.Domain.BusinessException("Enter the full path of the copy folder.") : Task.CompletedTask;
        public Task<IReadOnlyList<BackupFileView>> List() => Task.FromResult<IReadOnlyList<BackupFileView>>([new("id1", "auto-1.db", "This computer", "auto", DateTimeOffset.UtcNow, 2_000_000, "AB", "ABCDEF0123456789")]);
        public Task<BackupResult> Create() => Task.FromResult(new BackupResult("auto-1.db", "Copied and verified."));
        public Task<RestoreResult> Restore(string backupId, string confirmation) { Confirmation = confirmation; return Task.FromResult(new RestoreResult("auto-1.db", "before-restore-1.db", new(true, 1, 1, "X", true, [], [], DateTimeOffset.UtcNow))); }
        public Task<string> AutomaticStatus() => Task.FromResult("Automatic backup completed.");
    }
}
