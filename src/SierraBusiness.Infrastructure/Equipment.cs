using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

// ---------------- append-only entities (protected by database guards and the integrity seal chain) ----------------
public sealed class EquipmentTermsVersion
{
    public Guid Id { get; set; } public Guid ProductId { get; set; } public int Version { get; set; }
    public int CustomerWarrantyMonths { get; set; } public WarrantyStart StartsAt { get; set; } public int SupplierWarrantyMonths { get; set; }
    public bool RequiresInstallation { get; set; } public string ChecklistJson { get; set; } = "[]"; public string Coverage { get; set; } = ""; public string CreatedBy { get; set; } = "";
}
public sealed class ProductLink { public Guid Id { get; set; } public Guid EquipmentProductId { get; set; } public Guid ItemProductId { get; set; } public CompatibilityKind Kind { get; set; } public string Note { get; set; } = ""; public string CreatedBy { get; set; } = ""; }
public sealed class ProductLinkRemoval { public Guid Id { get; set; } public Guid LinkId { get; set; } public string Reason { get; set; } = ""; public string CreatedBy { get; set; } = ""; }
public sealed class UnitProfile { public Guid Id { get; set; } public Guid ProductId { get; set; } public string Serial { get; set; } = ""; public int Version { get; set; } public string SpecificationsJson { get; set; } = "[]"; public string Accessories { get; set; } = ""; public string Notes { get; set; } = ""; public string CreatedBy { get; set; } = ""; }
public sealed class Installation
{
    public Guid Id { get; set; } public string Branch { get; set; } = ""; public string Number { get; set; } = ""; public Guid SaleId { get; set; } public string Customer { get; set; } = "";
    public string Site { get; set; } = ""; public DateOnly Scheduled { get; set; } public string Engineer { get; set; } = ""; public string Notes { get; set; } = ""; public string CreatedBy { get; set; } = "";
}
public sealed class InstallationUnit { public Guid Id { get; set; } public Guid InstallationId { get; set; } public Guid ProductId { get; set; } public string Serial { get; set; } = ""; public string ChecklistJson { get; set; } = "[]"; }
public sealed class InstallationCompletion { public Guid Id { get; set; } public Guid InstallationId { get; set; } public DateOnly Completed { get; set; } public string ChecklistDoneJson { get; set; } = "[]"; public string AcceptedBy { get; set; } = ""; public string TrainingGivenTo { get; set; } = ""; public string Notes { get; set; } = ""; public string CreatedBy { get; set; } = ""; }
public sealed class InstallationCancellation { public Guid Id { get; set; } public Guid InstallationId { get; set; } public string Reason { get; set; } = ""; public string CreatedBy { get; set; } = ""; }
/// <summary>Customer warranty on one sold serial. Void (derived) once the sale is reversed or the unit comes back.</summary>
public sealed class Warranty
{
    public Guid Id { get; set; } public string Branch { get; set; } = ""; public Guid ProductId { get; set; } public string Serial { get; set; } = ""; public string Customer { get; set; } = "";
    public Guid SaleId { get; set; } public Guid? InstallationId { get; set; } public DateOnly Start { get; set; } public DateOnly End { get; set; } public int Months { get; set; } public string Coverage { get; set; } = ""; public string CreatedBy { get; set; } = "";
}

/// <summary>Serial history and warranty rules shared by the equipment service and posting.</summary>
public static class EquipmentRules
{
    public sealed record Step(Movement Movement, BusinessDocument Document, DateTimeOffset Time, bool Effective);
    /// <summary>Where one serial unit is now, from its effective stock movements.</summary>
    public sealed record State(IReadOnlyList<Step> Steps, decimal OnHand, string Location, BusinessDocument? Sale, BusinessDocument? LastOut, BusinessDocument? LastIn)
    {
        public bool WithCustomer => OnHand <= 0 && Sale is not null;
    }

