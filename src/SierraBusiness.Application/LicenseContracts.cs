namespace SierraBusiness.Application;

public enum LicenseState { NotConfigured, NotActivated, Valid, RefreshRecommended, Expired, ClockAnomaly, Invalid, RefreshRequired }
public sealed record LicenseCheck(LicenseState State, DateTimeOffset? ExpiresAt = null, IReadOnlyDictionary<string, bool>? Features = null)
{
    public bool Usable => State is LicenseState.Valid or LicenseState.RefreshRecommended;
    public bool Allows(string feature) => Usable && Features?.GetValueOrDefault(feature) == true;
}

public interface ILicenseService
{
    void Demand(string permission);
    LicenseCheck Current { get; }
    Task<LicenseCheck> Check(CancellationToken cancellationToken = default);
    Task<LicenseCheck> Activate(string email, string password, string licenseKey, string? mfaCode = null, CancellationToken cancellationToken = default);
    Task<LicenseCheck> Refresh(CancellationToken cancellationToken = default);
    Task Deactivate(CancellationToken cancellationToken = default);
}

/// <summary>Paid builds without a usable public configuration preserve records but grant no commercial writes.</summary>
public sealed class UnconfiguredLicenseService : ILicenseService
{
    public LicenseCheck Current => new(LicenseState.NotConfigured);
    public void Demand(string permission)
    {
        if (!permission.EndsWith(".read", StringComparison.Ordinal) && permission is not "backups.create" and not "users.manage")
            throw new UnauthorizedAccessException("The software publisher must configure licensing before commercial actions are available. Saved records remain accessible.");
    }
    public Task<LicenseCheck> Check(CancellationToken cancellationToken = default) => Task.FromResult(Current);
    public Task<LicenseCheck> Activate(string email, string password, string licenseKey, string? mfaCode = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Licensing is not configured.");
    public Task<LicenseCheck> Refresh(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Licensing is not configured.");
    public Task Deactivate(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Licensing is not configured.");
}
