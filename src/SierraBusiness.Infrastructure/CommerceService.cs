using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed class CommerceService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : ICommerceService
{
    private async Task<Actor> Actor(string permission) { var a = await session.CurrentActor(); a.Demand(permission); if (a.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(a.Id)) throw new UnauthorizedAccessException(); return a; }
    public async Task<IReadOnlyList<PricePolicyView>> Prices()
    {
        await Actor("pricing.manage"); await using var db = await factory.CreateDbContextAsync(); var policies = await db.PricePolicies.AsNoTracking().ToListAsync();
        return (await db.Products.AsNoTracking().OrderBy(x => x.Sku).ToListAsync()).Select(x => new PricePolicyView(x.Id, x.Sku, x.Name, policies.Where(p => p.ProductId == x.Id).OrderByDescending(p => p.Version).FirstOrDefault()?.MinimumPrice)).ToArray();
    }
    public Task<Guid> SetPrice(PricePolicyCommand input) => Execute(input.OperationId, "pricing.manage", "price.policy", input, async (db, actor) => {
        Rules.Positive(input.MinimumPrice, "Minimum price"); Rules.Required(input.Reason, "Reason");
        if (!await db.Products.AnyAsync(x => x.Id == input.ProductId)) throw new BusinessException("Product not found.");
        var version = (await db.PricePolicies.Where(x => x.ProductId == input.ProductId).Select(x => (long?)x.Version).MaxAsync() ?? 0) + 1;
        var policy = new PricePolicy { Id = Guid.NewGuid(), ProductId = input.ProductId, MinimumPrice = input.MinimumPrice, Version = version }; db.PricePolicies.Add(policy); return policy.Id;
    });
    public async Task<IReadOnlyList<ReservationView>> Reservations()
    {
        var actor = await Actor("business.read"); await using var db = await factory.CreateDbContextAsync();
        var rows = await (from r in db.StockReservations join o in db.TradeOrders on r.OrderId equals o.Id join p in db.Products on r.ProductId equals p.Id where o.Branch == actor.Branch && !db.OrderClosures.Any(c => c.OrderId == o.Id) select new { r, o.Number, o.Party, p.Name }).ToListAsync();
        var ids = rows.Select(x => x.r.Id).ToArray(); var releases = await db.ReservationReleases.Where(x => ids.Contains(x.ReservationId)).ToListAsync();
        return rows.Select(x => new ReservationView(x.r.Id, x.r.OrderId, x.Number, x.Party, x.r.ProductId, x.Name, x.r.Location, x.r.Serial, x.r.Batch, x.r.Expiry, x.r.Quantity - releases.Where(r => r.ReservationId == x.r.Id).Sum(r => r.Quantity))).Where(x => x.Remaining > 0).ToArray();
    }
    public Task<Guid> Reserve(ReservationCommand input) => Execute(input.OperationId, "sales.create", "stock.reserve", input, async (db, actor) => {
        var order = await db.TradeOrders.SingleOrDefaultAsync(x => x.Id == input.OrderId && x.Branch == actor.Branch && x.Type == OrderType.SalesOrder) ?? throw new BusinessException("Sales order not found.");
        if (await db.OrderClosures.AnyAsync(x => x.OrderId == order.Id)) throw new BusinessException("Order is closed.");
        Rules.Positive(input.Quantity, "Reserved quantity"); var p = await db.Products.FindAsync(input.ProductId) ?? throw new BusinessException("Product not found.");
        await InventoryManagement.Active(db, actor.Branch, p.Id);
        var serial = Normalize(input.Serial); var batch = Normalize(input.Batch); Rules.Tracking(p.Tracking, input.Quantity, serial, batch, input.Expiry, DateOnly.FromDateTime(DateTime.UtcNow), true);
        if (p.Tracking != TrackingMode.Serial && serial != null || p.Tracking is not TrackingMode.Batch and not TrackingMode.BatchExpiry && (batch != null || input.Expiry != null)) throw new BusinessException("Tracking identity does not match this product.");
        await InventoryManagement.SerialAvailable(db, actor.Branch, serial);
        var line = await db.TradeOrderLines.SingleOrDefaultAsync(x => x.OrderId == order.Id && x.ProductId == p.Id) ?? throw new BusinessException("Product is not on this order.");
        var fulfilled = (await db.OrderFulfillments.Where(x => x.OrderId == order.Id && x.ProductId == p.Id && !db.Documents.Any(d => d.Reverses == x.DocumentId)).ToListAsync()).Sum(x => x.Quantity);
        var reservations = await db.StockReservations.Where(x => x.OrderId == order.Id && x.ProductId == p.Id).ToListAsync(); var ids = reservations.Select(x => x.Id).ToArray();
        var reserved = reservations.Sum(x => x.Quantity) - (await db.ReservationReleases.Where(x => ids.Contains(x.ReservationId)).ToListAsync()).Sum(x => x.Quantity);
        if (input.Quantity > line.Quantity - fulfilled - reserved) throw new BusinessException("Reservation exceeds the unreserved order remainder.");
        var stock = (await db.Movements.Where(x => x.Branch == actor.Branch && x.ProductId == p.Id && x.Location == input.Location && x.Serial == serial && x.Batch == batch && x.Expiry == input.Expiry).ToListAsync()).Sum(x => x.Quantity);
        if (input.Quantity > stock - await CommerceRules.Reserved(db, actor.Branch, p.Id, input.Location, serial, batch, input.Expiry)) throw new BusinessException("Insufficient unreserved stock.");
        var r = new StockReservation { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = p.Id, Location = input.Location, Serial = serial, Batch = batch, Expiry = input.Expiry, Quantity = input.Quantity }; db.StockReservations.Add(r); return r.Id;
    });
    public Task<Guid> Release(ReleaseReservationCommand input) => Execute(input.OperationId, "sales.create", "stock.reservation.release", input, async (db, actor) => {
        var r = await db.StockReservations.SingleOrDefaultAsync(x => x.Id == input.ReservationId && db.TradeOrders.Any(o => o.Id == x.OrderId && o.Branch == actor.Branch)) ?? throw new BusinessException("Reservation not found.");
        var remaining = r.Quantity - (await db.ReservationReleases.Where(x => x.ReservationId == r.Id).ToListAsync()).Sum(x => x.Quantity);
        if (remaining <= 0) throw new BusinessException("Reservation already released or consumed.");
        var release = new ReservationRelease { Id = Guid.NewGuid(), ReservationId = r.Id, Quantity = remaining, Reason = Rules.Required(input.Reason, "Reason") }; db.ReservationReleases.Add(release); return release.Id;
    });
    public async Task<IReadOnlyList<CreditView>> Credits()
    {
        var actor = await Actor("accounting.read"); await using var db = await factory.CreateDbContextAsync();
        var credits = await (from c in db.CustomerCredits join d in db.Documents on c.ReturnId equals d.Id where d.Branch == actor.Branch && !db.Documents.Any(r => r.Reverses == d.Id) select new { c, d.Number, d.Party }).ToListAsync();
        var ids = credits.Select(x => x.c.Id).ToArray(); var uses = await db.CustomerCreditUses.Where(x => ids.Contains(x.CreditId) && !db.Documents.Any(d => d.Reverses == x.DocumentId)).ToListAsync();
        var chequeStates = await ChequeAdvances.States(db,actor.Branch);
        return credits.Select(x => new CreditView(x.c.Id,x.Number,x.Party,x.c.Amount,chequeStates.TryGetValue(x.c.ReturnId,out var state) && state != ChequeState.Cleared ? 0 : x.c.Amount-uses.Where(u => u.CreditId == x.c.Id).Sum(u => u.Amount))).ToArray();
    }
    public Task<Guid> UseCredit(UseCreditCommand input) => Execute(input.OperationId, "accounting.write", "customer.credit.use", input, async (db, actor) => {
        var date = DateOnly.FromDateTime(DateTime.UtcNow); if (date <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough)) throw new BusinessException("Current period is closed.");
        Rules.Positive(input.Amount, "Amount"); if (AccountingEngine.Money(input.Amount) != input.Amount) throw new BusinessException("Use at most two decimal places.");
        if ((input.InvoiceId is null) == (input.RefundAccount is null)) throw new BusinessException("Select a refund account or an invoice, not both.");
        var credit = await db.CustomerCredits.SingleOrDefaultAsync(x => x.Id == input.CreditId && db.Documents.Any(d => d.Id == x.ReturnId && d.Branch == actor.Branch) && !db.Documents.Any(d => d.Reverses == x.ReturnId)) ?? throw new BusinessException("Customer credit not found.");
        var original = await db.Documents.SingleAsync(x => x.Id == credit.ReturnId);
        await Fx.RejectLocalAdvance(db,original.Id);
        await ChequeAdvances.DemandAvailable(db,actor.Branch,original.Id);
        var used = (await db.CustomerCreditUses.Where(x => x.CreditId == credit.Id && !db.Documents.Any(d => d.Reverses == x.DocumentId)).ToListAsync()).Sum(x => x.Amount);
        if (input.Amount > credit.Amount - used) throw new BusinessException("Amount exceeds the remaining customer credit.");
        var doc = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-CRU-{input.OperationId:N}", Kind = DocumentKind.CustomerCreditUse, BusinessDate = date, Party = original.Party, Total = input.Amount, Settlement = input.InvoiceId is null ? "Customer refund" : "Customer credit allocation", Detail = JsonSerializer.Serialize(input) };
        var destination = input.RefundAccount ?? 1100;
        if (input.RefundAccount is not null && !await db.LedgerAccounts.AnyAsync(x => x.Id == destination && x.CashOrBank)) throw new BusinessException("Refund destination must be cash or bank.");
        if (input.InvoiceId is not null)
        {
            var invoice = await db.Documents.SingleOrDefaultAsync(x => x.Id == input.InvoiceId && x.Branch == actor.Branch && ((x.Kind == DocumentKind.Sale || x.Kind == DocumentKind.ServiceInvoice) && x.Settlement == "Credit" || x.Kind == DocumentKind.ChequeBounce || x.Kind == DocumentKind.InterestCharge || x.Kind == DocumentKind.OpeningBalance && x.Settlement == "Credit") && x.Party.ToUpper() == original.Party.ToUpper() && !db.Documents.Any(d => d.Reverses == x.Id)) ?? throw new BusinessException("Select an outstanding credit invoice for this customer.");
            var settled = (await db.BillAllocations.Where(x => x.InvoiceId == invoice.Id && !db.Documents.Any(d => d.Reverses == x.VoucherId)).ToListAsync()).Sum(x => x.Amount);
            var returns = (await db.ReturnAllocations.Where(x => x.OriginalDocumentId == invoice.Id && !db.Documents.Any(d => d.Reverses == x.ReturnDocumentId)).ToListAsync()).Sum(x => x.BillCredit);
            if (input.Amount > invoice.Total - settled - returns) throw new BusinessException("Credit exceeds the invoice's outstanding balance.");
            db.BillAllocations.Add(new() { Id = Guid.NewGuid(), VoucherId = doc.Id, InvoiceId = invoice.Id, Amount = input.Amount });
        }
        Rules.Required(input.Reason, "Reason"); db.Documents.Add(doc); db.CustomerCreditUses.Add(new() { Id = Guid.NewGuid(), CreditId = credit.Id, DocumentId = doc.Id, Amount = input.Amount });
        db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, Account = (Account)2400, Debit = input.Amount });
        db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, Account = (Account)destination, Credit = input.Amount }); return doc.Id;
    });
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    private async Task<Guid> Execute<T>(Guid operation, string permission, string action, T input, Func<BusinessDb, Actor, Task<Guid>> work)
    {
        var actor = await Actor(permission); if (operation == Guid.Empty) throw new BusinessException("Operation ID is required.");
        var payload = JsonSerializer.Serialize(input); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + action + payload)));
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var prior = await db.Operations.SingleOrDefaultAsync(x => x.Id == operation); if (prior != null) { if (prior.Fingerprint != hash) throw new BusinessException("Operation ID already used with different data or identity."); return prior.ResultId; }
        var id = await work(db, actor); db.Operations.Add(new() { Id = operation, ResultId = id, Branch = actor.Branch, Actor = actor.Id, Action = action, Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = hash }); await db.SaveChangesAsync(); await tx.CommitAsync(); return id;
    }
}

