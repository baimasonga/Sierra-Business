using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

/// <summary>Configuration section "Sync". A branch sets Role=Branch, Server and Key; head office sets Role=Central and one key per branch.</summary>
public sealed class SyncOptions
{
    public SyncRole Role { get; set; } = SyncRole.Off;
    public string Server { get; set; } = "";
    public string Key { get; set; } = "";
    /// <summary>Head office: branch code → that branch's key.</summary>
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int BatchRows { get; set; } = 500;
}

/// <summary>Outbox: one row per changed record, written in the same save as the change.</summary>
public sealed class SyncChange
{
    public long Seq { get; set; }
    public Guid Commit { get; set; }
    public string Entity { get; set; } = "";
    public string Key { get; set; } = "";
    public SyncOperation Operation { get; set; }
    public string Payload { get; set; } = "";
    public string Scope { get; set; } = "";
    /// <summary>Empty for changes made here; otherwise the branch (or "central") the change came from.</summary>
    public string Origin { get; set; } = "";
    public DateTimeOffset Time { get; set; }
}
/// <summary>Branch: the head-office link ("central"). Head office: one row per branch.</summary>
public sealed class SyncPeer
{
    public string Name { get; set; } = "";
    public long Pushed { get; set; }
    public long Pulled { get; set; }
    public DateTimeOffset? LastAttempt { get; set; }
    public DateTimeOffset? LastSuccess { get; set; }
    public string LastError { get; set; } = "";
}
public sealed class SyncConflict
{
    public Guid Id { get; set; }
    public DateTimeOffset Time { get; set; }
    public string Origin { get; set; } = "";
    public string Entity { get; set; } = "";
    public string Key { get; set; } = "";
    public string Reason { get; set; } = "";
    public string IncomingJson { get; set; } = "";
    public bool Reviewed { get; set; }
}

/// <summary>Captures replicated changes into the outbox inside SaveChanges, in the same transaction.</summary>
/// <param name="branch">This installation's branch code, used as the owner of records that carry no branch of their own (attachment contents).</param>
public sealed class SyncCaptureInterceptor(SyncRole role, string branch) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) { Capture(eventData.Context); return result; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Capture(eventData.Context); return ValueTask.FromResult(result); }
    private void Capture(DbContext? context)
    {
        if (context is not BusinessDb db || role == SyncRole.Off) return;
        var origin = db.SyncApplyingOrigin;
        if (origin is not null && role == SyncRole.Branch) return; // a branch never re-sends what head office sent it
        SyncEngine.Capture(db, origin ?? "", origin ?? branch);
    }
}

