namespace SierraBusiness.Application;
public sealed record PricePolicyCommand(Guid OperationId, Guid ProductId, decimal MinimumPrice, string Reason);
public sealed record PricePolicyView(Guid ProductId, string Sku, string Name, decimal? MinimumPrice);
public sealed record ReservationCommand(Guid OperationId, Guid OrderId, Guid ProductId, string Location, decimal Quantity, string? Serial = null, string? Batch = null, DateOnly? Expiry = null);
public sealed record ReleaseReservationCommand(Guid OperationId, Guid ReservationId, string Reason);
public sealed record ReservationView(Guid Id, Guid OrderId, string OrderNumber, string Party, Guid ProductId, string Product, string Location, string? Serial, string? Batch, DateOnly? Expiry, decimal Remaining);
public sealed record CreditView(Guid Id, string Number, string Party, decimal Amount, decimal Remaining);
public sealed record UseCreditCommand(Guid OperationId, Guid CreditId, decimal Amount, int? RefundAccount, Guid? InvoiceId, string Reason);
public interface ICommerceService
{
    Task<IReadOnlyList<PricePolicyView>> Prices();
    Task<Guid> SetPrice(PricePolicyCommand input);
    Task<IReadOnlyList<ReservationView>> Reservations();
    Task<Guid> Reserve(ReservationCommand input);
    Task<Guid> Release(ReleaseReservationCommand input);
    Task<IReadOnlyList<CreditView>> Credits();
    Task<Guid> UseCredit(UseCreditCommand input);
}
