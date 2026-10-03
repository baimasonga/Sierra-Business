using SierraBusiness.Application;
namespace SierraBusiness.Infrastructure;
public sealed class TradeOrder
{
    public Guid Id { get; set; }
    public string Branch { get; set; } = "";
    public string Number { get; set; } = "";
    public OrderType Type { get; set; }
    public string Party { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateOnly DueDate { get; set; }
    public string Reference { get; set; } = "";
    public Guid? QuotationId { get; set; }
}
public sealed class TradeOrderLine
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
public sealed class OrderFulfillment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public Guid DocumentId { get; set; }
    public decimal Quantity { get; set; }
}
public sealed class OrderClosure
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Reason { get; set; } = "";
}