public static class SyncEngine
{
    public const string CentralPeer = "central", BackfillMarker = "(backfill)";
    private static readonly HashSet<Type> Excluded = [typeof(IntegritySeal), typeof(SyncChange), typeof(SyncPeer), typeof(SyncConflict), typeof(JobState), typeof(JobRun), typeof(Reminder), typeof(ReminderDismissal), typeof(UpdateStatus)];
    /// <summary>Records without a branch column that still belong to the branch that made them (file contents are not copied to every branch).</summary>
    private static readonly HashSet<Type> BranchPrivate = [typeof(FileBlob)];
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = false };

    public static bool Replicated(IEntityType type) => !type.IsOwned() && type.FindPrimaryKey() is not null && type.ClrType.Namespace == typeof(BusinessDb).Namespace && !Excluded.Contains(type.ClrType);
    private static bool Protected(IEntityType type) => HistoryProtection.ProtectedTypes.Contains(type.ClrType);

    internal static void Capture(BusinessDb db, string origin, string owner)
    {
        db.ChangeTracker.DetectChanges();
        var entries = db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && Replicated(e.Metadata)).ToList();
        if (entries.Count == 0) return;
        var commit = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        foreach (var e in entries) db.SyncChanges.Add(Change(e.Metadata, e.Entity, e.State == EntityState.Deleted ? SyncOperation.Delete : SyncOperation.Upsert, commit, origin, now, owner));
    }

    private static SyncChange Change(IEntityType type, object row, SyncOperation op, Guid commit, string origin, DateTimeOffset now, string owner) => new()
    {
        Commit = commit, Entity = type.ClrType.Name, Key = KeyOf(type, row), Operation = op, Origin = origin, Time = now,
        Payload = op == SyncOperation.Delete ? "" : JsonSerializer.Serialize(row, type.ClrType, Json),
        Scope = type.FindProperty("Branch") is { } p && p.ClrType == typeof(string) ? (string?)p.GetGetter().GetClrValue(row) ?? "" : BranchPrivate.Contains(type.ClrType) ? owner.ToUpperInvariant() : ""
    };
    private static string KeyOf(IEntityType type, object row) => JsonSerializer.Serialize(type.FindPrimaryKey()!.Properties.Select(p => p.GetGetter().GetClrValue(row)).ToArray());
    private static object?[] ParseKey(IEntityType type, string key)
    {
        var values = JsonSerializer.Deserialize<JsonElement[]>(key) ?? [];
        var props = type.FindPrimaryKey()!.Properties;
        if (values.Length != props.Count) throw new FormatException("Key does not match the record type.");
        return props.Select((p, i) => values[i].Deserialize(p.ClrType)).ToArray();
    }
    private static int? VersionOf(IEntityType type, object row) => type.FindProperty("Version") is { } p && p.ClrType == typeof(int) ? (int?)p.GetGetter().GetClrValue(row) : null;
    public static SyncChangeDto Dto(SyncChange c, string centralName) => new(c.Seq, c.Commit, c.Entity, c.Key, c.Operation, c.Payload, c.Scope, c.Origin.Length == 0 ? centralName : c.Origin);
    internal static string Clean(string message) { message = Regex.Replace(message ?? "", "(?i)(key|token|password|bearer)[=: ]+\\S+", "$1=***"); return message.Length > 200 ? message[..200] : message; }

    /// <summary>Applies changes. The whole batch is tried in one transaction first (fast); if anything in it cannot be saved, it is applied
    /// commit by commit so that a commit that cannot be applied is recorded as a conflict and the rest carry on.</summary>
    internal static async Task<(int Applied, int Skipped, int Conflicts)> Apply(IDbContextFactory<BusinessDb> factory, IReadOnlyList<SyncChangeDto> changes, string origin, bool atCentral, ISet<string>? pendingLocal, CancellationToken ct)
    {
        var commits = changes.GroupBy(x => x.Commit).OrderBy(g => g.Min(x => x.Seq)).ToList();
        if (commits.Count == 0) return (0, 0, 0);
        if (commits.Count > 1)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            db.SyncApplyingOrigin = origin;
            var conflicts = new List<SyncConflict>(); int applied = 0, skipped = 0;
            var known = await Prefetch(db, changes, ct);
            foreach (var commit in commits) { var r = await Stage(db, commit, origin, atCentral, pendingLocal, conflicts, known, ct); applied += r.Applied; skipped += r.Skipped; }
            try
            {
                db.SyncConflicts.AddRange(conflicts);
                await db.SaveChangesAsync(ct);
                return (applied, skipped, conflicts.Count);
            }
            catch (Exception ex) when (ex is DbUpdateException or BusinessException or InvalidOperationException) { } // fall back to one commit at a time
        }
        int total = 0, skippedTotal = 0; var recorded = new List<SyncConflict>();
        foreach (var commit in commits)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            db.SyncApplyingOrigin = origin;
            var conflicts = new List<SyncConflict>();
            var r = await Stage(db, commit, origin, atCentral, pendingLocal, conflicts, await Prefetch(db, commit.ToList(), ct), ct);
            try { await db.SaveChangesAsync(ct); total += r.Applied; skippedTotal += r.Skipped; recorded.AddRange(conflicts); }
            catch (Exception ex) when (ex is DbUpdateException or BusinessException or InvalidOperationException)
            {
                var first = commit.OrderBy(x => x.Seq).First();
                recorded.Add(NewConflict(first with { Data = JsonSerializer.Serialize(commit.Select(x => new { x.Entity, x.Key })) }, origin, "Could not be applied: " + Clean((ex.InnerException ?? ex).Message)));
            }
        }
        if (recorded.Count > 0) { await using var db = await factory.CreateDbContextAsync(ct); db.SyncConflicts.AddRange(recorded); await db.SaveChangesAsync(ct); }
        return (total, skippedTotal, recorded.Count);
    }

    private static SyncConflict NewConflict(SyncChangeDto c, string origin, string reason) => new() { Id = Guid.NewGuid(), Time = DateTimeOffset.UtcNow, Origin = origin, Entity = c.Entity, Key = c.Key.Length > 200 ? c.Key[..200] : c.Key, Reason = reason.Length > 200 ? reason[..200] : reason, IncomingJson = c.Data };

    /// <summary>Stages one commit's changes in the context; conflicts that need no save are added to <paramref name="conflicts"/>.</summary>
    private static async Task<(int Applied, int Skipped)> Stage(BusinessDb db, IGrouping<Guid, SyncChangeDto> commit, string origin, bool atCentral, ISet<string>? pendingLocal, List<SyncConflict> conflicts, Dictionary<string, HashSet<string>> known, CancellationToken ct)
    {
        int applied = 0, skipped = 0;
        void Conflict(SyncChangeDto c, string reason) => conflicts.Add(NewConflict(c, origin, reason));
        var types = TypesFor(db);
        foreach (var c in commit.OrderBy(x => x.Seq))
        {
            if (!types.TryGetValue(c.Entity, out var type)) { Conflict(c, "Unknown record type: update this installation."); continue; }
            if (atCentral && c.Scope.Length > 0 && !string.Equals(c.Scope, origin, StringComparison.OrdinalIgnoreCase)) { Conflict(c, $"A branch may only send its own records (this one belongs to {c.Scope})."); continue; }
            object?[] key; try { key = ParseKey(type, c.Key); } catch (Exception) { Conflict(c, "Unreadable record key."); continue; }
            // Rows already loaded by Prefetch are found in memory; keys it proved absent need no query.
            var existing = known.TryGetValue(c.Entity, out var present) && !present.Contains(c.Key) ? db.ChangeTracker.Entries().FirstOrDefault(e => e.Metadata == type && KeyOf(type, e.Entity) == c.Key)?.Entity : await db.FindAsync(type.ClrType, key, ct);
            if (c.Operation == SyncOperation.Delete)
            {
                if (existing is null) skipped++;
                else if (Protected(type)) Conflict(c, "Posted history cannot be deleted; kept this copy.");
                else { db.Remove(existing); applied++; }
                continue;
            }
            object incoming;
            try { incoming = JsonSerializer.Deserialize(c.Data, type.ClrType, Json)!; } catch (Exception) { Conflict(c, "Unreadable record data."); continue; }
            if (existing is null) { db.Add(incoming); applied++; continue; }
            if (HistoryProtection.RowFingerprint(type, existing) == HistoryProtection.RowFingerprint(type, incoming)) { skipped++; continue; }
            if (Protected(type)) { Conflict(c, "Posted record differs from the copy here; kept this copy."); continue; }
            if (atCentral && VersionOf(type, existing) is int mine && VersionOf(type, incoming) is int theirs && theirs <= mine)
            { Conflict(c, $"Changed at head office too (version {mine}); kept head office's. The branch receives it on its next sync."); continue; }
            if (!atCentral && pendingLocal?.Contains(c.Entity + c.Key) == true) Conflict(c, "Changed here and at head office; head office's version replaced the change made here.");
            db.Entry(existing).CurrentValues.SetValues(incoming); applied++;
        }
        return (applied, skipped);
    }
    /// <summary>Loads, in a few queries, the rows a batch touches that already exist (single-column keys), so applying needs no query per row.</summary>
    private static async Task<Dictionary<string, HashSet<string>>> Prefetch(BusinessDb db, IReadOnlyList<SyncChangeDto> changes, CancellationToken ct)
    {
        var result = new Dictionary<string, HashSet<string>>();
        var types = TypesFor(db);
        foreach (var group in changes.GroupBy(x => x.Entity))
        {
            if (!types.TryGetValue(group.Key, out var type) || type.FindPrimaryKey()!.Properties is not [var pk] || pk.PropertyInfo is null) continue;
            var present = new HashSet<string>(StringComparer.Ordinal);
            var keys = new List<object?>();
            foreach (var c in group) { try { keys.Add(ParseKey(type, c.Key)[0]); } catch (Exception) { } }
            foreach (var chunk in keys.Distinct().Chunk(400))
            {
                var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(pk.ClrType))!;
                foreach (var k in chunk) list.Add(k);
                var row = System.Linq.Expressions.Expression.Parameter(type.ClrType, "x");
                var body = System.Linq.Expressions.Expression.Call(System.Linq.Expressions.Expression.Constant(list), list.GetType().GetMethod("Contains")!, System.Linq.Expressions.Expression.Property(row, pk.PropertyInfo));
                var set = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(type.ClrType).Invoke(db, null)!;
                var where = typeof(Queryable).GetMethods().First(m => m.Name == nameof(Queryable.Where) && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericArguments().Length == 2)
                    .MakeGenericMethod(type.ClrType).Invoke(null, [set, System.Linq.Expressions.Expression.Lambda(body, row)])!;
                var task = (Task)typeof(EntityFrameworkQueryableExtensions).GetMethod(nameof(EntityFrameworkQueryableExtensions.ToListAsync))!.MakeGenericMethod(type.ClrType).Invoke(null, [where, ct])!;
                await task;
                foreach (var found in (System.Collections.IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!) present.Add(KeyOf(type, found));
            }
            result[group.Key] = present;
        }
        return result;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IModel, Dictionary<string, IEntityType>> TypeCache = new();
    private static Dictionary<string, IEntityType> TypesFor(BusinessDb db) => TypeCache.GetValue(db.Model, m => m.GetEntityTypes().Where(Replicated).ToDictionary(x => x.ClrType.Name));

    /// <summary>Puts every existing replicated record in the outbox once, parents before children, so records made before sync was switched on travel too.</summary>
    public static async Task<int> Backfill(IDbContextFactory<BusinessDb> factory, string branch, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.SyncPeers.AnyAsync(x => x.Name == BackfillMarker, ct)) return 0;
        var types = db.Model.GetEntityTypes().Where(Replicated).ToList();
        var ordered = new List<IEntityType>(); var seen = new HashSet<IEntityType>();
        void Visit(IEntityType t, HashSet<IEntityType> path)
        {
            if (seen.Contains(t) || !path.Add(t)) return;
            foreach (var fk in t.GetForeignKeys()) if (fk.PrincipalEntityType != t && types.Contains(fk.PrincipalEntityType)) Visit(fk.PrincipalEntityType, path);
            seen.Add(t); ordered.Add(t);
        }
        foreach (var t in types.OrderBy(x => x.ClrType.Name, StringComparer.Ordinal)) Visit(t, []);
        var now = DateTimeOffset.UtcNow; var total = 0;
        foreach (var type in ordered)
        {
            var rows = await Rows(db, type, ct);
            var selfReferencing = type.GetForeignKeys().Any(fk => fk.PrincipalEntityType == type);
            foreach (var chunk in selfReferencing ? [rows] : rows.Chunk(500).Select(x => x.ToList()))
            {
                if (chunk.Count == 0) continue;
                var commit = Guid.NewGuid();
                foreach (var row in chunk) db.SyncChanges.Add(Change(type, row, SyncOperation.Upsert, commit, "", now, branch));
                await db.SaveChangesAsync(ct); db.ChangeTracker.Clear(); total += chunk.Count;
            }
        }
        db.SyncPeers.Add(new() { Name = BackfillMarker, LastSuccess = now, LastError = $"{total} existing records queued" });
        await db.SaveChangesAsync(ct);
        return total;
    }

    private static async Task<List<object>> Rows(BusinessDb db, IEntityType type, CancellationToken ct)
    {
        var set = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(type.ClrType).Invoke(db, null)!;
        var noTracking = typeof(EntityFrameworkQueryableExtensions).GetMethod(nameof(EntityFrameworkQueryableExtensions.AsNoTracking))!.MakeGenericMethod(type.ClrType).Invoke(null, [set])!;
        var task = (Task)typeof(EntityFrameworkQueryableExtensions).GetMethod(nameof(EntityFrameworkQueryableExtensions.ToListAsync))!.MakeGenericMethod(type.ClrType).Invoke(null, [noTracking, ct])!;
        await task;
        return ((System.Collections.IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!).Cast<object>().ToList();
    }

    /// <summary>Whole commits after <paramref name="after"/>, at least <paramref name="rows"/> changes when available.</summary>
    internal static async Task<(List<SyncChange> Changes, bool More)> Batch(IQueryable<SyncChange> source, long after, int rows, CancellationToken ct)
    {
        var page = await source.Where(x => x.Seq > after).OrderBy(x => x.Seq).Take(rows + 1).ToListAsync(ct);
        var more = page.Count > rows;
        if (!more) return (page, false);
        page.RemoveAt(page.Count - 1);
        var lastCommit = page[^1].Commit; var last = page[^1].Seq;
        page.AddRange(await source.Where(x => x.Commit == lastCommit && x.Seq > last).OrderBy(x => x.Seq).ToListAsync(ct)); // never split a commit
        var through = page[^1].Seq;
        return (page, await source.AnyAsync(x => x.Seq > through, ct));
    }
}

