using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed class BusinessService(IDbContextFactory<BusinessDb> factory, IOptions<TradingOptions> options) : IBusinessService
{
    private readonly TradingOptions settings = options.Value;
    private void Scope(Actor actor, string permission)
    {
        actor.Demand(permission);
        if (actor.Branch != settings.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("This account is not assigned to this branch.");
    }
    public async Task<BusinessSnapshot> Read(Actor actor, CancellationToken cancellationToken = default)
    {
        Scope(actor, "business.read");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var products = await db.Products.AsNoTracking().OrderBy(x => x.Sku).ToListAsync(cancellationToken);
        var movements = await db.Movements.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync(cancellationToken);
        var documents = await db.Documents.AsNoTracking().Where(x => x.Branch == actor.Branch).OrderByDescending(x => x.BusinessDate).ToListAsync(cancellationToken);
        var journals = await db.Journal.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync(cancellationToken);
        var operations = await db.Operations.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync(cancellationToken);
        return new(products.Select(x => new ProductView(x.Id, x.Sku, x.Name, x.Category, x.Unit, x.Price, x.Tracking)).ToList(),
            movements.GroupBy(x => new { x.ProductId, x.Serial, x.Batch, x.Expiry, x.Location }).Select(g => { var p = products.Single(x => x.Id == g.Key.ProductId); return new StockView(p.Id, p.Sku, p.Name, p.Unit, g.Key.Serial, g.Key.Batch, g.Key.Expiry, g.Sum(x => x.Quantity), g.Sum(x => x.Value), g.Key.Location); }).ToList(),
            documents.Select(x => new DocumentView(x.Id, x.Number, x.Kind, x.Party, x.BusinessDate, x.Total, x.Settlement, x.Reverses, x.Detail)).ToList(),
            actor.Permissions.Contains("accounting.read") ? journals.GroupBy(x => x.Account).OrderBy(x => x.Key).Select(g => new LedgerView(g.Key, g.Sum(x => x.Debit), g.Sum(x => x.Credit))).ToList() : [],
            actor.Permissions.Contains("audit.read") ? operations.OrderByDescending(x => x.Time).Take(100).Select(x => new AuditView(x.Time, x.Actor, x.Action, x.ResultId.ToString())).ToList() : [],
            operations.Count(x => !x.Acknowledged), journals.Where(x => x.Account == Account.Sales).Sum(x => x.Credit - x.Debit));
    }
    public Task<Guid> AddProduct(Actor actor, ProductInput input, CancellationToken cancellationToken = default)
    {
        Scope(actor, "catalogue.write");
        return Execute(actor, input.OperationId, "catalogue.create", input, async db => (await CreateProduct(db, actor, input, cancellationToken)).Id, cancellationToken);
    }
    /// <summary>Single source of catalogue rules, shared by manual entry and Excel import.</summary>
    /// <param name="uniquenessVerified">True only when the caller has already checked SKUs against the database inside the same serializable transaction (bulk import).</param>
    internal static async Task<Product> CreateProduct(BusinessDb db, Actor actor, ProductInput input, CancellationToken cancellationToken = default, bool uniquenessVerified = false)
    {
        Rules.Positive(input.Price, "Selling price");
        if (!Enum.IsDefined(input.Tracking)) throw new BusinessException("Unknown tracking mode.");
        if (input.Tracking is TrackingMode.Variant or TrackingMode.Compatibility) throw new BusinessException("Structured variant and compatibility tracking are not available. Use a separate quantity-tracked SKU for each stocked variant.");
        var sku = Rules.Required(input.Sku, "SKU", 40).ToUpperInvariant();
        if (!uniquenessVerified && (await db.Products.AnyAsync(x => x.Sku == sku, cancellationToken) || await db.ProductBarcodes.AnyAsync(x => x.Code == sku, cancellationToken) || db.Products.Local.Any(x => x.Sku == sku) || db.ProductBarcodes.Local.Any(x => x.Code == sku))) throw new BusinessException("That SKU already exists.");
        var p = new Product { Id = Guid.NewGuid(), Sku = sku, Name = Rules.Required(input.Name, "Product name"), Category = Rules.Required(input.Category, "Category"), Unit = Rules.Required(input.Unit, "Base unit", 30), Price = input.Price, Tracking = input.Tracking };
        db.Products.Add(p);
        if (input.MinimumPrice is not null) { actor.Demand("pricing.manage"); Rules.Positive(input.MinimumPrice.Value, "Minimum price"); db.PricePolicies.Add(new() { Id = Guid.NewGuid(), ProductId = p.Id, MinimumPrice = input.MinimumPrice.Value, Version = 1 }); }
        return p;
    }
    public Task<Guid> Post(Actor actor, TradeCommand input, CancellationToken cancellationToken = default)
    {
        if (input.Kind is not DocumentKind.Sale and not DocumentKind.Receipt and not DocumentKind.Delivery) throw new BusinessException("Use the reversal workflow to correct a posted document.");
        Scope(actor, input.Kind != DocumentKind.Receipt ? "sales.create" : "inventory.receive");
        return Execute(actor, input.OperationId, $"{input.Kind}.post", input, async db =>
        {
            if (input.HeldSaleId is not null) { if (input.Kind != DocumentKind.Sale || input.OrderId is not null || input.DeliveryId is not null) throw new BusinessException("Held drafts apply only to counter sales."); await PosService.Active(db, actor, input.HeldSaleId.Value); }
            if (input.BusinessDate <= await PeriodControls.ClosedThrough(db, actor.Branch, settings.ClosedThrough) || input.BusinessDate > DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("Business date must be in an open period and cannot be in the future.");
            if (input.Lines is null || input.Lines.Count is < 1 or > 200) throw new BusinessException("Enter between 1 and 200 lines.");
            var party = Rules.Required(input.Party, "Customer or supplier");
            var normalizedParty = party.ToUpperInvariant();
            var partyMaster = await db.Parties.SingleOrDefaultAsync(x => x.Branch == actor.Branch && x.NormalizedName == normalizedParty, cancellationToken);
            if (partyMaster is not null)
            {
                if (input.Kind != DocumentKind.Receipt && partyMaster.Type == PartyType.Supplier || input.Kind == DocumentKind.Receipt && partyMaster.Type == PartyType.Customer) throw new BusinessException("The selected party is not configured for this trading direction.");
                party = partyMaster.Name;
            }
            if (input.Payment is not null && !Enum.IsDefined(input.Payment.Value)) throw new BusinessException("Unsupported payment method.");
            if (input.Kind == DocumentKind.Receipt && input.Payment is not null) throw new BusinessException("Goods receipts create supplier payables; settlement is a separate workflow.");
            if (input.Kind == DocumentKind.Sale && input.Payment is null && party.Equals("Walk-in", StringComparison.OrdinalIgnoreCase)) throw new BusinessException("Credit sales require a named customer.");
            if (input.Payment is PaymentMethod.Cheque or PaymentMethod.BankTransfer)
            {
                Rules.Required(input.PaymentReference, "Payment reference"); Rules.Required(input.Bank, "Bank");
                if (input.PaymentDate is null) throw new BusinessException("Payment date is required.");
            }
            if (!await db.StockLocations.AnyAsync(x => x.Code == input.Location, cancellationToken)) throw new BusinessException("Warehouse not found.");
            var sale = input.Kind != DocumentKind.Receipt; var delivery = input.Kind == DocumentKind.Delivery;
            if (delivery && (input.OrderId is null || input.Payment is not null || input.DeliveryId is not null)) throw new BusinessException("Delivery notes require a sales order, with no payment or prior delivery.");
            if (input.DueDate < input.BusinessDate) throw new BusinessException("Due date cannot precede the invoice date.");
            var doc = new BusinessDocument { Id = Guid.NewGuid(), Number = $"{settings.Branch}-{settings.Device}-{input.Kind.ToString()[0]}-{input.OperationId:N}", Branch = actor.Branch, Kind = input.Kind, Party = party, BusinessDate = input.BusinessDate, Settlement = sale ? input.Payment?.ToString() ?? "Credit" : "Supplier payable" };
            var movements = await db.Movements.Where(x => x.Branch == actor.Branch).ToListAsync(cancellationToken);
            doc.DueDate = input.DueDate ?? input.BusinessDate.AddDays(partyMaster?.PaymentDays ?? 0);
            BusinessDocument? delivered = null;
            if (input.DeliveryId is not null)
            {
                delivered = await db.Documents.SingleOrDefaultAsync(x => x.Id == input.DeliveryId && x.Branch == actor.Branch && x.Kind == DocumentKind.Delivery, cancellationToken) ?? throw new BusinessException("Delivery note not found.");
                if (!sale || delivery || delivered.Party != party || delivered.BusinessDate > input.BusinessDate || await db.Documents.AnyAsync(x => x.Reverses == delivered.Id, cancellationToken) || await db.DeliveryInvoices.AnyAsync(x => x.DeliveryId == delivered.Id && !db.Documents.Any(r => r.Reverses == x.InvoiceId), cancellationToken)) throw new BusinessException("Delivery cannot be invoiced again or does not match this customer/date.");
                using var deliveryJson = JsonDocument.Parse(delivered.Detail); var agreed = deliveryJson.RootElement.GetProperty("Request").Deserialize<TradeCommand>()!;
                if (agreed.Location != input.Location || agreed.OrderId != input.OrderId || JsonSerializer.Serialize(agreed.Lines) != JsonSerializer.Serialize(input.Lines)) throw new BusinessException("Invoice must retain the complete delivery quantities, identities and agreed prices.");
                db.DeliveryInvoices.Add(new() { Id = Guid.NewGuid(), DeliveryId = delivered.Id, InvoiceId = doc.Id });
            }
            if (input.OrderId is not null && delivered is null)
            {
                var order = await db.TradeOrders.SingleOrDefaultAsync(x => x.Id == input.OrderId && x.Branch == actor.Branch, cancellationToken) ?? throw new BusinessException("Order not found in this branch.");
                if (order.Type != (sale ? OrderType.SalesOrder : OrderType.PurchaseOrder) || order.Party != party || input.BusinessDate < order.Date || await db.OrderClosures.AnyAsync(x => x.OrderId == order.Id, cancellationToken)) throw new BusinessException("Order type, party or date does not match, or the order is closed.");
                var ordered = await db.TradeOrderLines.Where(x => x.OrderId == order.Id).ToListAsync(cancellationToken);
                var fulfilled = await db.OrderFulfillments.Where(x => x.OrderId == order.Id && !db.Documents.Any(d => d.Reverses == x.DocumentId)).ToListAsync(cancellationToken);
                foreach (var group in input.Lines.GroupBy(x => x.ProductId))
                {
                    var orderLine = ordered.SingleOrDefault(x => x.ProductId == group.Key) ?? throw new BusinessException("Product is not on the order.");
                    var requested = group.Sum(x => x.Quantity * x.Conversion);
                    if (requested > orderLine.Quantity - fulfilled.Where(x => x.ProductId == group.Key).Sum(x => x.Quantity)) throw new BusinessException("This posting exceeds the unfulfilled order quantity.");
                    if (group.Any(x => x.UnitPrice != orderLine.UnitPrice * x.Conversion)) throw new BusinessException("The posting price must match the order's price per base unit.");
                    db.OrderFulfillments.Add(new() { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = group.Key, DocumentId = doc.Id, Quantity = requested });
                }
            }
            decimal net = 0, cost = 0;
            foreach (var line in input.Lines)
            {
                if (line is null) throw new BusinessException("Document lines cannot be empty.");
                Rules.Positive(line.Quantity, "Quantity"); Rules.Positive(line.UnitPrice, "Unit price"); Rules.Positive(line.Conversion, "Unit conversion");
                var p = await db.Products.FindAsync([line.ProductId], cancellationToken) ?? throw new BusinessException("Product does not exist.");
                await InventoryManagement.Active(db, actor.Branch, p.Id);
                if (sale) await CommerceRules.Price(db, p.Id, line.UnitPrice, line.Conversion);
                var quantity = line.Quantity * line.Conversion;
                Rules.Positive(quantity, "Base quantity");
                var serial = string.IsNullOrWhiteSpace(line.Serial) ? null : Rules.Required(line.Serial, "Serial", 100).ToUpperInvariant();
                var batch = string.IsNullOrWhiteSpace(line.Batch) ? null : Rules.Required(line.Batch, "Batch", 100).ToUpperInvariant();
                Rules.Tracking(p.Tracking, quantity, serial, batch, line.Expiry, input.BusinessDate, sale && delivered is null);
                if (p.Tracking != TrackingMode.Serial && serial is not null) throw new BusinessException("Serial is only allowed for serial-controlled products.");
                if (p.Tracking is not TrackingMode.Batch and not TrackingMode.BatchExpiry && (batch is not null || line.Expiry is not null)) throw new BusinessException("Batch and expiry are only allowed for batch-controlled products.");
                if (serial is not null && !sale && (movements.Any(x => x.Serial == serial) || await db.Movements.AnyAsync(x => x.Serial == serial, cancellationToken))) throw new BusinessException("Serial already exists. Use a return/reversal for previously sold units.");
                if (delivered is null) await InventoryManagement.SerialAvailable(db, actor.Branch, serial);
                var matching = movements.Where(x => x.ProductId == p.Id && x.Location == input.Location && x.Serial == serial && x.Batch == batch && x.Expiry == line.Expiry).ToList();
                var available = matching.Sum(x => x.Quantity);
                var reserved = sale && delivered is null ? await CommerceRules.Reserved(db, actor.Branch, p.Id, input.Location, serial, batch, line.Expiry, input.OrderId) : 0;
                if (sale && delivered is null && available - reserved < quantity) throw new BusinessException($"Insufficient stock for {p.Name}. Unreserved available: {available - reserved} {p.Unit}.");
                var amount = AccountingEngine.Money(line.Quantity * line.UnitPrice);
                if (amount <= 0) throw new BusinessException("Line amount must be at least NLe 0.01.");
                var deliveredCost = delivered is null ? 0 : -movements.Where(x => x.DocumentId == delivered.Id && x.ProductId == p.Id && x.Serial == serial && x.Batch == batch && x.Expiry == line.Expiry).Sum(x => x.Value);
                var lineCost = delivered is not null ? AccountingEngine.Money(deliveredCost * quantity / input.Lines.Where(x => x.ProductId == p.Id && (string.IsNullOrWhiteSpace(x.Serial) ? null : x.Serial.Trim().ToUpperInvariant()) == serial && (string.IsNullOrWhiteSpace(x.Batch) ? null : x.Batch.Trim().ToUpperInvariant()) == batch && x.Expiry == line.Expiry).Sum(x => x.Quantity * x.Conversion)) : sale ? (available == quantity ? matching.Sum(x => x.Value) : AccountingEngine.Money(matching.Sum(x => x.Value) / available * quantity)) : amount;
                var movement = new Movement { Id = Guid.NewGuid(), DocumentId = doc.Id, ProductId = p.Id, Branch = actor.Branch, Location = input.Location, Serial = serial, Batch = batch, Expiry = line.Expiry, Quantity = sale ? -quantity : quantity, Value = sale ? -lineCost : lineCost };
                if (delivered is null) { db.Movements.Add(movement); movements.Add(movement); if (sale) await CommerceRules.Consume(db, input.OrderId, p.Id, input.Location, serial, batch, line.Expiry, quantity); }
                net += amount; cost += lineCost;
            }
            var configuredRate = await db.SalesTaxRates.Where(x => x.Branch == actor.Branch && x.From <= input.BusinessDate).OrderByDescending(x => x.From).Select(x => (decimal?)x.Rate).FirstOrDefaultAsync(cancellationToken) ?? settings.TaxRate;
            if (configuredRate is < 0 or > 1) throw new BusinessException("Tax configuration is invalid.");
            if (delivered is not null) cost = -movements.Where(x => x.DocumentId == delivered.Id).Sum(x => x.Value);
            var tax = sale && !delivery ? AccountingEngine.Money(net * configuredRate) : 0;
            doc.Total = delivery ? 0 : net + tax; if (delivery) doc.Settlement = "Delivered; awaiting invoice";
            if (sale && !delivery && input.Payment is null && partyMaster is not null)
            {
                if (partyMaster.CreditHold) throw new BusinessException("This customer's account is on credit hold.");
                var bills = await db.Documents.Where(x => x.Branch == actor.Branch && x.Party.ToUpper() == normalizedParty && (x.Kind == DocumentKind.Sale && x.Settlement == "Credit" || x.Kind == DocumentKind.ChequeBounce) && !db.Documents.Any(r => r.Reverses == x.Id)).ToListAsync(cancellationToken);
                var ids = bills.Select(x => x.Id).ToList();
                var allocations = await db.BillAllocations.Where(x => ids.Contains(x.InvoiceId) && !db.Documents.Any(r => r.Reverses == x.VoucherId)).ToListAsync(cancellationToken);
                var outstanding = bills.Sum(x => x.Total) - allocations.Sum(x => x.Amount);
                outstanding -= (await db.ReturnAllocations.Where(x => ids.Contains(x.OriginalDocumentId) && !db.Documents.Any(r => r.Reverses == x.ReturnDocumentId)).ToListAsync(cancellationToken)).Sum(x => x.BillCredit);
                if (outstanding + doc.Total > partyMaster.CreditLimit) throw new BusinessException("This sale exceeds the customer's credit limit. Record payment or update the approved limit before posting.");
            }
            doc.Detail = JsonSerializer.Serialize(new { Request = input, TaxRate = sale ? configuredRate : 0, Net = net, Tax = tax, Cost = cost });
            db.Documents.Add(doc);
            var posting = sale ? AccountingEngine.Sale(net, tax, cost, input.Payment) : AccountingEngine.Receipt(cost);
            if (delivery) posting = AccountingEngine.Balanced([new((Account)1250, cost, 0), new(Account.Inventory, 0, cost)]);
            else if (delivered is not null) posting = posting.Select(x => x.Account == Account.Inventory ? x with { Account = (Account)1250 } : x).ToArray();
            AddJournal(db, doc, posting);
            if (input.HeldSaleId is not null) db.HeldSaleEnds.Add(new() { Id = Guid.NewGuid(), HeldSaleId = input.HeldSaleId.Value, DocumentId = doc.Id, Reason = "Posted as sale" });
            if (sale && !delivery && input.Payment == PaymentMethod.Cheque) await BankingRules.NewCheque(db, doc, input.PaymentReference!, input.Bank!, input.PaymentDate!.Value);
            return doc.Id;
        }, cancellationToken);
    }
    public Task<Guid> Reverse(Actor actor, ReverseCommand input, CancellationToken cancellationToken = default)
    {
        Scope(actor, "documents.reverse");
        return Execute(actor, input.OperationId, "document.reverse", input, async db =>
        {
            Rules.Required(input.Reason, "Reversal reason");
            var original = await db.Documents.SingleOrDefaultAsync(x => x.Id == input.DocumentId && x.Branch == actor.Branch, cancellationToken) ?? throw new BusinessException("Document not found.");
            if (original.Kind == DocumentKind.Reversal || await db.Documents.AnyAsync(x => x.Reverses == original.Id, cancellationToken)) throw new BusinessException("This document cannot be reversed again.");
            if (await db.ReturnAllocations.AnyAsync(x => x.OriginalDocumentId == original.Id && !db.Documents.Any(r => r.Reverses == x.ReturnDocumentId), cancellationToken)) throw new BusinessException("Reverse the linked return documents before reversing the original document.");
            if (await db.BillAllocations.AnyAsync(x => x.InvoiceId == original.Id && !db.Documents.Any(d => d.Reverses == x.VoucherId), cancellationToken)) throw new BusinessException("Reverse the allocated settlement vouchers before reversing this bill.");
            if (await db.DeliveryInvoices.AnyAsync(x => x.DeliveryId == original.Id && !db.Documents.Any(r => r.Reverses == x.InvoiceId), cancellationToken)) throw new BusinessException("Reverse the linked invoice before reversing this delivery.");
            if (await db.CustomerCreditUses.AnyAsync(x => db.CustomerCredits.Any(c => c.Id == x.CreditId && c.ReturnId == original.Id) && !db.Documents.Any(r => r.Reverses == x.DocumentId), cancellationToken)) throw new BusinessException("Reverse the credit refunds/allocations before reversing this credit note.");
            if (await db.ChequeEvents.AnyAsync(x => x.DocumentId == original.Id, cancellationToken)) throw new BusinessException("Correct cheque actions from Banking so the cheque state and journal stay together.");
            var sourceCheque = await db.ReceivedCheques.SingleOrDefaultAsync(x => x.SourceDocumentId == original.Id, cancellationToken);
            if (sourceCheque != null && (await db.ChequeEvents.Where(x => x.ChequeId == sourceCheque.Id).OrderByDescending(x => x.Version).Select(x => (ChequeState?)x.State).FirstOrDefaultAsync(cancellationToken) ?? ChequeState.Received) != ChequeState.Received) throw new BusinessException("Undo the cheque's later actions before reversing its original receipt or sale.");
            if (await db.SupplierCreditUses.AnyAsync(x => db.SupplierCredits.Any(c => c.Id == x.CreditId && c.ReturnId == original.Id) && !db.Documents.Any(r => r.Reverses == x.DocumentId), cancellationToken)) throw new BusinessException("Reverse the supplier credit refunds/allocations before reversing this debit note.");
            var date = DateOnly.FromDateTime(DateTime.UtcNow);
            if (date <= await PeriodControls.ClosedThrough(db, actor.Branch, settings.ClosedThrough)) throw new BusinessException("Current period is closed.");
            var doc = new BusinessDocument { Id = Guid.NewGuid(), Number = $"{settings.Branch}-{settings.Device}-REV-{input.OperationId:N}", Branch = actor.Branch, Kind = DocumentKind.Reversal, Party = original.Party, BusinessDate = date, Total = -original.Total, Settlement = original.Settlement, Reverses = original.Id, Detail = JsonSerializer.Serialize(input) };
            var originals = await db.Movements.Where(x => x.DocumentId == original.Id).ToListAsync(cancellationToken);
            var all = await db.Movements.Where(x => x.Branch == actor.Branch).ToListAsync(cancellationToken);
            var postingTimes = await db.Operations.Where(x => x.Branch == actor.Branch).Select(x => new { x.ResultId, x.Time }).ToListAsync(cancellationToken);
            var originalTime = postingTimes.Where(x => x.ResultId == original.Id).Select(x => x.Time).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
            var subsequent = postingTimes.Where(x => x.Time > originalTime).Select(x => x.ResultId).ToHashSet();
            foreach (var m in originals)
            {
                await InventoryManagement.SerialAvailable(db, actor.Branch, m.Serial);
                if (m.Quantity < 0 && m.Serial is not null && originals.Where(x => x.Serial == m.Serial).Sum(x => x.Quantity) < 0)
                {
                    var serialHistory = await db.Movements.Where(x => x.Serial == m.Serial).ToListAsync(cancellationToken);
                    if (serialHistory.Sum(x => x.Quantity) != 0) throw new BusinessException("This serial is already in stock. Resolve its later activity before reversing the stock removal.");
                }
                var matching = all.Where(x => x.ProductId == m.ProductId && x.Location == m.Location && x.Serial == m.Serial && x.Batch == m.Batch && x.Expiry == m.Expiry).ToList();
                if (m.Quantity > 0 && matching.Sum(x => x.Quantity) - m.Quantity < await CommerceRules.Reserved(db, actor.Branch, m.ProductId, m.Location, m.Serial, m.Batch, m.Expiry)) throw new BusinessException("Release the affected reservations before reversing this stock.");
                if (m.Quantity > 0 && (matching.Any(x => subsequent.Contains(x.DocumentId)) || matching.Sum(x => x.Quantity) < m.Quantity || matching.Sum(x => x.Value) < m.Value)) throw new BusinessException("Stock has subsequent activity or insufficient quantity/value. Use the valued return workflow instead.");
                if (original.Kind is DocumentKind.Sale or DocumentKind.Delivery && m.Serial is not null && matching.Sum(x => x.Quantity) != 0) throw new BusinessException("This serial is already in stock.");
                var reversal = new Movement { Id = Guid.NewGuid(), DocumentId = doc.Id, ProductId = m.ProductId, Branch = m.Branch, Location = m.Location, Serial = m.Serial, Batch = m.Batch, Expiry = m.Expiry, Quantity = -m.Quantity, Value = -m.Value };
                db.Movements.Add(reversal);
            }
            db.Documents.Add(doc);
            var journal = await db.Journal.Where(x => x.DocumentId == original.Id).ToListAsync(cancellationToken);
            foreach (var line in journal) db.Journal.Add(new JournalLine { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = doc.Branch, Account = line.Account, Debit = line.Credit, Credit = line.Debit, CostCentre = line.CostCentre });
            return doc.Id;
        }, cancellationToken);
    }
    private static void AddJournal(BusinessDb db, BusinessDocument doc, IReadOnlyList<Posting> posting)
    {
        foreach (var p in posting) db.Journal.Add(new JournalLine { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = doc.Branch, Account = p.Account, Debit = p.Debit, Credit = p.Credit });
    }
    private async Task<Guid> Execute<T>(Actor actor, Guid operationId, string action, T input, Func<BusinessDb, Task<Guid>> work, CancellationToken ct)
    {
        if (operationId == Guid.Empty) throw new BusinessException("An operation ID is required.");
        var payload = JsonSerializer.Serialize(input);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + action + payload)));
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existing = await db.Operations.SingleOrDefaultAsync(x => x.Id == operationId, ct);
        if (existing is not null)
        {
            if (existing.Fingerprint != fingerprint)
            {
                // Optional contract fields added by upgrades must not invalidate old, identical retries.
                var sameIdentity = existing.Branch == actor.Branch && existing.Actor == actor.Id && existing.Action == action;
                var oldInput = JsonSerializer.Deserialize<T>(existing.Payload);
                if (!sameIdentity || JsonSerializer.Serialize(oldInput) != payload) throw new BusinessException("Operation ID was already used with different data or identity.");
            }
            return existing.ResultId;
        }
        var result = await work(db);
        db.Operations.Add(new Operation { Id = operationId, ResultId = result, Branch = actor.Branch, Actor = actor.Id, Action = action, Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = fingerprint });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}






