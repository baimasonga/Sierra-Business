using Bunit;
using SierraBusiness.Application;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class LicenseScreenTests
{
    private sealed class License : ILicenseService
    {
        public LicenseCheck Current => new(LicenseState.NotActivated);
        public int Calls;
        public void Demand(string permission) { }
        public Task<LicenseCheck> Check(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task<LicenseCheck> Activate(string email, string password, string licenseKey, string? mfaCode = null, CancellationToken cancellationToken = default)
        { Assert.Equal("owner@example.test", email); Assert.Equal("one-use", password); Assert.Equal("license-key", licenseKey); Calls++; throw new HttpRequestException("provider details must never appear"); }
        public Task<LicenseCheck> Refresh(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task Deactivate(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    [Fact] public void FailedActivationClearsSecretsAndShowsSafeRecoveryMessage()
    {
        using var context = new BunitContext(); var service = new License();
        var component = context.Render<LicenseWorkspace>(p => p.Add(x => x.Service, service));
        component.Find("input[type=email]").Change("owner@example.test");
        component.FindAll("input[type=password]")[0].Change("one-use"); component.FindAll("input[type=password]")[1].Change("license-key");
        component.Find("form").Submit();
        component.WaitForAssertion(() => { Assert.Equal(1, service.Calls); Assert.Contains("Saved records have not been removed", component.Markup); });
        Assert.DoesNotContain("one-use", component.Markup); Assert.DoesNotContain("license-key", component.Markup); Assert.DoesNotContain("provider details", component.Markup);
    }
    [Fact] public void MissingReleaseConfigurationRestrictsWritesAndExplainsRecoveryWithoutLockingRecords()
    {
        var service = new UnconfiguredLicenseService(); service.Demand("business.read"); service.Demand("backups.create");
        Assert.Throws<UnauthorizedAccessException>(() => service.Demand("sales.create"));
        using var context = new BunitContext(); var component = context.Render<LicenseWorkspace>(p => p.Add(x => x.Service, service));
        Assert.Contains("public licensing configuration", component.Markup); Assert.Empty(component.FindAll("form")); Assert.Empty(component.FindAll("button"));
    }
}
