using SierraBusiness.Domain;
using Microsoft.EntityFrameworkCore;
namespace SierraBusiness.Infrastructure;
public sealed class PricePolicy { public Guid Id { get; set; } public Guid ProductId { get; set; } public decimal MinimumPrice { get; set; } public long Version { get; set; } }
public sealed class StockReservation { public Guid Id { get; set; } public Guid OrderId { get; set; } public Guid ProductId { get; set; } public string Location { get; set; } = "MAIN"; public string? Serial { get; set; } public string? Batch { get; set; } public DateOnly? Expiry { get; set; } public decimal Quantity { get; set; } }
public sealed class ReservationRelease { public Guid Id { get; set; } public Guid ReservationId { get; set; } public decimal Quantity { get; set; } public string Reason { get; set; } = ""; }
public sealed class DeliveryInvoice { public Guid Id { get; set; } public Guid DeliveryId { get; set; } public Guid InvoiceId { get; set; } }
public sealed class CustomerCredit { public Guid Id { get; set; } public Guid ReturnId { get; set; } public decimal Amount { get; set; } }
public sealed class CustomerCreditUse { public Guid Id { get; set; } public Guid CreditId { get; set; } public Guid DocumentId { get; set; } public decimal Amount { get; set; } }
public static class CommerceRules
{
    public static async Task Price(BusinessDb db, Guid product, decimal unitPrice, decimal conversion)
    {
        var policy = await db.PricePolicies.Where(x => x.ProductId == product).OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        if (policy is null) throw new BusinessException("This product is not approved for negotiated sales. Ask an authorized manager to configure its pricing policy.");
        if (unitPrice / conversion < policy.MinimumPrice) throw new BusinessException("The agreed price is not permitted. Renegotiate the price or contact an authorized manager.");
    }
    public static async Task<decimal> Reserved(BusinessDb db, string branch, Guid product, string location, string? serial, string? batch, DateOnly? expiry, Guid? exceptOrder = null)
    {
        var reservations = await db.StockReservations.Where(x => x.ProductId == product && x.Location == location && x.Serial == serial && x.Batch == batch && x.Expiry == expiry && x.OrderId != exceptOrder && db.TradeOrders.Any(o => o.Id == x.OrderId && o.Branch == branch) && !db.OrderClosures.Any(c => c.OrderId == x.OrderId)).ToListAsync();
        var ids = reservations.Select(x => x.Id).ToArray();
        var releases = await db.ReservationReleases.Where(x => ids.Contains(x.ReservationId)).ToListAsync();
        return reservations.Sum(x => x.Quantity) - releases.Sum(x => x.Quantity);
    }
    public static async Task Consume(BusinessDb db, Guid? order, Guid product, string location, string? serial, string? batch, DateOnly? expiry, decimal quantity)
    {
        if (order is null) return;
        var reservations = await db.StockReservations.Where(x => x.OrderId == order && x.ProductId == product && x.Location == location && x.Serial == serial && x.Batch == batch && x.Expiry == expiry).ToListAsync();
        foreach (var r in reservations)
        {
            var released = (await db.ReservationReleases.Where(x => x.ReservationId == r.Id).ToListAsync()).Sum(x => x.Quantity) + db.ReservationReleases.Local.Where(x => x.ReservationId == r.Id && db.Entry(x).State == EntityState.Added).Sum(x => x.Quantity);
            var take = Math.Min(quantity, r.Quantity - released); if (take <= 0) continue;
            db.ReservationReleases.Add(new() { Id = Guid.NewGuid(), ReservationId = r.Id, Quantity = take, Reason = "Consumed by stock issue" }); quantity -= take;
        }
    }
}
