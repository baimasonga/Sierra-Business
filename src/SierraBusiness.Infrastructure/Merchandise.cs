using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed class VariantGroup
{
    public Guid Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string Category { get; set; } = ""; public string Unit { get; set; } = "";
    public string AttributesJson { get; set; } = "[]"; public bool NeedsAssembly { get; set; } public bool NeedsDelivery { get; set; } public string Notes { get; set; } = "";
}
public sealed class VariantMember { public Guid ProductId { get; set; } public Guid GroupId { get; set; } public string ValuesJson { get; set; } = "{}"; }
public sealed class Bundle
{
    public Guid Id { get; set; } public int Version { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public decimal Price { get; set; }
    public bool Active { get; set; } public DateOnly? ValidUntil { get; set; } public string ItemsJson { get; set; } = "[]";
}

/// <summary>Bundle pricing rules shared by the sale posting and the bundle screen.</summary>
public static class BundleRules
{
    public static List<BundleItem> Items(Bundle b) => JsonSerializer.Deserialize<List<BundleItem>>(b.ItemsJson) ?? [];

    /// <summary>Spreads the price of <paramref name="quantity"/> bundles over the products by their list value; the line amounts add up to the bundle price exactly.</summary>
    public static List<TradeLine> Expand(Bundle bundle, IReadOnlyDictionary<Guid, Product> products, int quantity)
    {
        var items = Items(bundle);
        var target = AccountingEngine.Money(bundle.Price * quantity);
        var list = items.Sum(i => i.Quantity * products[i.ProductId].Price);
        var lines = new List<TradeLine>(); decimal allocated = 0;
        for (var k = 0; k < items.Count; k++)
        {
            var i = items[k]; var qty = i.Quantity * quantity;
            var amount = k == items.Count - 1 ? target - allocated : AccountingEngine.Money(target * (i.Quantity * products[i.ProductId].Price) / list);
            allocated += amount;
            var unit = Math.Round(amount / qty, 4, MidpointRounding.AwayFromZero);
            if (AccountingEngine.Money(qty * unit) == amount) { lines.Add(new(i.ProductId, qty, unit, BundleId: bundle.Id)); continue; }
            // Keep the package price exact: one unit carries the rounding difference.
            var rest = AccountingEngine.Money((qty - 1) * unit);
            lines.Add(new(i.ProductId, qty - 1, unit, BundleId: bundle.Id));
            lines.Add(new(i.ProductId, 1, amount - rest, BundleId: bundle.Id));
        }
        return lines;
    }

    /// <summary>Checks the bundle lines of a sale against their bundles. Returns the lines that are priced by an approved bundle (exempt from per-product minimum price and discount checks).</summary>
    public static async Task<HashSet<TradeLine>> Validate(BusinessDb db, TradeCommand input)
    {
        var bundled = new HashSet<TradeLine>(ReferenceEqualityComparer.Instance);
        var groups = input.Lines.Where(l => l?.BundleId is not null).GroupBy(l => l.BundleId!.Value).ToList();
        if (groups.Count == 0) return bundled;
        if (input.Kind != DocumentKind.Sale) throw new BusinessException("Bundle offers apply to sales only.");
        foreach (var g in groups)
        {
            var bundle = await db.Bundles.SingleOrDefaultAsync(x => x.Id == g.Key) ?? throw new BusinessException("The bundle offer was not found.");
            if (!bundle.Active || bundle.ValidUntil is DateOnly until && input.BusinessDate > until) throw new BusinessException($"The bundle offer {bundle.Code} is not available on {input.BusinessDate:dd MMM yyyy}.");
            var items = Items(bundle);
            var counts = new HashSet<decimal>();
            foreach (var item in items) counts.Add(g.Where(l => l.ProductId == item.ProductId).Sum(l => l.Quantity * l.Conversion) / item.Quantity);
            var k = counts.Count == 1 ? counts.Single() : 0;
            if (k <= 0 || k != decimal.Truncate(k) || g.Any(l => items.All(i => i.ProductId != l.ProductId)))
                throw new BusinessException($"The lines for bundle {bundle.Code} must contain exactly its products in whole bundles.");
            var amount = g.Sum(l => AccountingEngine.Money(l.Quantity * l.UnitPrice));
            if (amount != AccountingEngine.Money(bundle.Price * k)) throw new BusinessException($"Bundle {bundle.Code} is priced at NLe {bundle.Price:N2} each; the lines add up to NLe {amount:N2} for {k:0}.");
            foreach (var l in g) bundled.Add(l);
        }
        return bundled;
    }
}

public sealed class MerchandiseService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IMerchandiseService
{
    private async Task<Actor> Authorize(string? permission = null)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read"); if (permission is not null) actor.Demand(permission);
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }
    private static async Task<Dictionary<Guid, decimal>> Stock(BusinessDb db, string branch) =>
        (await db.Movements.AsNoTracking().Where(x => x.Branch == branch).Select(x => new { x.ProductId, x.Quantity }).ToListAsync()).GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

    // ---------------- variants ----------------

