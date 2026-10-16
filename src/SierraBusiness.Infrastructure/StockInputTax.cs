using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

/// <summary>Allocates reviewed GST included in a direct goods receipt. Standard-cost goods were received at standard with the price
/// difference expensed to 5100, so their share reduces that variance. Other goods reduce the inventory still on hand; once goods
/// have moved out, the reviewer confirms the portion relating to issued goods, which reduces cost of sales or another expense.</summary>
internal static class StockInputTax
{
    internal const Account VarianceAccount = (Account)5100;
    internal sealed record Result(List<Movement> Movements, decimal Standard, decimal OnHand, decimal Issued, bool LaterIssues, IReadOnlyList<StockInputTaxLine> Lines);

    private static List<(Guid Product,decimal Gross)> GrossLines(BusinessDocument receipt,List<Movement> sourceRows)
    {
        var result = new List<(Guid,decimal)>();
        if (!string.IsNullOrEmpty(receipt.Detail))
        {
            using var detail = JsonDocument.Parse(receipt.Detail);
            if (detail.RootElement.TryGetProperty("Request",out var request) && request.Deserialize<TradeCommand>() is { } trade)
            {
                var rate = detail.RootElement.TryGetProperty("ExchangeRate",out var fx) && fx.ValueKind == JsonValueKind.Number ? fx.GetDecimal() : 1;
                foreach (var line in trade.Lines) result.Add((line.ProductId,AccountingEngine.Money(AccountingEngine.Money(line.Quantity*line.UnitPrice)*rate)));
                if (result.Sum(r => r.Item2) == receipt.Total) return result;
                result.Clear();
            }
        }
        // Older receipts without their request: the recorded stock values are the gross purchase values.
        foreach (var row in sourceRows) result.Add((row.ProductId,row.Value));
        return result;
    }

