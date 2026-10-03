namespace SierraBusiness.Application;
public enum OrderType { Quotation, SalesOrder, PurchaseOrder }
public sealed record OrderLineInput(Guid ProductId, decimal Quantity, decimal UnitPrice);
public sealed record OrderCommand(Guid OperationId, OrderType Type, string Party, DateOnly Date, DateOnly DueDate, string Reference, List<OrderLineInput> Lines, Guid? QuotationId = null, string? Currency = null);
public sealed record CloseOrderCommand(Guid OperationId, Guid OrderId, string Reason);
public sealed record OrderLineView(Guid ProductId, string Sku, string Name, decimal Quantity, decimal UnitPrice, decimal Fulfilled, string Unit);
public sealed record OrderView(Guid Id, string Number, OrderType Type, string Party, DateOnly Date, DateOnly DueDate, string Reference, bool Closed, Guid? ConvertedTo, IReadOnlyList<OrderLineView> Lines, string Currency = "SLE");
public interface IOrderService
{
    Task<IReadOnlyList<OrderView>> Read();
    Task<Guid> Create(OrderCommand input);
    Task<Guid> Close(CloseOrderCommand input);
}
