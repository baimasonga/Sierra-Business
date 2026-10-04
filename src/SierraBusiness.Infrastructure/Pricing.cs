using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed class PriceRule
{
    public Guid Id { get; set; } public int Version { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public PriceRuleKind Kind { get; set; } public decimal Value { get; set; }
    public Guid? ProductId { get; set; } public string Category { get; set; } = ""; public string CustomerCategory { get; set; } = ""; public decimal MinQuantity { get; set; }
    public DateOnly From { get; set; } public DateOnly? To { get; set; } public bool Active { get; set; }
}

public static class PriceRules
{
    /// <summary>The lowest promotional price per base unit for this product, customer type, quantity and date, or null when no rule applies.</summary>
    public static async Task<(decimal Price, string Rule)?> Best(BusinessDb db, string customerCategory, Product p, decimal baseQuantity, DateOnly date)
    {
        var rules = await db.PriceRules.AsNoTracking().Where(r => r.Active && r.From <= date && (r.To == null || r.To >= date) && r.MinQuantity <= baseQuantity
            && (r.ProductId == p.Id || r.ProductId == null && (r.Category == "" || r.Category == p.Category))).ToListAsync();
        var best = rules.Where(r => r.CustomerCategory.Length == 0 || r.CustomerCategory.Equals(customerCategory, StringComparison.OrdinalIgnoreCase))
            .Select(r => (Price: r.Kind == PriceRuleKind.FixedPrice ? r.Value : Math.Round(p.Price * (100 - r.Value) / 100, 4, MidpointRounding.AwayFromZero), Rule: r.Code))
            .Where(x => x.Price < p.Price).OrderBy(x => x.Price).FirstOrDefault();
        return best.Rule is null ? null : best;
    }
}

public sealed class PricingService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IPricingService
{
    private async Task<Actor> Authorize(string? permission = null)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read"); if (permission is not null) actor.Demand(permission);
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }
    public async Task<IReadOnlyList<PriceRuleView>> Rules()
    {
        await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        return (await db.PriceRules.AsNoTracking().OrderBy(x => x.Code).ToListAsync()).Select(r => new PriceRuleView(r.Id, r.Version, r.Code, r.Name, r.Kind, r.Value, r.ProductId,
            r.ProductId is Guid pid && products.TryGetValue(pid, out var p) ? $"{p.Sku} · {p.Name}" : r.Category.Length > 0 ? $"Category: {r.Category}" : "All products", r.Category, r.CustomerCategory, r.MinQuantity, r.From, r.To, r.Active)).ToArray();
    }
    public async Task<Guid> Save(PriceRuleInput input)
    {
        var actor = await Authorize("pricing.manage");
        return await Commands.Run(factory, actor, input.OperationId, "pricing.rule", input, async db =>
        {
            var code = SierraBusiness.Domain.Rules.Required(input.Code, "Rule code", 30).ToUpperInvariant();
            if (!Enum.IsDefined(input.Kind)) throw new BusinessException("Choose percentage off or fixed price.");
            if (input.Kind == PriceRuleKind.PercentOff && input.Value is <= 0 or >= 100) throw new BusinessException("A percentage off is between 0 and 100.");
            if (input.Kind == PriceRuleKind.FixedPrice) { SierraBusiness.Domain.Rules.Positive(input.Value, "Fixed price"); if (input.ProductId is null) throw new BusinessException("A fixed price is set for one product."); }
            if (input.MinQuantity < 0) throw new BusinessException("The minimum quantity cannot be negative.");
            if (input.To < input.From) throw new BusinessException("The end date is before the start date.");
            if (input.ProductId is Guid pid && !await db.Products.AnyAsync(x => x.Id == pid)) throw new BusinessException("Product not found.");
            if (await db.PriceRules.AnyAsync(x => x.Code == code && x.Id != input.Id)) throw new BusinessException("Rule code already exists.");
            PriceRule r;
            if (input.Id is null) { r = new() { Id = Guid.NewGuid() }; db.PriceRules.Add(r); }
            else { r = await db.PriceRules.SingleOrDefaultAsync(x => x.Id == input.Id) ?? throw new BusinessException("Rule not found."); if (r.Version != input.ExpectedVersion) throw new BusinessException("The rule changed. Refresh before saving."); }
            r.Version++; r.Code = code; r.Name = SierraBusiness.Domain.Rules.Required(input.Name, "Rule name", 120); r.Kind = input.Kind; r.Value = input.Value; r.ProductId = input.ProductId;
            r.Category = input.ProductId is null ? (input.Category ?? "").Trim() : ""; r.CustomerCategory = (input.CustomerCategory ?? "").Trim(); r.MinQuantity = input.MinQuantity; r.From = input.From; r.To = input.To; r.Active = input.Active;
            return r.Id;
        });
    }
    public async Task<IReadOnlyList<PriceQuote>> Quote(string customer, DateOnly date, IReadOnlyList<TradeLine> lines)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var normalized = (customer ?? "").Trim().ToUpperInvariant();
        var category = (await db.Parties.AsNoTracking().SingleOrDefaultAsync(x => x.Branch == actor.Branch && x.NormalizedName == normalized))?.Category ?? "";
        var quotes = new List<PriceQuote>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.BundleId is not null) continue;
            var p = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == l.ProductId); if (p is null) continue;
            var best = await PriceRules.Best(db, category, p, l.Quantity * l.Conversion, date);
            if (best is { } b) quotes.Add(new(i, p.Id, p.Price * l.Conversion, Math.Round(b.Price * l.Conversion, 4), b.Rule));
        }
        return quotes;
    }
}
