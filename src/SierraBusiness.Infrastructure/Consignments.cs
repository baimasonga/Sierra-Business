using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
internal static class CustodyRules
{
    internal static async Task<List<ConsignmentSiteView>> Sites(BusinessDb db,string branch) => (await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == "consignment.site").ToListAsync()).Select(o => JsonSerializer.Deserialize<ConsignmentSiteCommand>(o.Payload)!).Select(c => new ConsignmentSiteView(c.Code,c.Partner,c.Ownership)).ToList();
    internal static async Task<List<CustodyStockView>> Stock(BusinessDb db,string branch)
    {
        var sites = (await Sites(db,branch)).ToDictionary(s => s.Code); var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id);
        var ops = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && (o.Action == "consignment.receive" || o.Action == "consignment.return" || o.Action == "Receipt.post")).ToListAsync();
        var receives = ops.Where(o => o.Action == "consignment.receive").Select(o => JsonSerializer.Deserialize<CustodyReceiveCommand>(o.Payload)!).ToArray();
        var returns = ops.Where(o => o.Action == "consignment.return").Select(o => JsonSerializer.Deserialize<CustodyReturnCommand>(o.Payload)!).ToArray();
        var reversed = (await db.Documents.AsNoTracking().Where(d => d.Branch == branch && d.Reverses != null).Select(d => d.Reverses!.Value).ToListAsync()).ToHashSet();
        var accepted = ops.Where(o => o.Action == "Receipt.post" && !reversed.Contains(o.ResultId)).Select(o => JsonSerializer.Deserialize<TradeCommand>(o.Payload)!).Where(c => c.CustodyReceiveId != null).ToArray();
        return receives.Select(c => { var site = sites[c.Site]; var p = products[c.ProductId]; var used = accepted.Where(a => a.CustodyReceiveId == c.OperationId).Sum(a => a.Lines.Sum(l => l.Quantity*l.Conversion)); var returned = returns.Where(r => r.ReceiveId == c.OperationId).Sum(r => r.Quantity); return new CustodyStockView(c.OperationId,c.Site,site.Partner,c.Date,p.Id,p.Sku,p.Name,p.Unit,c.Serial,c.Batch,c.Expiry,c.Quantity,used,returned,c.Quantity-used-returned,c.ReferenceUnitCost,c.Reference); }).ToList();
    }
    internal static async Task ValidateAcceptance(BusinessDb db,Actor actor,TradeCommand input)
    {
        if (input.CustodyReceiveId is not Guid id) return;
        if (input.Kind != DocumentKind.Receipt || input.OrderId != null || input.DeliveryId != null || input.HeldSaleId != null) throw new BusinessException("Accept supplier-owned custody stock through a direct goods receipt.");
        var stock = (await Stock(db,actor.Branch)).SingleOrDefault(c => c.ReceiveId == id) ?? throw new BusinessException("Custody receipt not found in this branch.");
        if (input.BusinessDate < stock.Date || !input.Party.Trim().Equals(stock.Partner,StringComparison.OrdinalIgnoreCase) || input.Lines.Any(l => l.ProductId != stock.ProductId || Normalize(l.Serial) != stock.Serial || Normalize(l.Batch) != stock.Batch || l.Expiry != stock.Expiry) || input.Lines.Sum(l => l.Quantity*l.Conversion) > stock.Remaining) throw new BusinessException("Accept only the remaining quantity, supplier and stock identity of this custody receipt.");
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
internal static class DeliveryRejections
{
    internal static async Task<List<Movement>> Movements(BusinessDb db,string branch,Guid? delivery = null)
    {
        var ops = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == "delivery.reject" && !db.Documents.Any(d => d.Reverses == o.ResultId)).ToListAsync();
        var ids = ops.Where(o => delivery is null || JsonSerializer.Deserialize<DeliveryRejectionCommand>(o.Payload)!.DeliveryId == delivery).Select(o => o.ResultId).ToArray();
        return await db.Movements.AsNoTracking().Where(m => ids.Contains(m.DocumentId)).ToListAsync();
    }
    internal static async Task<List<TradeLine>> Remaining(BusinessDb db,string branch,BusinessDocument delivery)
    {
        using var detail = JsonDocument.Parse(delivery.Detail); var request = detail.RootElement.GetProperty("Request").Deserialize<TradeCommand>()!;
        return DeliveryRemainder.Lines(request,(await Movements(db,branch,delivery.Id)).Select(m => new ReturnLine(m.ProductId,m.Serial,m.Batch,m.Expiry,m.Quantity)));
    }
}
public sealed partial class InventoryService
{
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    public async Task<IReadOnlyList<ConsignmentSiteView>> ConsignmentSites() { var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync(); return await CustodyRules.Sites(db,actor.Branch); }
    public Task<Guid> SaveConsignmentSite(ConsignmentSiteCommand input) => Execute(input.OperationId,"consignment.site",input,async (db,actor) =>
    {
        if (!Enum.IsDefined(input.Ownership) || input.Code != input.Code.Trim().ToUpperInvariant() || !Regex.IsMatch(input.Code,"^CON-[A-Z0-9_-]{1,16}$")) throw new BusinessException("Use a code starting CON- followed by 1–16 uppercase letters, digits, hyphens or underscores.");
        Rules.Required(input.Partner,"Consignment partner",160);
        if (input.Partner != input.Partner.Trim()) throw new BusinessException("Remove spaces before or after the partner name.");
        if ((await CustodyRules.Sites(db,actor.Branch)).Any(s => s.Code == input.Code) || await db.StockLocations.AnyAsync(l => l.Code == input.Code)) throw new BusinessException("That consignment site code is already used.");
        if (input.Ownership == ConsignmentOwnership.CompanyOwned) { var name = "Consigned to "+input.Partner; db.StockLocations.Add(new StockLocation { Code = input.Code,Name = name[..Math.Min(160,name.Length)] }); }
        return input.OperationId;
    });
    public async Task<IReadOnlyList<CustodyStockView>> CustodyStock() { var actor = await Authorize(); actor.Demand("accounting.read"); await using var db = await factory.CreateDbContextAsync(); return await CustodyRules.Stock(db,actor.Branch); }
    public Task<Guid> ReceiveCustody(CustodyReceiveCommand input) => Execute(input.OperationId,"consignment.receive",input,async (db,actor) =>
    {
        var site = (await CustodyRules.Sites(db,actor.Branch)).SingleOrDefault(s => s.Code == input.Site && s.Ownership == ConsignmentOwnership.SupplierOwned) ?? throw new BusinessException("Choose a supplier-owned custody site.");
        if (input.Date <= await PeriodControls.ClosedThrough(db,actor.Branch,options.Value.ClosedThrough) || input.Date > DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("Use an open-period custody date no later than today.");
        var p = await db.Products.FindAsync(input.ProductId) ?? throw new BusinessException("Product not found."); await InventoryManagement.Active(db,actor.Branch,p.Id); Rules.Positive(input.Quantity,"Custody quantity"); Rules.Positive(input.ReferenceUnitCost,"Reference unit cost"); Rules.Required(input.Reference,"Custody reference",160);
        Rules.Tracking(p.Tracking,input.Quantity,input.Serial,input.Batch,input.Expiry,input.Date,false);
        if (p.Tracking != TrackingMode.Serial && input.Serial != null || p.Tracking is not (TrackingMode.Batch or TrackingMode.BatchExpiry) && (input.Batch != null || input.Expiry != null)) throw new BusinessException("Use the product's configured tracking identity.");
        if (input.Serial is not null && (input.Serial != input.Serial.Trim().ToUpperInvariant() || (await CustodyRules.Stock(db,actor.Branch)).Any(c => c.Serial == input.Serial && c.Remaining > 0) || await db.Movements.AnyAsync(m => m.Serial == input.Serial))) throw new BusinessException("Use an uppercase, previously unrecorded custody serial.");
        if (input.Batch is not null && input.Batch != input.Batch.Trim().ToUpperInvariant()) throw new BusinessException("Use an uppercase batch.");
        return input.OperationId;
    });
    public Task<Guid> ReturnCustody(CustodyReturnCommand input) => Execute(input.OperationId,"consignment.return",input,async (db,actor) =>
    {
        Rules.Positive(input.Quantity,"Custody return quantity"); Rules.Required(input.Reason,"Custody return reason",160);
        var stock = (await CustodyRules.Stock(db,actor.Branch)).SingleOrDefault(c => c.ReceiveId == input.ReceiveId) ?? throw new BusinessException("Custody receipt not found.");
        if (input.Date < stock.Date || input.Date <= await PeriodControls.ClosedThrough(db,actor.Branch,options.Value.ClosedThrough) || input.Date > DateOnly.FromDateTime(DateTime.UtcNow) || input.Quantity > stock.Remaining || stock.Serial is not null && input.Quantity != 1) throw new BusinessException("Return only remaining custody units on an open date on or after receipt.");
        return input.OperationId;
    });
    public async Task<IReadOnlyList<PendingDeliveryView>> PendingDeliveries()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync(); var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id); var output = new List<PendingDeliveryView>();
        var docs = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && d.Kind == DocumentKind.Delivery && !db.Documents.Any(r => r.Reverses == d.Id) && !db.DeliveryInvoices.Any(i => i.DeliveryId == d.Id && !db.Documents.Any(r => r.Reverses == i.InvoiceId))).ToListAsync();
        foreach (var doc in docs)
        {
            using var json = JsonDocument.Parse(doc.Detail); var request = json.RootElement.GetProperty("Request").Deserialize<TradeCommand>()!; var rejected = await DeliveryRejections.Movements(db,actor.Branch,doc.Id);
            var lines = request.Lines.GroupBy(l => (l.ProductId,Serial: Normalize(l.Serial),Batch: Normalize(l.Batch),l.Expiry)).Select(g => new ReturnableLine(g.Key.ProductId,products[g.Key.ProductId].Name,g.Key.Serial,g.Key.Batch,g.Key.Expiry,g.Sum(l => l.Quantity*l.Conversion),rejected.Where(m => m.ProductId == g.Key.ProductId && m.Serial == g.Key.Serial && m.Batch == g.Key.Batch && m.Expiry == g.Key.Expiry).Sum(m => m.Quantity))).Where(l => l.OriginalQuantity > l.ReturnedQuantity).ToArray();
            if (lines.Length > 0) output.Add(new(doc.Id,doc.Number,doc.Party,doc.BusinessDate,request.Location,lines));
        }
        return output;
    }
    public Task<Guid> RejectDelivery(DeliveryRejectionCommand input) => Execute(input.OperationId,"delivery.reject",input,async (db,actor) =>
    {
        Rules.Required(input.Reason,"Delivery rejection reason",160); if (input.Lines is null || input.Lines.Count is < 1 or > 200 || input.Lines.Any(l => l is null)) throw new BusinessException("Select 1–200 rejected stock identities.");
        var delivery = await db.Documents.SingleOrDefaultAsync(d => d.Id == input.DeliveryId && d.Branch == actor.Branch && d.Kind == DocumentKind.Delivery && !db.Documents.Any(r => r.Reverses == d.Id)) ?? throw new BusinessException("Active delivery not found.");
        if (input.Date < delivery.BusinessDate || input.Date > DateOnly.FromDateTime(DateTime.UtcNow) || input.Date <= await PeriodControls.ClosedThrough(db,actor.Branch,options.Value.ClosedThrough) || await db.DeliveryInvoices.AnyAsync(i => i.DeliveryId == delivery.Id && !db.Documents.Any(r => r.Reverses == i.InvoiceId))) throw new BusinessException("Reject uninvoiced deliveries on an open date on or after dispatch. Use sales returns after invoicing.");
        using var detail = JsonDocument.Parse(delivery.Detail); var originalRequest = detail.RootElement.GetProperty("Request").Deserialize<TradeCommand>()!; var source = await db.Movements.AsNoTracking().Where(m => m.DocumentId == delivery.Id && m.Quantity < 0).ToListAsync(); var earlier = await DeliveryRejections.Movements(db,actor.Branch,delivery.Id);
        var doc = new BusinessDocument { Id = Guid.NewGuid(),Branch = actor.Branch,Number = $"{actor.Branch}-{options.Value.Device}-REJECT-{input.OperationId:N}",Kind = DocumentKind.DeliveryRejection,Party = delivery.Party,BusinessDate = input.Date,Settlement = "Rejected before invoice",Detail = JsonSerializer.Serialize(new { Request = input }) };
        decimal total = 0; var identities = new HashSet<(Guid,string?,string?,DateOnly?)>();
        foreach (var line in input.Lines)
        {
            var serial = Normalize(line.Serial); var batch = Normalize(line.Batch); if (!identities.Add((line.ProductId,serial,batch,line.Expiry))) throw new BusinessException("Reject each stock identity once per document."); Rules.Positive(line.Quantity,"Rejected quantity");
            var matching = source.Where(m => m.ProductId == line.ProductId && m.Serial == serial && m.Batch == batch && m.Expiry == line.Expiry).ToArray(); if (matching.Length == 0) throw new BusinessException("The rejected stock identity is not on this delivery.");
            var p = await db.Products.FindAsync(line.ProductId) ?? throw new BusinessException("Product not found."); Rules.Tracking(p.Tracking,line.Quantity,serial,batch,line.Expiry,input.Date,false); await InventoryManagement.SerialAvailable(db,actor.Branch,serial); await InventoryCost.GuardDate(db,actor.Branch,p.Id,input.Date);
            if (serial is not null && await db.Movements.Where(m => m.Serial == serial).SumAsync(m => m.Quantity) != 0) throw new BusinessException("That serial is already in stock.");
            var old = earlier.Where(m => m.ProductId == p.Id && m.Serial == serial && m.Batch == batch && m.Expiry == line.Expiry).ToArray(); var quantity = -matching.Sum(m => m.Quantity); var cumulative = old.Sum(m => m.Quantity)+line.Quantity;
            if (cumulative > quantity) throw new BusinessException("Rejected quantity exceeds the delivery remainder.");
            var cost = AccountingEngine.Money(-matching.Sum(m => m.Value)*cumulative/quantity)-old.Sum(m => m.Value); total += cost;
            db.Movements.Add(new Movement { Id = Guid.NewGuid(),DocumentId = doc.Id,Branch = actor.Branch,ProductId = p.Id,Location = originalRequest.Location,Serial = serial,Batch = batch,Expiry = line.Expiry,Quantity = line.Quantity,Value = cost });
            if (originalRequest.OrderId is Guid order) db.OrderFulfillments.Add(new OrderFulfillment { Id = Guid.NewGuid(),OrderId = order,ProductId = p.Id,DocumentId = doc.Id,Quantity = -line.Quantity });
        }
        doc.Total = total; db.Documents.Add(doc);
        foreach (var posting in AccountingEngine.Balanced([new(Account.Inventory,total,0),new((Account)1250,0,total)])) db.Journal.Add(new JournalLine { Id = Guid.NewGuid(),DocumentId = doc.Id,Branch = actor.Branch,Account = posting.Account,Debit = posting.Debit,Credit = posting.Credit });
        return doc.Id;
    });
}
