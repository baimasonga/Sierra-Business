using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed partial class ReportService
{
    private sealed record AgeIdentity(Guid Product, string Location, string? Serial, string? Batch, DateOnly? Expiry);
    private sealed class AgeLayer(decimal quantity, DateOnly? date) { public decimal Quantity = quantity; public DateOnly? Date = date; }
    public async Task<StockAgeingReport> StockAgeing(DateOnly asAt, int slowDays = 90)
    {
        var actor = await Authorize();
        if (slowDays < 1 || slowDays > 3660) throw new BusinessException("Slow-moving days must be between 1 and 3660.");
        await using var db = await factory.CreateDbContextAsync();
        var documents = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && d.BusinessDate <= asAt).ToDictionaryAsync(d => d.Id);
        var reversed = documents.Values.Where(d => d.Reverses != null).Select(d => d.Reverses!.Value).ToHashSet();
        var movements = (await db.Movements.AsNoTracking().Where(m => m.Branch == actor.Branch).ToListAsync()).Where(m => documents.ContainsKey(m.DocumentId)).ToArray();
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id);
        var times = (await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch).Select(o => new { o.ResultId, o.Time }).ToListAsync())
            .GroupBy(o => o.ResultId).ToDictionary(g => g.Key, g => g.Min(o => o.Time));
        var layers = new Dictionary<AgeIdentity, List<AgeLayer>>(); var dispatches = new Dictionary<AgeIdentity, DateOnly>();
        List<AgeLayer> Layers(AgeIdentity key) { if (!layers.TryGetValue(key, out var list)) layers[key] = list = []; return list; }
        static AgeIdentity Identity(Movement m) => new(m.ProductId, m.Location, m.Serial, m.Batch, m.Expiry);
        void Add(AgeIdentity key, decimal quantity, DateOnly? date)
        {
            foreach (var deficit in Layers(key).Where(l => l.Quantity < 0)) { var offset = Math.Min(quantity, -deficit.Quantity); deficit.Quantity += offset; quantity -= offset; }
            if (quantity > 0) Layers(key).Add(new(quantity, date));
        }
        List<AgeLayer> Take(AgeIdentity key, decimal quantity)
        {
            var taken = new List<AgeLayer>();
            foreach (var layer in Layers(key).OrderBy(l => l.Date ?? DateOnly.MinValue))
            {
                var amount = Math.Min(quantity, Math.Max(0, layer.Quantity)); if (amount == 0) continue;
                layer.Quantity -= amount; quantity -= amount; taken.Add(new(amount, layer.Date)); if (quantity == 0) break;
            }
            if (quantity > 0) { Layers(key).Add(new(-quantity, null)); taken.Add(new(quantity, null)); }
            return taken;
        }
        foreach (var group in movements.GroupBy(m => m.DocumentId).OrderBy(g => documents[g.Key].BusinessDate).ThenBy(g => times.GetValueOrDefault(g.Key)).ThenBy(g => documents[g.Key].Number))
        {
            var doc = documents[group.Key]; if (doc.Reverses != null || reversed.Contains(doc.Id)) continue;
            if (doc.Kind == DocumentKind.StockTransfer)
            {
                var carried = new List<AgeLayer>();
                foreach (var m in group.Where(m => m.Quantity < 0)) carried.AddRange(Take(Identity(m), -m.Quantity));
                foreach (var m in group.Where(m => m.Quantity > 0))
                {
                    var remaining = m.Quantity;
                    foreach (var l in carried) { var amount = Math.Min(remaining, l.Quantity); if (amount <= 0) continue; Add(Identity(m), amount, l.Date); remaining -= amount; l.Quantity -= amount; }
                    if (remaining > 0) Add(Identity(m), remaining, null);
                }
                continue;
            }
            foreach (var m in group)
            {
                var key = Identity(m);
                if (m.Quantity > 0) Add(key, m.Quantity, doc.BusinessDate);
                else if (m.Quantity < 0)
                {
                    Take(key, -m.Quantity);
                    if (doc.Kind is DocumentKind.Sale or DocumentKind.Delivery or DocumentKind.ServiceIssue) dispatches[key] = doc.BusinessDate;
                }
            }
        }
        var rows = movements.GroupBy(m => Identity(m)).Select(g =>
        {
            var key = g.Key; var p = products[key.Product]; var quantity = g.Sum(m => m.Quantity); var value = g.Sum(m => m.Value);
            var available = Layers(key); var known = available.Where(l => l.Date != null && l.Quantity > 0).ToArray();
            decimal Bucket(int min, int max) => known.Where(l => asAt.DayNumber - l.Date!.Value.DayNumber >= min && asAt.DayNumber - l.Date.Value.DayNumber <= max).Sum(l => l.Quantity);
            var b30 = Bucket(0, 30); var b60 = Bucket(31, 60); var b90 = Bucket(61, 90); var old = Bucket(91, int.MaxValue);
            DateOnly? last = dispatches.TryGetValue(key, out var date) ? date : null;
            var slow = quantity > 0 && (last is DateOnly issued ? asAt.DayNumber - issued.DayNumber >= slowDays : available.Any(l => l.Quantity > 0 && l.Date is DateOnly received && asAt.DayNumber - received.DayNumber >= slowDays));
            return new StockAgeingRow(p.Id, p.Sku, p.Name, p.Unit, key.Location, key.Serial, key.Batch, key.Expiry, quantity, b30, b60, b90, old, quantity - b30 - b60 - b90 - old, value, last, slow);
        }).Where(r => r.Quantity != 0 || r.Value != 0).OrderBy(r => r.Sku).ThenBy(r => r.Location).ThenBy(r => r.Batch).ToArray();
        return new(asAt, slowDays, rows, rows.Sum(r => r.Value));
    }
}