/// <summary>Head office: receives each branch's changes and serves shared masters plus the branch's own records.</summary>
public sealed class SyncServer(IDbContextFactory<BusinessDb> factory, IOptions<SyncOptions> sync, IOptions<TradingOptions> trading)
{
    public static bool ValidBranch(string? branch) => branch is { Length: > 0 and <= 60 } && Regex.IsMatch(branch, "^[A-Za-z0-9_-]+$");
    public bool Authorized(string? branch, string? key)
    {
        if (sync.Value.Role != SyncRole.Central || !ValidBranch(branch) || string.IsNullOrEmpty(key) || string.Equals(branch, trading.Value.Branch, StringComparison.OrdinalIgnoreCase)) return false;
        if (!sync.Value.Keys.TryGetValue(branch!, out var expected) || expected.Length < 16) return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }

    private async Task<SyncPeer> Peer(BusinessDb db, string branch, CancellationToken ct)
    {
        var name = branch.ToUpperInvariant();
        var peer = await db.SyncPeers.SingleOrDefaultAsync(x => x.Name == name, ct);
        if (peer is null) { peer = new() { Name = name }; db.SyncPeers.Add(peer); }
        return peer;
    }

    public async Task<SyncPushResult> Push(SyncPushRequest request, CancellationToken ct = default)
    {
        var branch = request.Branch.ToUpperInvariant();
        await SyncEngine.Backfill(factory, trading.Value.Branch, ct);
        long already; await using (var db = await factory.CreateDbContextAsync(ct)) { var p = await Peer(db, branch, ct); p.LastAttempt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); already = p.Pushed; }
        var fresh = request.Changes.Where(x => x.Seq > already).OrderBy(x => x.Seq).ToList();
        var (applied, skipped, conflicts) = await SyncEngine.Apply(factory, fresh, branch, atCentral: true, null, ct);
        var through = Math.Max(already, request.Changes.Count == 0 ? already : request.Changes.Max(x => x.Seq));
        await using (var db = await factory.CreateDbContextAsync(ct)) { var p = await Peer(db, branch, ct); p.Pushed = through; p.LastSuccess = DateTimeOffset.UtcNow; p.LastError = conflicts > 0 ? $"{conflicts} conflict(s) in the last push" : ""; await db.SaveChangesAsync(ct); }
        return new(through, applied, skipped + (request.Changes.Count - fresh.Count), conflicts);
    }

    public async Task<SyncPullResult> Pull(string branch, long after, int max, CancellationToken ct = default)
    {
        branch = branch.ToUpperInvariant(); max = Math.Clamp(max, 1, 5000);
        await SyncEngine.Backfill(factory, trading.Value.Branch, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var source = db.SyncChanges.AsNoTracking().Where(x => x.Origin.ToUpper() != branch && (x.Scope == "" || x.Scope.ToUpper() == branch));
        var (changes, more) = await SyncEngine.Batch(source, after, max, ct);
        var through = changes.Count == 0 ? after : changes[^1].Seq;
        var peer = await Peer(db, branch, ct); peer.Pulled = through; peer.LastAttempt = peer.LastSuccess = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new(changes.Select(x => SyncEngine.Dto(x, trading.Value.Branch)).ToList(), through, more);
    }
}

