using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

// Append-only records (database guards and integrity seals). The current state of a tender is its latest event.
public sealed class Tender
{
    public Guid Id { get; set; } public string Branch { get; set; } = ""; public string Number { get; set; } = ""; public string Reference { get; set; } = ""; public string Institution { get; set; } = "";
    public string Title { get; set; } = ""; public string Officer { get; set; } = ""; public DateOnly Closing { get; set; } public string ClosingTime { get; set; } = ""; public string SubmissionMethod { get; set; } = "";
    public string Notes { get; set; } = ""; public DateTimeOffset Created { get; set; } public string CreatedBy { get; set; } = "";
}
public sealed class TenderBid
{
    public Guid Id { get; set; } public Guid TenderId { get; set; } public int Version { get; set; } public string LinesJson { get; set; } = "[]"; public decimal TaxRate { get; set; }
    public decimal Subtotal { get; set; } public decimal Tax { get; set; } public decimal Total { get; set; } public decimal CostTotal { get; set; } public string Reason { get; set; } = "";
    public DateTimeOffset Created { get; set; } public string CreatedBy { get; set; } = "";
}
public sealed class TenderEvent
{
    public Guid Id { get; set; } public Guid TenderId { get; set; } public int Sequence { get; set; } public string Kind { get; set; } = ""; public TenderStatus Status { get; set; } public int? BidVersion { get; set; }
    public string Reason { get; set; } = ""; public string DataJson { get; set; } = "{}"; public string Actor { get; set; } = ""; public DateTimeOffset Time { get; set; }
}
public sealed class TenderAward { public Guid Id { get; set; } public Guid TenderId { get; set; } public Guid OrderId { get; set; } public int BidVersion { get; set; } public string LinesJson { get; set; } = "[]"; }

