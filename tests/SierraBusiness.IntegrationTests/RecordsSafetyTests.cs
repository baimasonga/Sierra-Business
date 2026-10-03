using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SierraBusiness.Application;
using SierraBusiness.Desktop;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Automatic/off-machine backups, restore and count approval in the real desktop service graph.</summary>
public sealed class RecordsSafetyTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sierra-safety-" + Guid.NewGuid().ToString("N"));
    private readonly string copyFolder = Path.Combine(Path.GetTempPath(), "sierra-copy-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider services = default!;
    private const string Password = "Local-Test-Owner!42";
    private LocalAccountService Accounts => services.GetRequiredService<LocalAccountService>();
    private DesktopStore Store => services.GetRequiredService<DesktopStore>();
    private IBackupManager Backups => services.GetRequiredService<IBackupManager>();
    private BusinessService Business => services.GetRequiredService<BusinessService>();
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(copyFolder);
        var collection = new ServiceCollection(); collection.AddLogging(); collection.AddSierraDesktop(directory);
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await Store.Initialize(services.GetRequiredService<IDbContextFactory<BusinessDb>>());
    }
    public async Task DisposeAsync()
    {
        await services.DisposeAsync(); SqliteConnection.ClearAllPools();
        foreach (var path in new[] { directory, copyFolder }) if (Directory.Exists(path)) Directory.Delete(path, true);
    }
    private async Task<Actor> Owner() { await Accounts.CreateOwner("owner@example.test", Password, Password); return await Accounts.SignIn("owner@example.test", Password); }
    private async Task<Guid> Product(Actor actor) { var id = await Business.AddProduct(actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: 1)); await Business.Post(actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(id, 10, 60)], null)); return id; }
    private Task Sell(Actor actor, Guid product) => Business.Post(actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(product, 1, 100)], PaymentMethod.Cash));
    private string[] BackupFiles(string folder) => Directory.Exists(folder) ? Directory.GetFiles(folder, "*.db") : [];

    [Fact] public async Task NoAutomaticBackupBeforeTheOwnerExists()
    {
        Assert.False(await Store.RunAutomaticIfDue());
        Assert.Empty(BackupFiles(Store.BackupDirectory));
    }

    [Fact] public async Task AutomaticBackupRunsWhenDueWritesManifestAndTrimsOldAutomaticOnly()
    {
        await Owner();
        Directory.CreateDirectory(Store.BackupDirectory);
        var manual = Path.Combine(Store.BackupDirectory, "manual-20200101-000000-keep.db"); File.WriteAllText(manual, "x");
        foreach (var name in new[] { "auto-20200101-000000-a.db", "auto-20200102-000000-b.db", "auto-20200103-000000-c.db" })
        { var file = Path.Combine(Store.BackupDirectory, name); File.WriteAllText(file, "x"); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-5)); }
        Store.SaveSettings(new(true, 24, 2, ""));

        Assert.True(await Store.RunAutomaticIfDue());
        Assert.False(await Store.RunAutomaticIfDue()); // not due again for 24 hours
        var autos = BackupFiles(Store.BackupDirectory).Select(Path.GetFileName).Where(x => x!.StartsWith("auto-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, autos.Length); Assert.Contains("auto-20200103-000000-c.db", autos);
        Assert.True(File.Exists(manual)); // manual backups are never trimmed
        var latest = Store.ListBackups().First();
        Assert.Equal("auto", latest.Manifest.Kind); Assert.Equal(64, latest.Manifest.Sha256.Length); Assert.NotEmpty(latest.Manifest.SealHead);
        Assert.Contains("completed", Store.LastAutomaticStatus);
    }

    [Fact] public async Task CopyFolderReceivesAVerifiedCopyAndBadFoldersAreRejected()
    {
        await Owner();
        Assert.Throws<BusinessException>(() => Store.SaveSettings(new(true, 24, 14, "relative\\folder")));
        Assert.Throws<BusinessException>(() => Store.SaveSettings(new(true, 24, 14, Path.Combine(directory, "inside"))));
        Assert.Throws<BusinessException>(() => Store.SaveSettings(new(true, 24, 14, Path.Combine(copyFolder, "missing"))));
        await Backups.SaveSettings(new(true, 24, 14, copyFolder));

        var result = await Backups.Create();
        Assert.StartsWith("Copied and verified", result.CopyStatus);
        var copy = Directory.GetFiles(copyFolder, Path.GetFileName(result.Path), SearchOption.AllDirectories).Single();
        Assert.Equal(await File.ReadAllBytesAsync(result.Path), await File.ReadAllBytesAsync(copy));
        Assert.Contains(await Backups.List(), x => x.Location == "Copy folder" && x.Name == Path.GetFileName(result.Path));
    }

    [Fact] public async Task MissingCopyDriveKeepsTheLocalBackup()
    {
        await Owner(); await Backups.SaveSettings(new(true, 24, 14, copyFolder));
        Directory.Delete(copyFolder, true); File.WriteAllText(copyFolder, "drive removed"); // the folder path is now unusable
        var result = await Backups.Create();
        Assert.True(File.Exists(result.Path)); Assert.Contains("failed", result.CopyStatus);
        File.Delete(copyFolder);
    }

    [Fact] public async Task RestoreReturnsToTheBackupTakesASafetyCopyAndSignsOut()
    {
        var actor = await Owner(); var product = await Product(actor); await Sell(actor, product);
        await Backups.Create();
        await Sell(actor, product); await Sell(actor, product);
        Assert.Equal(7, (await Business.Read(actor)).Stock.Sum(x => x.Quantity));
        var backup = (await Backups.List()).Single(x => x.Kind == "manual");

        await Assert.ThrowsAsync<BusinessException>(() => Backups.Restore(backup.Id, "yes"));
        var result = await Backups.Restore(backup.Id, "RESTORE");

        Assert.True(result.Integrity.Ok, string.Join("\n", result.Integrity.Problems));
        Assert.True(File.Exists(result.SafetyBackup));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Accounts.CurrentActor()); // must sign in again
        actor = await Accounts.SignIn("owner@example.test", Password);
        Assert.Equal(9, (await Business.Read(actor)).Stock.Sum(x => x.Quantity));
        await Sell(actor, product); // trading continues on the restored data
        await Assert.ThrowsAsync<SqliteException>(async () => { await using var c = new SqliteConnection(Store.ConnectionString); await c.OpenAsync(); await using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM Movements"; await cmd.ExecuteNonQueryAsync(); });
    }

    [Fact] public async Task RestoreRefusesDamagedNewerAndUnauthorisedBackups()
    {
        var actor = await Owner(); await Product(actor);
        var made = await Backups.Create();
        var id = (await Backups.List()).Single().Id;

        var bytes = await File.ReadAllBytesAsync(made.Path); bytes[^10] ^= 0xFF; await File.WriteAllBytesAsync(made.Path, bytes);
        Assert.Contains("changed or damaged", (await Assert.ThrowsAsync<BusinessException>(() => Backups.Restore(id, "RESTORE"))).Message);

        var newer = await Backups.Create(); File.Delete(newer.Path + ".manifest.json");
        await using (var c = new SqliteConnection($"Data Source={newer.Path};Pooling=False")) { await c.OpenAsync(); await using var cmd = c.CreateCommand(); cmd.CommandText = "INSERT INTO __EFMigrationsHistory VALUES ('29990101000000_Future', '99.0')"; await cmd.ExecuteNonQueryAsync(); }
        var newerId = (await Backups.List()).Single(x => x.Name == Path.GetFileName(newer.Path)).Id;
        Assert.Contains("newer version", (await Assert.ThrowsAsync<BusinessException>(() => Backups.Restore(newerId, "RESTORE"))).Message);

        var users = services.GetRequiredService<IUserAdministration>();
        await users.Create(new("clerk@example.test", Password, ["business.read", "backups.create"]));
        await Accounts.SignIn("clerk@example.test", Password);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Backups.Restore(newerId, "RESTORE"));
    }

    [Fact] public async Task CountPreparerCannotApproveWhenAnotherApproverExists()
    {
        var actor = await Owner(); var product = await Product(actor);
        var management = services.GetRequiredService<IInventoryManagement>();
        var solo = await management.SubmitCount(new(Guid.NewGuid(), "Solo shop", [new(Guid.NewGuid(), product, "MAIN", 10, 9, 0, "Missing")]));
        await management.DecideCount(new(Guid.NewGuid(), solo, true, "Checked")); // only approver: allowed and labelled
        Assert.Contains("self-approved", (await management.Counts()).Single(x => x.Id == solo).DecisionReason);

        await services.GetRequiredService<IUserAdministration>().Create(new("manager@example.test", Password, ["business.read", "inventory.receive", "documents.reverse"]));
        var team = await management.SubmitCount(new(Guid.NewGuid(), "Team count", [new(Guid.NewGuid(), product, "MAIN", 9, 8, 0, "Missing")]));
        Assert.Contains("Another authorised person", (await Assert.ThrowsAsync<BusinessException>(() => management.DecideCount(new(Guid.NewGuid(), team, true, "Mine")))).Message);
        await management.DecideCount(new(Guid.NewGuid(), team, false, "Owner may still reject")); // rejection is not restricted

        var recount = await management.SubmitCount(new(Guid.NewGuid(), "Recount", [new(Guid.NewGuid(), product, "MAIN", 9, 8, 0, "Missing")]));
        await Accounts.SignIn("manager@example.test", Password);
        await management.DecideCount(new(Guid.NewGuid(), recount, true, "Second person checked"));
        Assert.Equal("Approved", (await management.Counts()).Single(x => x.Id == recount).Status);
    }
}
