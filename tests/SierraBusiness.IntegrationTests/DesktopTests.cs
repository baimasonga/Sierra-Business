using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SierraBusiness.Application;
using SierraBusiness.Desktop;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class DesktopTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sierra-desktop-tests-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider services = default!;
    private readonly TestClock clock = new();
    private const string Password = "Local-Test-Owner!42";
    private LocalAccountService Accounts => services.GetRequiredService<LocalAccountService>();
    public async Task InitializeAsync()
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSierraDesktop(directory);
        collection.AddSingleton<TimeProvider>(clock);
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await services.GetRequiredService<DesktopStore>().Initialize(services.GetRequiredService<IDbContextFactory<BusinessDb>>());
    }
    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
    private async Task<Actor> CreateAndSignIn()
    {
        await Accounts.CreateOwner("owner@example.test", Password, Password);
        return await Accounts.SignIn("owner@example.test", Password);
    }
    [Fact] public async Task FirstRunCreatesOwnerOnceAndSupportsLocalTrading()
    {
        Assert.False(await Accounts.HasOwner());
        var actor = await CreateAndSignIn();
        Assert.True(await Accounts.HasOwner());
        Assert.Contains("sales.create", actor.Permissions);
        await Assert.ThrowsAsync<BusinessException>(() => Accounts.CreateOwner("other@example.test", Password, Password));
        var business = services.GetRequiredService<IBusinessService>();
        var id = await business.AddProduct(actor, new(Guid.NewGuid(), "LOCAL", "Local product", "General", "piece", 10, TrackingMode.Quantity, MinimumPrice: .0001m));
        Assert.Equal(id, Assert.Single((await business.Read(actor)).Products).Id);
    }
    [Fact] public async Task FailedOwnerCreationDoesNotLeavePartialAccount()
    {
        await Assert.ThrowsAsync<BusinessException>(() => Accounts.CreateOwner("owner@example.test", "weak", "weak"));
        Assert.False(await Accounts.HasOwner());
        await Assert.ThrowsAsync<BusinessException>(() => Accounts.CreateOwner("owner@example.test", Password, "different"));
        Assert.False(await Accounts.HasOwner());
    }
    [Fact] public async Task SignOutAndExpiryRejectStaleUiActor()
    {
        var actor = await CreateAndSignIn();
        var business = services.GetRequiredService<IBusinessService>();
        Accounts.SignOut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.Read(actor));
        actor = await Accounts.SignIn("owner@example.test", Password);
        clock.Now = clock.Now.AddHours(9);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.Read(actor));
    }
    [Fact] public async Task PermissionRevocationIsEnforcedEvenWithStaleUiClaims()
    {
        var actor = await CreateAndSignIn();
        using (var scope = services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var owner = (await users.FindByIdAsync(actor.Id))!;
            var claim = (await users.GetClaimsAsync(owner)).Single(x => x.Type == "permission" && x.Value == "catalogue.write");
            Assert.True((await users.RemoveClaimAsync(owner, claim)).Succeeded);
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<IBusinessService>().AddProduct(actor, new(Guid.NewGuid(), "DENIED", "Product", "General", "piece", 1, TrackingMode.Quantity, MinimumPrice: .0001m)));
    }
    [Fact] public async Task WrongPasswordsLockTheLocalAccount()
    {
        await CreateAndSignIn();
        for (var i = 0; i < 5; i++) await Assert.ThrowsAsync<BusinessException>(() => Accounts.SignIn("owner@example.test", "wrong"));
        await Assert.ThrowsAsync<BusinessException>(() => Accounts.SignIn("owner@example.test", Password));
    }
    [Fact] public async Task AnotherInstanceCannotInitializeTheSameDatabase()
    {
        using var second = new DesktopStore(directory);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => second.Initialize(services.GetRequiredService<IDbContextFactory<BusinessDb>>()));
        Assert.Contains("already open", error.Message);
    }
    [Fact] public async Task BackupRestoresTradingAndOwnerIntoIndependentDatabase()
    {
        var actor = await CreateAndSignIn();
        var business = services.GetRequiredService<IBusinessService>();
        var product = await business.AddProduct(actor, new(Guid.NewGuid(), "BACKUP", "Backup product", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: .0001m));
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        await business.Post(actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", date, [new(product, 10, 60)], null));
        await business.Post(actor, new(Guid.NewGuid(), DocumentKind.Sale, "Customer", date, [new(product, 2, 100)], PaymentMethod.Cash));
        var backup = await services.GetRequiredService<DesktopBackupService>().Create(actor);
        Assert.True(File.Exists(backup));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(backup)!, "*.partial"));
        // Further live writes must not change the snapshot being restored.
        await business.Post(actor, new(Guid.NewGuid(), DocumentKind.Sale, "Later customer", date, [new(product, 1, 100)], PaymentMethod.Cash));
        var restoredDirectory = Path.Combine(directory, "restored");
        Directory.CreateDirectory(restoredDirectory);
        File.Copy(backup, Path.Combine(restoredDirectory, "sierra.db"));
        var collection = new ServiceCollection();
        collection.AddLogging(); collection.AddSierraDesktop(restoredDirectory);
        await using var restored = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await restored.GetRequiredService<DesktopStore>().Initialize(restored.GetRequiredService<IDbContextFactory<BusinessDb>>());
        var restoredActor = await restored.GetRequiredService<LocalAccountService>().SignIn("owner@example.test", Password);
        var snapshot = await restored.GetRequiredService<IBusinessService>().Read(restoredActor);
        Assert.Equal(8, Assert.Single(snapshot.Stock).Quantity);
        Assert.Equal(480, Assert.Single(snapshot.Stock).Value);
        Assert.Equal(2, snapshot.Documents.Count);
        Assert.Equal(3, snapshot.PendingSync);
        Assert.Equal(snapshot.Ledger.Sum(x => x.Debit), snapshot.Ledger.Sum(x => x.Credit));
        Assert.Equal(7, Assert.Single((await business.Read(actor)).Stock).Quantity);
    }
    [Fact] public async Task BackupRejectsSignedOutAndRevokedPermissionWithoutCreatingFiles()
    {
        var actor = await CreateAndSignIn();
        var backups = services.GetRequiredService<DesktopBackupService>();
        Accounts.SignOut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => backups.Create(actor));
        await Accounts.SignIn("owner@example.test", Password);
        using (var scope = services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var owner = (await users.FindByIdAsync(actor.Id))!;
            var claim = (await users.GetClaimsAsync(owner)).Single(x => x.Type == "permission" && x.Value == "backups.create");
            Assert.True((await users.RemoveClaimAsync(owner, claim)).Succeeded);
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => backups.Create(actor));
        Assert.False(Directory.Exists(Path.Combine(directory, "Backups")));
    }
    [Fact] public async Task WebIdentityValidationReloadsClaimsAndRejectsRevokedStamp()
    {
        var actor = await CreateAndSignIn();
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = (await users.FindByIdAsync(actor.Id))!;
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id),
            new System.Security.Claims.Claim("AspNet.Identity.SecurityStamp", (await users.GetSecurityStampAsync(user))!)
        }, "test"));
        var validator = new IdentityActorValidator(services.GetRequiredService<IServiceScopeFactory>());
        Assert.Contains("sales.create", (await validator.Validate(principal)).Permissions);
        var claim = (await users.GetClaimsAsync(user)).Single(x => x.Type == "permission" && x.Value == "sales.create");
        Assert.True((await users.RemoveClaimAsync(user, claim)).Succeeded);
        Assert.DoesNotContain("sales.create", (await validator.Validate(principal)).Permissions);
        Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => validator.Validate(principal));
    }
    [Fact] public void DesktopSettingsPersistAndRejectInvalidTax()
    {
        var settingsDirectory = Path.Combine(directory, "settings-test");
        var initial = DesktopTradingConfiguration.Load(settingsDirectory);
        Assert.Equal("FREETOWN", initial.Branch);
        var path = Path.Combine(settingsDirectory, "trading-settings.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new TradingOptions { Branch = "BO", Device = "POS02", TaxRate = .1m, ClosedThrough = new(2026, 1, 1) }));
        var loaded = DesktopTradingConfiguration.Load(settingsDirectory);
        Assert.Equal("BO", loaded.Branch); Assert.Equal("POS02", loaded.Device);
        Assert.Equal(.1m, loaded.TaxRate); Assert.Equal(new DateOnly(2026, 1, 1), loaded.ClosedThrough);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new TradingOptions { TaxRate = 2 }));
        Assert.Throws<InvalidOperationException>(() => DesktopTradingConfiguration.Load(settingsDirectory));
    }
    [Fact] public async Task HealthCheckDetectsMissingSchema()
    {
        var factory = services.GetRequiredService<IDbContextFactory<BusinessDb>>();
        var check = new SierraBusiness.Web.DatabaseHealthCheck(factory);
        var context = new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext();
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, (await check.CheckHealthAsync(context)).Status);
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE Products");
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, (await check.CheckHealthAsync(context)).Status);
    }
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

