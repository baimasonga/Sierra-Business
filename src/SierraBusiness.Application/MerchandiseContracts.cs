using SierraBusiness.Domain;
namespace SierraBusiness.Application;

/// <summary>Specification Skill 18 (0.22.0). A variant group is a family of separately stocked products (for example one chair in several colours and sizes); every variant is an ordinary product with its own stock, barcode and price.</summary>
public sealed record VariantAttribute(string Name, IReadOnlyList<string> Values);
public sealed record CreateVariantGroup(Guid OperationId, string BaseSku, string Name, string Category, string Unit, decimal Price, IReadOnlyList<VariantAttribute> Attributes,
    TrackingMode Tracking = TrackingMode.Quantity, bool NeedsAssembly = false, bool NeedsDelivery = false, string Notes = "", decimal? MinimumPrice = null);
public sealed record AddVariant(Guid OperationId, Guid GroupId, IReadOnlyDictionary<string, string> Values, decimal Price, string? Sku = null, decimal? MinimumPrice = null);
public sealed record VariantView(Guid ProductId, string Sku, string Name, IReadOnlyDictionary<string, string> Values, decimal Price, decimal OnHand);
public sealed record VariantGroupView(Guid Id, string Code, string Name, string Category, string Unit, IReadOnlyList<string> Attributes, bool NeedsAssembly, bool NeedsDelivery, string Notes, IReadOnlyList<VariantView> Variants)
{
    public decimal OnHand => Variants.Sum(x => x.OnHand);
}

/// <summary>A bundle offer: a set of products sold together at one price. It is a pricing construct only; each product's stock moves as usual.</summary>
public sealed record BundleItem(Guid ProductId, decimal Quantity);
public sealed record SaveBundle(Guid OperationId, Guid? Id, int ExpectedVersion, string Code, string Name, decimal Price, IReadOnlyList<BundleItem> Items, bool Active = true, DateOnly? ValidUntil = null);
public sealed record BundleItemView(Guid ProductId, string Sku, string Name, decimal Quantity, decimal ListPrice);
public sealed record BundleView(Guid Id, int Version, string Code, string Name, decimal Price, bool Active, DateOnly? ValidUntil, IReadOnlyList<BundleItemView> Items)
{
    public decimal ListValue => Items.Sum(x => x.Quantity * x.ListPrice);
    public decimal Saving => ListValue - Price;
}

public interface IMerchandiseService
{
    Task<IReadOnlyList<VariantGroupView>> Variants();
    Task<Guid> CreateVariants(CreateVariantGroup input);
    Task<Guid> AddVariant(AddVariant input);
    Task<IReadOnlyList<BundleView>> Bundles();
    Task<Guid> SaveBundle(SaveBundle input);
    /// <summary>The sale lines for a number of bundles, with the bundle price spread over the products by list value.</summary>
    Task<IReadOnlyList<TradeLine>> Expand(Guid bundleId, int quantity);
}