/// <summary>Branch side over HTTPS: POST /api/sync/push, GET /api/sync/pull. The branch code and key travel in headers, never in the address.</summary>
public sealed class HttpSyncTransport(HttpClient http, string branch, string key) : ISyncTransport
{
    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Add("X-Sierra-Branch", branch);
        message.Headers.Authorization = new("Bearer", key);
        return message;
    }
    public async Task<SyncPushResult> Push(SyncPushRequest request, CancellationToken ct = default)
    {
        using var message = Request(HttpMethod.Post, "api/sync/push"); message.Content = JsonContent.Create(request);
        using var response = await http.SendAsync(message, ct); await Check(response);
        return (await response.Content.ReadFromJsonAsync<SyncPushResult>(ct))!;
    }
    public async Task<SyncPullResult> Pull(string branchCode, long after, int max, CancellationToken ct = default)
    {
        using var message = Request(HttpMethod.Get, $"api/sync/pull?after={after}&max={max}");
        using var response = await http.SendAsync(message, ct); await Check(response);
        return (await response.Content.ReadFromJsonAsync<SyncPullResult>(ct))!;
    }
    private static async Task Check(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) throw new PermanentJobException("Head office refused this branch's sync key. Check the branch code and key in the sync settings.");
        throw new HttpRequestException($"Head office returned {(int)response.StatusCode}: {SyncEngine.Clean(await response.Content.ReadAsStringAsync())}");
    }
    public static ISyncTransport Create(SyncOptions options, string branch)
    {
        if (!Uri.TryCreate(options.Server.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback) throw new PermanentJobException("Set the head-office address (https://…) in the sync settings.");
        if (options.Key.Length < 16) throw new PermanentJobException("Set this branch's sync key (at least 16 characters) in the sync settings.");
        return new HttpSyncTransport(new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromMinutes(2) }, branch, options.Key);
    }
}

