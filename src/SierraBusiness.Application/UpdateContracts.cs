namespace SierraBusiness.Application;

/// <summary>0.26.0: checks a published update feed (JSON: version, url, sha256, notes). The app never downloads or installs by itself:
/// it shows the newer version, where to get it and the SHA-256 to compare before installing.</summary>
public sealed record UpdateInfo(string Current, string? Latest, bool Available, string Url, string Sha256, string Notes, DateTimeOffset? Checked, string Problem, bool Enabled);
public interface IUpdateService
{
    Task<UpdateInfo> Status();
    Task<UpdateInfo> CheckNow();
}
