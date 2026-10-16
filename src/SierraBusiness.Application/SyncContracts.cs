namespace SierraBusiness.Application;

/// <summary>0.25.0 multi-branch synchronisation. Each branch keeps working offline on its own database; a head-office server collects every
/// branch's records and hands out shared masters (catalogue, customers and suppliers, price rules, settings).</summary>
public enum SyncRole { Off, Branch, Central }
public enum SyncOperation { Upsert, Delete }

public sealed record SyncCompatibility(int Protocol, int Schema);
public static class SyncContract
{
    public const string ProtocolHeader = "X-Rokela-Sync-Protocol", SchemaHeader = "X-Rokela-Sync-Schema";
    // Protocol 3 adds currency-unit advances and current-period tax corrections. Protocol 4 adds interest-charge customer bills
    // and the later costing, tax and period records. Physical schema remains 1.
    // Older peers cannot safely interpret these journals and metadata, even though their tables match.
    public static SyncCompatibility Current { get; } = new(4, 1);
    public static bool Supports(SyncCompatibility? value) => value == Current;
    public const string UpgradeMessage = "Sync versions are incompatible. Upgrade head office and every branch to a compatible Rokela Business Admin release. Local changes remain queued.";
}

/// <summary>One captured change. <paramref name="Scope"/> is the branch the row belongs to, or empty for shared masters.</summary>
public sealed record SyncChangeDto(long Seq, Guid Commit, string Entity, string Key, SyncOperation Operation, string Data, string Scope, string Origin);
public sealed record SyncPushRequest(string Branch, IReadOnlyList<SyncChangeDto> Changes);
public sealed record SyncPushResult(long Through, int Applied, int Skipped, int Conflicts);
public sealed record SyncPullResult(IReadOnlyList<SyncChangeDto> Changes, long Through, bool More);

public sealed record SyncPeerView(string Name, long Pushed, long Pulled, DateTimeOffset? LastAttempt, DateTimeOffset? LastSuccess, string LastError);
public sealed record SyncConflictView(Guid Id, DateTimeOffset Time, string Origin, string Entity, string Key, string Reason, bool Reviewed);
public sealed record SyncStatusView(SyncRole Role, string Branch, string Server, int PendingChanges, IReadOnlyList<SyncPeerView> Peers, IReadOnlyList<SyncConflictView> Conflicts)
{
    public SyncPeerView? Central => Peers.FirstOrDefault(x => x.Name == "central");
}
public sealed record SyncRunResult(int Pulled, int Pushed, int Conflicts, string Message);

public interface ISyncService
{
    Task<SyncStatusView> Status();
    /// <summary>Branch: pull from head office, then push local changes. Needs audit.read or users.manage.</summary>
    Task<SyncRunResult> SyncNow();
    Task MarkReviewed(Guid conflictId);
}

/// <summary>How a branch reaches head office (HTTP in production, in-process in tests).</summary>
public interface ISyncTransport
{
    Task<SyncPushResult> Push(SyncPushRequest request, CancellationToken cancellationToken = default);
    Task<SyncPullResult> Pull(string branch, long after, int max, CancellationToken cancellationToken = default);
}

/// <summary>Where this installation finds head office. Changes take effect when the app is restarted.</summary>
public sealed record SyncSettingsView(SyncRole Role, string Server, bool HasKey, bool Editable, string Warning = "");
public interface ISyncSettings
{
    SyncSettingsView Read();
    /// <summary>Blank server switches branch sync off. A null key keeps the saved one.</summary>
    void Save(string server, string? key);
}
