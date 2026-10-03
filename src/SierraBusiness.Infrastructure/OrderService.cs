using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed class OrderService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IOrderService
{
    private async Task<Actor> Authorize()
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }
    public async Task<IReadOnlyList<OrderView>> Read()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var orders = await db.TradeOrders.AsNoTracking().Where(x => x.Branch == actor.Branch).OrderByDescending(x => x.Date).ToListAsync();
        var lines = await db.TradeOrderLines.AsNoTracking().Where(x => db.TradeOrders.Any(o => o.Id == x.OrderId && o.Branch == actor.Branch)).ToListAsync();
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var fulfilled = await db.OrderFulfillments.AsNoTracking().Where(x => db.TradeOrders.Any(o => o.Id == x.OrderId && o.Branch == actor.Branch) && !db.Documents.Any(d => d.Reverses == x.DocumentId)).ToListAsync();
        var closed = (await db.OrderClosures.Where(x => db.TradeOrders.Any(o => o.Id == x.OrderId && o.Branch == actor.Branch)).Select(x => x.OrderId).ToListAsync()).ToHashSet();
        return orders.Select(o => new OrderView(o.Id, o.Number, o.Type, o.Party, o.Date, o.DueDate, o.Reference, closed.Contains(o.Id), orders.SingleOrDefault(x => x.QuotationId == o.Id)?.Id,
            lines.Where(x => x.OrderId == o.Id).Select(l => new OrderLineView(l.ProductId, products[l.ProductId].Sku, products[l.ProductId].Name, l.Quantity, l.UnitPrice, fulfilled.Where(f => f.OrderId == o.Id && f.ProductId == l.ProductId).Sum(f => f.Quantity), products[l.ProductId].Unit)).ToArray())).ToArray();
    }
    public Task<Guid> Create(OrderCommand input) => Execute(input.OperationId, "order.create", input, async (db, actor) =>
    {
        if (!Enum.IsDefined(input.Type)) throw new BusinessException("Select a valid order type.");
        actor.Demand(input.Type == OrderType.PurchaseOrder ? "inventory.receive" : "sales.create");
        if (input.Date <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough) || input.Date > DateOnly.FromDateTime(DateTime.UtcNow) || input.DueDate < input.Date) throw new BusinessException("Use an open order date no later than today and a due/expiry date on or after it.");
        if (input.Lines is null || input.Lines.Count is < 1 or > 200 || input.Lines.Select(x => x.ProductId).Distinct().Count() != input.Lines.Count) throw new BusinessException("Enter 1–200 lines, one per product.");
        var party = Rules.Required(input.Party, "Customer / supplier");
        if (input.Reference.Length > 160) throw new BusinessException("Reference is limited to 160 characters.");
        if (input.QuotationId is not null)
        {
            var quote = await db.TradeOrders.SingleOrDefaultAsync(x => x.Id == input.QuotationId && x.Branch == actor.Branch) ?? throw new BusinessException("Quotation not found.");
            if (input.Type != OrderType.SalesOrder || quote.Type != OrderType.Quotation || quote.Party != party || quote.DueDate < input.Date || input.Date < quote.Date || await db.OrderClosures.AnyAsync(x => x.OrderId == quote.Id) || await db.TradeOrders.AnyAsync(x => x.QuotationId == quote.Id)) throw new BusinessException("Only an open, unexpired quotation for the same customer can be converted once to a sales order.");
            var quoteLines = await db.TradeOrderLines.Where(x => x.OrderId == quote.Id).ToListAsync();
            if (quoteLines.Count != input.Lines.Count || quoteLines.Any(q => !input.Lines.Any(l => l.ProductId == q.ProductId && l.Quantity == q.Quantity && l.UnitPrice == q.UnitPrice))) throw new BusinessException("Quotation conversion must retain its quantities and prices. Create a new quotation for changes.");
        }
        var order = new TradeOrder { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-O-{input.OperationId:N}", Type = input.Type, Party = party, Date = input.Date, DueDate = input.DueDate, Reference = input.Reference.Trim(), QuotationId = input.QuotationId };
        db.TradeOrders.Add(order);
        foreach (var line in input.Lines)
        {
            Rules.Positive(line.Quantity, "Quantity"); Rules.Positive(line.UnitPrice, "Price per base unit");
            if (!await db.Products.AnyAsync(x => x.Id == line.ProductId)) throw new BusinessException("Product not found.");
            await InventoryManagement.Active(db, actor.Branch, line.ProductId);
            if (input.Type != OrderType.PurchaseOrder) await CommerceRules.Price(db, line.ProductId, line.UnitPrice, 1);
            db.TradeOrderLines.Add(new() { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = line.ProductId, Quantity = line.Quantity, UnitPrice = line.UnitPrice });
        }
        return order.Id;
    });
    public Task<Guid> Close(CloseOrderCommand input) => Execute(input.OperationId, "order.close", input, async (db, actor) =>
    {
        var order = await db.TradeOrders.SingleOrDefaultAsync(x => x.Id == input.OrderId && x.Branch == actor.Branch) ?? throw new BusinessException("Order not found.");
        actor.Demand(order.Type == OrderType.PurchaseOrder ? "inventory.receive" : "sales.create");
        if (await db.OrderClosures.AnyAsync(x => x.OrderId == order.Id) || await db.TradeOrders.AnyAsync(x => x.QuotationId == order.Id)) throw new BusinessException("The order is already closed or converted.");
        var closure = new OrderClosure { Id = Guid.NewGuid(), OrderId = order.Id, Reason = Rules.Required(input.Reason, "Closure reason") }; db.OrderClosures.Add(closure); return closure.Id;
    });
    private async Task<Guid> Execute<T>(Guid operationId, string action, T input, Func<BusinessDb, Actor, Task<Guid>> work)
    {
        var actor = await Authorize(); if (operationId == Guid.Empty) throw new BusinessException("Operation ID is required.");
        var payload = JsonSerializer.Serialize(input); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + action + payload)));
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.Operations.SingleOrDefaultAsync(x => x.Id == operationId);
        if (previous is not null) { if (hash != previous.Fingerprint) throw new BusinessException("Operation ID already used with different data or identity."); return previous.ResultId; }
        var id = await work(db, actor); db.Operations.Add(new() { Id = operationId, ResultId = id, Branch = actor.Branch, Actor = actor.Id, Action = action, Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = hash });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return id;
    }
}