public sealed class TenderService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : ITenderService
{
    private static readonly TenderStatus[] Final = [TenderStatus.Won, TenderStatus.Lost, TenderStatus.Cancelled];
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private async Task<Actor> Authorize(params string[] permissions)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        foreach (var p in permissions) actor.Demand(p);
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }
    private static bool SeesCosts(Actor a) => a.Permissions.Contains("sales.create") || a.Permissions.Contains("pricing.manage") || a.Permissions.Contains("accounting.read");
    private static string Optional(string? value, string field, int max)
    {
        value = (value ?? "").Trim();
        if (value.Length > max) throw new BusinessException($"{field} is limited to {max} characters.");
        return value;
    }
    private static string Time(string? value)
    {
        value = (value ?? "").Trim();
        if (value.Length > 0 && !TimeOnly.TryParseExact(value, "HH:mm", out _)) throw new BusinessException("Enter the closing time as HH:mm, for example 12:00.");
        return value;
    }

    // ---------------- state ----------------

    private sealed record State(Tender Tender, List<TenderEvent> Events, List<TenderBid> Bids)
    {
        public TenderStatus Status => Events.Count == 0 ? TenderStatus.Draft : Events.MaxBy(x => x.Sequence)!.Status;
        public int LatestVersion => Bids.Count == 0 ? 0 : Bids.Max(x => x.Version);
        public int? Approved => Events.Where(x => x.Kind == "Approved").MaxBy(x => x.Sequence)?.BidVersion is int v && v == LatestVersion ? v : null;
        public int? Submitted => Events.Where(x => x.Kind is "Submitted").MaxBy(x => x.Sequence)?.BidVersion;
        public (DateOnly Date, string Time) Closing
        {
            get
            {
                var change = Events.Where(x => x.Kind == "ClosingChanged").MaxBy(x => x.Sequence);
                if (change is null) return (Tender.Closing, Tender.ClosingTime);
                using var json = JsonDocument.Parse(change.DataJson);
                return (DateOnly.Parse(json.RootElement.GetProperty("Closing").GetString()!), json.RootElement.GetProperty("ClosingTime").GetString() ?? "");
            }
        }
    }
    private static async Task<State> Load(BusinessDb db, Actor actor, Guid id)
    {
        var tender = await db.Tenders.SingleOrDefaultAsync(x => x.Id == id && x.Branch == actor.Branch) ?? throw new BusinessException("Tender not found.");
        return new(tender, await db.TenderEvents.Where(x => x.TenderId == id).ToListAsync(), await db.TenderBids.Where(x => x.TenderId == id).ToListAsync());
    }
    private static Guid Event(BusinessDb db, State state, Actor actor, string kind, TenderStatus status, int? version, string reason, object? data = null)
    {
        var row = new TenderEvent { Id = Guid.NewGuid(), TenderId = state.Tender.Id, Sequence = (state.Events.Count == 0 ? 0 : state.Events.Max(x => x.Sequence)) + 1, Kind = kind, Status = status, BidVersion = version, Reason = reason, DataJson = JsonSerializer.Serialize(data ?? new { }), Actor = actor.Id, Time = DateTimeOffset.UtcNow };
        db.TenderEvents.Add(row); state.Events.Add(row); return row.Id;
    }
    private static void Open(State s) { if (Final.Contains(s.Status)) throw new BusinessException($"This tender is {s.Status.ToString().ToLowerInvariant()}; it can no longer be changed."); }

    // ---------------- queries ----------------

    public async Task<IReadOnlyList<TenderView>> List()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var tenders = await db.Tenders.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync();
        var ids = tenders.Select(x => x.Id).ToList();
        var events = (await db.TenderEvents.AsNoTracking().Where(x => ids.Contains(x.TenderId)).ToListAsync()).ToLookup(x => x.TenderId);
        var bids = (await db.TenderBids.AsNoTracking().Where(x => ids.Contains(x.TenderId)).ToListAsync()).ToLookup(x => x.TenderId);
        var awards = await db.TenderAwards.AsNoTracking().Where(x => ids.Contains(x.TenderId)).ToDictionaryAsync(x => x.TenderId);
        var orderIds = awards.Values.Select(x => x.OrderId).ToList();
        var orders = await db.TradeOrders.AsNoTracking().Where(x => orderIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Sku);
        var names = await db.Users.AsNoTracking().Where(x => x.UserName != null).ToDictionaryAsync(x => x.Id, x => x.UserName!);
        string Name(string id) => names.GetValueOrDefault(id, id);
        var costs = SeesCosts(actor);
        return tenders.Select(t =>
        {
            var s = new State(t, events[t.Id].ToList(), bids[t.Id].ToList());
            var (closing, time) = s.Closing;
            var approvals = s.Events.Where(x => x.Kind == "Approved").ToList();
            var submittedVersions = s.Events.Where(x => x.Kind == "Submitted").Select(x => x.BidVersion).ToHashSet();
            var outcome = s.Events.Where(x => x.Kind is "Won" or "Lost" or "Cancelled").MaxBy(x => x.Sequence) is { } end ? (end.Kind == "Lost" ? Lost(end) : $"{end.Kind}: {end.Reason}") : "";
            awards.TryGetValue(t.Id, out var award);
            return new TenderView(t.Id, t.Number, t.Reference, t.Institution, t.Title, t.Officer, closing, time, closing.DayNumber - Today.DayNumber, t.SubmissionMethod, t.Notes, s.Status,
                s.Bids.OrderByDescending(x => x.Version).Select(b => View(b, products, costs, Name(b.CreatedBy), approvals.Where(a => a.BidVersion == b.Version).Select(a => Name(a.Actor)).LastOrDefault() ?? "", submittedVersions.Contains(b.Version))).ToArray(),
                s.Events.OrderBy(x => x.Sequence).Select(e => new TenderEventView(e.Sequence, e.Kind, e.Status, e.BidVersion, e.Reason, Name(e.Actor), e.Time)).ToArray(),
                s.Approved, s.Submitted, award?.OrderId, award is null ? "" : orders.GetValueOrDefault(award.OrderId, ""), outcome, costs);
        }).OrderBy(x => Final.Contains(x.Status)).ThenBy(x => x.Closing).ToArray();
    }
    private static string Lost(TenderEvent e)
    {
        using var json = JsonDocument.Parse(e.DataJson);
        var winner = json.RootElement.TryGetProperty("Winner", out var w) ? w.GetString() ?? "" : "";
        var amount = json.RootElement.TryGetProperty("WinningAmount", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDecimal() : (decimal?)null;
        return "Lost" + (winner.Length > 0 ? $" to {winner}" : "") + (amount is not null ? $" at NLe {amount:N2}" : "") + $": {e.Reason}";
    }

    private sealed record StoredLine(int Line, Guid? ProductId, string Description, decimal Quantity, string Unit, decimal SupplierCost, string CostCurrency, decimal ExchangeRate, decimal LandedPercent, decimal MarginPercent,
        decimal LocalUnitCost, decimal SuggestedPrice, decimal UnitPrice, decimal LineTotal, int? AlternativeTo, string Notes);
    private static TenderBidView View(TenderBid b, IReadOnlyDictionary<Guid, string> products, bool costs, string by, string approvedBy, bool submitted)
    {
        var lines = JsonSerializer.Deserialize<List<StoredLine>>(b.LinesJson)!;
        var margin = b.Subtotal == 0 ? 0 : Math.Round((b.Subtotal - b.CostTotal) / b.Subtotal * 100, 1);
        return new(b.Version, lines.Select(l => new TenderLineView(l.Line, l.ProductId, l.ProductId is Guid p ? products.GetValueOrDefault(p, "") : "", l.Description, l.Quantity, l.Unit,
                costs ? l.SupplierCost : 0, l.CostCurrency, costs ? l.ExchangeRate : 0, costs ? l.LandedPercent : 0, costs ? l.MarginPercent : 0, costs ? l.LocalUnitCost : 0, costs ? l.SuggestedPrice : 0,
                l.UnitPrice, l.LineTotal, costs && l.UnitPrice > 0 ? Math.Round((l.UnitPrice - l.LocalUnitCost) / l.UnitPrice * 100, 1) : 0, l.AlternativeTo, l.Notes, costs && l.UnitPrice < l.LocalUnitCost)).ToArray(),
            b.TaxRate, b.Subtotal, b.Tax, b.Total, costs ? b.CostTotal : 0, costs ? margin : 0, b.Reason, b.Created, by, approvedBy, submitted);
    }

    // ---------------- commands ----------------

    public async Task<Guid> Create(CreateTenderCommand input)
    {
        var actor = await Authorize("sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "tender.create", input, async db =>
        {
            if (input.Closing < Today.AddDays(-366) || input.Closing > Today.AddYears(3)) throw new BusinessException("Enter a realistic closing date.");
            var reference = Rules.Required(input.Reference, "Tender reference", 120);
            var institution = Rules.Required(input.Institution, "Institution", 160);
            if (await db.Tenders.AnyAsync(x => x.Branch == actor.Branch && x.Reference.ToUpper() == reference.ToUpper() && x.Institution.ToUpper() == institution.ToUpper())) throw new BusinessException($"Tender {reference} from {institution} is already registered.");
            var tender = new Tender
            {
                Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-TND-{input.OperationId:N}", Reference = reference, Institution = institution,
                Title = Rules.Required(input.Title, "Tender title", 200), Officer = Optional(input.Officer, "Responsible officer", 100), Closing = input.Closing, ClosingTime = Time(input.ClosingTime),
                SubmissionMethod = Optional(input.SubmissionMethod, "Submission method", 120), Notes = Optional(input.Notes, "Notes", 200), Created = DateTimeOffset.UtcNow, CreatedBy = actor.Id
            };
            db.Tenders.Add(tender);
            Event(db, new(tender, [], []), actor, "Created", TenderStatus.Draft, null, "Tender registered");
            return tender.Id;
        });
    }

    public async Task<Guid> SaveBid(SaveBidCommand input)
    {
        var actor = await Authorize("sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "tender.bid", input, async db =>
        {
            var s = await Load(db, actor, input.TenderId); Open(s);
            if (s.Status == TenderStatus.Submitted) throw new BusinessException("The submitted bid cannot be changed. If the institution asks for clarification or a revised price, record the clarification first; a new version then needs approval and resubmission.");
            if (input.ExpectedVersion != s.LatestVersion) throw new BusinessException("Someone else saved a newer bid version. Refresh before saving.");
            if (input.Lines is null || input.Lines.Count is < 1 or > 200) throw new BusinessException("Enter 1 to 200 bid lines.");
            if (s.LatestVersion > 0 && (input.Reason ?? "").Trim().Length == 0) throw new BusinessException("Give the reason for this new bid version (for example a supplier price change).");
            var lines = new List<StoredLine>();
            for (var i = 0; i < input.Lines.Count; i++)
            {
                var l = input.Lines[i] ?? throw new BusinessException("Bid lines cannot be empty.");
                var n = i + 1;
                if (l.ProductId is Guid p && !await db.Products.AnyAsync(x => x.Id == p)) throw new BusinessException($"Line {n}: product not found.");
                Rules.Positive(l.Quantity, $"Line {n} quantity"); Rules.Positive(l.UnitPrice, $"Line {n} bid price");
                if (l.SupplierCost < 0 || l.SupplierCost > 1_000_000_000 || decimal.Round(l.SupplierCost, 4) != l.SupplierCost) throw new BusinessException($"Line {n}: supplier cost must be 0 or more, with up to four decimal places.");
                var currency = Fx.Normalize(l.CostCurrency);
                var rate = currency is null ? 1m : l.ExchangeRate;
                if (rate <= 0 || rate > 1_000_000 || decimal.Round(rate, 4) != rate) throw new BusinessException($"Line {n}: enter the exchange rate as NLe per one {currency}.");
                if (l.LandedPercent is < 0 or > 1000 || l.MarginPercent is < -100 or > 1000) throw new BusinessException($"Line {n}: landed cost and margin percentages must be realistic.");
                if (l.AlternativeTo is int alt && (alt < 1 || alt >= n || lines[alt - 1].AlternativeTo is not null)) throw new BusinessException($"Line {n}: an alternative offer must refer to an earlier main line.");
                var local = decimal.Round(l.SupplierCost * rate * (1 + l.LandedPercent / 100), 4);
                lines.Add(new(n, l.ProductId, Rules.Required(l.Description, $"Line {n} description", 160), l.Quantity, Rules.Required(l.Unit, $"Line {n} unit", 30), l.SupplierCost, currency ?? Fx.Local, rate, l.LandedPercent, l.MarginPercent,
                    local, AccountingEngine.Money(local * (1 + l.MarginPercent / 100)), l.UnitPrice, AccountingEngine.Money(l.Quantity * l.UnitPrice), l.AlternativeTo, Optional(l.Notes, $"Line {n} notes", 160)));
            }
            var taxRate = await db.SalesTaxRates.Where(x => x.Branch == actor.Branch && x.From <= Today).OrderByDescending(x => x.From).Select(x => (decimal?)x.Rate).FirstOrDefaultAsync() ?? options.Value.TaxRate;
            var main = lines.Where(x => x.AlternativeTo is null).ToList();
            var subtotal = main.Sum(x => x.LineTotal); var tax = AccountingEngine.Money(subtotal * taxRate);
            var bid = new TenderBid { Id = Guid.NewGuid(), TenderId = s.Tender.Id, Version = s.LatestVersion + 1, LinesJson = JsonSerializer.Serialize(lines), TaxRate = taxRate, Subtotal = subtotal, Tax = tax, Total = subtotal + tax,
                CostTotal = main.Sum(x => AccountingEngine.Money(x.Quantity * x.LocalUnitCost)), Reason = Optional(input.Reason, "Reason", 200), Created = DateTimeOffset.UtcNow, CreatedBy = actor.Id };
            db.TenderBids.Add(bid); s.Bids.Add(bid);
            Event(db, s, actor, "BidSaved", TenderStatus.BidPreparation, bid.Version, bid.Reason.Length > 0 ? bid.Reason : "First bid version");
            return bid.Id;
        });
    }

    public async Task<Guid> Approve(TenderDecisionCommand input)
    {
        var actor = await Authorize("sales.create", "pricing.manage");
        return await Commands.Run(factory, actor, input.OperationId, "tender.approve", input, async db =>
        {
            var s = await Load(db, actor, input.TenderId); Open(s);
            if (s.Status != TenderStatus.BidPreparation || input.Version != s.LatestVersion) throw new BusinessException("Only the latest bid version, while in preparation, can be approved.");
            var bid = s.Bids.Single(x => x.Version == input.Version);
            var reason = Optional(input.Reason, "Approval note", 160);
            if (bid.CreatedBy == actor.Id)
            {
                if (await OtherApprover(db, actor)) throw new BusinessException("Another person with pricing authority must approve a bid you prepared.");
                reason = (reason + " (self-approved: no other pricing approver is set up)").Trim();
            }
            return Event(db, s, actor, "Approved", TenderStatus.Approved, bid.Version, reason, new { bid.Total });
        });
    }
    private static async Task<bool> OtherApprover(BusinessDb db, Actor actor)
    {
        var users = await db.UserClaims.Where(x => x.ClaimType == "branch" && x.ClaimValue == actor.Branch && x.UserId != actor.Id).Select(x => x.UserId).ToListAsync();
        if (users.Count == 0) return false;
        var approvers = await db.UserClaims.Where(x => users.Contains(x.UserId) && x.ClaimType == "permission" && x.ClaimValue == "pricing.manage").Select(x => x.UserId).ToListAsync();
        var now = DateTimeOffset.UtcNow;
        return (await db.Users.Where(x => approvers.Contains(x.Id)).ToListAsync()).Any(x => x.LockoutEnd is null || x.LockoutEnd <= now);
    }

    public async Task<Guid> Submit(SubmitTenderCommand input)
    {
        var actor = await Authorize("sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "tender.submit", input, async db =>
        {
            var s = await Load(db, actor, input.TenderId); Open(s);
            if (s.Status != TenderStatus.Approved || s.Approved != input.Version) throw new BusinessException("Submit the approved latest bid version. Changed bids need approval first.");
            if (input.SubmittedOn > Today || input.SubmittedOn < DateOnly.FromDateTime(s.Tender.Created.UtcDateTime)) throw new BusinessException("The submission date must be between the tender's registration and today.");
            if (input.SubmittedOn > s.Closing.Date) throw new BusinessException("The closing date has passed. Record the institution's extension first if one was granted.");
            return Event(db, s, actor, "Submitted", TenderStatus.Submitted, input.Version, Optional(input.Reason, "Note", 160), new { input.SubmittedOn, Receipt = Optional(input.Receipt, "Submission receipt", 120) });
        });
    }

    public async Task<Guid> Clarify(TenderNoteCommand input)
    {
        var actor = await Authorize("sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "tender.clarify", input, async db =>
        {
            var s = await Load(db, actor, input.TenderId); Open(s);
            if (s.Status != TenderStatus.Submitted) throw new BusinessException("Clarifications are recorded after the bid has been submitted.");
            return Event(db, s, actor, "Clarification", TenderStatus.Clarification, s.Submitted, Rules.Required(input.Reason, "What the institution asked", 200));
        });
    }

    public async Task<Guid> ChangeClosing(ChangeClosingCommand input)
    {
        var actor = await Authorize("sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "tender.closing", input, async db =>
        {
            var s = await Load(db, actor, input.TenderId); Open(s);
            if (input.Closing < Today.AddDays(-366) || input.Closing > Today.AddYears(3)) throw new BusinessException("Enter a realistic closing date.");
            return Event(db, s, actor, "ClosingChanged", s.Status, null, Rules.Required(input.Reason, "Reason (for example the extension notice)", 160), new { Closing = input.Closing.ToString("yyyy-MM-dd"), ClosingTime = Time(input.ClosingTime) });
        });
    }

    public async Task<Guid> Win(WinTenderCommand input)
    {
        var actor = await Authorize("sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "tender.win", input, async db =>
        {
            var s = await Load(db, actor, input.TenderId); Open(s);
            if (s.Status is not (TenderStatus.Submitted or TenderStatus.Clarification) || s.Submitted is not int version) throw new BusinessException("Only a submitted tender can be won.");
            if (input.OrderDate <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough) || input.OrderDate > Today || input.DueDate < input.OrderDate) throw new BusinessException("Use an open order date no later than today and a due date on or after it.");
            var lines = JsonSerializer.Deserialize<List<StoredLine>>(s.Bids.Single(x => x.Version == version).LinesJson)!;
            if (input.Lines is null || input.Lines.Count == 0 || input.Lines.Select(x => x.Line).Distinct().Count() != input.Lines.Count) throw new BusinessException("Choose the awarded lines, each once.");
            var awarded = new List<(StoredLine Line, decimal Quantity)>();
            foreach (var a in input.Lines)
            {
                var line = lines.SingleOrDefault(x => x.Line == a.Line) ?? throw new BusinessException($"Line {a.Line} is not on the submitted bid.");
                Rules.Positive(a.Quantity, $"Awarded quantity for line {a.Line}");
                if (a.Quantity > line.Quantity) throw new BusinessException($"Line {a.Line}: the award cannot exceed the {line.Quantity:0.####} bid.");
                if (line.ProductId is null) throw new BusinessException($"Line {a.Line} ({line.Description}) is not linked to a catalogue product. Add the product to the catalogue and save a bid version that uses it before converting.");
                awarded.Add((line, a.Quantity));
            }
            if (awarded.Select(x => x.Line.ProductId).Distinct().Count() != awarded.Count) throw new BusinessException("Each product can appear once on the sales order. Award one line per product.");
            var award = Optional(input.AwardReference, "Award letter / contract reference", 100);
            var reference = $"Tender {s.Tender.Reference}{(award.Length > 0 ? $" · award {award}" : "")}";
            var order = new TradeOrder { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-O-{input.OperationId:N}", Type = OrderType.SalesOrder, Party = s.Tender.Institution,
                Date = input.OrderDate, DueDate = input.DueDate, Reference = reference.Length > 160 ? reference[..160] : reference };
            db.TradeOrders.Add(order);
            foreach (var (line, quantity) in awarded)
            {
                await InventoryManagement.Active(db, actor.Branch, line.ProductId!.Value);
                await CommerceRules.Price(db, line.ProductId.Value, line.UnitPrice, 1);
                db.TradeOrderLines.Add(new() { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = line.ProductId.Value, Quantity = quantity, UnitPrice = line.UnitPrice });
            }
            db.TenderAwards.Add(new() { Id = Guid.NewGuid(), TenderId = s.Tender.Id, OrderId = order.Id, BidVersion = version, LinesJson = JsonSerializer.Serialize(input.Lines) });
            Event(db, s, actor, "Won", TenderStatus.Won, version, award.Length > 0 ? $"Awarded ({award})" : "Awarded", new { Order = order.Id, Awarded = awarded.Sum(x => AccountingEngine.Money(x.Quantity * x.Line.UnitPrice)) });
            return order.Id;
        });
    }

    public async Task<Guid> Lose(LoseTenderCommand input)
    {
        var actor = await Authorize("sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "tender.lose", input, async db =>
        {
            var s = await Load(db, actor, input.TenderId); Open(s);
            if (s.Status is not (TenderStatus.Submitted or TenderStatus.Clarification)) throw new BusinessException("Only a submitted tender can be recorded as lost. Cancel it if you did not bid.");
            if (input.WinningAmount is < 0 or > 1_000_000_000_000) throw new BusinessException("Enter a realistic winning amount.");
            return Event(db, s, actor, "Lost", TenderStatus.Lost, s.Submitted, Rules.Required(input.Reason, "Reason or feedback", 200), new { Winner = Optional(input.Winner, "Winning bidder", 160), input.WinningAmount });
        });
    }

    public async Task<Guid> Cancel(TenderNoteCommand input)
    {
        var actor = await Authorize("sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "tender.cancel", input, async db =>
        {
            var s = await Load(db, actor, input.TenderId); Open(s);
            return Event(db, s, actor, "Cancelled", TenderStatus.Cancelled, null, Rules.Required(input.Reason, "Reason (for example we decided not to bid, or the tender was withdrawn)", 200));
        });
    }
}
