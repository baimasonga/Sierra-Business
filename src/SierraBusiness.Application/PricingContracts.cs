namespace SierraBusiness.Application;

/// <summary>0.24.0: promotions and price rules. A rule sets a lower price for a product (or a whole product category), optionally only for a customer type, from a quantity, between dates. Prices at or above the best rule are allowed without discount approval.</summary>
public enum PriceRuleKind { PercentOff, FixedPrice }
/// <param name="Value">Percentage off list price, or the fixed price per base unit.</param>
/// <param name="Category">Product category the rule covers when no product is chosen; empty with no product = every product.</param>
/// <param name="CustomerCategory">Customer organisation type (for example Government or NGO); empty = every customer, including walk-in.</param>
public sealed record PriceRuleInput(Guid OperationId, Guid? Id, int ExpectedVersion, string Code, string Name, PriceRuleKind Kind, decimal Value, Guid? ProductId, string Category, string CustomerCategory,
    decimal MinQuantity, DateOnly From, DateOnly? To, bool Active = true);
public sealed record PriceRuleView(Guid Id, int Version, string Code, string Name, PriceRuleKind Kind, decimal Value, Guid? ProductId, string ProductLabel, string Category, string CustomerCategory,
    decimal MinQuantity, DateOnly From, DateOnly? To, bool Active);
/// <summary>The best price for a line: per sale unit (unit price × conversion).</summary>
public sealed record PriceQuote(int Line, Guid ProductId, decimal ListPrice, decimal Price, string Rule);
public interface IPricingService
{
    Task<IReadOnlyList<PriceRuleView>> Rules();
    Task<Guid> Save(PriceRuleInput input);
    Task<IReadOnlyList<PriceQuote>> Quote(string customer, DateOnly date, IReadOnlyList<TradeLine> lines);
}