    public async Task<IReadOnlyList<VariantGroupView>> Variants()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var groups = await db.VariantGroups.AsNoTracking().OrderBy(x => x.Code).ToListAsync();
        var members = (await db.VariantMembers.AsNoTracking().ToListAsync()).ToLookup(x => x.GroupId);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var stock = await Stock(db, actor.Branch);
        return groups.Select(g => new VariantGroupView(g.Id, g.Code, g.Name, g.Category, g.Unit, JsonSerializer.Deserialize<List<string>>(g.AttributesJson) ?? [], g.NeedsAssembly, g.NeedsDelivery, g.Notes,
            members[g.Id].Select(m => { var p = products[m.ProductId]; return new VariantView(p.Id, p.Sku, p.Name, JsonSerializer.Deserialize<Dictionary<string, string>>(m.ValuesJson) ?? [], p.Price, stock.GetValueOrDefault(p.Id)); }).OrderBy(x => x.Sku).ToArray())).ToArray();
    }

    private static string Code(string value)
    {
        var clean = new string(value.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (clean.Length == 0) throw new BusinessException($"'{value}' needs at least one letter or digit.");
        return clean.Length <= 4 ? clean : clean[..3];
    }
    private static async Task<string> FreeSku(BusinessDb db, string sku)
    {
        var candidate = sku; var n = 2;
        while (await db.Products.AnyAsync(x => x.Sku == candidate) || db.Products.Local.Any(x => x.Sku == candidate) || await db.ProductBarcodes.AnyAsync(x => x.Code == candidate)) candidate = $"{sku}{n++}";
        if (candidate.Length > 40) throw new BusinessException("The generated SKU is too long. Use a shorter base SKU.");
        return candidate;
    }

    public async Task<Guid> CreateVariants(CreateVariantGroup input)
    {
        var actor = await Authorize("catalogue.write");
        return await Commands.Run(factory, actor, input.OperationId, "variants.create", input, async db =>
        {
            var baseSku = Rules.Required(input.BaseSku, "Base SKU", 30).ToUpperInvariant();
            var name = Rules.Required(input.Name, "Product name", 120);
            if (await db.VariantGroups.AnyAsync(x => x.Code == baseSku)) throw new BusinessException("A variant group with this base SKU already exists. Add variants to it instead.");
            var attributes = (input.Attributes ?? []).Select(a => new VariantAttribute(Rules.Required(a.Name, "Attribute name", 40), (a.Values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList())).ToList();
            if (attributes.Count is < 1 or > 3 || attributes.Any(a => a.Values.Count is < 1 or > 30) || attributes.Select(a => a.Name.ToUpperInvariant()).Distinct().Count() != attributes.Count)
                throw new BusinessException("Give 1 to 3 different attributes (for example Colour, Size, Finish), each with 1 to 30 values.");
            if (attributes.SelectMany(a => a.Values).Any(v => v.Length > 40)) throw new BusinessException("Attribute values are limited to 40 characters.");
            IEnumerable<Dictionary<string, string>> combos = [new()];
            foreach (var a in attributes) combos = combos.SelectMany(c => a.Values.Select(v => new Dictionary<string, string>(c) { [a.Name] = v })).ToList();
            var list = combos.ToList();
            if (list.Count > 200) throw new BusinessException($"That makes {list.Count} variants; the limit is 200 per group.");
            var group = new VariantGroup { Id = Guid.NewGuid(), Code = baseSku, Name = name, Category = Rules.Required(input.Category, "Category"), Unit = Rules.Required(input.Unit, "Base unit", 30),
                AttributesJson = JsonSerializer.Serialize(attributes.Select(a => a.Name)), NeedsAssembly = input.NeedsAssembly, NeedsDelivery = input.NeedsDelivery, Notes = (input.Notes ?? "").Trim() };
            if (group.Notes.Length > 300) throw new BusinessException("Notes are limited to 300 characters.");
            db.VariantGroups.Add(group);
            foreach (var values in list) await Create(db, actor, group, values, input.Price, null, input.Tracking, input.MinimumPrice);
            return group.Id;
        });
    }

    private static async Task Create(BusinessDb db, Actor actor, VariantGroup group, Dictionary<string, string> values, decimal price, string? sku, TrackingMode tracking, decimal? minimum)
    {
        var attributes = JsonSerializer.Deserialize<List<string>>(group.AttributesJson)!;
        var ordered = attributes.Select(a => values.TryGetValue(a, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : throw new BusinessException($"Give a value for {a}.")).ToList();
        if (values.Count != attributes.Count) throw new BusinessException($"Give exactly these attributes: {string.Join(", ", attributes)}.");
        var existing = await db.VariantMembers.Where(x => x.GroupId == group.Id).Select(x => x.ValuesJson).ToListAsync();
        var key = string.Join("|", ordered).ToUpperInvariant();
        if (existing.Select(j => JsonSerializer.Deserialize<Dictionary<string, string>>(j)!).Any(d => string.Join("|", attributes.Select(a => d[a])).ToUpperInvariant() == key)
            || db.VariantMembers.Local.Where(x => x.GroupId == group.Id).Any(m => string.Join("|", attributes.Select(a => JsonSerializer.Deserialize<Dictionary<string, string>>(m.ValuesJson)![a])).ToUpperInvariant() == key))
            throw new BusinessException($"{group.Name} already has the variant {string.Join(", ", ordered)}.");
        var chosen = string.IsNullOrWhiteSpace(sku) ? await FreeSku(db, group.Code + "-" + string.Join("-", ordered.Select(Code))) : sku.Trim().ToUpperInvariant();
        var product = await BusinessService.CreateProduct(db, actor, new(Guid.NewGuid(), chosen, $"{group.Name} – {string.Join(", ", ordered)}", group.Category, group.Unit, price, tracking, minimum));
        db.VariantMembers.Add(new() { ProductId = product.Id, GroupId = group.Id, ValuesJson = JsonSerializer.Serialize(attributes.Zip(ordered).ToDictionary(x => x.First, x => x.Second)) });
    }

    public async Task<Guid> AddVariant(AddVariant input)
    {
        var actor = await Authorize("catalogue.write");
        return await Commands.Run(factory, actor, input.OperationId, "variants.add", input, async db =>
        {
            var group = await db.VariantGroups.SingleOrDefaultAsync(x => x.Id == input.GroupId) ?? throw new BusinessException("Variant group not found.");
            var tracking = await db.VariantMembers.Where(x => x.GroupId == group.Id).Join(db.Products, m => m.ProductId, p => p.Id, (m, p) => p.Tracking).FirstOrDefaultAsync();
            await Create(db, actor, group, new(input.Values ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase), input.Price, input.Sku, tracking, input.MinimumPrice);
            return group.Id;
        });
    }

    // ---------------- bundles ----------------

    public async Task<IReadOnlyList<BundleView>> Bundles()
    {
        await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        return (await db.Bundles.AsNoTracking().OrderBy(x => x.Code).ToListAsync()).Select(b => new BundleView(b.Id, b.Version, b.Code, b.Name, b.Price, b.Active, b.ValidUntil,
            BundleRules.Items(b).Select(i => new BundleItemView(i.ProductId, products[i.ProductId].Sku, products[i.ProductId].Name, i.Quantity, products[i.ProductId].Price)).ToArray())).ToArray();
    }

    public async Task<Guid> SaveBundle(SaveBundle input)
    {
        var actor = await Authorize("pricing.manage");
        return await Commands.Run(factory, actor, input.OperationId, "bundle.save", input, async db =>
        {
            var code = Rules.Required(input.Code, "Bundle code", 30).ToUpperInvariant();
            Rules.Positive(input.Price, "Bundle price");
            if (AccountingEngine.Money(input.Price) != input.Price) throw new BusinessException("The bundle price has at most two decimals.");
            var items = (input.Items ?? []).ToList();
            if (items.Count is < 2 or > 20 || items.Select(x => x.ProductId).Distinct().Count() != items.Count) throw new BusinessException("A bundle has 2 to 20 different products.");
            foreach (var i in items)
            {
                if (i.Quantity <= 0 || i.Quantity != decimal.Truncate(i.Quantity) || i.Quantity > 1000) throw new BusinessException("Bundle quantities are whole numbers from 1 to 1000.");
                var p = await db.Products.SingleOrDefaultAsync(x => x.Id == i.ProductId) ?? throw new BusinessException("A bundle product was not found.");
                if (p.Tracking != TrackingMode.Quantity) throw new BusinessException($"{p.Sku} is serial- or batch-tracked; bundles hold quantity-tracked products only, so each unit can still be chosen at sale.");
            }
            if (await db.Bundles.AnyAsync(x => x.Code == code && x.Id != input.Id)) throw new BusinessException("Bundle code already exists.");
            Bundle b;
            if (input.Id is null) { b = new() { Id = Guid.NewGuid() }; db.Bundles.Add(b); }
            else { b = await db.Bundles.SingleOrDefaultAsync(x => x.Id == input.Id) ?? throw new BusinessException("Bundle not found."); if (b.Version != input.ExpectedVersion) throw new BusinessException("The bundle changed. Refresh before saving."); }
            b.Version++; b.Code = code; b.Name = Rules.Required(input.Name, "Bundle name", 120); b.Price = input.Price; b.Active = input.Active; b.ValidUntil = input.ValidUntil; b.ItemsJson = JsonSerializer.Serialize(items);
            return b.Id;
        });
    }

    public async Task<IReadOnlyList<TradeLine>> Expand(Guid bundleId, int quantity)
    {
        await Authorize("sales.create");
        if (quantity is < 1 or > 1000) throw new BusinessException("Sell 1 to 1000 bundles at a time.");
        await using var db = await factory.CreateDbContextAsync();
        var bundle = await db.Bundles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == bundleId) ?? throw new BusinessException("Bundle not found.");
        if (!bundle.Active || bundle.ValidUntil is DateOnly until && until < DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("This bundle offer is not active.");
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        return BundleRules.Expand(bundle, products, quantity);
    }
}
