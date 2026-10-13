using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

// Append-only records (database guards and integrity seals). A job's state is its latest event; parts are ordinary stock documents.
public sealed class ServiceJob
{
    public Guid Id { get; set; } public string Branch { get; set; } = ""; public string Number { get; set; } = ""; public string Customer { get; set; } = ""; public string Site { get; set; } = "";
    public string Contact { get; set; } = ""; public Guid? ProductId { get; set; } public string Serial { get; set; } = ""; public string Equipment { get; set; } = ""; public string Problem { get; set; } = "";
    public ServicePriority Priority { get; set; } public string Source { get; set; } = ""; public DateOnly ReportedOn { get; set; } public Guid? PlanId { get; set; } public DateOnly? PlanDue { get; set; }
    public string ChecklistJson { get; set; } = "[]"; public DateTimeOffset Created { get; set; } public string CreatedBy { get; set; } = "";
}
public sealed class ServiceJobEvent
{
    public Guid Id { get; set; } public Guid JobId { get; set; } public int Sequence { get; set; } public string Kind { get; set; } = ""; public ServiceStatus Status { get; set; }
    public string Text { get; set; } = ""; public string DataJson { get; set; } = "{}"; public Guid? DocumentId { get; set; } public string Actor { get; set; } = ""; public DateTimeOffset Time { get; set; }
}
public sealed class MaintenancePlan
{
    public Guid Id { get; set; } public string Branch { get; set; } = ""; public string Number { get; set; } = ""; public string Customer { get; set; } = ""; public string Site { get; set; } = "";
    public Guid ProductId { get; set; } public string Serial { get; set; } = ""; public int IntervalMonths { get; set; } public DateOnly FirstDue { get; set; } public string ChecklistJson { get; set; } = "[]";
    public string Engineer { get; set; } = ""; public string Notes { get; set; } = ""; public DateTimeOffset Created { get; set; } public string CreatedBy { get; set; } = "";
}
public sealed class MaintenancePlanEnd { public Guid Id { get; set; } public Guid PlanId { get; set; } public string Reason { get; set; } = ""; public string Actor { get; set; } = ""; public DateTimeOffset Time { get; set; } }

/// <summary>Ledger accounts and shared rules for after-sales service.</summary>
public static class ServiceRules
{
    public const Account WorkInProgress = (Account)1260, Income = (Account)4300, Costs = (Account)5400;
    public static readonly DocumentKind[] StockKinds = [DocumentKind.ServiceIssue, DocumentKind.ServiceReturn, DocumentKind.ServiceClose];

    /// <summary>Customer receivable documents: credit sales, credit service invoices and bounced cheques.</summary>
    public static IQueryable<BusinessDocument> Receivable(IQueryable<BusinessDocument> documents) =>
        documents.Where(x => (x.Kind == DocumentKind.Sale || x.Kind == DocumentKind.ServiceInvoice) && x.Settlement == "Credit" || x.Kind == DocumentKind.ChequeBounce);
    public static bool IsReceivable(BusinessDocument x) => (x.Kind is DocumentKind.Sale or DocumentKind.ServiceInvoice) && x.Settlement == "Credit" || x.Kind == DocumentKind.ChequeBounce;

    /// <summary>Credit limit and hold check shared by credit sales and credit service invoices.</summary>
    public static async Task CheckCredit(BusinessDb db, string branch, BusinessParty? customer, string party, decimal amount, CancellationToken ct = default)
    {
        if (party.Equals("Walk-in", StringComparison.OrdinalIgnoreCase)) throw new BusinessException("Credit requires a named customer.");
        if (customer is null) throw new BusinessException($"Credit needs a registered customer. Add {party} under Customers & suppliers (with a credit limit), or take payment now.");
        if (customer.CreditHold) throw new BusinessException("This customer's account is on credit hold.");
        var normalized = party.ToUpperInvariant();
        var bills = await Receivable(db.Documents.Where(x => x.Branch == branch && x.Party.ToUpper() == normalized)).Where(x => !db.Documents.Any(r => r.Reverses == x.Id)).ToListAsync(ct);
        var ids = bills.Select(x => x.Id).ToList();
        var allocated = (await db.BillAllocations.Where(x => ids.Contains(x.InvoiceId) && !db.Documents.Any(r => r.Reverses == x.VoucherId)).ToListAsync(ct)).Sum(x => x.Amount);
        var credited = (await db.ReturnAllocations.Where(x => ids.Contains(x.OriginalDocumentId) && !db.Documents.Any(r => r.Reverses == x.ReturnDocumentId)).ToListAsync(ct)).Sum(x => x.BillCredit);
        if (bills.Sum(x => x.Total) - allocated - credited + amount > customer.CreditLimit) throw new BusinessException("This exceeds the customer's credit limit. Record payment or update the approved limit before posting.");
    }

    /// <summary>Service history lines for one serial unit's timeline.</summary>
    public static async Task<IReadOnlyList<EquipmentEvent>> History(BusinessDb db, string branch, Guid productId, string serial)
    {
        var jobs = await db.ServiceJobs.AsNoTracking().Where(x => x.Branch == branch && x.ProductId == productId && x.Serial == serial).ToListAsync();
        var ids = jobs.Select(x => x.Id).ToList();
        var events = (await db.ServiceJobEvents.AsNoTracking().Where(x => ids.Contains(x.JobId) && (x.Kind == "Completed" || x.Kind == "Coverage" || x.Kind == "Closed" || x.Kind == "Cancelled" || x.Kind == "SupplierClaim")).ToListAsync()).ToLookup(x => x.JobId);
        var result = new List<EquipmentEvent>();
        foreach (var j in jobs)
        {
            result.Add(new(j.ReportedOn, "Service", (j.Source == "Preventive" ? "Preventive maintenance visit" : $"Service job: {j.Problem}"), j.Number, null));
            foreach (var e in events[j.Id].OrderBy(x => x.Sequence))
            {
                var date = DateOnly.FromDateTime(e.Time.UtcDateTime);
                switch (e.Kind)
                {
                    case "Coverage": var c = JsonSerializer.Deserialize<CoverageData>(e.DataJson)!; result.Add(new(date, "Service", c.Billing == ServiceBilling.Warranty ? "Warranty claim accepted" + (c.Exception ? " (exception approved)" : "") : $"Billed as {c.Billing.ToString().ToLowerInvariant()}", j.Number, null)); break;
                    case "Completed": var d = JsonSerializer.Deserialize<CompletionData>(e.DataJson)!; result.Add(new(d.Date, "Service", $"Service completed: {d.Resolution}; accepted by {d.AcceptedBy}", j.Number, null)); break;
                    case "SupplierClaim": result.Add(new(date, "Service", $"Claim lodged with supplier: {e.Text}", j.Number, null)); break;
                    case "Cancelled": result.Add(new(date, "Service", $"Service job cancelled: {e.Text}", j.Number, null)); break;
                }
            }
        }
        return result;
    }

    internal sealed record CoverageData(ServiceBilling Billing, bool CoversParts, bool CoversLabour, bool Exception);
    internal sealed record CompletionData(DateOnly Date, string Resolution, string AcceptedBy, List<string> ChecklistDone);
    internal sealed record LabourData(DateOnly Date, string Engineer, decimal Hours, decimal Rate, string Description);
    internal sealed record IssueData(Guid ProductId, decimal Quantity, decimal UnitPrice);
    internal sealed record ReturnData(Guid IssueDocumentId, decimal Quantity);
    internal sealed record ClaimData(string Supplier, string Reference);
    internal sealed record InvoiceLine(string Description, decimal Quantity, decimal UnitPrice, decimal Amount, TaxCategory Category = TaxCategory.Standard);
}