    internal static async Task<Result> Plan(BusinessDb db,string branch,BusinessDocument receipt,decimal allowable,decimal? issuedTax,bool preview = false)
    {
        var sourceRows = await db.Movements.AsNoTracking().Where(m => m.DocumentId == receipt.Id && m.Quantity > 0).ToListAsync();
        if (sourceRows.Count == 0) throw new BusinessException("The receipt has no received stock.");
        var gross = GrossLines(receipt,sourceRows);
        if (gross.Sum(g => g.Gross) != receipt.Total || receipt.Total <= 0) throw new BusinessException("Use a direct receipt whose recorded gross stock value equals the reviewed invoice net plus GST.");
        if (allowable > receipt.Total) throw new BusinessException("Allowable GST cannot exceed the receipt value.");
        // Share by gross line value, rounding once cumulatively so the shares add up exactly.
        var shares = new Dictionary<Guid,decimal>(); decimal running = 0,allocated = 0;
        for (var i = 0; i < gross.Count; i++)
        {
            running += gross[i].Gross; var cumulative = i == gross.Count-1 ? allowable : AccountingEngine.Money(allowable*running/receipt.Total);
            shares[gross[i].Product] = shares.GetValueOrDefault(gross[i].Product)+cumulative-allocated; allocated = cumulative;
        }
        var posted = await db.Operations.AsNoTracking().Where(o => o.ResultId == receipt.Id).Select(o => (DateTimeOffset?)o.Time).FirstOrDefaultAsync() ?? throw new BusinessException("Source receipt posting history not found.");
        var later = (await db.Operations.AsNoTracking().Where(o => o.Branch == branch).Select(o => new { o.ResultId,o.Time }).ToListAsync()).Where(o => o.Time > posted).Select(o => o.ResultId).ToHashSet();
        var products = await db.Products.AsNoTracking().Where(p => shares.Keys.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var reversed = (await db.Documents.AsNoTracking().Where(d => d.Branch == branch && d.Reverses != null).Select(d => d.Reverses!.Value).ToListAsync()).ToHashSet();
        var lines = new List<StockInputTaxLine>(); var movements = new List<Movement>(); decimal standard = 0,issuable = 0,suggested = 0; var laterIssues = false;
        var plain = new List<(Guid Product,decimal Share,List<Movement> Rows,decimal OnHandQuantity,bool Moved)>();
        foreach (var (product,share) in shares)
        {
            var rows = sourceRows.Where(r => r.ProductId == product).ToList(); var received = rows.Sum(r => r.Quantity);
            var all = await db.Movements.AsNoTracking().Where(m => m.Branch == branch && m.ProductId == product).ToListAsync();
            var onHand = all.Sum(m => m.Quantity);
            if ((await InventoryCost.Policy(db,branch,product)).Method == InventoryCostMethod.Standard)
            {
                standard += share; lines.Add(new(product,products.GetValueOrDefault(product)?.Sku ?? "",true,share,received,onHand,0)); continue;
            }
            var moved = all.Any(m => later.Contains(m.DocumentId) && (m.Quantity < 0 || m.Value < 0) && !reversed.Contains(m.DocumentId));
            laterIssues |= moved; issuable += share;
            var proposal = moved ? AccountingEngine.Money(share*Math.Clamp((received-onHand)/received,0,1)) : 0; suggested += proposal;
            lines.Add(new(product,products.GetValueOrDefault(product)?.Sku ?? "",false,share,received,onHand,proposal));
            plain.Add((product,share,rows,onHand,moved));
        }
        var issued = issuedTax ?? (preview ? suggested : 0);
        if (!laterIssues && issued != 0) throw new BusinessException("No received goods have moved out since the receipt; the whole allowable GST reduces inventory.");
        if (laterIssues && issuedTax is null && !preview) throw new BusinessException("Goods from this receipt may already have been issued. Preview the allocation and confirm the GST relating to issued goods.");
        if (issued < 0 || AccountingEngine.Money(issued) != issued || issued > issuable) throw new BusinessException("GST for issued goods must be between zero and the non-standard share of the allowable GST, with two decimal places.");
        var onHandTax = issuable-issued;
        // Spread the on-hand amount over products in proportion to their shares, then over identities.
        decimal spread = 0,running2 = 0;
        for (var i = 0; i < plain.Count; i++)
        {
            var item = plain[i]; running2 += item.Share;
            var cumulative = i == plain.Count-1 ? onHandTax : issuable == 0 ? 0 : AccountingEngine.Money(onHandTax*running2/issuable); var amount = cumulative-spread; spread = cumulative; if (amount == 0) continue;
            if (!item.Moved)
            {
                // Nothing has moved: reduce the received identities in proportion to their received values.
                var total = item.Rows.Sum(r => r.Value); decimal done = 0,run = 0;
                for (var j = 0; j < item.Rows.Count; j++) { var row = item.Rows[j]; run += row.Value; var c = j == item.Rows.Count-1 ? amount : AccountingEngine.Money(amount*run/total); var v = c-done; done = c; if (v != 0) movements.Add(Change(branch,row.ProductId,row.Location,row.Serial,row.Batch,row.Expiry,v)); }
                continue;
            }
            var stock = (await db.Movements.AsNoTracking().Where(m => m.Branch == branch && m.ProductId == item.Product).ToListAsync()).GroupBy(m => new InventoryCost.Identity(m.ProductId,m.Location,m.Serial,m.Batch,m.Expiry)).Select(g => (g.Key,Quantity: g.Sum(m => m.Quantity),Value: g.Sum(m => m.Value))).Where(g => g.Quantity > 0).ToList();
            var quantity = stock.Sum(g => g.Quantity);
            if (quantity <= 0) throw new BusinessException($"No stock of {lines.Single(l => l.ProductId == item.Product).Sku} remains; allocate its GST to issued goods.");
            decimal assigned = 0,qty = 0;
            for (var j = 0; j < stock.Count; j++)
            {
                var g = stock[j]; qty += g.Quantity; var c = j == stock.Count-1 ? amount : AccountingEngine.Money(amount*qty/quantity); var v = c-assigned; assigned = c;
                if (v > g.Value) throw new BusinessException("The GST on hand exceeds the remaining stock value. Allocate more of it to issued goods.");
                if (v != 0) movements.Add(Change(branch,g.Key.Product,g.Key.Location,g.Key.Serial,g.Key.Batch,g.Key.Expiry,v));
            }
        }
        return new(movements,standard,onHandTax,issued,laterIssues,lines);
    }
    private static Movement Change(string branch,Guid product,string location,string? serial,string? batch,DateOnly? expiry,decimal value) =>
        new() { Id = Guid.NewGuid(),Branch = branch,ProductId = product,Location = location,Serial = serial,Batch = batch,Expiry = expiry,Quantity = 0,Value = -value };
}