    public static async Task<Dictionary<Guid, EquipmentTermsVersion>> LatestTerms(BusinessDb db, IEnumerable<Guid> productIds, CancellationToken ct = default)
    {
        var ids = productIds.Distinct().ToList();
        return (await db.EquipmentTerms.Where(x => ids.Contains(x.ProductId)).ToListAsync(ct)).GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.MaxBy(x => x.Version)!);
    }
    public static IReadOnlyList<string> Checklist(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? [];
    public static DateOnly End(DateOnly start, int months) => start.AddMonths(months).AddDays(-1);

    public static async Task<Dictionary<(Guid, string), State>> States(BusinessDb db, string branch, IReadOnlyCollection<(Guid ProductId, string Serial)>? units = null, CancellationToken ct = default)
    {
        var query = db.Movements.Where(x => x.Branch == branch && x.Serial != null);
        if (units is not null) { var serials = units.Select(x => x.Serial).Distinct().ToList(); query = query.Where(x => serials.Contains(x.Serial!)); }
        var movements = await query.ToListAsync(ct);
        var docIds = movements.Select(x => x.DocumentId).Distinct().ToList();
        var documents = await db.Documents.Where(x => docIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var reversed = (await db.Documents.Where(x => x.Reverses != null && docIds.Contains(x.Reverses!.Value)).Select(x => x.Reverses!.Value).ToListAsync(ct)).ToHashSet();
        var times = (await db.Operations.Where(x => docIds.Contains(x.ResultId)).Select(x => new { x.ResultId, x.Time }).ToListAsync(ct)).GroupBy(x => x.ResultId).ToDictionary(g => g.Key, g => g.Min(x => x.Time));
        var result = new Dictionary<(Guid, string), State>();
        foreach (var unit in movements.GroupBy(x => (x.ProductId, x.Serial!)))
        {
            if (units is not null && !units.Contains(unit.Key)) continue;
            var steps = unit.Select(m => { var d = documents[m.DocumentId]; return new Step(m, d, times.TryGetValue(d.Id, out var t) ? t : new DateTimeOffset(d.BusinessDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), d.Kind != DocumentKind.Reversal && !reversed.Contains(d.Id)); })
                .OrderBy(x => x.Time).ThenBy(x => x.Movement.Quantity).ToList();
            var effective = steps.Where(x => x.Effective && x.Movement.Quantity != 0).ToList();
            var onHand = unit.Sum(x => x.Quantity);
            var location = unit.GroupBy(x => x.Location).FirstOrDefault(g => g.Sum(x => x.Quantity) > 0)?.Key ?? "";
            var lastOut = effective.LastOrDefault(x => x.Movement.Quantity < 0)?.Document;
            var lastIn = effective.LastOrDefault(x => x.Movement.Quantity > 0 && x.Document.Kind is DocumentKind.Receipt or DocumentKind.OpeningStock)?.Document;
            var last = effective.LastOrDefault();
            var sale = onHand <= 0 && last is not null && last.Movement.Quantity < 0 && last.Document.Kind is DocumentKind.Sale or DocumentKind.Delivery ? last.Document : null;
            result[unit.Key] = new(steps, onHand, location, sale, lastOut, lastIn);
        }
        return result;
    }

    /// <summary>Starts customer warranties for serial units leaving stock on a sale or delivery, where the product's terms start at sale.</summary>
    public static async Task StartAtSale(BusinessDb db, BusinessDocument stockDocument, IEnumerable<Movement> movements, string actor, CancellationToken ct = default)
    {
        var units = movements.Where(x => x.Serial is not null && x.Quantity < 0).ToList();
        if (units.Count == 0) return;
        var terms = await LatestTerms(db, units.Select(x => x.ProductId), ct);
        foreach (var m in units)
            if (terms.TryGetValue(m.ProductId, out var t) && t.CustomerWarrantyMonths > 0 && t.StartsAt == WarrantyStart.Sale)
                db.Warranties.Add(New(stockDocument, m.ProductId, m.Serial!, stockDocument.BusinessDate, t, null, actor));
    }
    public static Warranty New(BusinessDocument sale, Guid productId, string serial, DateOnly start, EquipmentTermsVersion terms, Guid? installation, string actor) => new()
    {
        Id = Guid.NewGuid(), Branch = sale.Branch, ProductId = productId, Serial = serial, Customer = sale.Party, SaleId = sale.Id, InstallationId = installation,
        Start = start, End = End(start, terms.CustomerWarrantyMonths), Months = terms.CustomerWarrantyMonths, Coverage = terms.Coverage, CreatedBy = actor
    };
    public static string Status(Warranty w, IReadOnlyDictionary<(Guid, string), State> states, DateOnly today) =>
        !states.TryGetValue((w.ProductId, w.Serial), out var s) || s.Sale?.Id != w.SaleId ? "Void" : today > w.End ? "Expired" : "Active";
}

public sealed class EquipmentService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IEquipmentService
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private async Task<Actor> Authorize(params string[] permissions)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        foreach (var p in permissions) actor.Demand(p);
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }
    private static string Normalize(string serial) => Rules.Required(serial, "Serial number", 100).ToUpperInvariant();

    // ---------------- terms and compatibility ----------------

    public async Task<IReadOnlyList<EquipmentTerms>> Terms()
    {
        await Authorize(); await using var db = await factory.CreateDbContextAsync();
        return (await db.EquipmentTerms.AsNoTracking().ToListAsync()).GroupBy(x => x.ProductId).Select(g => g.MaxBy(x => x.Version)!)
            .Select(t => new EquipmentTerms(t.ProductId, t.Version, t.CustomerWarrantyMonths, t.StartsAt, t.SupplierWarrantyMonths, t.RequiresInstallation, EquipmentRules.Checklist(t.ChecklistJson), t.Coverage)).ToArray();
    }

    public async Task<Guid> SaveTerms(SaveEquipmentTerms input)
    {
        var actor = await Authorize("catalogue.write");
        return await Commands.Run(factory, actor, input.OperationId, "equipment.terms", input, async db =>
        {
            var t = input.Terms ?? throw new BusinessException("Enter the equipment terms.");
            if (!await db.Products.AnyAsync(x => x.Id == t.ProductId)) throw new BusinessException("Product not found.");
            if (t.CustomerWarrantyMonths is < 0 or > 120 || t.SupplierWarrantyMonths is < 0 or > 120) throw new BusinessException("Warranty periods are 0 to 120 months.");
            if (!Enum.IsDefined(t.StartsAt)) throw new BusinessException("Choose when the customer warranty starts.");
            if (t.StartsAt == WarrantyStart.Installation && !t.RequiresInstallation) throw new BusinessException("A warranty that starts at installation needs \"installation required\".");
            var checklist = (t.Checklist ?? []).Select(x => x?.Trim() ?? "").Where(x => x.Length > 0).ToList();
            if (checklist.Count > 30 || checklist.Any(x => x.Length > 120) || checklist.Distinct(StringComparer.OrdinalIgnoreCase).Count() != checklist.Count) throw new BusinessException("Use up to 30 different checklist items of at most 120 characters.");
            if ((t.Coverage ?? "").Length > 200) throw new BusinessException("Coverage notes are limited to 200 characters.");
            var current = await db.EquipmentTerms.Where(x => x.ProductId == t.ProductId).OrderByDescending(x => x.Version).FirstOrDefaultAsync();
            if ((current?.Version ?? 0) != t.Version) throw new BusinessException("These terms were changed by someone else. Refresh before saving.");
            var row = new EquipmentTermsVersion { Id = Guid.NewGuid(), ProductId = t.ProductId, Version = t.Version + 1, CustomerWarrantyMonths = t.CustomerWarrantyMonths, StartsAt = t.StartsAt, SupplierWarrantyMonths = t.SupplierWarrantyMonths,
                RequiresInstallation = t.RequiresInstallation, ChecklistJson = JsonSerializer.Serialize(checklist), Coverage = (t.Coverage ?? "").Trim(), CreatedBy = actor.Id };
            db.EquipmentTerms.Add(row); return row.Id;
        });
    }

    public async Task<IReadOnlyList<CompatibilityLink>> Compatibility()
    {
        await Authorize(); await using var db = await factory.CreateDbContextAsync();
        return await db.ProductLinks.AsNoTracking().Where(x => !db.ProductLinkRemovals.Any(r => r.LinkId == x.Id)).Select(x => new CompatibilityLink(x.Id, x.EquipmentProductId, x.ItemProductId, x.Kind, x.Note)).ToArrayAsync();
    }

    public async Task<Guid> Link(LinkCompatibility input)
    {
        var actor = await Authorize("catalogue.write");
        return await Commands.Run(factory, actor, input.OperationId, "equipment.link", input, async db =>
        {
            if (!Enum.IsDefined(input.Kind)) throw new BusinessException("Choose accessory, consumable or spare part.");
            if (input.EquipmentProductId == input.ItemProductId) throw new BusinessException("Link two different products.");
            if (await db.Products.CountAsync(x => x.Id == input.EquipmentProductId || x.Id == input.ItemProductId) != 2) throw new BusinessException("Product not found.");
            if (await db.ProductLinks.AnyAsync(x => x.EquipmentProductId == input.EquipmentProductId && x.ItemProductId == input.ItemProductId && !db.ProductLinkRemovals.Any(r => r.LinkId == x.Id))) throw new BusinessException("These products are already linked.");
            if ((input.Note ?? "").Length > 120) throw new BusinessException("Notes are limited to 120 characters.");
            var link = new ProductLink { Id = Guid.NewGuid(), EquipmentProductId = input.EquipmentProductId, ItemProductId = input.ItemProductId, Kind = input.Kind, Note = (input.Note ?? "").Trim(), CreatedBy = actor.Id };
            db.ProductLinks.Add(link); return link.Id;
        });
    }

    public async Task<Guid> Unlink(UnlinkCompatibility input)
    {
        var actor = await Authorize("catalogue.write");
        return await Commands.Run(factory, actor, input.OperationId, "equipment.unlink", input, async db =>
        {
            if (!await db.ProductLinks.AnyAsync(x => x.Id == input.LinkId) || await db.ProductLinkRemovals.AnyAsync(x => x.LinkId == input.LinkId)) throw new BusinessException("Link not found or already removed.");
            var removal = new ProductLinkRemoval { Id = Guid.NewGuid(), LinkId = input.LinkId, Reason = Rules.Required(input.Reason, "Reason", 160), CreatedBy = actor.Id };
            db.ProductLinkRemovals.Add(removal); return removal.Id;
        });
    }

    public async Task<IReadOnlyList<CompatibleItemView>> CompatibleWith(Guid productId)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        return await Compatible(db, actor, productId);
    }
    private static async Task<IReadOnlyList<CompatibleItemView>> Compatible(BusinessDb db, Actor actor, Guid productId)
    {
        var links = await db.ProductLinks.AsNoTracking().Where(x => (x.EquipmentProductId == productId || x.ItemProductId == productId) && !db.ProductLinkRemovals.Any(r => r.LinkId == x.Id)).ToListAsync();
        var other = links.Select(x => x.EquipmentProductId == productId ? x.ItemProductId : x.EquipmentProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(x => other.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var stock = (await db.Movements.AsNoTracking().Where(x => x.Branch == actor.Branch && other.Contains(x.ProductId)).Select(x => new { x.ProductId, x.Quantity }).ToListAsync()).GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        return links.Select(l => { var reverse = l.ItemProductId == productId; var p = products[reverse ? l.EquipmentProductId : l.ItemProductId]; return new CompatibleItemView(l.Id, p.Id, p.Sku, p.Name, l.Kind, l.Note, stock.GetValueOrDefault(p.Id), reverse); })
            .OrderBy(x => x.Reverse).ThenBy(x => x.Kind).ThenBy(x => x.Sku).ToArray();
    }

    // ---------------- lookup ----------------

    public async Task<IReadOnlyList<EquipmentSearchResult>> Search(string text)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var term = (text ?? "").Trim();
        if (term.Length < 2) return [];
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var states = await EquipmentRules.States(db, actor.Branch);
        return states.Where(x => x.Key.Item2.Contains(term, StringComparison.OrdinalIgnoreCase) || (products[x.Key.Item1].Sku + " " + products[x.Key.Item1].Name).Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Key.Item2).Take(50).Select(x => new EquipmentSearchResult(x.Key.Item1, products[x.Key.Item1].Sku, products[x.Key.Item1].Name, x.Key.Item2, StatusText(x.Value), x.Value.Sale?.Party ?? "")).ToArray();
    }
    private static string StatusText(EquipmentRules.State s) =>
        s.OnHand > 0 ? $"In stock ({s.Location})" : s.Sale is not null ? $"With customer" : s.LastOut?.Kind switch { DocumentKind.PurchaseReturn => "Returned to supplier", DocumentKind.StockCount => "Removed by stock count", _ => "Not in stock" };

    public async Task<EquipmentUnitView?> Unit(Guid productId, string serial)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var key = (productId, Normalize(serial));
        var states = await EquipmentRules.States(db, actor.Branch, [key]);
        if (!states.TryGetValue(key, out var state)) return null;
        var product = await db.Products.AsNoTracking().SingleAsync(x => x.Id == productId);
        var settings = await InventoryManagement.Latest<ProductSettings>(db, actor.Branch, "product", productId.ToString());
        var inService = state.OnHand > 0 && await InventoryManagement.Latest<SerialServiceState>(db, actor.Branch, "serial", key.Item2) is { InService: true };
        var terms = (await EquipmentRules.LatestTerms(db, [productId])).GetValueOrDefault(productId);
        var profile = await db.UnitProfiles.AsNoTracking().Where(x => x.ProductId == productId && x.Serial == key.Item2).OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        var warranties = await db.Warranties.AsNoTracking().Where(x => x.Branch == actor.Branch && x.ProductId == productId && x.Serial == key.Item2).OrderBy(x => x.Start).ToListAsync();
        var installations = await (from u in db.InstallationUnits join i in db.Installations on u.InstallationId equals i.Id where i.Branch == actor.Branch && u.ProductId == productId && u.Serial == key.Item2 select i).AsNoTracking().ToListAsync();
        var installationIds = installations.Select(x => x.Id).ToList();
        var completions = await db.InstallationCompletions.AsNoTracking().Where(x => installationIds.Contains(x.InstallationId)).ToDictionaryAsync(x => x.InstallationId);
        var cancellations = await db.InstallationCancellations.AsNoTracking().Where(x => installationIds.Contains(x.InstallationId)).ToDictionaryAsync(x => x.InstallationId);
        var accounting = actor.Permissions.Contains("accounting.read");

        var timeline = new List<EquipmentEvent>();
        foreach (var step in state.Steps)
        {
            var d = step.Document; var m = step.Movement; var mark = step.Effective || d.Kind == DocumentKind.Reversal ? "" : " (reversed)";
            var text = d.Kind switch
            {
                _ when m.Quantity == 0 => accounting ? $"Cost adjusted by NLe {m.Value:N2} ({d.Kind})" : null,
                DocumentKind.Receipt => $"Received from {d.Party} into {m.Location}" + (accounting ? $" at NLe {m.Value:N2}" : ""),
                DocumentKind.OpeningStock => $"Opening stock in {m.Location}",
                DocumentKind.Sale => $"Sold to {d.Party}",
                DocumentKind.Delivery => $"Delivered to {d.Party}",
                DocumentKind.SalesReturn => $"Returned by {d.Party} into {m.Location}",
                DocumentKind.PurchaseReturn => $"Returned to supplier {d.Party}",
                DocumentKind.StockTransfer => m.Quantity > 0 ? $"Moved to {m.Location}" : null,
                DocumentKind.StockCount => m.Quantity > 0 ? $"Found in stock count ({m.Location})" : $"Removed by stock count ({m.Location})",
                DocumentKind.Reversal => "Reversal of an earlier document",
                _ => $"{d.Kind} ({(m.Quantity > 0 ? "in" : "out")})"
            };
            if (text is not null) timeline.Add(new(d.BusinessDate, d.Kind.ToString(), text + mark, d.Number, d.Id));
        }
        foreach (var i in installations)
        {
            timeline.Add(new(i.Scheduled, "Installation", $"Installation scheduled at {i.Site} (engineer {i.Engineer})", i.Number, null));
            if (completions.TryGetValue(i.Id, out var c)) timeline.Add(new(c.Completed, "Installation", $"Installed and commissioned; accepted by {c.AcceptedBy}" + (c.TrainingGivenTo.Length > 0 ? $"; training given to {c.TrainingGivenTo}" : ""), i.Number, null));
            if (cancellations.TryGetValue(i.Id, out var x)) timeline.Add(new(i.Scheduled, "Installation", $"Installation cancelled: {x.Reason}", i.Number, null));
        }
        var today = Today;
        var warrantyViews = warranties.Select(w => new WarrantyView(w.Id, w.ProductId, product.Sku, w.Serial, w.Customer, w.Start, w.End, EquipmentRules.Status(w, states, today), w.InstallationId is null ? "Sale" : "Installation", w.Coverage)).ToArray();
        foreach (var w in warrantyViews) timeline.Add(new(w.Start, "Warranty", $"Customer warranty to {w.End:dd MMM yyyy}, started at {w.Source.ToLowerInvariant()} ({w.Status.ToLowerInvariant()})", "", null));
        timeline = timeline.OrderBy(x => x.Date).ToList();

        var active = warrantyViews.Where(x => x.Status == "Active").MaxBy(x => x.End);
        var supplierEnd = state.LastIn is not null && terms?.SupplierWarrantyMonths > 0 ? EquipmentRules.End(state.LastIn.BusinessDate, terms.SupplierWarrantyMonths) : (DateOnly?)null;
        var coverage = active is not null ? new WarrantyCoverage(true, $"Covered by customer warranty until {active.End:dd MMM yyyy} ({active.End.DayNumber - today.DayNumber} days left).", active.End)
            : state.WithCustomer ? new WarrantyCoverage(false, warrantyViews.Any(x => x.Status == "Expired" && state.Sale?.Id == warranties.Single(w => w.Id == x.Id).SaleId) ? "Customer warranty has expired. Repairs are chargeable." : "No customer warranty is recorded for this sale.", null)
            : new WarrantyCoverage(false, "Not with a customer.", null);
        var completedForSale = state.Sale is not null && installations.Any(i => i.SaleId == state.Sale.Id && completions.ContainsKey(i.Id));
        var canStart = state.WithCustomer && terms?.CustomerWarrantyMonths > 0 && !warranties.Any(w => w.SaleId == state.Sale!.Id) && (terms.StartsAt == WarrantyStart.Sale || completedForSale);
        var lastIn = state.Steps.LastOrDefault(x => x.Effective && x.Movement.Quantity > 0 && x.Document.Kind is DocumentKind.Receipt or DocumentKind.OpeningStock);
        return new(productId, product.Sku, product.Name, settings?.Brand ?? "", settings?.Model ?? "", key.Item2, (inService ? $"In service ({state.Location})" : StatusText(state)) + (completedForSale ? ", installed" : ""), state.Location, state.Sale?.Party ?? "",
            lastIn?.Document.BusinessDate, lastIn?.Document.Kind == DocumentKind.Receipt ? lastIn.Document.Party : "", accounting ? lastIn?.Movement.Value : null, state.Sale?.BusinessDate, state.Sale?.Number ?? "", supplierEnd,
            profile is null ? [] : JsonSerializer.Deserialize<List<UnitSpecification>>(profile.SpecificationsJson) ?? [], profile?.Version ?? 0, profile?.Accessories ?? "", profile?.Notes ?? "",
            coverage, warrantyViews, timeline, await Compatible(db, actor, productId), canStart == true);
    }

    public async Task<Guid> SaveProfile(SaveUnitProfile input)
    {
        var actor = await Authorize();
        if (!actor.Permissions.Contains("catalogue.write") && !actor.Permissions.Contains("inventory.receive") && !actor.Permissions.Contains("sales.create")) throw new UnauthorizedAccessException("You cannot change equipment records.");
        return await Commands.Run(factory, actor, input.OperationId, "equipment.profile", input, async db =>
        {
            var serial = Normalize(input.Serial);
            if (!await db.Movements.AnyAsync(x => x.Branch == actor.Branch && x.ProductId == input.ProductId && x.Serial == serial)) throw new BusinessException("This serial has no history in this branch.");
            var specs = (input.Specifications ?? []).Select(x => new UnitSpecification((x.Name ?? "").Trim(), (x.Value ?? "").Trim())).Where(x => x.Name.Length > 0 || x.Value.Length > 0).ToList();
            if (specs.Count > 30 || specs.Any(x => x.Name.Length is 0 or > 60 || x.Value.Length is 0 or > 160) || specs.Select(x => x.Name.ToUpperInvariant()).Distinct().Count() != specs.Count) throw new BusinessException("Enter up to 30 specifications, each with a different name (60 characters) and a value (160 characters).");
            if ((input.Accessories ?? "").Length > 200 || (input.Notes ?? "").Length > 200) throw new BusinessException("Accessories and notes are limited to 200 characters each.");
            var current = await db.UnitProfiles.Where(x => x.ProductId == input.ProductId && x.Serial == serial).MaxAsync(x => (int?)x.Version) ?? 0;
            if (current != input.ExpectedVersion) throw new BusinessException("This unit's record was changed by someone else. Refresh before saving.");
            var row = new UnitProfile { Id = Guid.NewGuid(), ProductId = input.ProductId, Serial = serial, Version = current + 1, SpecificationsJson = JsonSerializer.Serialize(specs), Accessories = (input.Accessories ?? "").Trim(), Notes = (input.Notes ?? "").Trim(), CreatedBy = actor.Id };
            db.UnitProfiles.Add(row); return row.Id;
        });
    }

    // ---------------- warranties ----------------

    public async Task<IReadOnlyList<WarrantyView>> Warranties()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Warranties.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync();
        var states = await EquipmentRules.States(db, actor.Branch, rows.Select(x => (x.ProductId, x.Serial)).Distinct().ToList());
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var today = Today;
        return rows.Select(w => new WarrantyView(w.Id, w.ProductId, products[w.ProductId].Sku, w.Serial, w.Customer, w.Start, w.End, EquipmentRules.Status(w, states, today), w.InstallationId is null ? "Sale" : "Installation", w.Coverage))
            .OrderBy(x => x.Status != "Active").ThenBy(x => x.End).ToArray();
    }

    public async Task<Guid> StartWarranty(StartWarrantyCommand input)
    {
        var actor = await Authorize("sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "warranty.start", input, async db =>
        {
            var key = (input.ProductId, Normalize(input.Serial));
            var state = (await EquipmentRules.States(db, actor.Branch, [key])).GetValueOrDefault(key);
            if (state?.WithCustomer != true) throw new BusinessException("Only a unit that is with a customer can have a warranty.");
            var terms = (await EquipmentRules.LatestTerms(db, [input.ProductId])).GetValueOrDefault(input.ProductId);
            if (terms is null || terms.CustomerWarrantyMonths == 0) throw new BusinessException("Set a customer warranty period for this product under Equipment setup first.");
            if (await db.Warranties.AnyAsync(x => x.SaleId == state.Sale!.Id && x.ProductId == key.Item1 && x.Serial == key.Item2)) throw new BusinessException("This sale already has a warranty for the unit.");
            var start = state.Sale!.BusinessDate; Guid? installation = null;
            if (terms.StartsAt == WarrantyStart.Installation)
            {
                var done = await (from u in db.InstallationUnits join i in db.Installations on u.InstallationId equals i.Id join c in db.InstallationCompletions on i.Id equals c.InstallationId
                                  where i.SaleId == state.Sale.Id && u.ProductId == key.Item1 && u.Serial == key.Item2 select new { i.Id, c.Completed }).FirstOrDefaultAsync()
                           ?? throw new BusinessException("The warranty starts at installation. Complete the installation first.");
                start = done.Completed; installation = done.Id;
            }
            var warranty = EquipmentRules.New(state.Sale, key.Item1, key.Item2, start, terms, installation, actor.Id);
            db.Warranties.Add(warranty); return warranty.Id;
        });
    }

    // ---------------- installations ----------------

    public async Task<IReadOnlyList<InstallableSale>> Installable()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var states = await EquipmentRules.States(db, actor.Branch);
        var busy = (await (from u in db.InstallationUnits join i in db.Installations on u.InstallationId equals i.Id where i.Branch == actor.Branch && !db.InstallationCancellations.Any(c => c.InstallationId == i.Id) select new { i.SaleId, u.ProductId, u.Serial }).ToListAsync())
            .Select(x => (x.SaleId, x.ProductId, x.Serial)).ToHashSet();
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        return states.Where(x => x.Value.WithCustomer && !busy.Contains((x.Value.Sale!.Id, x.Key.Item1, x.Key.Item2))).GroupBy(x => x.Value.Sale!.Id)
            .Select(g => { var sale = g.First().Value.Sale!; return new InstallableSale(sale.Id, sale.Number, sale.Party, sale.BusinessDate, g.Select(x => new InstallationUnitView(x.Key.Item1, products[x.Key.Item1].Sku, products[x.Key.Item1].Name, x.Key.Item2)).OrderBy(x => x.Sku).ThenBy(x => x.Serial).ToArray()); })
            .OrderByDescending(x => x.Date).ToArray();
    }

    public async Task<IReadOnlyList<InstallationView>> Installations()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Installations.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync();
        var ids = rows.Select(x => x.Id).ToList(); var saleIds = rows.Select(x => x.SaleId).ToList();
        var units = await db.InstallationUnits.AsNoTracking().Where(x => ids.Contains(x.InstallationId)).ToListAsync();
        var completions = await db.InstallationCompletions.AsNoTracking().Where(x => ids.Contains(x.InstallationId)).ToDictionaryAsync(x => x.InstallationId);
        var cancellations = await db.InstallationCancellations.AsNoTracking().Where(x => ids.Contains(x.InstallationId)).ToDictionaryAsync(x => x.InstallationId);
        var sales = await db.Documents.AsNoTracking().Where(x => saleIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var reversed = (await db.Documents.Where(x => x.Reverses != null && saleIds.Contains(x.Reverses!.Value)).Select(x => x.Reverses!.Value).ToListAsync()).ToHashSet();
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        return rows.Select(i =>
        {
            completions.TryGetValue(i.Id, out var c); cancellations.TryGetValue(i.Id, out var x);
            var mine = units.Where(u => u.InstallationId == i.Id).ToList();
            var status = x is not null ? "Cancelled" : c is not null ? "Completed" : reversed.Contains(i.SaleId) ? "Scheduled (sale reversed)" : "Scheduled";
            return new InstallationView(i.Id, i.Number, i.Customer, i.Site, i.SaleId, sales[i.SaleId].Number, sales[i.SaleId].BusinessDate, i.Scheduled, i.Engineer, i.Notes, status,
                mine.Select(u => new InstallationUnitView(u.ProductId, products[u.ProductId].Sku, products[u.ProductId].Name, u.Serial)).ToArray(),
                mine.SelectMany(u => EquipmentRules.Checklist(u.ChecklistJson)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                c?.Completed, c?.AcceptedBy ?? "", c?.TrainingGivenTo ?? "", c?.Notes ?? "", x?.Reason ?? "", c is null ? [] : EquipmentRules.Checklist(c.ChecklistDoneJson));
        }).OrderBy(x => x.Status != "Scheduled").ThenByDescending(x => x.Scheduled).ToArray();
    }

    public async Task<Guid> ScheduleInstallation(InstallationCommand input)
    {
        var actor = await Authorize("sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "installation.schedule", input, async db =>
        {
            var sale = await db.Documents.SingleOrDefaultAsync(x => x.Id == input.SaleId && x.Branch == actor.Branch && (x.Kind == DocumentKind.Sale || x.Kind == DocumentKind.Delivery) && !db.Documents.Any(r => r.Reverses == x.Id))
                ?? throw new BusinessException("Choose a posted, unreversed sale or delivery note.");
            if (input.Units is null || input.Units.Count is < 1 or > 50) throw new BusinessException("Choose 1 to 50 units to install.");
            var keys = input.Units.Select(u => (u.ProductId, Normalize(u.Serial))).ToList();
            if (keys.Distinct().Count() != keys.Count) throw new BusinessException("Each unit can be listed once.");
            if (input.Scheduled < sale.BusinessDate || input.Scheduled > Today.AddDays(365)) throw new BusinessException("Schedule the installation on or after the sale date and within a year.");
            var states = await EquipmentRules.States(db, actor.Branch, keys);
            foreach (var k in keys)
                if (!states.TryGetValue(k, out var s) || s.Sale?.Id != sale.Id) throw new BusinessException($"Serial {k.Item2} was not supplied to {sale.Party} on {sale.Number}, or has since come back into stock.");
            var serials = keys.Select(k => k.Item2).ToList();
            if (await (from u in db.InstallationUnits join i in db.Installations on u.InstallationId equals i.Id where i.SaleId == sale.Id && serials.Contains(u.Serial) && !db.InstallationCancellations.Any(c => c.InstallationId == i.Id) select u).AnyAsync())
                throw new BusinessException("One of these units already has an installation for this sale.");
            var terms = await EquipmentRules.LatestTerms(db, keys.Select(k => k.ProductId));
            if ((input.Notes ?? "").Length > 200) throw new BusinessException("Notes are limited to 200 characters.");
            var job = new Installation { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-INS-{input.OperationId:N}", SaleId = sale.Id, Customer = sale.Party,
                Site = Rules.Required(input.Site, "Installation site", 160), Scheduled = input.Scheduled, Engineer = Rules.Required(input.Engineer, "Engineer", 100), Notes = (input.Notes ?? "").Trim(), CreatedBy = actor.Id };
            db.Installations.Add(job);
            foreach (var k in keys) db.InstallationUnits.Add(new() { Id = Guid.NewGuid(), InstallationId = job.Id, ProductId = k.ProductId, Serial = k.Item2, ChecklistJson = terms.GetValueOrDefault(k.ProductId)?.ChecklistJson ?? "[]" });
            return job.Id;
        });
    }

    public async Task<Guid> CompleteInstallation(CompleteInstallationCommand input)
    {
        var actor = await Authorize("sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "installation.complete", input, async db =>
        {
            var job = await Open(db, actor, input.InstallationId);
            var sale = await db.Documents.SingleAsync(x => x.Id == job.SaleId);
            if (await db.Documents.AnyAsync(x => x.Reverses == sale.Id)) throw new BusinessException("The sale was reversed. Cancel this installation instead.");
            if (input.Completed < sale.BusinessDate || input.Completed > Today) throw new BusinessException("The completion date must be between the sale date and today.");
            var units = await db.InstallationUnits.Where(x => x.InstallationId == job.Id).ToListAsync();
            var states = await EquipmentRules.States(db, actor.Branch, units.Select(u => (u.ProductId, u.Serial)).ToList());
            if (units.Any(u => !states.TryGetValue((u.ProductId, u.Serial), out var s) || s.Sale?.Id != sale.Id)) throw new BusinessException("A unit on this installation has come back into stock. Cancel the installation and schedule the remaining units again.");
            var checklist = units.SelectMany(u => EquipmentRules.Checklist(u.ChecklistJson)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var done = (input.ChecklistDone ?? []).Select(x => x.Trim()).ToList();
            var missing = checklist.Where(c => !done.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            if (missing.Count > 0) throw new BusinessException($"Complete every commissioning check first. Outstanding: {string.Join("; ", missing)}.");
            if ((input.TrainingGivenTo ?? "").Length > 160 || (input.Notes ?? "").Length > 200) throw new BusinessException("Training is limited to 160 characters and notes to 200.");
            db.InstallationCompletions.Add(new() { Id = Guid.NewGuid(), InstallationId = job.Id, Completed = input.Completed, ChecklistDoneJson = JsonSerializer.Serialize(checklist), AcceptedBy = Rules.Required(input.AcceptedBy, "Accepted by (customer representative)", 120), TrainingGivenTo = (input.TrainingGivenTo ?? "").Trim(), Notes = (input.Notes ?? "").Trim(), CreatedBy = actor.Id });
            var terms = await EquipmentRules.LatestTerms(db, units.Select(u => u.ProductId));
            foreach (var u in units)
                if (terms.TryGetValue(u.ProductId, out var t) && t.CustomerWarrantyMonths > 0 && t.StartsAt == WarrantyStart.Installation && !await db.Warranties.AnyAsync(w => w.SaleId == sale.Id && w.ProductId == u.ProductId && w.Serial == u.Serial))
                    db.Warranties.Add(EquipmentRules.New(sale, u.ProductId, u.Serial, input.Completed, t, job.Id, actor.Id));
            return job.Id;
        });
    }

    public async Task<Guid> CancelInstallation(CancelInstallationCommand input)
    {
        var actor = await Authorize("sales.create");
        return await Commands.Run(factory, actor, input.OperationId, "installation.cancel", input, async db =>
        {
            var job = await Open(db, actor, input.InstallationId);
            var row = new InstallationCancellation { Id = Guid.NewGuid(), InstallationId = job.Id, Reason = Rules.Required(input.Reason, "Cancellation reason", 160), CreatedBy = actor.Id };
            db.InstallationCancellations.Add(row); return row.Id;
        });
    }
    private static async Task<Installation> Open(BusinessDb db, Actor actor, Guid id)
    {
        var job = await db.Installations.SingleOrDefaultAsync(x => x.Id == id && x.Branch == actor.Branch) ?? throw new BusinessException("Installation not found.");
        if (await db.InstallationCompletions.AnyAsync(x => x.InstallationId == id) || await db.InstallationCancellations.AnyAsync(x => x.InstallationId == id)) throw new BusinessException("This installation is already completed or cancelled.");
        return job;
    }
}
