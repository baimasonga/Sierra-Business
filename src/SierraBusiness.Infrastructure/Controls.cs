using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure.Imports;
namespace SierraBusiness.Infrastructure;

// Append-only records (database guards and integrity seals).
public sealed class ControlPolicyVersion { public Guid Id { get; set; } public string Branch { get; set; } = ""; public int Version { get; set; } public string PolicyJson { get; set; } = "{}"; public string CreatedBy { get; set; } = ""; public DateTimeOffset Time { get; set; } }
public sealed class ApprovalRequest
{
    public Guid Id { get; set; } public string Branch { get; set; } = ""; public string Number { get; set; } = ""; public string Action { get; set; } = ""; public string Summary { get; set; } = "";
    public string PayloadJson { get; set; } = "{}"; public string PayloadHash { get; set; } = ""; public string TriggersJson { get; set; } = "[]"; public string RequestedBy { get; set; } = ""; public DateTimeOffset RequestedAt { get; set; }
}
public sealed class ApprovalDecision { public Guid Id { get; set; } public Guid RequestId { get; set; } public ApprovalState State { get; set; } public string Note { get; set; } = ""; public bool SelfApproved { get; set; } public string Actor { get; set; } = ""; public DateTimeOffset Time { get; set; } }
public sealed class ApprovalUse { public Guid Id { get; set; } public Guid RequestId { get; set; } public Guid OperationId { get; set; } public string Actor { get; set; } = ""; public DateTimeOffset Time { get; set; } }
public sealed class ApprovalNote { public Guid Id { get; set; } public Guid RequestId { get; set; } public string Note { get; set; } = ""; public string Actor { get; set; } = ""; public DateTimeOffset Time { get; set; } }

/// <summary>Business controls applied inside posting transactions. A posting that trips a control is refused and an approval request is saved; once approved, the identical posting goes through once.</summary>
public static class Controls
{
    public const string Trade = "trade.post", Reverse = "document.reverse", Count = "stock.count", Voucher = "voucher.post", Return = "return.post";
    internal static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly JsonSerializerOptions Json = new();

    public static async Task<ControlPolicy> Current(BusinessDb db, string branch)
    {
        var row = await db.ControlPolicies.AsNoTracking().Where(x => x.Branch == branch).OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        return row is null ? new() : JsonSerializer.Deserialize<ControlPolicy>(row.PolicyJson)! with { Version = row.Version };
    }
    public static ApprovalTrigger? Backdated(ControlPolicy policy, DateOnly date) =>
        policy.BackdateDays is int days && date < Today.AddDays(-days) ? new("Backdated", $"dated {date:dd MMM yyyy}, {Today.DayNumber - date.DayNumber} days ago (limit {days} days)", "accounting.write") : null;

