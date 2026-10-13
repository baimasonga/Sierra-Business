using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

internal static class InventoryCost
{
    internal sealed record Identity(Guid Product,string Location,string? Serial,string? Batch,DateOnly? Expiry);
    private sealed class Lot(decimal quantity,decimal value,long order,Guid origin,Guid added)
    {
        public decimal Quantity = quantity, Value = value; public long Order = order; public Guid Origin = origin, Added = added;
        public Lot Copy(decimal quantity,decimal value,Guid? added = null) => new(quantity,value,Order,Origin,added ?? Added);
    }
    private sealed class Cache { public Dictionary<string,Dictionary<Guid,InventoryCostPolicy>> Branches = []; }
    private static readonly ConditionalWeakTable<BusinessDb,Cache> Policies = new();
    internal static async Task<Dictionary<Guid,InventoryCostPolicy>> PolicyMap(BusinessDb db,string branch)
    {
        var cache = Policies.GetValue(db,_ => new Cache()); if (cache.Branches.TryGetValue(branch,out var map)) return map;
        var ops = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == "inventory.cost-method").ToListAsync();
        map = ops.Select(o => JsonSerializer.Deserialize<InventoryCostCommand>(o.Payload)!).GroupBy(c => c.ProductId).ToDictionary(g => g.Key,g => { var c = g.OrderByDescending(c => c.ExpectedVersion).First(); return new InventoryCostPolicy(c.ProductId,c.ExpectedVersion+1,c.Method,c.StandardUnitCost,c.From); });
        return cache.Branches[branch] = map;
    }
    internal static async Task<InventoryCostPolicy> Policy(BusinessDb db,string branch,Guid product) => (await PolicyMap(db,branch)).GetValueOrDefault(product) ?? new(product,0,InventoryCostMethod.WeightedAverage,0,new(2000,1,1));
    internal static async Task GuardDate(BusinessDb db,string branch,Guid product,DateOnly date)
    {
        var policy = await Policy(db,branch,product); if (policy.Method == InventoryCostMethod.WeightedAverage && policy.Version <= 1) return;
        if (date < policy.From || await db.Movements.AnyAsync(m => m.Branch == branch && m.ProductId == product && db.Documents.Any(d => d.Id == m.DocumentId && d.BusinessDate > date))) throw new BusinessException("Cost-controlled items cannot be posted before their current policy date or later stock activity.");
    }
    internal static async Task<decimal> Issue(BusinessDb db,string branch,Guid product,string location,string? serial,string? batch,DateOnly? expiry,decimal quantity,IEnumerable<Movement> matching)
    {
        var rows = matching.ToArray(); var available = rows.Sum(m => m.Quantity); var value = rows.Sum(m => m.Value);
        if (quantity <= 0 || quantity > available) throw new BusinessException("Insufficient stock for cost calculation.");
        if (quantity == available) return value;
        var policy = await Policy(db,branch,product);
        if (policy.Method != InventoryCostMethod.Fifo) return AccountingEngine.Money(value/available*quantity);
        var layers = await Rebuild(db,branch,DateOnly.MaxValue,product,false,false);
        var key = new Identity(product,location,serial,batch,expiry); if (!layers.TryGetValue(key,out var list)) throw new BusinessException("FIFO layers are unavailable for this stock identity.");
        return Take(list,quantity,false).Sum(l => l.Value);
    }
    private static List<Lot> Take(List<Lot> list,decimal quantity,bool lifo,Guid? added = null)
    {
        var taken = new List<Lot>(); var candidates = list.Where(l => l.Quantity > 0 && (added is null || l.Added == added)).OrderBy(l => lifo ? -l.Order : l.Order).ToArray();
        foreach (var lot in candidates)
        {
            var amount = Math.Min(quantity,lot.Quantity); if (amount <= 0) continue;
            var value = amount == lot.Quantity ? lot.Value : AccountingEngine.Money(lot.Value*amount/lot.Quantity);
            lot.Quantity -= amount; lot.Value -= value; quantity -= amount; taken.Add(lot.Copy(amount,value)); if (quantity == 0) break;
        }
        if (quantity != 0) throw new BusinessException("Stock layer history does not reconcile. Check the product's posted movements."); return taken;
    }
    private static void ResizeValue(List<Lot> lots,decimal target)
    {
        var current = lots.Sum(l => l.Value); var quantity = lots.Sum(l => l.Quantity); if (target < 0 || quantity <= 0 && target != 0) throw new BusinessException("Stock layer values do not reconcile.");
        var active = lots.Where(l => l.Quantity > 0).ToArray();
        foreach (var lot in active) lot.Value = AccountingEngine.Money(target*(current == 0 ? lot.Quantity/quantity : lot.Value/current));
        var difference = target-active.Sum(l => l.Value);
        foreach (var lot in active.OrderByDescending(l => l.Value)) { if (difference >= 0) { lot.Value += difference; difference = 0; break; } var reduction = Math.Min(lot.Value,-difference); lot.Value -= reduction; difference += reduction; if (difference == 0) break; }
    }
    internal static async Task<Dictionary<Identity,decimal>> Values(BusinessDb db,string branch,DateOnly asAt,bool lifo)
    {
        var layers = await Rebuild(db,branch,asAt,null,lifo,true); return layers.ToDictionary(p => p.Key,p => p.Value.Sum(l => l.Value));
    }
    private static async Task<Dictionary<Identity,List<Lot>>> Rebuild(BusinessDb db,string branch,DateOnly asAt,Guid? product,bool lifo,bool hypothetical)
    {
        var docs = await db.Documents.AsNoTracking().Where(d => d.Branch == branch && d.BusinessDate <= asAt).ToDictionaryAsync(d => d.Id);
        var rows = await db.Movements.AsNoTracking().Where(m => m.Branch == branch && (product == null || m.ProductId == product)).ToListAsync();
        rows = rows.Where(m => docs.ContainsKey(m.DocumentId)).ToList();
        foreach (var row in db.Movements.Local.Where(m => db.Entry(m).State == EntityState.Added && m.Branch == branch && (product == null || m.ProductId == product))) rows.Add(row);
        foreach (var doc in db.Documents.Local.Where(d => db.Entry(d).State == EntityState.Added)) docs.TryAdd(doc.Id,doc);
        foreach (var id in rows.Select(m => m.DocumentId).Distinct()) docs.TryAdd(id,new BusinessDocument { Id = id, Kind = DocumentKind.Sale, BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow) });
        var times = (await db.Operations.AsNoTracking().Where(o => o.Branch == branch).Select(o => new { o.ResultId,o.Time }).ToListAsync()).GroupBy(o => o.ResultId).ToDictionary(g => g.Key,g => g.Min(o => o.Time));
        var layers = new Dictionary<Identity,List<Lot>>(); var consumed = new Dictionary<(Guid,Identity),List<Lot>>(); long order = 0;
        Identity Key(Movement m) => new(m.ProductId,m.Location,m.Serial,m.Batch,m.Expiry);
        List<Lot> Stock(Identity key) { if (!layers.TryGetValue(key,out var list)) layers[key] = list = []; return list; }
        foreach (var group in rows.GroupBy(m => m.DocumentId).OrderBy(g => times.GetValueOrDefault(g.Key,db.Movements.Local.Any(m => m.DocumentId == g.Key && db.Entry(m).State == EntityState.Added) ? DateTimeOffset.MaxValue : new DateTimeOffset(docs[g.Key].BusinessDate.ToDateTime(TimeOnly.MinValue),TimeSpan.Zero))).ThenBy(g => docs[g.Key].Number))
        {
            var doc = docs[group.Key]; var carried = new Dictionary<Guid,List<Lot>>();
            var outgoing = group.Where(m => m.Quantity < 0).ToList();
            while (outgoing.Count > 0)
            {
                decimal Match(Movement candidate)
                {
                    var stock = Stock(Key(candidate)); var added = doc.Reverses is Guid reversed && stock.Any(l => l.Added == reversed && l.Quantity > 0) ? reversed : (Guid?)null;
                    try { return Math.Abs(Take(stock.Select(l => l.Copy(l.Quantity,l.Value)).ToList(),-candidate.Quantity,lifo,added).Sum(l => l.Value)+candidate.Value); } catch (BusinessException) { return decimal.MaxValue; }
                }
                var movement = hypothetical ? outgoing[0] : outgoing.OrderBy(Match).First(); outgoing.Remove(movement);
                var key = Key(movement); var list = Stock(key); var before = list.Sum(l => l.Value);
                var taken = Take(list,-movement.Quantity,lifo,doc.Reverses is Guid reversed && Stock(key).Any(l => l.Added == reversed && l.Quantity > 0) ? reversed : null);
                if (!hypothetical) { ResizeValue(taken,-movement.Value); ResizeValue(list,before+movement.Value); }
                if (!consumed.TryGetValue((doc.Id,key),out var saved)) consumed[(doc.Id,key)] = saved = [];
                saved.AddRange(taken.Select(l => l.Copy(l.Quantity,l.Value)));
                if (!carried.TryGetValue(movement.ProductId,out var moved)) carried[movement.ProductId] = moved = []; moved.AddRange(taken.Select(l => l.Copy(l.Quantity,l.Value)));
            }
            var transfer = doc.Kind == DocumentKind.StockTransfer || doc.Reverses is Guid original && docs.GetValueOrDefault(original)?.Kind == DocumentKind.StockTransfer;
            int ReceiptPosition(Movement m)
            {
                if (doc.Kind != DocumentKind.Receipt || string.IsNullOrEmpty(doc.Detail)) return 0;
                using var detail = JsonDocument.Parse(doc.Detail); if (!detail.RootElement.TryGetProperty("Request",out var request)) return 0;
                var trade = request.Deserialize<TradeCommand>(); if (trade is null) return 0; var rate = detail.RootElement.TryGetProperty("ExchangeRate",out var fx) ? fx.GetDecimal() : 1;
                var index = trade.Lines.FindIndex(l => l.ProductId == m.ProductId && l.Quantity*l.Conversion == m.Quantity && AccountingEngine.Money(AccountingEngine.Money(l.Quantity*l.UnitPrice)*rate) == m.Value);
                return index < 0 ? int.MaxValue : index;
            }
            foreach (var movement in group.Where(m => m.Quantity >= 0).OrderBy(ReceiptPosition))
            {
                var key = Key(movement); var list = Stock(key);
                if (movement.Quantity == 0) { ResizeValue(list,list.Sum(l => l.Value)+movement.Value); continue; }
                List<Lot>? source = null;
                if (doc.Reverses is Guid reversed && consumed.TryGetValue((reversed,key),out var undone)) source = undone;
                else if (transfer && carried.TryGetValue(movement.ProductId,out var moved)) source = moved;
                if (source is not null && source.Sum(l => l.Quantity) >= movement.Quantity)
                {
                    var copies = new List<Lot>(); var total = source.Sum(l => l.Quantity); decimal assigned = 0;
                    for (var i = 0; i < source.Count; i++) { var quantity = i == source.Count-1 ? movement.Quantity-assigned : movement.Quantity*source[i].Quantity/total; assigned += quantity; copies.Add(source[i].Copy(quantity,source[i].Value*quantity/source[i].Quantity,doc.Id)); }
                    if (!hypothetical) ResizeValue(copies,movement.Value); list.AddRange(copies);
                }
                else list.Add(new(movement.Quantity,movement.Value,++order,movement.Id,doc.Id));
            }
        }
        if (!hypothetical)
            foreach (var group in rows.GroupBy(Key)) { var list = Stock(group.Key); if (list.Sum(l => l.Quantity) != group.Sum(m => m.Quantity) || list.Sum(l => l.Value) != group.Sum(m => m.Value)) throw new BusinessException("FIFO stock layers do not match the protected stock register."); }
        return layers;
    }
}
