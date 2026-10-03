using SierraBusiness.Domain;
namespace SierraBusiness.Application;

public sealed record Actor(string Id, string Branch, IReadOnlySet<string> Permissions)
{
    public void Demand(string permission) { if (!Permissions.Contains(permission)) throw new UnauthorizedAccessException($"Permission required: {permission}"); }
}
public sealed record ProductInput(Guid OperationId, string Sku, string Name, string Category, string Unit, decimal Price, TrackingMode Tracking, decimal? MinimumPrice = null);
public sealed record ProductView(Guid Id, string Sku, string Name, string Category, string Unit, decimal Price, TrackingMode Tracking);
public sealed record TradeLine(Guid ProductId, decimal Quantity, decimal UnitPrice, decimal Conversion = 1, string? Serial = null, string? Batch = null, DateOnly? Expiry = null);
public sealed record TradeCommand(Guid OperationId, DocumentKind Kind, string Party, DateOnly BusinessDate, List<TradeLine> Lines, PaymentMethod? Payment, string? PaymentReference = null, string? Bank = null, DateOnly? PaymentDate = null, DateOnly? DueDate = null, Guid? OrderId = null, string Location = "MAIN", Guid? DeliveryId = null, Guid? HeldSaleId = null, string? Currency = null);
public sealed record ReverseCommand(Guid OperationId, Guid DocumentId, string Reason);
public sealed record StockView(Guid ProductId, string Sku, string Name, string Unit, string? Serial, string? Batch, DateOnly? Expiry, decimal Quantity, decimal Value, string Location = "MAIN");
public sealed record DocumentView(Guid Id, string Number, DocumentKind Kind, string Party, DateOnly BusinessDate, decimal Total, string Settlement, Guid? Reverses, string Detail);
public sealed record LedgerView(Account Account, decimal Debit, decimal Credit);
public sealed record AuditView(DateTimeOffset Time, string Actor, string Action, string Reference);
public sealed record BusinessSnapshot(IReadOnlyList<ProductView> Products, IReadOnlyList<StockView> Stock, IReadOnlyList<DocumentView> Documents, IReadOnlyList<LedgerView> Ledger, IReadOnlyList<AuditView> Audit, int PendingSync, decimal NetSales = 0);
public interface IBusinessService
{
    Task<BusinessSnapshot> Read(Actor actor, CancellationToken cancellationToken = default);
    Task<Guid> AddProduct(Actor actor, ProductInput input, CancellationToken cancellationToken = default);
    Task<Guid> Post(Actor actor, TradeCommand input, CancellationToken cancellationToken = default);
    Task<Guid> Reverse(Actor actor, ReverseCommand input, CancellationToken cancellationToken = default);
}