    internal sealed class ApprovalNeeded(string action, string payload, string hash, string summary, List<ApprovalTrigger> triggers) : Exception("Approval needed.")
    {
        public string Action { get; } = action; public string Payload { get; } = payload; public string Hash { get; } = hash; public string Summary { get; } = summary; public List<ApprovalTrigger> Triggers { get; } = triggers;
    }
    private static string Hash(string action, string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(action + "\n" + payload)));

    /// <summary>Lets the posting continue when nothing tripped or a matching approval exists (consuming it); otherwise stops it for approval.</summary>
    public static async Task Gate<T>(BusinessDb db, Actor actor, string action, T payloadWithoutApproval, Guid? approvalId, Guid operationId, IReadOnlyCollection<ApprovalTrigger> triggers, string summary)
    {
        if (triggers.Count == 0) return;
        var payload = JsonSerializer.Serialize(payloadWithoutApproval, Json); var hash = Hash(action, payload);
        ApprovalRequest? request;
        if (approvalId is Guid id)
        {
            request = await db.ApprovalRequests.SingleOrDefaultAsync(x => x.Id == id && x.Branch == actor.Branch) ?? throw new BusinessException("Approval request not found.");
            if (request.PayloadHash != hash || request.Action != action) throw new BusinessException($"Approval {request.Number} was given for different details. Request approval for this version.");
        }
        else
            request = await db.ApprovalRequests.Where(x => x.Branch == actor.Branch && x.PayloadHash == hash && x.RequestedBy == actor.Id && db.ApprovalDecisions.Any(d => d.RequestId == x.Id && d.State == ApprovalState.Approved) && !db.ApprovalUses.Any(u => u.RequestId == x.Id)).FirstOrDefaultAsync();
        if (request is null) throw new ApprovalNeeded(action, payload, hash, summary, [.. triggers]);
        var decision = await db.ApprovalDecisions.SingleOrDefaultAsync(x => x.RequestId == request.Id);
        if (decision?.State != ApprovalState.Approved) throw new BusinessException(decision is null ? $"Approval {request.Number} is still waiting for a decision." : $"Approval {request.Number} was {decision.State.ToString().ToLowerInvariant()}.");
        if (await db.ApprovalUses.AnyAsync(x => x.RequestId == request.Id)) throw new BusinessException($"Approval {request.Number} has already been used.");
        var approved = JsonSerializer.Deserialize<List<ApprovalTrigger>>(request.TriggersJson)!.Select(x => x.Rule).ToHashSet();
        var unapproved = triggers.Where(x => !approved.Contains(x.Rule)).ToList();
        if (unapproved.Count > 0) throw new BusinessException($"Conditions changed since approval {request.Number}: {string.Join("; ", unapproved.Select(x => x.Detail))}. Request approval again.");
        db.ApprovalUses.Add(new() { Id = Guid.NewGuid(), RequestId = request.Id, OperationId = operationId, Actor = actor.Id, Time = DateTimeOffset.UtcNow });
    }

    /// <summary>Runs a posting; if a control stopped it, saves (or reuses) the approval request outside the rolled-back transaction and reports it.</summary>
    public static async Task<Guid> Guard(IDbContextFactory<BusinessDb> factory, Actor actor, Func<Task<Guid>> run)
    {
        try { return await run(); }
        catch (ApprovalNeeded needed)
        {
            await using var db = await factory.CreateDbContextAsync();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var existing = await db.ApprovalRequests.Where(x => x.Branch == actor.Branch && x.PayloadHash == needed.Hash && x.RequestedBy == actor.Id && !db.ApprovalDecisions.Any(d => d.RequestId == x.Id)).FirstOrDefaultAsync();
            var request = existing ?? new ApprovalRequest
            {
                Id = Guid.NewGuid(), Branch = actor.Branch, Number = "APR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), Action = needed.Action, Summary = needed.Summary,
                PayloadJson = needed.Payload, PayloadHash = needed.Hash, TriggersJson = JsonSerializer.Serialize(needed.Triggers), RequestedBy = actor.Id, RequestedAt = DateTimeOffset.UtcNow
            };
            if (existing is null) { db.ApprovalRequests.Add(request); await db.SaveChangesAsync(); }
            await tx.CommitAsync();
            throw new ApprovalRequiredException(request.Id, request.Number,
                $"Approval needed: {string.Join("; ", needed.Triggers.Select(x => x.Detail))}. Request {request.Number} {(existing is null ? "has been sent" : "is already waiting")} for an approver. Once approved, post it again here or from Administration → Approvals.");
        }
    }

    /// <summary>Whether someone other than the requester, active in this branch, holds every permission needed.</summary>
    internal static async Task<bool> OtherApprover(BusinessDb db, string branch, string requester, IReadOnlyCollection<string> permissions)
    {
        var users = await db.UserClaims.Where(x => x.ClaimType == "branch" && x.ClaimValue == branch && x.UserId != requester).Select(x => x.UserId).ToListAsync();
        if (users.Count == 0) return false;
        var claims = await db.UserClaims.Where(x => users.Contains(x.UserId) && x.ClaimType == "permission").ToListAsync();
        var now = DateTimeOffset.UtcNow;
        var active = (await db.Users.Where(x => users.Contains(x.Id)).ToListAsync()).Where(x => x.LockoutEnd is null || x.LockoutEnd <= now).Select(x => x.Id);
        return active.Any(id => permissions.All(p => claims.Any(c => c.UserId == id && c.ClaimValue == p)));
    }
}