public sealed class ServiceDesk(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IServiceDesk
{
    private static readonly ServiceStatus[] Working = [ServiceStatus.Logged, ServiceStatus.Assigned, ServiceStatus.InProgress];
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private TradingOptions Settings => options.Value;

    private async Task<Actor> Authorize(bool write = false, params string[] permissions)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        if (write && !actor.Permissions.Contains("sales.create") && !actor.Permissions.Contains("inventory.receive")) throw new UnauthorizedAccessException("You cannot change service jobs.");
        foreach (var p in permissions) actor.Demand(p);
        if (actor.Branch != Settings.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }
    private static bool SeesCosts(Actor a) => a.Permissions.Contains("sales.create") || a.Permissions.Contains("pricing.manage") || a.Permissions.Contains("accounting.read");
    private static string Optional(string? value, string field, int max)
    {
        value = (value ?? "").Trim();
        if (value.Length > max) throw new BusinessException($"{field} is limited to {max} characters.");
        return value;
    }
    private static List<string> Checklist(IEnumerable<string>? items)
    {
        var list = (items ?? []).Select(x => x?.Trim() ?? "").Where(x => x.Length > 0).ToList();
        if (list.Count > 30 || list.Any(x => x.Length > 120) || list.Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count) throw new BusinessException("Use up to 30 different checklist items of at most 120 characters.");
        return list;
    }
    private async Task OpenDate(BusinessDb db, Actor actor, DateOnly date)
    {
        if (date <= await PeriodControls.ClosedThrough(db, actor.Branch, Settings.ClosedThrough) || date > Today) throw new BusinessException("The date must be in an open period and cannot be in the future.");
    }

    // ---------------- job state ----------------

    private sealed class Job(ServiceJob row, List<ServiceJobEvent> events, HashSet<Guid> reversed)
    {
        public ServiceJob Row { get; } = row; public List<ServiceJobEvent> Events { get; } = events; public HashSet<Guid> Reversed { get; } = reversed;
        private ServiceJobEvent Last => Events.MaxBy(x => x.Sequence)!;
        public ServiceJobEvent? Closing => Events.Where(x => x.Kind == "Closed").MaxBy(x => x.Sequence);
        /// <summary>A reversed service invoice returns the job to "completed, awaiting billing".</summary>
        public bool CloseReversed => Closing is { DocumentId: Guid d } && Reversed.Contains(d) && Last.Kind == "Closed";
        public ServiceStatus Status => CloseReversed ? ServiceStatus.Completed : Last.Status;
        public string Engineer => Events.Where(x => x.Kind == "Assigned").MaxBy(x => x.Sequence)?.Text ?? "";
        public ServiceRules.CoverageData? Coverage => Events.Where(x => x.Kind == "Coverage").MaxBy(x => x.Sequence) is { } e ? JsonSerializer.Deserialize<ServiceRules.CoverageData>(e.DataJson) : null;
        public ServiceJobEvent? CoverageEvent => Events.Where(x => x.Kind == "Coverage").MaxBy(x => x.Sequence);
        public ServiceRules.CompletionData? Completion => Status is ServiceStatus.Completed or ServiceStatus.Closed && Events.Where(x => x.Kind == "Completed").MaxBy(x => x.Sequence) is { } e ? JsonSerializer.Deserialize<ServiceRules.CompletionData>(e.DataJson) : null;
        public IEnumerable<ServiceJobEvent> Issues => Events.Where(x => x.Kind == "PartIssued");
        public decimal Returned(Guid issue) => Events.Where(x => x.Kind == "PartReturned").Select(x => JsonSerializer.Deserialize<ServiceRules.ReturnData>(x.DataJson)!).Where(x => x.IssueDocumentId == issue).Sum(x => x.Quantity);
        public IEnumerable<ServiceRules.LabourData> Labour => Events.Where(x => x.Kind == "Labour").OrderBy(x => x.Sequence).Select(x => JsonSerializer.Deserialize<ServiceRules.LabourData>(x.DataJson)!);
        public IReadOnlyList<string> Checklist => EquipmentRules.Checklist(Row.ChecklistJson);
        public (string Text, bool Open)? Claim
        {
            get
            {
                var claim = Events.Where(x => x.Kind == "SupplierClaim").MaxBy(x => x.Sequence);
                if (claim is null) return null;
                var data = JsonSerializer.Deserialize<ServiceRules.ClaimData>(claim.DataJson)!;
                var outcome = Events.Where(x => x.Kind == "SupplierClaimOutcome" && x.Sequence > claim.Sequence).MaxBy(x => x.Sequence);
                return ($"{data.Supplier}, ref {data.Reference}" + (outcome is null ? " — awaiting supplier" : $" — {outcome.Text}"), outcome is null);
            }
        }
    }
    private static async Task<Job> Load(BusinessDb db, Actor actor, Guid id)
    {
        var row = await db.ServiceJobs.SingleOrDefaultAsync(x => x.Id == id && x.Branch == actor.Branch) ?? throw new BusinessException("Service job not found.");
        var events = await db.ServiceJobEvents.Where(x => x.JobId == id).ToListAsync();
        var docs = events.Where(x => x.DocumentId is not null).Select(x => x.DocumentId!.Value).ToList();
        var reversed = (await db.Documents.Where(x => x.Reverses != null && docs.Contains(x.Reverses!.Value)).Select(x => x.Reverses!.Value).ToListAsync()).ToHashSet();
        return new(row, events, reversed);
    }
    private static Guid Event(BusinessDb db, Job job, Actor actor, string kind, ServiceStatus status, string text = "", object? data = null, Guid? document = null)
    {
        var row = new ServiceJobEvent { Id = Guid.NewGuid(), JobId = job.Row.Id, Sequence = (job.Events.Count == 0 ? 0 : job.Events.Max(x => x.Sequence)) + 1, Kind = kind, Status = status, Text = text,
            DataJson = JsonSerializer.Serialize(data ?? new { }), DocumentId = document, Actor = actor.Id, Time = DateTimeOffset.UtcNow };
        db.ServiceJobEvents.Add(row); job.Events.Add(row); return row.Id;
    }
    private static void Require(Job job, params ServiceStatus[] allowed)
    {
        if (!allowed.Contains(job.Status)) throw new BusinessException(job.Status switch
        {
            ServiceStatus.Closed => "This service job is closed.",
            ServiceStatus.Cancelled => "This service job is cancelled.",
            ServiceStatus.Completed => "The work is recorded as completed. Reopen the job to change it.",
            _ => "Assign an engineer and start the work first."
        });
    }
    private static ServiceStatus Started(Job job) => job.Status is ServiceStatus.Logged or ServiceStatus.Assigned ? ServiceStatus.InProgress : job.Status;

    // ---------------- warranty position ----------------

    private sealed record Position(string Hint, bool WarrantyActive, string Customer, DateOnly? SupplierEnd, bool WithCustomer);
    private static async Task<Dictionary<(Guid, string), Position>> Positions(BusinessDb db, string branch, IReadOnlyCollection<(Guid ProductId, string Serial)> units, Func<(Guid, string), DateOnly> asOf)
    {
        var result = new Dictionary<(Guid, string), Position>();
        if (units.Count == 0) return result;
        var states = await EquipmentRules.States(db, branch, units);
        var serials = units.Select(x => x.Serial).Distinct().ToList();
        var warranties = (await db.Warranties.AsNoTracking().Where(x => x.Branch == branch && serials.Contains(x.Serial)).ToListAsync()).ToLookup(x => (x.ProductId, x.Serial));
        var terms = await EquipmentRules.LatestTerms(db, units.Select(x => x.ProductId));
        foreach (var unit in units.Distinct())
        {
            var date = asOf(unit);
            states.TryGetValue(unit, out var state);
            var valid = warranties[unit].Where(w => EquipmentRules.Status(w, states, date) == "Active" && w.Start <= date).MaxBy(w => w.End);
            var expired = warranties[unit].Where(w => state?.Sale?.Id == w.SaleId && w.End < date).MaxBy(w => w.End);
            var supplierEnd = state?.LastIn is not null && terms.GetValueOrDefault(unit.ProductId)?.SupplierWarrantyMonths > 0 ? EquipmentRules.End(state.LastIn.BusinessDate, terms[unit.ProductId].SupplierWarrantyMonths) : (DateOnly?)null;
            var hint = valid is not null ? $"Under customer warranty until {valid.End:dd MMM yyyy}" + (valid.Coverage.Length > 0 ? $" ({valid.Coverage})" : "") + "."
                : expired is not null ? $"Customer warranty expired on {expired.End:dd MMM yyyy}. Repairs are chargeable."
                : state?.WithCustomer == true ? "No customer warranty is recorded for this unit."
                : state is null ? "This serial has no history here; it was not supplied by this branch." : "This unit is not with a customer.";
            result[unit] = new(hint, valid is not null, state?.WithCustomer == true ? state.Sale!.Party : "", supplierEnd, state?.WithCustomer == true);
        }
        return result;
    }

    // ---------------- queries ----------------

    public async Task<ServiceDeskView> Read()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var jobs = await db.ServiceJobs.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync();
        var ids = jobs.Select(x => x.Id).ToList();
        var events = (await db.ServiceJobEvents.AsNoTracking().Where(x => ids.Contains(x.JobId)).ToListAsync()).ToLookup(x => x.JobId);
        var docIds = events.SelectMany(g => g).Where(x => x.DocumentId is not null).Select(x => x.DocumentId!.Value).ToList();
        var documents = await db.Documents.AsNoTracking().Where(x => docIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var movements = (await db.Movements.AsNoTracking().Where(x => docIds.Contains(x.DocumentId)).ToListAsync()).ToLookup(x => x.DocumentId);
        var reversed = (await db.Documents.AsNoTracking().Where(x => x.Reverses != null && docIds.Contains(x.Reverses!.Value)).Select(x => x.Reverses!.Value).ToListAsync()).ToHashSet();
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var plans = await db.MaintenancePlans.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync();
        var planIds = plans.Select(x => x.Id).ToList();
        var ends = await db.MaintenancePlanEnds.AsNoTracking().Where(x => planIds.Contains(x.PlanId)).ToDictionaryAsync(x => x.PlanId);
        var names = await db.Users.AsNoTracking().Where(x => x.UserName != null).ToDictionaryAsync(x => x.Id, x => x.UserName!);
        string Name(string id) => names.GetValueOrDefault(id, id);
        var costs = SeesCosts(actor);
        var units = jobs.Where(x => x.ProductId is not null && x.Serial.Length > 0).Select(x => (x.ProductId!.Value, x.Serial)).Distinct().ToList();
        var reportDates = jobs.Where(x => x.ProductId is not null && x.Serial.Length > 0).GroupBy(x => (x.ProductId!.Value, x.Serial)).ToDictionary(g => g.Key, g => g.Max(x => x.ReportedOn));
        var positions = await Positions(db, actor.Branch, units, u => reportDates[u]);

        var views = jobs.Select(j =>
        {
            var job = new Job(j, events[j.Id].ToList(), reversed);
            var coverage = job.Coverage; var coverageEvent = job.CoverageEvent;
            var chargeParts = coverage is not null && (coverage.Billing == ServiceBilling.Chargeable || coverage.Billing == ServiceBilling.Warranty && !coverage.CoversParts);
            var chargeLabour = coverage is not null && (coverage.Billing == ServiceBilling.Chargeable || coverage.Billing == ServiceBilling.Warranty && !coverage.CoversLabour);
            var parts = job.Issues.OrderBy(x => x.Sequence).Select(e =>
            {
                var data = JsonSerializer.Deserialize<ServiceRules.IssueData>(e.DataJson)!; var doc = documents[e.DocumentId!.Value]; var m = movements[doc.Id].Single(); var p = products[m.ProductId];
                var returned = job.Returned(doc.Id);
                var returnCost = job.Events.Where(x => x.Kind == "PartReturned" && JsonSerializer.Deserialize<ServiceRules.ReturnData>(x.DataJson)!.IssueDocumentId == doc.Id).Sum(x => movements[x.DocumentId!.Value].Sum(r => r.Value));
                return new ServicePartView(doc.Id, doc.Number, doc.BusinessDate, p.Id, p.Sku, p.Name, p.Unit, -m.Quantity, returned, m.Location, m.Serial, m.Batch, data.UnitPrice, costs ? -m.Value - returnCost : null, chargeParts);
            }).ToArray();
            var labour = job.Labour.Select(l => new ServiceLabourView(l.Date, l.Engineer, l.Hours, l.Rate, l.Description, chargeLabour)).ToArray();
            var completion = job.Completion;
            var closing = job.Closing is { } c && !job.CloseReversed ? c : null;
            var closeDoc = closing?.DocumentId is Guid cd ? documents.GetValueOrDefault(cd) : null;
            positions.TryGetValue((j.ProductId ?? Guid.Empty, j.Serial), out var position);
            var plan = j.PlanId is null ? null : plans.FirstOrDefault(x => x.Id == j.PlanId);
            return new ServiceJobView(j.Id, j.Number, j.Customer, j.Site, j.Contact, j.ProductId, j.ProductId is Guid pid ? products[pid].Sku : "", j.Equipment, j.Serial, j.Problem, j.Priority, j.Source, j.ReportedOn,
                plan?.Number ?? "", j.PlanDue, job.Status, job.Engineer, job.Events.Where(x => x.Kind == "Diagnosed").MaxBy(x => x.Sequence)?.Text ?? "",
                completion?.Resolution ?? "", completion?.AcceptedBy ?? "", completion?.Date, job.Checklist, completion?.ChecklistDone ?? [],
                coverage is null ? null : new ServiceCoverageView(coverage.Billing, coverage.CoversParts, coverage.CoversLabour, coverageEvent!.Text, coverage.Exception, Name(coverageEvent.Actor)),
                position?.Hint ?? (j.Serial.Length == 0 ? "No serial number recorded; warranty cannot be checked." : ""), position?.WarrantyActive ?? false, position?.SupplierEnd,
                parts, labour, parts.Where(x => x.Chargeable).Sum(x => AccountingEngine.Money((x.Quantity - x.Returned) * x.UnitPrice)), labour.Where(x => x.Chargeable).Sum(x => AccountingEngine.Money(x.Hours * x.Rate)),
                costs ? parts.Sum(x => x.Cost ?? 0) : null, closeDoc?.Id, closeDoc?.Number ?? "", closeDoc?.Settlement ?? (closing is not null ? "No charge" : ""), closeDoc?.Total ?? 0,
                job.Claim?.Text ?? "", job.Events.OrderBy(x => x.Sequence).Select(e => new ServiceEventView(e.Sequence, e.Kind, e.Status, Describe(e, documents), Name(e.Actor), e.Time)).ToArray(), costs);
        }).OrderBy(x => x.Status is ServiceStatus.Closed or ServiceStatus.Cancelled).ThenByDescending(x => x.Priority).ThenBy(x => x.ReportedOn).ToArray();

        var planViews = plans.Select(p =>
        {
            ends.TryGetValue(p.Id, out var end);
            var visits = jobs.Where(x => x.PlanId == p.Id).ToList();
            var done = visits.Select(v => views.Single(x => x.Id == v.Id)).Where(x => x.CompletedOn is not null).ToList();
            return new MaintenancePlanView(p.Id, p.Number, p.Customer, p.Site, p.ProductId, products[p.ProductId].Sku, products[p.ProductId].Name, p.Serial, p.IntervalMonths, p.FirstDue,
                end is null ? NextDue(p, visits.Select(x => x.PlanDue!.Value).ToHashSet()) : null, EquipmentRules.Checklist(p.ChecklistJson), p.Engineer, p.Notes, end is null, end?.Reason ?? "", visits.Count, done.Max(x => x.CompletedOn));
        }).OrderBy(x => !x.Active).ThenBy(x => x.NextDue).ToArray();

        var states = await EquipmentRules.States(db, actor.Branch);
        var withCustomers = states.Where(x => x.Value.WithCustomer).Select(x => x.Key).ToList();
        var unitPositions = await Positions(db, actor.Branch, withCustomers, _ => Today);
        var serviceable = withCustomers.Select(k => new ServiceableUnit(k.Item1, products[k.Item1].Sku, products[k.Item1].Name, k.Item2, unitPositions[k].Customer, unitPositions[k].Hint, unitPositions[k].WarrantyActive))
            .OrderBy(x => x.Customer).ThenBy(x => x.Serial).ToArray();
        var stock = (await db.Movements.AsNoTracking().Where(x => x.Branch == actor.Branch).Select(x => new { x.ProductId, x.Quantity }).ToListAsync()).GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var partOptions = products.Values.Where(p => stock.GetValueOrDefault(p.Id) > 0).OrderBy(p => p.Sku).Select(p => new ServicePartOption(p.Id, p.Sku, p.Name, p.Unit, p.Tracking, p.Price, stock[p.Id])).ToArray();
        var locations = await db.StockLocations.AsNoTracking().Select(x => x.Code).OrderBy(x => x).ToListAsync();
        var rate = await db.SalesTaxRates.Where(x => x.Branch == actor.Branch && x.From <= Today).OrderByDescending(x => x.From).Select(x => (decimal?)x.Rate).FirstOrDefaultAsync() ?? Settings.TaxRate;
        return new(views, planViews, serviceable, partOptions, locations, rate);
    }
    private static DateOnly? NextDue(MaintenancePlan p, HashSet<DateOnly> generated)
    {
        for (var k = 0; k < 600; k++) { var due = p.FirstDue.AddMonths(k * p.IntervalMonths); if (!generated.Contains(due)) return due; }
        return null;
    }
    private static string ShortNumber(string? number) { var parts = (number ?? "").Split('-'); return parts.Length >= 3 && parts[^1].Length > 8 ? $"{parts[^2]}-{parts[^1][^8..].ToUpperInvariant()}" : number ?? ""; }
    private static string Describe(ServiceJobEvent e, IReadOnlyDictionary<Guid, BusinessDocument> documents)
    {
        switch (e.Kind)
        {
            case "Logged": return "Job logged";
            case "Assigned": return $"Assigned to {e.Text}";
            case "Diagnosed": return $"Diagnosis: {e.Text}";
            case "Labour": var l = JsonSerializer.Deserialize<ServiceRules.LabourData>(e.DataJson)!; return $"Labour: {l.Hours:0.##} h by {l.Engineer}" + (l.Description.Length > 0 ? $" — {l.Description}" : "");
            case "PartIssued": var i = JsonSerializer.Deserialize<ServiceRules.IssueData>(e.DataJson)!; return $"Part issued from stock: {i.Quantity:0.####} ({ShortNumber(documents.GetValueOrDefault(e.DocumentId!.Value)?.Number)})";
            case "PartReturned": var r = JsonSerializer.Deserialize<ServiceRules.ReturnData>(e.DataJson)!; return $"Unused part returned to stock: {r.Quantity:0.####} — {e.Text}";
            case "Coverage": var c = JsonSerializer.Deserialize<ServiceRules.CoverageData>(e.DataJson)!;
                var scope = c.Billing == ServiceBilling.Warranty ? (c.CoversParts && c.CoversLabour ? " (parts and labour)" : c.CoversParts ? " (parts only; labour charged)" : c.CoversLabour ? " (labour only; parts charged)" : " (nothing covered)") : "";
                return $"Billing decided: {c.Billing}{scope}" + (c.Exception ? " — exception approved" : "") + (e.Text.Length > 0 ? $": {e.Text}" : "");
            case "Completed": var d = JsonSerializer.Deserialize<ServiceRules.CompletionData>(e.DataJson)!; return $"Work completed on {d.Date:dd MMM yyyy}, accepted by {d.AcceptedBy}";
            case "Reopened": return $"Reopened: {e.Text}";
            case "Closed": return e.DocumentId is Guid doc && documents.TryGetValue(doc, out var x) ? (x.Kind == DocumentKind.ServiceInvoice ? $"Invoiced {ShortNumber(x.Number)}: NLe {x.Total:N2} ({x.Settlement})" : $"Closed at no charge; parts cost written off ({ShortNumber(x.Number)})") : "Closed at no charge";
            case "Cancelled": return $"Cancelled: {e.Text}";
            case "SupplierClaim": return $"Supplier claim lodged: {e.Text}";
            case "SupplierClaimOutcome": return $"Supplier claim outcome: {e.Text}";
            default: return e.Text;
        }
    }

    // ---------------- logging and work ----------------

    public async Task<Guid> Log(LogServiceJobCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.log", input, async db =>
        {
            if (input.ReportedOn > Today || input.ReportedOn < Today.AddDays(-365)) throw new BusinessException("The reported date must be within the last year and not in the future.");
            if (!Enum.IsDefined(input.Priority)) throw new BusinessException("Choose a priority.");
            var serial = (input.Serial ?? "").Trim().ToUpperInvariant();
            if (serial.Length > 100) throw new BusinessException("Serial number is limited to 100 characters.");
            var equipment = Optional(input.Equipment, "Equipment", 160);
            var customer = (input.Customer ?? "").Trim();
            if (input.ProductId is Guid pid)
            {
                var product = await db.Products.SingleOrDefaultAsync(x => x.Id == pid) ?? throw new BusinessException("Product not found.");
                if (equipment.Length == 0) equipment = product.Name;
                if (serial.Length > 0 && customer.Length == 0)
                {
                    var state = (await EquipmentRules.States(db, actor.Branch, [(pid, serial)])).GetValueOrDefault((pid, serial));
                    if (state?.WithCustomer == true) customer = state.Sale!.Party;
                }
            }
            if (equipment.Length == 0) throw new BusinessException("Describe the equipment, or choose the catalogue product.");
            var row = new ServiceJob
            {
                Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{Settings.Device}-SVC-{input.OperationId:N}", Customer = Rules.Required(customer, "Customer", 160),
                Site = Optional(input.Site, "Site", 160), Contact = Optional(input.Contact, "Contact", 120), ProductId = input.ProductId, Serial = serial, Equipment = equipment,
                Problem = Rules.Required(input.Problem, "Problem reported", 500), Priority = input.Priority, Source = "Customer request", ReportedOn = input.ReportedOn, Created = DateTimeOffset.UtcNow, CreatedBy = actor.Id
            };
            db.ServiceJobs.Add(row);
            var job = new Job(row, [], []);
            Event(db, job, actor, "Logged", ServiceStatus.Logged);
            var engineer = Optional(input.Engineer, "Engineer", 100);
            if (engineer.Length > 0) Event(db, job, actor, "Assigned", ServiceStatus.Assigned, engineer);
            return row.Id;
        });
    }

    public async Task<Guid> Assign(AssignServiceCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.assign", input, async db =>
        {
            var job = await Load(db, actor, input.JobId); Require(job, Working);
            var engineer = Rules.Required(input.Engineer, "Engineer", 100);
            if (engineer == job.Engineer) throw new BusinessException("The job is already assigned to this engineer.");
            return Event(db, job, actor, "Assigned", job.Status == ServiceStatus.Logged ? ServiceStatus.Assigned : job.Status, engineer);
        });
    }

    public async Task<Guid> Diagnose(ServiceTextCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.diagnose", input, async db =>
        {
            var job = await Load(db, actor, input.JobId); Require(job, Working);
            return Event(db, job, actor, "Diagnosed", Started(job), Rules.Required(input.Text, "Diagnosis", 500));
        });
    }

    public async Task<Guid> Note(ServiceTextCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.note", input, async db =>
        {
            var job = await Load(db, actor, input.JobId);
            return Event(db, job, actor, "Note", job.Status, Rules.Required(input.Text, "Note", 500));
        });
    }

    public async Task<Guid> AddLabour(AddLabourCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.labour", input, async db =>
        {
            var job = await Load(db, actor, input.JobId); Require(job, Working);
            if (input.Hours <= 0 || input.Hours > 24 || decimal.Round(input.Hours, 2) != input.Hours) throw new BusinessException("Hours must be more than 0 and at most 24, with up to two decimals.");
            if (input.Rate < 0 || input.Rate > 10_000_000 || decimal.Round(input.Rate, 2) != input.Rate) throw new BusinessException("The hourly rate must be between 0 and 10,000,000.");
            if (input.Date < job.Row.ReportedOn || input.Date > Today) throw new BusinessException("Labour must be dated between the reported date and today.");
            var data = new ServiceRules.LabourData(input.Date, Rules.Required(input.Engineer, "Engineer", 100), input.Hours, input.Rate, Optional(input.Description, "Work description", 160));
            return Event(db, job, actor, "Labour", Started(job), data: data);
        });
    }

    // ---------------- parts: ordinary stock documents, once each ----------------

    public async Task<Guid> IssuePart(IssuePartCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.part.issue", input, async db =>
        {
            var job = await Load(db, actor, input.JobId); Require(job, Working);
            await OpenDate(db, actor, input.Date);
            if (input.Date < job.Row.ReportedOn) throw new BusinessException("Parts cannot be issued before the job was reported.");
            var p = await db.Products.SingleOrDefaultAsync(x => x.Id == input.ProductId) ?? throw new BusinessException("Part not found in the catalogue.");
            await InventoryManagement.Active(db, actor.Branch, p.Id);
            Rules.Positive(input.Quantity, "Quantity");
            var location = Rules.Required(input.Location, "Warehouse", 40);
            if (!await db.StockLocations.AnyAsync(x => x.Code == location)) throw new BusinessException("Warehouse not found.");
            var serial = string.IsNullOrWhiteSpace(input.Serial) ? null : Rules.Required(input.Serial, "Serial", 100).ToUpperInvariant();
            var batch = string.IsNullOrWhiteSpace(input.Batch) ? null : Rules.Required(input.Batch, "Batch", 100).ToUpperInvariant();
            Rules.Tracking(p.Tracking, input.Quantity, serial, batch, input.Expiry, input.Date, true);
            if (p.Tracking != TrackingMode.Serial && serial is not null) throw new BusinessException("Serial is only allowed for serial-controlled parts.");
            if (p.Tracking is not TrackingMode.Batch and not TrackingMode.BatchExpiry && (batch is not null || input.Expiry is not null)) throw new BusinessException("Batch and expiry are only allowed for batch-controlled parts.");
            await InventoryManagement.SerialAvailable(db, actor.Branch, serial);
            var matching = await db.Movements.Where(x => x.Branch == actor.Branch && x.ProductId == p.Id && x.Location == location && x.Serial == serial && x.Batch == batch && x.Expiry == input.Expiry).ToListAsync();
            var available = matching.Sum(x => x.Quantity);
            var reserved = await CommerceRules.Reserved(db, actor.Branch, p.Id, location, serial, batch, input.Expiry);
            if (available - reserved < input.Quantity) throw new BusinessException($"Insufficient stock for {p.Name} in {location}. Unreserved available: {available - reserved} {p.Unit}.");
            await InventoryCost.GuardDate(db,actor.Branch,p.Id,input.Date);
            var cost = await InventoryCost.Issue(db,actor.Branch,p.Id,location,serial,batch,input.Expiry,input.Quantity,matching);
            var price = input.UnitPrice == 0 ? p.Price : input.UnitPrice;
            if (price < 0 || price > 1_000_000_000 || decimal.Round(price, 4) != price) throw new BusinessException("The part's charge price must be zero or positive.");
            var doc = new BusinessDocument { Id = Guid.NewGuid(), Number = $"{actor.Branch}-{Settings.Device}-SVP-{input.OperationId:N}", Branch = actor.Branch, Kind = DocumentKind.ServiceIssue, Party = job.Row.Customer,
                BusinessDate = input.Date, Total = 0, Settlement = $"Service job {job.Row.Number}", Detail = JsonSerializer.Serialize(new { JobId = job.Row.Id, Request = input, UnitPrice = price, Cost = cost }) };
            db.Documents.Add(doc);
            db.Movements.Add(new Movement { Id = Guid.NewGuid(), DocumentId = doc.Id, ProductId = p.Id, Branch = actor.Branch, Location = location, Serial = serial, Batch = batch, Expiry = input.Expiry, Quantity = -input.Quantity, Value = -cost });
            Journal(db, doc, new(ServiceRules.WorkInProgress, cost, 0), new(Account.Inventory, 0, cost));
            Event(db, job, actor, "PartIssued", Started(job), $"{p.Sku} {p.Name}", new ServiceRules.IssueData(p.Id, input.Quantity, price), doc.Id);
            return doc.Id;
        });
    }

    public async Task<Guid> ReturnPart(ReturnPartCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.part.return", input, async db =>
        {
            var job = await Load(db, actor, input.JobId); Require(job, [.. Working, ServiceStatus.Completed]);
            var issue = job.Issues.SingleOrDefault(x => x.DocumentId == input.IssueDocumentId) ?? throw new BusinessException("That part was not issued to this job.");
            var date = Today; await OpenDate(db, actor, date);
            var issued = await db.Movements.SingleAsync(x => x.DocumentId == input.IssueDocumentId);
            var outstanding = -issued.Quantity - job.Returned(input.IssueDocumentId);
            Rules.Positive(input.Quantity, "Quantity");
            if (input.Quantity > outstanding) throw new BusinessException($"Only {outstanding:0.####} of this part is still with the job.");
            var returnedValue = job.Events.Where(x => x.Kind == "PartReturned" && JsonSerializer.Deserialize<ServiceRules.ReturnData>(x.DataJson)!.IssueDocumentId == input.IssueDocumentId).Select(x => x.DocumentId!.Value).ToList();
            var alreadyValue = await db.Movements.Where(x => returnedValue.Contains(x.DocumentId)).SumAsync(x => x.Value);
            var value = input.Quantity == outstanding ? -issued.Value - alreadyValue : AccountingEngine.Money(-issued.Value * input.Quantity / -issued.Quantity);
            var doc = new BusinessDocument { Id = Guid.NewGuid(), Number = $"{actor.Branch}-{Settings.Device}-SVR-{input.OperationId:N}", Branch = actor.Branch, Kind = DocumentKind.ServiceReturn, Party = job.Row.Customer,
                BusinessDate = date, Total = 0, Settlement = $"Service job {job.Row.Number}", Detail = JsonSerializer.Serialize(new { JobId = job.Row.Id, Request = input }) };
            db.Documents.Add(doc);
            db.Movements.Add(new Movement { Id = Guid.NewGuid(), DocumentId = doc.Id, ProductId = issued.ProductId, Branch = actor.Branch, Location = issued.Location, Serial = issued.Serial, Batch = issued.Batch, Expiry = issued.Expiry, Quantity = input.Quantity, Value = value });
            Journal(db, doc, new(Account.Inventory, value, 0), new(ServiceRules.WorkInProgress, 0, value));
            Event(db, job, actor, "PartReturned", job.Status, Rules.Required(input.Reason, "Reason", 160), new ServiceRules.ReturnData(input.IssueDocumentId, input.Quantity), doc.Id);
            return doc.Id;
        });
    }
    private static void Journal(BusinessDb db, BusinessDocument doc, params Posting[] lines)
    {
        foreach (var p in AccountingEngine.Balanced(lines)) db.Journal.Add(new JournalLine { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = doc.Branch, Account = p.Account, Debit = p.Debit, Credit = p.Credit });
    }

    // ---------------- warranty claim / billing decision ----------------

    public async Task<Guid> DecideCoverage(CoverageCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.coverage", input, async db =>
        {
            var job = await Load(db, actor, input.JobId); Require(job, [.. Working, ServiceStatus.Completed]);
            if (!Enum.IsDefined(input.Billing)) throw new BusinessException("Choose who pays for this job.");
            var reason = Optional(input.Reason, "Reason", 160);
            var exception = false;
            var authority = actor.Permissions.Contains("pricing.manage");
            switch (input.Billing)
            {
                case ServiceBilling.Warranty:
                    var valid = job.Row.ProductId is Guid pid && job.Row.Serial.Length > 0 && (await Positions(db, actor.Branch, [(pid, job.Row.Serial)], _ => job.Row.ReportedOn))[(pid, job.Row.Serial)].WarrantyActive;
                    if (!valid)
                    {
                        if (!authority) throw new BusinessException("This unit has no customer warranty covering the reported date. A manager with pricing authority can approve a warranty exception with a reason.");
                        if (reason.Length == 0) throw new BusinessException("Give the reason for the warranty exception.");
                        exception = true;
                    }
                    if (!input.CoversParts && !input.CoversLabour) throw new BusinessException("A warranty claim covers parts, labour or both. Choose \"chargeable\" if neither is covered.");
                    break;
                case ServiceBilling.Contract:
                    var planned = job.Row.PlanId is not null || job.Row.ProductId is Guid cp && await db.MaintenancePlans.AnyAsync(x => x.Branch == actor.Branch && x.ProductId == cp && x.Serial == job.Row.Serial && !db.MaintenancePlanEnds.Any(e => e.PlanId == x.Id));
                    if (!planned)
                    {
                        if (!authority) throw new BusinessException("This unit has no active maintenance plan. A manager with pricing authority can approve contract cover with a reason.");
                        if (reason.Length == 0) throw new BusinessException("Give the reason for contract cover.");
                        exception = true;
                    }
                    break;
                case ServiceBilling.Goodwill:
                    if (!authority) throw new UnauthorizedAccessException("Free-of-charge (goodwill) service needs pricing authority.");
                    if (reason.Length == 0) throw new BusinessException("Give the reason for free-of-charge service.");
                    break;
            }
            var data = new ServiceRules.CoverageData(input.Billing, input.Billing != ServiceBilling.Warranty || input.CoversParts, input.Billing != ServiceBilling.Warranty || input.CoversLabour, exception);
            return Event(db, job, actor, "Coverage", job.Status, reason, data);
        });
    }

    // ---------------- completion and closing ----------------

    public async Task<Guid> Complete(CompleteServiceCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.complete", input, async db =>
        {
            var job = await Load(db, actor, input.JobId); Require(job, ServiceStatus.Assigned, ServiceStatus.InProgress);
            if (input.Completed < job.Row.ReportedOn || input.Completed > Today) throw new BusinessException("The completion date must be between the reported date and today.");
            var done = (input.ChecklistDone ?? []).Select(x => x.Trim()).ToList();
            var missing = job.Checklist.Where(c => !done.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            if (missing.Count > 0) throw new BusinessException($"Complete every check first. Outstanding: {string.Join("; ", missing)}.");
            var data = new ServiceRules.CompletionData(input.Completed, Rules.Required(input.Resolution, "Work done / resolution", 500), Rules.Required(input.AcceptedBy, "Accepted by (customer representative)", 120), [.. job.Checklist]);
            return Event(db, job, actor, "Completed", ServiceStatus.Completed, data: data);
        });
    }

    public async Task<Guid> Reopen(ServiceTextCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.reopen", input, async db =>
        {
            var job = await Load(db, actor, input.JobId);
            if (job.Status != ServiceStatus.Completed || job.CloseReversed) throw new BusinessException(job.CloseReversed ? "The invoice was reversed; close the job again with the corrected billing." : "Only completed, unbilled work can be reopened.");
            return Event(db, job, actor, "Reopened", ServiceStatus.InProgress, Rules.Required(input.Text, "Reason", 160));
        });
    }

    public async Task<Guid> Close(CloseServiceCommand input)
    {
        var actor = await Authorize(false, "sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "service.close", input, async db =>
        {
            var job = await Load(db, actor, input.JobId); Require(job, ServiceStatus.Completed);
            var coverage = job.Coverage ?? throw new BusinessException("Decide who pays (chargeable, warranty, contract or goodwill) before closing.");
            await OpenDate(db, actor, input.Date);
            if (input.Date < job.Completion!.Date) throw new BusinessException("The closing date cannot be before the work was completed.");
            var chargeParts = coverage.Billing == ServiceBilling.Chargeable || coverage.Billing == ServiceBilling.Warranty && !coverage.CoversParts;
            var chargeLabour = coverage.Billing == ServiceBilling.Chargeable || coverage.Billing == ServiceBilling.Warranty && !coverage.CoversLabour;

            var lines = new List<ServiceRules.InvoiceLine>();
            decimal partsCost = 0;
            var issueIds = job.Issues.Select(x => x.DocumentId!.Value).ToList();
            var returnIds = job.Events.Where(x => x.Kind == "PartReturned").Select(x => x.DocumentId!.Value).ToList();
            var movements = await db.Movements.Where(x => issueIds.Contains(x.DocumentId) || returnIds.Contains(x.DocumentId)).ToListAsync();
            var products = await db.Products.ToDictionaryAsync(x => x.Id);
            foreach (var issue in job.Issues.OrderBy(x => x.Sequence))
            {
                var data = JsonSerializer.Deserialize<ServiceRules.IssueData>(issue.DataJson)!;
                var quantity = data.Quantity - job.Returned(issue.DocumentId!.Value);
                var cost = -movements.Where(x => x.DocumentId == issue.DocumentId).Sum(x => x.Value) - job.Events.Where(x => x.Kind == "PartReturned" && JsonSerializer.Deserialize<ServiceRules.ReturnData>(x.DataJson)!.IssueDocumentId == issue.DocumentId).Sum(r => movements.Where(m => m.DocumentId == r.DocumentId).Sum(m => m.Value));
                partsCost += cost;
                if (!chargeParts || quantity == 0) continue;
                if (data.UnitPrice <= 0) throw new BusinessException($"Set a charge price for {products[data.ProductId].Name} before billing; issue it again with a price.");
                await CommerceRules.Price(db, data.ProductId, data.UnitPrice, 1);
                lines.Add(new($"{products[data.ProductId].Sku} {products[data.ProductId].Name}", quantity, data.UnitPrice, AccountingEngine.Money(quantity * data.UnitPrice), await TaxRules.Category(db, data.ProductId, input.Date)));
            }
            if (chargeLabour)
                foreach (var l in job.Labour.Where(l => l.Rate > 0))
                    lines.Add(new($"Labour {l.Date:dd MMM yyyy}, {l.Engineer}" + (l.Description.Length > 0 ? $": {l.Description}" : ""), l.Hours, l.Rate, AccountingEngine.Money(l.Hours * l.Rate)));
            var net = lines.Sum(x => x.Amount);
            if (net == 0 && input.ReliefGrantId is not null) throw new BusinessException("GST relief requires a chargeable invoice. Remove the certificate from this no-charge job.");
            var chargedCost = chargeParts ? partsCost : 0; var absorbedCost = partsCost - chargedCost;

            Guid? documentId = null;
            if (net > 0)
            {
                var rate = await db.SalesTaxRates.Where(x => x.Branch == actor.Branch && x.From <= input.Date).OrderByDescending(x => x.From).Select(x => (decimal?)x.Rate).FirstOrDefaultAsync() ?? Settings.TaxRate;
                if (rate is < 0 or > 1) throw new BusinessException("Tax configuration is invalid.");
                var party = await db.Parties.SingleOrDefaultAsync(x => x.Branch == actor.Branch && x.NormalizedName == job.Row.Customer.ToUpperInvariant());
                if (party is not null && party.Type == PartyType.Supplier) throw new BusinessException("The customer on this job is registered as a supplier only.");
                var reliefGrant = input.ReliefGrantId is Guid grantId ? await TaxRules.ValidateRelief(db,actor,grantId,party?.Id,input.Date,lines.Where(l => l.Category == TaxCategory.Standard).Sum(l => l.Amount)) : null;
                if (reliefGrant is not null) lines = lines.Select(l => l.Category == TaxCategory.Standard ? l with { Category = TaxCategory.Relieved } : l).ToList();
                var taxable = lines.Where(x => x.Category == TaxCategory.Standard).Sum(x => x.Amount); var tax = AccountingEngine.Money(taxable*rate);
                if (input.Payment is not null && !Enum.IsDefined(input.Payment.Value)) throw new BusinessException("Unsupported payment method.");
                if (input.Payment is PaymentMethod.Cheque or PaymentMethod.BankTransfer)
                {
                    Rules.Required(input.PaymentReference, "Payment reference"); Rules.Required(input.Bank, "Bank");
                    if (input.PaymentDate is null) throw new BusinessException("Payment date is required.");
                }
                if (input.DueDate < input.Date) throw new BusinessException("Due date cannot precede the invoice date.");
                if (input.Payment is null) await ServiceRules.CheckCredit(db, actor.Branch, party, job.Row.Customer, net + tax);
                var doc = new BusinessDocument { Id = Guid.NewGuid(), Number = $"{actor.Branch}-{Settings.Device}-SIN-{input.OperationId:N}", Branch = actor.Branch, Kind = DocumentKind.ServiceInvoice,
                    Party = party?.Name ?? job.Row.Customer, BusinessDate = input.Date, DueDate = input.DueDate ?? input.Date.AddDays(party?.PaymentDays ?? 0), Total = net + tax, Settlement = input.Payment?.ToString() ?? "Credit",
                    Detail = JsonSerializer.Serialize(new { JobId = job.Row.Id,Request = input,coverage.Billing,Lines = lines,Net = net,TaxRate = rate,Tax = tax,PartsCost = partsCost,Taxable = taxable,ZeroRated = lines.Where(x => x.Category == TaxCategory.ZeroRated).Sum(x => x.Amount),Exempt = lines.Where(x => x.Category == TaxCategory.Exempt).Sum(x => x.Amount),Relieved = lines.Where(x => x.Category == TaxCategory.Relieved).Sum(x => x.Amount),ReliefGrantId = input.ReliefGrantId,ReliefAuthorityReference = reliefGrant?.AuthorityReference }) };
                db.Documents.Add(doc);
                var debit = input.Payment switch { PaymentMethod.Cash => Account.Cash, PaymentMethod.BankTransfer => Account.Bank, PaymentMethod.Cheque => Account.ChequesInHand, _ => Account.Receivables };
                Journal(db, doc, new(debit, net + tax, 0), new(ServiceRules.Income, 0, net), new(Account.TaxPayable, 0, tax),
                    new(Account.CostOfSales, chargedCost, 0), new(ServiceRules.Costs, absorbedCost, 0), new(ServiceRules.WorkInProgress, 0, partsCost));
                if (input.Payment == PaymentMethod.Cheque) await BankingRules.NewCheque(db, doc, input.PaymentReference!, input.Bank!, input.PaymentDate!.Value);
                documentId = doc.Id;
            }
            else if (partsCost != 0)
            {
                if (input.Payment is not null) throw new BusinessException("Nothing is chargeable on this job, so no payment is taken.");
                var doc = new BusinessDocument { Id = Guid.NewGuid(), Number = $"{actor.Branch}-{Settings.Device}-SCL-{input.OperationId:N}", Branch = actor.Branch, Kind = DocumentKind.ServiceClose, Party = job.Row.Customer,
                    BusinessDate = input.Date, Total = 0, Settlement = "No charge", Detail = JsonSerializer.Serialize(new { JobId = job.Row.Id, Request = input, coverage.Billing, PartsCost = partsCost }) };
                db.Documents.Add(doc);
                Journal(db, doc, new(ServiceRules.Costs, partsCost, 0), new(ServiceRules.WorkInProgress, 0, partsCost));
                documentId = doc.Id;
            }
            else if (input.Payment is not null) throw new BusinessException("Nothing is chargeable on this job, so no payment is taken.");
            Event(db, job, actor, "Closed", ServiceStatus.Closed, data: new { Net = net, PartsCost = partsCost }, document: documentId);
            return documentId ?? job.Row.Id;
        });
    }

    public async Task<Guid> Cancel(ServiceTextCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.cancel", input, async db =>
        {
            var job = await Load(db, actor, input.JobId); Require(job, [.. Working, ServiceStatus.Completed]);
            if (job.CloseReversed) throw new BusinessException("The invoice was reversed; close the job again rather than cancelling it.");
            if (job.Issues.Any(i => JsonSerializer.Deserialize<ServiceRules.IssueData>(i.DataJson)!.Quantity > job.Returned(i.DocumentId!.Value))) throw new BusinessException("Return the parts issued to this job to stock first, or close the job so their cost is accounted for.");
            return Event(db, job, actor, "Cancelled", ServiceStatus.Cancelled, Rules.Required(input.Text, "Cancellation reason", 160));
        });
    }

    // ---------------- supplier warranty claims ----------------

    public async Task<Guid> SupplierClaim(SupplierClaimCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.supplier.claim", input, async db =>
        {
            var job = await Load(db, actor, input.JobId);
            if (job.Status == ServiceStatus.Cancelled) throw new BusinessException("This service job is cancelled.");
            if (job.Row.ProductId is null || job.Row.Serial.Length == 0) throw new BusinessException("A supplier claim needs the equipment's product and serial number.");
            if (job.Claim is { Open: true }) throw new BusinessException("A supplier claim is already open on this job. Record its outcome first.");
            var data = new ServiceRules.ClaimData(Rules.Required(input.Supplier, "Supplier", 160), Rules.Required(input.Reference, "Claim reference", 100));
            var note = Optional(input.Note, "Note", 200);
            return Event(db, job, actor, "SupplierClaim", job.Status, $"{data.Supplier}, ref {data.Reference}" + (note.Length > 0 ? $" — {note}" : ""), data);
        });
    }

    public async Task<Guid> SupplierClaimOutcome(SupplierClaimOutcomeCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.supplier.outcome", input, async db =>
        {
            var job = await Load(db, actor, input.JobId);
            if (job.Claim is not { Open: true }) throw new BusinessException("There is no open supplier claim on this job.");
            var note = Optional(input.Note, "Note", 200);
            return Event(db, job, actor, "SupplierClaimOutcome", job.Status, Rules.Required(input.Outcome, "Outcome", 100) + (note.Length > 0 ? $" — {note}" : ""));
        });
    }

    // ---------------- preventive maintenance ----------------

    public async Task<Guid> CreatePlan(MaintenancePlanCommand input)
    {
        var actor = await Authorize(false, "sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "service.plan.create", input, async db =>
        {
            var serial = Rules.Required(input.Serial, "Serial", 100).ToUpperInvariant();
            var state = (await EquipmentRules.States(db, actor.Branch, [(input.ProductId, serial)])).GetValueOrDefault((input.ProductId, serial));
            if (state?.WithCustomer != true) throw new BusinessException("Maintenance plans are for units supplied to a customer. This serial is not with a customer.");
            if (input.IntervalMonths is < 1 or > 60) throw new BusinessException("Choose a visit interval of 1 to 60 months.");
            if (input.FirstDue < Today.AddDays(-365) || input.FirstDue > Today.AddDays(730)) throw new BusinessException("The first visit must be within a year before or two years after today.");
            if (await db.MaintenancePlans.AnyAsync(x => x.Branch == actor.Branch && x.ProductId == input.ProductId && x.Serial == serial && !db.MaintenancePlanEnds.Any(e => e.PlanId == x.Id))) throw new BusinessException("This unit already has an active maintenance plan. End it before starting a new one.");
            var plan = new MaintenancePlan
            {
                Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{Settings.Device}-PM-{input.OperationId:N}", Customer = state.Sale!.Party, Site = Optional(input.Site, "Site", 160),
                ProductId = input.ProductId, Serial = serial, IntervalMonths = input.IntervalMonths, FirstDue = input.FirstDue, ChecklistJson = JsonSerializer.Serialize(Checklist(input.Checklist)),
                Engineer = Optional(input.Engineer, "Engineer", 100), Notes = Optional(input.Notes, "Notes", 200), Created = DateTimeOffset.UtcNow, CreatedBy = actor.Id
            };
            db.MaintenancePlans.Add(plan); return plan.Id;
        });
    }

    public async Task<Guid> EndPlan(EndMaintenancePlanCommand input)
    {
        var actor = await Authorize(false, "sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "service.plan.end", input, async db =>
        {
            var plan = await db.MaintenancePlans.SingleOrDefaultAsync(x => x.Id == input.PlanId && x.Branch == actor.Branch) ?? throw new BusinessException("Maintenance plan not found.");
            if (await db.MaintenancePlanEnds.AnyAsync(x => x.PlanId == plan.Id)) throw new BusinessException("This plan has already ended.");
            var end = new MaintenancePlanEnd { Id = Guid.NewGuid(), PlanId = plan.Id, Reason = Rules.Required(input.Reason, "Reason", 160), Actor = actor.Id, Time = DateTimeOffset.UtcNow };
            db.MaintenancePlanEnds.Add(end); return end.Id;
        });
    }

    /// <summary>Creates a planned service job for every maintenance visit due up to the given date that has no job yet.</summary>
    public async Task<Guid> GenerateDue(GenerateMaintenanceCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "service.plan.generate", input, async db =>
        {
            if (input.Through < Today || input.Through > Today.AddDays(90)) throw new BusinessException("Generate visits due from today up to 90 days ahead.");
            var plans = await db.MaintenancePlans.Where(x => x.Branch == actor.Branch && !db.MaintenancePlanEnds.Any(e => e.PlanId == x.Id)).ToListAsync();
            var ids = plans.Select(x => x.Id).ToList();
            var generated = (await db.ServiceJobs.Where(x => x.PlanId != null && ids.Contains(x.PlanId!.Value)).Select(x => new { x.PlanId, x.PlanDue }).ToListAsync()).Select(x => (x.PlanId!.Value, x.PlanDue!.Value)).ToHashSet();
            var states = await EquipmentRules.States(db, actor.Branch, plans.Select(p => (p.ProductId, p.Serial)).ToList());
            Guid first = Guid.Empty; var count = 0;
            foreach (var plan in plans.OrderBy(x => x.FirstDue))
            {
                if (!states.TryGetValue((plan.ProductId, plan.Serial), out var state) || !state.WithCustomer || !string.Equals(state.Sale!.Party, plan.Customer, StringComparison.OrdinalIgnoreCase)) continue; // unit returned or changed hands
                var product = await db.Products.SingleAsync(x => x.Id == plan.ProductId);
                for (var k = 0; k < 600; k++)
                {
                    var due = plan.FirstDue.AddMonths(k * plan.IntervalMonths);
                    if (due > input.Through) break;
                    if (generated.Contains((plan.Id, due))) continue;
                    if (++count > 200) throw new BusinessException("More than 200 visits are due. Generate a shorter period first.");
                    var row = new ServiceJob
                    {
                        Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{Settings.Device}-SVC-{Guid.NewGuid():N}", Customer = plan.Customer, Site = plan.Site, ProductId = plan.ProductId, Serial = plan.Serial,
                        Equipment = product.Name, Problem = $"Preventive maintenance visit due {due:dd MMM yyyy}" + (plan.Notes.Length > 0 ? $". {plan.Notes}" : ""), Priority = ServicePriority.Normal, Source = "Preventive",
                        ReportedOn = due < Today ? due : Today, PlanId = plan.Id, PlanDue = due, ChecklistJson = plan.ChecklistJson, Created = DateTimeOffset.UtcNow, CreatedBy = actor.Id
                    };
                    db.ServiceJobs.Add(row);
                    var job = new Job(row, [], []);
                    Event(db, job, actor, "Logged", ServiceStatus.Logged, $"Generated from maintenance plan {plan.Number}");
                    if (plan.Engineer.Length > 0) Event(db, job, actor, "Assigned", ServiceStatus.Assigned, plan.Engineer);
                    Event(db, job, actor, "Coverage", job.Status, $"Maintenance plan {plan.Number}", new ServiceRules.CoverageData(ServiceBilling.Contract, true, true, false));
                    if (first == Guid.Empty) first = row.Id;
                }
            }
            return first;
        });
    }
}
