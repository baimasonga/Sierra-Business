using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SierraBusiness.Application;
using SierraBusiness.Desktop;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class LicensedDesktopTests
{
    [Fact] public async Task UnconfiguredPaidDesktopPreservesRecordReadsButRejectsCommercialCommandsIncludingAlternativeRoleChecks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sierra-licensed-desktop-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = new ServiceCollection().AddSierraDesktop(directory).AddSingleton<ILicenseService, UnconfiguredLicenseService>().BuildServiceProvider();
            var factory = services.GetRequiredService<IDbContextFactory<BusinessDb>>();
            await services.GetRequiredService<DesktopStore>().Initialize(factory);
            var accounts = services.GetRequiredService<LocalAccountService>();
            await accounts.CreateOwner("owner@example.test", "Local-Owner-Password-123!", "Local-Owner-Password-123!");
            var actor = await accounts.SignIn("owner@example.test", "Local-Owner-Password-123!");
            var business = services.GetRequiredService<IBusinessService>();
            Assert.Empty((await business.Read(actor)).Products);
            actor.Demand("backups.create");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.AddProduct(actor, new(Guid.NewGuid(), "BLOCKED", "Blocked product", "General", "piece", 10, TrackingMode.Quantity)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<IEquipmentService>().SaveProfile(new(Guid.NewGuid(), Guid.NewGuid(), "SERIAL", 0, [])));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<IServiceDesk>().Log(new(Guid.NewGuid(), "Customer", "Site", "Contact", null, "", "Equipment", "Fault", ServicePriority.Normal, DateOnly.FromDateTime(DateTime.Today))));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<IDocumentLibrary>().Add(new(Guid.NewGuid(), new(LibraryDocumentType.Other, "Blocked upload"), new("fixture.pdf", [1, 2, 3]))));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<IComplianceService>().RegisterPending());
            await using var db = await factory.CreateDbContextAsync();
            Assert.Equal(0, await db.Products.CountAsync()); Assert.Equal(0, await db.Operations.CountAsync());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public void AlternativePermissionsStillNeedBothLocalAuthorityAndTheMatchingLicensedFeature()
    {
        var actor = new Actor("local", "branch", new HashSet<string> { "sales.create", "inventory.receive" })
        { LicenseDemand = permission => { if (permission == "sales.create") throw new UnauthorizedAccessException(); } };
        actor.DemandAny("sales.create", "inventory.receive");
        Assert.Throws<UnauthorizedAccessException>(() => actor.DemandAny("sales.create", "accounting.write"));
    }
}