public sealed class SyncService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> trading, IOptions<SyncOptions> sync, IServiceProvider services) : ISyncService
{
    private async Task<Actor> Authorize()
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        if (!actor.Permissions.Contains("audit.read") && !actor.Permissions.Contains("users.manage")) throw new UnauthorizedAccessException("Branch sync needs the audit or user-administration permission.");
        if (actor.Branch != trading.Value.Branch) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }

    public async Task<SyncStatusView> Status()
    {
        await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var peers = await db.SyncPeers.AsNoTracking().Where(x => x.Name != SyncEngine.BackfillMarker).OrderBy(x => x.Name).ToListAsync();
        var pushed = peers.FirstOrDefault(x => x.Name == SyncEngine.CentralPeer)?.Pushed ?? 0;
        var pending = sync.Value.Role == SyncRole.Branch ? await db.SyncChanges.CountAsync(x => x.Origin == "" && x.Seq > pushed) : 0;
        var conflicts = (await db.SyncConflicts.AsNoTracking().ToListAsync()).OrderByDescending(x => x.Time).Take(200)
            .Select(x => new SyncConflictView(x.Id, x.Time, x.Origin, x.Entity, x.Key, x.Reason, x.Reviewed)).ToList();
        return new(sync.Value.Role, trading.Value.Branch, sync.Value.Server, pending, peers.Select(x => new SyncPeerView(x.Name, x.Pushed, x.Pulled, x.LastAttempt, x.LastSuccess, x.LastError)).ToList(), conflicts);
    }

    public async Task<SyncRunResult> SyncNow()
    {
        await Authorize();
        if (sync.Value.Role != SyncRole.Branch) throw new BusinessException("This installation is not set up as a branch. Head office receives changes when branches sync.");
        try { return await Run(factory, trading.Value, sync.Value, services.GetService<ISyncTransport>() ?? HttpSyncTransport.Create(sync.Value, trading.Value.Branch), default); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or PermanentJobException) { throw new BusinessException("Sync did not finish: " + SyncEngine.Clean(ex.Message) + " Changes stay queued and are sent next time."); }
    }

    public async Task MarkReviewed(Guid conflictId)
    {
        await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var row = await db.SyncConflicts.SingleOrDefaultAsync(x => x.Id == conflictId) ?? throw new BusinessException("Conflict not found.");
        row.Reviewed = true; await db.SaveChangesAsync();
    }

    /// <summary>Pull first (so a new branch starts from head office's masters), then push. Safe to repeat after any failure.</summary>
    public static async Task<SyncRunResult> Run(IDbContextFactory<BusinessDb> factory, TradingOptions trading, SyncOptions sync, ISyncTransport transport, CancellationToken ct)
    {
        await SyncEngine.Backfill(factory, trading.Branch, ct);
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var peer = await db.SyncPeers.SingleOrDefaultAsync(x => x.Name == SyncEngine.CentralPeer, ct);
            if (peer is null) { peer = new() { Name = SyncEngine.CentralPeer }; db.SyncPeers.Add(peer); }
            peer.LastAttempt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
        }
        int pulled = 0, pushed = 0, conflicts = 0;
        try
        {
            while (true)
            {
                long after; HashSet<string> pending;
                await using (var db = await factory.CreateDbContextAsync(ct))
                {
                    var peer = await db.SyncPeers.AsNoTracking().SingleAsync(x => x.Name == SyncEngine.CentralPeer, ct); after = peer.Pulled;
                    pending = (await db.SyncChanges.AsNoTracking().Where(x => x.Origin == "" && x.Seq > peer.Pushed).Select(x => x.Entity + x.Key).ToListAsync(ct)).ToHashSet();
                }
                var page = await transport.Pull(trading.Branch, after, sync.BatchRows, ct);
                var result = await SyncEngine.Apply(factory, page.Changes, SyncEngine.CentralPeer, atCentral: false, pending, ct);
                pulled += result.Applied; conflicts += result.Conflicts;
                await using (var db = await factory.CreateDbContextAsync(ct)) { var peer = await db.SyncPeers.SingleAsync(x => x.Name == SyncEngine.CentralPeer, ct); peer.Pulled = Math.Max(peer.Pulled, page.Through); await db.SaveChangesAsync(ct); }
                if (!page.More) break;
            }
            while (true)
            {
                List<SyncChange> batch;
                await using (var db = await factory.CreateDbContextAsync(ct))
                {
                    var peer = await db.SyncPeers.AsNoTracking().SingleAsync(x => x.Name == SyncEngine.CentralPeer, ct);
                    batch = (await SyncEngine.Batch(db.SyncChanges.AsNoTracking().Where(x => x.Origin == ""), peer.Pushed, sync.BatchRows, ct)).Changes;
                }
                if (batch.Count == 0) break;
                var result = await transport.Push(new(trading.Branch, batch.Select(x => SyncEngine.Dto(x, trading.Branch)).ToList()), ct);
                pushed += batch.Count; conflicts += result.Conflicts;
                await using (var db = await factory.CreateDbContextAsync(ct))
                {
                    var peer = await db.SyncPeers.SingleAsync(x => x.Name == SyncEngine.CentralPeer, ct); peer.Pushed = Math.Max(peer.Pushed, result.Through); await db.SaveChangesAsync(ct);
                    var keep = peer.Pushed; // the newest row stays so sequence numbers keep rising
                    await db.SyncChanges.Where(x => x.Seq < keep).ExecuteDeleteAsync(ct);
                }
            }
            await using (var db = await factory.CreateDbContextAsync(ct))
            {
                var peer = await db.SyncPeers.SingleAsync(x => x.Name == SyncEngine.CentralPeer, ct); peer.LastSuccess = DateTimeOffset.UtcNow; peer.LastError = conflicts > 0 ? $"{conflicts} conflict(s) to review" : ""; await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await using var db = await factory.CreateDbContextAsync(CancellationToken.None);
            var peer = await db.SyncPeers.SingleAsync(x => x.Name == SyncEngine.CentralPeer, CancellationToken.None); peer.LastError = SyncEngine.Clean(ex.Message); await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        return new(pulled, pushed, conflicts, $"Received {pulled}, sent {pushed} change(s)" + (conflicts > 0 ? $"; {conflicts} conflict(s) to review." : "."));
    }
}

/// <summary>Branch: sync with head office every five minutes while the app runs. Failures retry with the scheduler's backoff; nothing is lost.</summary>
public sealed class SyncJob : IBackgroundJob
{
    public string Name => "sync"; public string Description => "Branch sync with head office"; public TimeSpan Interval => TimeSpan.FromMinutes(5);
    public async Task<string> Run(JobContext c, CancellationToken ct)
    {
        var sync = c.Services.GetService<IOptions<SyncOptions>>()?.Value ?? new();
        if (sync.Role != SyncRole.Branch) return "Branch sync is off.";
        var transport = c.Services.GetService<ISyncTransport>() ?? HttpSyncTransport.Create(sync, c.Branch);
        return (await SyncService.Run(c.Factory, c.Options.Value, sync, transport, ct)).Message;
    }
}