public sealed class ControlService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IControlService
{
    private async Task<Actor> Authorize(params string[] permissions)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        foreach (var p in permissions) actor.Demand(p);
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }
    private static string Short(string? number) { var parts = (number ?? "").Split('-'); return parts.Length >= 3 && parts[^1].Length > 8 ? $"{parts[^2]}-{parts[^1][^8..].ToUpperInvariant()}" : number ?? ""; }

    // ---------------- policy ----------------

    public async Task<ControlPolicy> Policy() { var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync(); return await Controls.Current(db, actor.Branch); }

    public async Task<Guid> SavePolicy(SaveControlPolicy input)
    {
        var actor = await Authorize("users.manage");
        return await Commands.Run(factory, actor, input.OperationId, "controls.policy", input, async db =>
        {
            var p = input.Policy ?? throw new BusinessException("Enter the control settings.");
            if (p.MaxDiscountPercent is < 0 or > 100) throw new BusinessException("The discount limit is a percentage from 0 to 100.");
            if (p.BackdateDays is < 0 or > 365) throw new BusinessException("The back-dating limit is 0 to 365 days.");
            if (p.ReversalAbove < 0 || p.StockAdjustmentAbove < 0 || p.RefundAbove < 0) throw new BusinessException("Approval thresholds cannot be negative.");
            if (!TimeOnly.TryParseExact(p.WorkStart, "HH:mm", out var start) || !TimeOnly.TryParseExact(p.WorkEnd, "HH:mm", out var end) || end <= start) throw new BusinessException("Enter working hours as HH:mm, with the end after the start.");
            var current = await db.ControlPolicies.Where(x => x.Branch == actor.Branch).MaxAsync(x => (int?)x.Version) ?? 0;
            if (current != p.Version) throw new BusinessException("The controls were changed by someone else. Refresh before saving.");
            var row = new ControlPolicyVersion { Id = Guid.NewGuid(), Branch = actor.Branch, Version = current + 1, PolicyJson = JsonSerializer.Serialize(p with { Version = current + 1 }), CreatedBy = actor.Id, Time = DateTimeOffset.UtcNow };
            db.ControlPolicies.Add(row); return row.Id;
        });
    }

    // ---------------- approvals ----------------

    private sealed record State(ApprovalRequest Request, ApprovalDecision? Decision, ApprovalUse? Use, List<ApprovalTrigger> Triggers)
    {
        public ApprovalState Value => Use is not null ? ApprovalState.Used : Decision?.State ?? ApprovalState.Pending;
        public string[] Permissions => Triggers.Select(x => x.Permission).Distinct().ToArray();
    }
    private static async Task<State> Load(BusinessDb db, Actor actor, Guid id)
    {
        var request = await db.ApprovalRequests.SingleOrDefaultAsync(x => x.Id == id && x.Branch == actor.Branch) ?? throw new BusinessException("Approval request not found.");
        return new(request, await db.ApprovalDecisions.SingleOrDefaultAsync(x => x.RequestId == id), await db.ApprovalUses.SingleOrDefaultAsync(x => x.RequestId == id), JsonSerializer.Deserialize<List<ApprovalTrigger>>(request.TriggersJson)!);
    }
    private static async Task<(bool Allowed, bool Self)> MayDecide(BusinessDb db, Actor actor, State s, ControlPolicy policy)
    {
        if (!s.Permissions.All(actor.CanDemand)) return (false, false);
        if (s.Request.RequestedBy != actor.Id) return (true, false);
        return policy.SelfApprovalWhenAlone && !await Controls.OtherApprover(db, actor.Branch, actor.Id, s.Permissions) ? (true, true) : (false, false);
    }

    public async Task<IReadOnlyList<ApprovalView>> Approvals()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var policy = await Controls.Current(db, actor.Branch);
        var requests = await db.ApprovalRequests.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync();
        var ids = requests.Select(x => x.Id).ToList();
        var decisions = await db.ApprovalDecisions.AsNoTracking().Where(x => ids.Contains(x.RequestId)).ToDictionaryAsync(x => x.RequestId);
        var uses = await db.ApprovalUses.AsNoTracking().Where(x => ids.Contains(x.RequestId)).ToDictionaryAsync(x => x.RequestId);
        var notes = (await db.ApprovalNotes.AsNoTracking().Where(x => ids.Contains(x.RequestId)).ToListAsync()).ToLookup(x => x.RequestId);
        var ops = uses.Values.Select(x => x.OperationId).ToList();
        var results = await db.Operations.AsNoTracking().Where(x => ops.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.ResultId);
        var resultIds = results.Values.ToList();
        var numbers = await db.Documents.AsNoTracking().Where(x => resultIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number);
        var names = await db.Users.AsNoTracking().Where(x => x.UserName != null).ToDictionaryAsync(x => x.Id, x => x.UserName!);
        string Name(string id) => names.GetValueOrDefault(id, id);
        var views = new List<ApprovalView>();
        foreach (var r in requests.OrderByDescending(x => x.RequestedAt).Take(300))
        {
            decisions.TryGetValue(r.Id, out var d); uses.TryGetValue(r.Id, out var u);
            var s = new State(r, d, u, JsonSerializer.Deserialize<List<ApprovalTrigger>>(r.TriggersJson)!);
            var (may, _) = s.Value == ApprovalState.Pending ? await MayDecide(db, actor, s, policy) : (false, false);
            var used = u is not null && results.TryGetValue(u.OperationId, out var doc) ? Short(numbers.GetValueOrDefault(doc, "")) : "";
            views.Add(new(r.Id, r.Number, r.Action, r.Summary, s.Triggers, Name(r.RequestedBy), r.RequestedAt, string.Join(" · ", notes[r.Id].OrderBy(x => x.Time).Select(x => $"{Name(x.Actor)}: {x.Note}")),
                s.Value, d is null ? "" : Name(d.Actor), d?.Time, d?.Note ?? "", d?.SelfApproved ?? false, used, r.RequestedBy == actor.Id, may, s.Value == ApprovalState.Approved && r.RequestedBy == actor.Id));
        }
        return views.OrderBy(x => x.State switch { ApprovalState.Pending => 0, ApprovalState.Approved => 1, _ => 2 }).ThenByDescending(x => x.RequestedAt).ToArray();
    }

    public async Task<Guid> Decide(ApprovalDecisionCommand input)
    {
        var actor = await Authorize();
        return await Commands.Run(factory, actor, input.OperationId, "approval.decide", input, async db =>
        {
            var s = await Load(db, actor, input.RequestId);
            if (s.Value != ApprovalState.Pending) throw new BusinessException($"Request {s.Request.Number} has already been decided.");
            var (may, self) = await MayDecide(db, actor, s, await Controls.Current(db, actor.Branch));
            if (!may) throw new UnauthorizedAccessException(s.Request.RequestedBy == actor.Id ? "You cannot approve your own request while another approver is available." : $"Deciding this request needs: {string.Join(", ", s.Permissions)}.");
            var note = (input.Note ?? "").Trim();
            if (note.Length > 200) throw new BusinessException("The note is limited to 200 characters.");
            if (!input.Approve && note.Length == 0) throw new BusinessException("Give the reason for rejecting the request.");
            var row = new ApprovalDecision { Id = Guid.NewGuid(), RequestId = s.Request.Id, State = input.Approve ? ApprovalState.Approved : ApprovalState.Rejected, Note = note, SelfApproved = self, Actor = actor.Id, Time = DateTimeOffset.UtcNow };
            db.ApprovalDecisions.Add(row); return row.Id;
        });
    }

    public async Task<Guid> Withdraw(ApprovalNoteCommand input)
    {
        var actor = await Authorize();
        return await Commands.Run(factory, actor, input.OperationId, "approval.withdraw", input, async db =>
        {
            var s = await Load(db, actor, input.RequestId);
            if (s.Request.RequestedBy != actor.Id) throw new UnauthorizedAccessException("Only the person who asked can withdraw a request.");
            if (s.Value is not (ApprovalState.Pending or ApprovalState.Approved)) throw new BusinessException("This request is already closed.");
            if (s.Decision is not null) throw new BusinessException("An approved request cannot be withdrawn; simply do not post it.");
            var row = new ApprovalDecision { Id = Guid.NewGuid(), RequestId = s.Request.Id, State = ApprovalState.Withdrawn, Note = (input.Note ?? "").Trim(), Actor = actor.Id, Time = DateTimeOffset.UtcNow };
            db.ApprovalDecisions.Add(row); return row.Id;
        });
    }

    public async Task<Guid> AddNote(ApprovalNoteCommand input)
    {
        var actor = await Authorize();
        return await Commands.Run(factory, actor, input.OperationId, "approval.note", input, async db =>
        {
            var s = await Load(db, actor, input.RequestId);
            if (s.Request.RequestedBy != actor.Id && !s.Permissions.All(actor.CanDemand)) throw new UnauthorizedAccessException("Only the requester or an approver can add notes.");
            var row = new ApprovalNote { Id = Guid.NewGuid(), RequestId = s.Request.Id, Note = Rules.Required(input.Note, "Note", 200), Actor = actor.Id, Time = DateTimeOffset.UtcNow };
            db.ApprovalNotes.Add(row); return row.Id;
        });
    }

    public async Task<Guid> Post(PostApprovedCommand input)
    {
        var actor = await Authorize();
        ApprovalRequest request;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var s = await Load(db, actor, input.RequestId);
            if (s.Request.RequestedBy != actor.Id) throw new UnauthorizedAccessException("Only the person who asked can post an approved request.");
            if (s.Value != ApprovalState.Approved) throw new BusinessException(s.Value == ApprovalState.Used ? "This approval has already been used." : $"Request {s.Request.Number} is {s.Value.ToString().ToLowerInvariant()}.");
            request = s.Request;
        }
        var o = options;
        switch (request.Action)
        {
            case Controls.Trade: return await new BusinessService(factory, o).Post(actor, JsonSerializer.Deserialize<TradeCommand>(request.PayloadJson)! with { ApprovalId = request.Id });
            case Controls.Reverse: return await new BusinessService(factory, o).Reverse(actor, JsonSerializer.Deserialize<ReverseCommand>(request.PayloadJson)! with { ApprovalId = request.Id });
            case Controls.Count: return await new InventoryService(factory, session, o).Count(JsonSerializer.Deserialize<StockCountCommand>(request.PayloadJson)! with { ApprovalId = request.Id });
            case Controls.Voucher: return await new FinanceService(factory, session, o).PostVoucher(JsonSerializer.Deserialize<VoucherCommand>(request.PayloadJson)! with { ApprovalId = request.Id });
            case Controls.Return: return await new ReturnService(factory, session, o).Post(JsonSerializer.Deserialize<ReturnCommand>(request.PayloadJson)! with { ApprovalId = request.Id });
            default: throw new BusinessException("This kind of request cannot be posted from here.");
        }
    }

    // ---------------- exceptions and audit ----------------

    public async Task<ExceptionReport> Exceptions(DateOnly from, DateOnly to)
    {
        var actor = await Authorize("audit.read");
        if (from > to) throw new BusinessException("The report start must be on or before its end.");
        await using var db = await factory.CreateDbContextAsync();
        var policy = await Controls.Current(db, actor.Branch);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var ops = (await db.Operations.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync()).Where(x => x.Time >= start && x.Time < end).ToList();
        var resultIds = ops.Select(x => x.ResultId).ToList();
        var docs = await db.Documents.AsNoTracking().Where(x => resultIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var originalIds = docs.Values.Where(x => x.Reverses != null).Select(x => x.Reverses!.Value).ToList();
        var originals = await db.Documents.AsNoTracking().Where(x => originalIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var names = await db.Users.AsNoTracking().Where(x => x.UserName != null).ToDictionaryAsync(x => x.Id, x => x.UserName!);
        string Name(string id) => names.GetValueOrDefault(id, id);
        var uses = (await db.ApprovalUses.AsNoTracking().ToListAsync()).ToDictionary(x => x.OperationId);
        var requests = await db.ApprovalRequests.AsNoTracking().Where(x => x.Branch == actor.Branch).ToDictionaryAsync(x => x.Id);
        string Approval(Guid op) => uses.TryGetValue(op, out var u) && requests.TryGetValue(u.RequestId, out var r) ? r.Number : "";
        var maxDiscount = policy.MaxDiscountPercent ?? 10m; var backdate = policy.BackdateDays ?? 3;
        TimeOnly.TryParseExact(policy.WorkStart, "HH:mm", out var workStart); TimeOnly.TryParseExact(policy.WorkEnd, "HH:mm", out var workEnd);
        if (workEnd == default) workEnd = new(19, 0);

        var rows = new List<ExceptionRow>();
        foreach (var op in ops)
        {
            var who = Name(op.Actor);
            var time = TimeOnly.FromDateTime(op.Time.UtcDateTime);
            if (time < workStart || time >= workEnd) rows.Add(new(op.Time, who, "After hours", op.Action, docs.ContainsKey(op.ResultId) ? op.ResultId : null, $"{op.Action} at {op.Time.UtcDateTime:HH:mm} (working hours {policy.WorkStart}–{policy.WorkEnd})", 0, Approval(op.Id)));
            if (!docs.TryGetValue(op.ResultId, out var doc)) continue;
            var reference = Short(doc.Number);
            if (doc.Kind == DocumentKind.Reversal && doc.Reverses is Guid o && originals.TryGetValue(o, out var original))
            {
                var reason = TryReason(op.Payload);
                rows.Add(new(op.Time, who, "Reversal", reference, doc.Id, $"Reversed {original.Kind} {Short(original.Number)} ({original.Party}){(reason.Length > 0 ? $": {reason}" : "")}", Math.Abs(original.Total), Approval(op.Id)));
                continue;
            }
            var days = DateOnly.FromDateTime(op.Time.UtcDateTime).DayNumber - doc.BusinessDate.DayNumber;
            if (days > backdate) rows.Add(new(op.Time, who, "Back-dated", reference, doc.Id, $"{doc.Kind} dated {doc.BusinessDate:dd MMM yyyy}, posted {days} days later", doc.Total, Approval(op.Id)));
            if (doc.Kind == DocumentKind.StockCount && doc.Total != 0) rows.Add(new(op.Time, who, "Stock adjustment", reference, doc.Id, $"Count adjustment: {doc.Party}", doc.Total, Approval(op.Id)));
            if (doc.Kind == DocumentKind.SalesReturn) rows.Add(new(op.Time, who, "Refund", reference, doc.Id, $"Sales return for {doc.Party} ({doc.Settlement}){(TryReason(op.Payload) is { Length: > 0 } why ? $": {why}" : "")}", Math.Abs(doc.Total), Approval(op.Id)));
            if (doc.Kind is DocumentKind.Sale or DocumentKind.Delivery)
            {
                try
                {
                    using var json = JsonDocument.Parse(doc.Detail);
                    var root = json.RootElement;
                    if (root.TryGetProperty("Request", out var request) && request.Deserialize<TradeCommand>() is { } command && command.DeliveryId is null)
                        foreach (var line in command.Lines)
                            if (products.TryGetValue(line.ProductId, out var p) && p.Price > 0)
                            {
                                var per = line.UnitPrice / line.Conversion; var pct = (p.Price - per) / p.Price * 100;
                                if (pct > maxDiscount) rows.Add(new(op.Time, who, "Discount", reference, doc.Id, $"{p.Sku} at NLe {per:N2}, {pct:0.#}% below list NLe {p.Price:N2} ({doc.Party})", AccountingEngine.Money((p.Price - per) * line.Quantity * line.Conversion), Approval(op.Id)));
                            }
                    if (doc.Kind == DocumentKind.Sale && root.TryGetProperty("Net", out var net) && root.TryGetProperty("Cost", out var cost) && net.GetDecimal() < cost.GetDecimal())
                        rows.Add(new(op.Time, who, "Below cost", reference, doc.Id, $"Sold to {doc.Party} for NLe {net.GetDecimal():N2} against cost NLe {cost.GetDecimal():N2}", cost.GetDecimal() - net.GetDecimal(), Approval(op.Id)));
                }
                catch (JsonException) { }
            }
        }
        var decisions = (await db.ApprovalDecisions.AsNoTracking().ToListAsync()).Where(x => x.Time >= start && x.Time < end && requests.ContainsKey(x.RequestId));
        foreach (var d in decisions.Where(x => x.State is ApprovalState.Approved or ApprovalState.Rejected))
        {
            var r = requests[d.RequestId];
            rows.Add(new(d.Time, Name(d.Actor), d.SelfApproved ? "Self-approved" : d.State == ApprovalState.Approved ? "Approved" : "Rejected", r.Number, null, $"{r.Summary} — requested by {Name(r.RequestedBy)}{(d.Note.Length > 0 ? $"; note: {d.Note}" : "")}", 0, r.Number));
        }
        rows = rows.OrderByDescending(x => x.Time).ToList();
        var users = rows.GroupBy(x => x.Actor).Select(g =>
        {
            int N(string kind) => g.Count(x => x.Kind == kind);
            var reversals = N("Reversal"); var backdated = N("Back-dated"); var afterHours = N("After hours"); var self = N("Self-approved");
            return new UserExceptionSummary(g.Key, reversals, backdated, N("Discount"), N("Below cost"), N("Stock adjustment"), afterHours, self, reversals >= 3 || backdated >= 3 || afterHours >= 5 || self >= 3);
        }).OrderByDescending(x => x.Review).ThenBy(x => x.Actor).ToArray();
        return new(from, to, policy, rows, users);
    }
    private static string TryReason(string payload)
    {
        try { using var json = JsonDocument.Parse(payload); return json.RootElement.TryGetProperty("Reason", out var r) ? r.GetString() ?? "" : ""; } catch (JsonException) { return ""; }
    }

    public async Task<IReadOnlyList<AuditEntry>> AuditTrail(DateOnly from, DateOnly to)
    {
        var actor = await Authorize("audit.read");
        if (from > to) throw new BusinessException("The report start must be on or before its end.");
        await using var db = await factory.CreateDbContextAsync();
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var ops = (await db.Operations.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync()).Where(x => x.Time >= start && x.Time < end).OrderByDescending(x => x.Time).ToList();
        var ids = ops.Select(x => x.ResultId).ToList();
        var numbers = await db.Documents.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number);
        var names = await db.Users.AsNoTracking().Where(x => x.UserName != null).ToDictionaryAsync(x => x.Id, x => x.UserName!);
        return ops.Select(x => new AuditEntry(x.Time, names.GetValueOrDefault(x.Actor, x.Actor), x.Action, numbers.TryGetValue(x.ResultId, out var n) ? Short(n) : "", x.ResultId)).ToArray();
    }

    public async Task<byte[]> Export(DateOnly from, DateOnly to)
    {
        var report = await Exceptions(from, to); var trail = await AuditTrail(from, to); var approvals = await Approvals();
        static string T(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd HH:mm");
        return WorkbookWriter.Write([
            ("Exceptions", [["Time (UTC)", "User", "Kind", "Reference", "Detail", "Amount (NLe)", "Approval"], .. report.Rows.Select(r => (IReadOnlyList<string>)[T(r.Time), r.Actor, r.Kind, r.Reference, r.Detail, r.Amount.ToString("0.00"), r.Approval])]),
            ("By user", [["User", "Reversals", "Back-dated", "Discounts", "Below cost", "Stock adjustments", "After hours", "Self-approvals", "For review"], .. report.Users.Select(u => (IReadOnlyList<string>)[u.Actor, $"{u.Reversals}", $"{u.Backdated}", $"{u.Discounts}", $"{u.BelowCost}", $"{u.StockAdjustments}", $"{u.AfterHours}", $"{u.SelfApprovals}", u.Review ? "Yes" : ""])]),
            ("Approvals", [["Number", "Requested", "By", "Summary", "Reasons", "State", "Decided by", "Note", "Self-approved", "Document"], .. approvals.Select(a => (IReadOnlyList<string>)[a.Number, T(a.RequestedAt), a.RequestedBy, a.Summary, string.Join("; ", a.Triggers.Select(x => x.Detail)), a.State.ToString(), a.DecidedBy, a.DecisionNote, a.SelfApproved ? "Yes" : "", a.UsedDocument])]),
            ("Audit trail", [["Time (UTC)", "User", "Action", "Document"], .. trail.Select(e => (IReadOnlyList<string>)[T(e.Time), e.Actor, e.Action, e.Reference])])
        ]);
    }
}
