using SierraBusiness.Domain;
namespace SierraBusiness.Application;
public enum ConsignmentOwnership { CompanyOwned, SupplierOwned }
public sealed record ConsignmentSiteCommand(Guid OperationId,string Code,string Partner,ConsignmentOwnership Ownership);
public sealed record ConsignmentSiteView(string Code,string Partner,ConsignmentOwnership Ownership);
public sealed record CustodyReceiveCommand(Guid OperationId,string Site,DateOnly Date,Guid ProductId,decimal Quantity,decimal ReferenceUnitCost,string Reference,string? Serial = null,string? Batch = null,DateOnly? Expiry = null);
public sealed record CustodyReturnCommand(Guid OperationId,Guid ReceiveId,DateOnly Date,decimal Quantity,string Reason);
public sealed record CustodyStockView(Guid ReceiveId,string Site,string Partner,DateOnly Date,Guid ProductId,string Sku,string Name,string Unit,string? Serial,string? Batch,DateOnly? Expiry,decimal Received,decimal Accepted,decimal Returned,decimal Remaining,decimal ReferenceUnitCost,string Reference);
public sealed record DeliveryRejectionCommand(Guid OperationId,Guid DeliveryId,DateOnly Date,string Reason,IReadOnlyList<ReturnLine> Lines);
public sealed record PendingDeliveryView(Guid Id,string Number,string Party,DateOnly Date,string Location,IReadOnlyList<ReturnableLine> Lines);
public static class DeliveryRemainder
{
    private static (Guid,string?,string?,DateOnly?) Key(Guid product,string? serial,string? batch,DateOnly? expiry) => (product,string.IsNullOrWhiteSpace(serial) ? null : serial.Trim().ToUpperInvariant(),string.IsNullOrWhiteSpace(batch) ? null : batch.Trim().ToUpperInvariant(),expiry);
    public static List<TradeLine> Lines(TradeCommand original,IEnumerable<ReturnLine> rejected)
    {
        var returned = rejected.GroupBy(l => Key(l.ProductId,l.Serial,l.Batch,l.Expiry)).ToDictionary(g => g.Key,g => g.Sum(l => l.Quantity));
        if (returned.Count == 0) return original.Lines.ToList();
        var result = new List<TradeLine>();
        foreach (var line in original.Lines)
        {
            var key = Key(line.ProductId,line.Serial,line.Batch,line.Expiry); var removed = Math.Min(line.Quantity*line.Conversion,returned.GetValueOrDefault(key)); returned[key] = returned.GetValueOrDefault(key)-removed;
            if (removed == 0) { result.Add(line); continue; } var remainder = line.Quantity*line.Conversion-removed;
            if (remainder > 0) result.Add(line with { Quantity = remainder,Conversion = 1,UnitPrice = line.UnitPrice/line.Conversion });
        }
        if (returned.Values.Any(q => q != 0)) throw new BusinessException("Rejected quantities exceed this delivery's original quantities."); return result;
    }
}
