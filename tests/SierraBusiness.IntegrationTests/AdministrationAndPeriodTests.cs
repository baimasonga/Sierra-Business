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
public sealed class AdministrationAndPeriodTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sierra-admin-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider services = null!;
    private const string Password = "Owner-Test-Password!42";
    private LocalAccountService Accounts => services.GetRequiredService<LocalAccountService>();
    private IUserAdministration Admin => services.GetRequiredService<IUserAdministration>();
    private IDbContextFactory<BusinessDb> Factory => services.GetRequiredService<IDbContextFactory<BusinessDb>>();
    public async Task InitializeAsync()
    {
        var collection = new ServiceCollection(); collection.AddLogging(); collection.AddSierraDesktop(directory); services = collection.BuildServiceProvider();
        await services.GetRequiredService<DesktopStore>().Initialize(Factory); await Accounts.CreateOwner("owner@example.test", Password, Password); await Accounts.SignIn("owner@example.test", Password);
    }
    public async Task DisposeAsync() { await services.DisposeAsync(); SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    [Fact] public async Task UserPermissionsDisablementAndPasswordResetAreAuditedWithoutSecrets()
    {
        var id = await Admin.Create(new("clerk@example.test", "Clerk-Test-Password!42", BusinessPermissions.Presets["Sales clerk"].ToList()));
        await Admin.Update(new(id, true, BusinessPermissions.Presets["Read only"].ToList()));
        Accounts.SignOut(); await Assert.ThrowsAsync<BusinessException>(() => Accounts.SignIn("clerk@example.test", "Clerk-Test-Password!42"));
        await Accounts.SignIn("owner@example.test", Password); await Admin.Update(new(id, false, BusinessPermissions.Presets["Sales clerk"].ToList()));
        await Assert.ThrowsAsync<BusinessException>(() => Admin.ResetPassword(id, "weak"));
        Accounts.SignOut(); await Accounts.SignIn("clerk@example.test", "Clerk-Test-Password!42");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Admin.Read());
        Accounts.SignOut(); var owner = await Accounts.SignIn("owner@example.test", Password);
        await Assert.ThrowsAsync<BusinessException>(() => Admin.Update(new(owner.Id, true, BusinessPermissions.All.ToList())));
        await Admin.ResetPassword(id, "Changed-Test-Password!42");
        await using var db = await Factory.CreateDbContextAsync(); var audit = await db.Operations.Where(x => x.Action.StartsWith("user.")).ToListAsync();
        Assert.DoesNotContain(audit, x => x.Payload.Contains("Password!42") || x.Payload.Contains("weak")); Assert.Contains(audit, x => x.Action == "user.password-reset");
        Accounts.SignOut(); await Accounts.SignIn("clerk@example.test", "Changed-Test-Password!42");
    }
    [Fact] public async Task ClosedPeriodBlocksBackdatedVouchersAndTradingButAllowsIdempotentReplay()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow); var finance = services.GetRequiredService<IFinanceService>(); var periods = services.GetRequiredService<IPeriodService>();
        var voucher = new VoucherCommand(Guid.NewGuid(), VoucherType.Journal, today.AddDays(-5), "Opening capital", [new(1000, 100, 0), new(3000, 0, 100)], []);
        var id = await finance.PostVoucher(voucher); await periods.Close(new(Guid.NewGuid(), today.AddDays(-2), "Month end verified"));
        Assert.Equal(id, await finance.PostVoucher(voucher));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(voucher with { OperationId = Guid.NewGuid() }));
        var business = services.GetRequiredService<IBusinessService>(); var actor = await Accounts.CurrentActor();
        var p = await business.AddProduct(actor, new(Guid.NewGuid(), "P", "Product", "General", "piece", 10, TrackingMode.Quantity, MinimumPrice: .0001m));
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today.AddDays(-3), [new(p, 2, 10)], null)));
        await business.Post(actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(p, 2, 10)], null));
        Assert.Equal(today.AddDays(-2), (await periods.Read()).ClosedThrough);
    }
    [Fact] public async Task EffectiveTaxIsAppliedAndCannotRewritePostedHistory()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow); var periods = services.GetRequiredService<IPeriodService>();
        await periods.SetTax(new(Guid.NewGuid(), today, .12m, "Configured test tax"));
        var actor = await Accounts.CurrentActor(); var business = services.GetRequiredService<IBusinessService>();
        var p = await business.AddProduct(actor, new(Guid.NewGuid(), "P", "Product", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: .0001m));
        await business.Post(actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(p, 2, 50)], null));
        var id = await business.Post(actor, new(Guid.NewGuid(), DocumentKind.Sale, "Buyer", today, [new(p, 1, 100)], PaymentMethod.Cash));
        Assert.Equal(112, (await business.Read(actor)).Documents.Single(x => x.Id == id).Total);
        await Assert.ThrowsAsync<BusinessException>(() => periods.SetTax(new(Guid.NewGuid(), today, .20m, "Invalid historical change")));
        await periods.SetTax(new(Guid.NewGuid(), today.AddDays(1), .20m, "Future rate"));
        Assert.Equal(112, (await business.Read(actor)).Documents.Single(x => x.Id == id).Total);
    }
}

