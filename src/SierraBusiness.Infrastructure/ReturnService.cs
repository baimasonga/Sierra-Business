using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed class ReturnAllocation
{
    public Guid Id { get; set; }
    public Guid OriginalDocumentId { get; set; }
    public Guid ReturnDocumentId { get; set; }
    public Guid ProductId { get; set; }
    public string? Serial { get; set; }
    public string? Batch { get; set; }
    public DateOnly? Expiry { get; set; }
    public decimal Quantity { get; set; }
    public decimal Net { get; set; }
    public decimal Tax { get; set; }
    public decimal Cost { get; set; }
    public decimal BillCredit { get; set; }
}
public sealed class ReturnService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IReturnService
{
    private async Task<Actor> Authorize(bool write = false)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read"); if (write) actor.Demand("documents.reverse");
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch."); return actor;
    }
    public async Task<IReadOnlyList<ReturnableDocument>> Read()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var docs = await db.Documents.AsNoTracking().Where(x => x.Branch == actor.Branch && (x.Kind == DocumentKind.Sale || x.Kind == DocumentKind.Receipt) && !db.Documents.Any(r => r.Reverses == x.Id)).ToListAsync();
        var movements = await db.Movements.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync(); var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var deliveryLinks = await db.DeliveryInvoices.Where(x => db.Documents.Any(d => d.Id == x.InvoiceId && d.Branch == actor.Branch && !db.Documents.Any(r => r.Reverses == d.Id))).ToListAsync();
        movements.AddRange(deliveryLinks.SelectMany(link => movements.Where(m => m.DocumentId == link.DeliveryId).Select(m => new Movement { DocumentId = link.InvoiceId, ProductId = m.ProductId, Location = m.Location, Serial = m.Serial, Batch = m.Batch, Expiry = m.Expiry, Quantity = m.Quantity, Value = m.Value })).ToList());
        var returns = await db.ReturnAllocations.AsNoTracking().Where(x => db.Documents.Any(d => d.Id == x.OriginalDocumentId && d.Branch == actor.Branch) && !db.Documents.Any(d => d.Reverses == x.ReturnDocumentId)).ToListAsync();
        return docs.OrderByDescending(x => x.BusinessDate).Select(d => new ReturnableDocument(d.Id, d.Number, d.Party, d.Kind == DocumentKind.Sale, movements.First(x => x.DocumentId == d.Id).Location,
            movements.Where(x => x.DocumentId == d.Id).GroupBy(x => new { x.ProductId, x.Serial, x.Batch, x.Expiry }).Select(g => new ReturnableLine(g.Key.ProductId, products[g.Key.ProductId].Name, g.Key.Serial, g.Key.Batch, g.Key.Expiry, Math.Abs(g.Sum(x => x.Quantity)), returns.Where(x => x.OriginalDocumentId == d.Id && x.ProductId == g.Key.ProductId && x.Serial == g.Key.Serial && x.Batch == g.Key.Batch && x.Expiry == g.Key.Expiry).Sum(x => x.Quantity))).ToArray())).ToArray();
    }
    public async Task<Guid> Post(ReturnCommand input)
    {
        var actor = await Authorize(true); var date = DateOnly.FromDateTime(DateTime.UtcNow);
        if (input.OperationId == Guid.Empty || input.Lines is null || input.Lines.Count is < 1 or > 200) throw new BusinessException("Return needs an operation ID and 1–200 lines.");
        var payload = JsonSerializer.Serialize(input); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + "document.return" + payload)));
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.Operations.SingleOrDefaultAsync(x => x.Id == input.OperationId); if (previous is not null) { if (hash != previous.Fingerprint && (previous.Actor != actor.Id || previous.Branch != actor.Branch || previous.Action != "document.return" || JsonSerializer.Serialize(JsonSerializer.Deserialize<ReturnCommand>(previous.Payload)) != payload)) throw new BusinessException("Operation ID already used with different data or identity."); return previous.ResultId; }
        if (date <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough)) throw new BusinessException("Current accounting period is closed.");
        var original = await db.Documents.SingleOrDefaultAsync(x => x.Id == input.OriginalDocumentId && x.Branch == actor.Branch) ?? throw new BusinessException("Original document not found.");
        if (original.Kind is not DocumentKind.Sale and not DocumentKind.Receipt || await db.Documents.AnyAsync(x => x.Reverses == original.Id)) throw new BusinessException("Only unreversed sales and goods receipts can be returned.");
        var sale = original.Kind == DocumentKind.Sale;
        if (sale && original.Settlement == "Cheque" && input.Disposition == ReturnDisposition.Original) throw new BusinessException("Choose cash/bank refund or customer credit; cheque instruments are managed in Banking.");
        if (!Enum.IsDefined(input.Disposition) || (!sale && input.Disposition == ReturnDisposition.CustomerCredit || sale && input.Disposition == ReturnDisposition.SupplierCredit)) throw new BusinessException("Select a valid return settlement.");
        if (sale && input.Disposition == ReturnDisposition.CustomerCredit && original.Party.Equals("Walk-in", StringComparison.OrdinalIgnoreCase)) throw new BusinessException("Customer credit requires a named customer.");
        using var detail = JsonDocument.Parse(original.Detail); var originalRequest = detail.RootElement.GetProperty("Request").Deserialize<TradeCommand>()!;
        var exchangeRate = detail.RootElement.TryGetProperty("ExchangeRate", out var storedRate) && storedRate.ValueKind == JsonValueKind.Number ? storedRate.GetDecimal() : 1m;
        var taxTotal = detail.RootElement.GetProperty("Tax").GetDecimal(); var originalNet = detail.RootElement.GetProperty("Net").GetDecimal();
        // Only standard-rated lines carried tax; sales from before 0.20.0 have no categories and were wholly standard-rated.
        var categories = sale && detail.RootElement.TryGetProperty("Categories", out var storedCategories) ? storedCategories.Deserialize<Dictionary<Guid, TaxCategory>>()! : null;
        var originalTaxable = categories is null ? originalNet : TaxRules.Value(detail.RootElement, "Taxable");
        bool Taxed(Guid product) => categories is null || categories.GetValueOrDefault(product, TaxCategory.Standard) == TaxCategory.Standard;
        var movements = await db.Movements.Where(x => x.Branch == actor.Branch).ToListAsync();
        var source = movements.Where(x => x.DocumentId == (originalRequest.DeliveryId ?? original.Id)).ToList();
        var returned = await db.ReturnAllocations.Where(x => x.OriginalDocumentId == original.Id && !db.Documents.Any(r => r.Reverses == x.ReturnDocumentId)).ToListAsync();
        var doc = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-RET-{input.OperationId:N}", Kind = sale ? DocumentKind.SalesReturn : DocumentKind.PurchaseReturn, Party = original.Party, BusinessDate = date, Settlement = original.Settlement, Detail = JsonSerializer.Serialize(new { Request = input, Reason = Rules.Required(input.Reason, "Reason") }) };
        decimal net = 0, tax = 0, cost = 0;
        // Receipts awaiting the supplier's invoice: uninvoiced goods go back against goods received not invoiced; invoiced goods need a supplier credit or refund.
        var grniLines = !sale && Grni.IsAwaiting(original) ? await Grni.Lines(db, actor.Branch, [original.Id]) : null;
        bool? beforeInvoice = null;
        foreach (var line in input.Lines)
        {
            Rules.Positive(line.Quantity, "Return quantity");
            var serial = Normalize(line.Serial); var batch = Normalize(line.Batch);
            await InventoryManagement.SerialAvailable(db, actor.Branch, serial);
            var matching = source.Where(x => x.ProductId == line.ProductId && x.Serial == serial && x.Batch == batch && x.Expiry == line.Expiry).ToList();
            if (matching.Count == 0) throw new BusinessException("Return line does not match the original stock identity.");
            var originalQuantity = Math.Abs(matching.Sum(x => x.Quantity));
            var earlier = returned.Where(x => x.ProductId == line.ProductId && x.Serial == serial && x.Batch == batch && x.Expiry == line.Expiry).ToList();
            if (line.Quantity > originalQuantity - earlier.Sum(x => x.Quantity)) throw new BusinessException("Return exceeds the unreturned quantity.");
            var requestLines = originalRequest.Lines.Where(x => x.ProductId == line.ProductId && Normalize(x.Serial) == serial && Normalize(x.Batch) == batch && x.Expiry == line.Expiry).ToList();
            var lineNet = requestLines.Sum(x => AccountingEngine.Money(AccountingEngine.Money(x.Quantity * x.UnitPrice) * exchangeRate)); // foreign receipts are valued in NLe at their own rate
            // Cumulative rounding ensures repeated partial returns equal the original full amount.
            var cumulative = earlier.Sum(x => x.Quantity) + line.Quantity;
            var refundNet = AccountingEngine.Money(lineNet * cumulative / originalQuantity) - earlier.Sum(x => x.Net);
            if (grniLines is not null)
            {
                var g = grniLines.Single(x => x.ProductId == line.ProductId && x.Serial == serial && x.Batch == batch && x.Expiry == line.Expiry);
                var thisDoc = returned.Where(x => x.ReturnDocumentId == doc.Id && x.ProductId == line.ProductId && x.Serial == serial && x.Batch == batch && x.Expiry == line.Expiry).ToList();
                var uninvoiced = g.Outstanding - thisDoc.Sum(x => x.Quantity);
                var lineBefore = uninvoiced >= line.Quantity ? true : uninvoiced <= 0 ? false : throw new BusinessException($"Only {uninvoiced:0.####} of these units are not yet invoiced. Return those first, then the invoiced units separately.");
                if (beforeInvoice is not null && beforeInvoice != lineBefore) throw new BusinessException("Return goods not yet invoiced and goods already invoiced on separate return documents.");
                beforeInvoice = lineBefore;
                if (lineBefore) refundNet = Grni.Clear(g.Value, g.Received, g.Consumed + thisDoc.Sum(x => x.Quantity), line.Quantity, g.Cleared + thisDoc.Sum(x => x.BillCredit));
            }
            var originalCost = Math.Abs(matching.Sum(x => x.Value));
            var returnCost = AccountingEngine.Money(originalCost * cumulative / originalQuantity) - earlier.Sum(x => x.Cost);
            var stock = movements.Where(x => x.ProductId == line.ProductId && x.Location == matching[0].Location && x.Serial == serial && x.Batch == batch && x.Expiry == line.Expiry).ToList();
            var available = stock.Sum(x => x.Quantity);
            if (serial is not null && (line.Quantity != 1 || sale && movements.Where(x => x.Serial == serial).Sum(x => x.Quantity) != 0)) throw new BusinessException("A serialized return must be one unit and not already in stock.");
            if (!sale)
            {
                if (available - await CommerceRules.Reserved(db, actor.Branch, line.ProductId, matching[0].Location, serial, batch, line.Expiry) < line.Quantity) throw new BusinessException("Insufficient stock in the original warehouse for a purchase return.");
                returnCost = available == line.Quantity ? stock.Sum(x => x.Value) : AccountingEngine.Money(stock.Sum(x => x.Value) / available * line.Quantity);
            }
            // Tax is allocated across all returns at invoice level, with full-return residual correction below.
            var refundTax = sale && Taxed(line.ProductId) && originalTaxable != 0 ? AccountingEngine.Money(taxTotal * refundNet / originalTaxable) : 0;
            var allocation = new ReturnAllocation { Id = Guid.NewGuid(), OriginalDocumentId = original.Id, ReturnDocumentId = doc.Id, ProductId = line.ProductId, Serial = serial, Batch = batch, Expiry = line.Expiry, Quantity = line.Quantity, Net = refundNet, Tax = refundTax, Cost = returnCost };
            if (beforeInvoice == true) allocation.BillCredit = refundNet; // clears goods received not invoiced line by line
            returned.Add(allocation); db.ReturnAllocations.Add(allocation);
            var movement = new Movement { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, ProductId = line.ProductId, Location = matching[0].Location, Serial = serial, Batch = batch, Expiry = line.Expiry, Quantity = sale ? line.Quantity : -line.Quantity, Value = sale ? returnCost : -returnCost };
            movements.Add(movement); db.Movements.Add(movement); net += refundNet; tax += refundTax; cost += returnCost;
        }
        var alreadyTaxable = returned.Where(x => x.ReturnDocumentId != doc.Id && Taxed(x.ProductId)).Sum(x => x.Net);
        var thisTaxable = returned.Where(x => x.ReturnDocumentId == doc.Id && Taxed(x.ProductId)).Sum(x => x.Net);
        var alreadyTax = returned.Where(x => x.ReturnDocumentId != doc.Id).Sum(x => x.Tax);
        var lastTaxed = returned.LastOrDefault(x => x.ReturnDocumentId == doc.Id && Taxed(x.ProductId));
        var correctedTax = sale && lastTaxed is not null && originalTaxable != 0 ? AccountingEngine.Money(taxTotal * (alreadyTaxable + thisTaxable) / originalTaxable) - alreadyTax : tax;
        if (lastTaxed is not null) lastTaxed.Tax += correctedTax - tax;
        tax = correctedTax;
        var refund = net + tax;
        decimal billCredit = 0;
        if (beforeInvoice == true && input.Disposition != ReturnDisposition.Original) throw new BusinessException("These goods are not yet invoiced, so nothing has been paid: choose 'Original settlement'. The return reduces goods received not invoiced.");
        if (beforeInvoice == false && input.Disposition == ReturnDisposition.Original) throw new BusinessException("These goods are already on a supplier invoice. Choose a supplier credit (then allocate it to that invoice under Banking) or a cash/bank refund.");
        if (beforeInvoice is not null) billCredit = beforeInvoice.Value ? net : 0;
        else if (!sale || original.Settlement == "Credit")
        {
            var settled = (await db.BillAllocations.Where(x => x.InvoiceId == original.Id && !db.Documents.Any(r => r.Reverses == x.VoucherId)).ToListAsync()).Sum(x => x.Amount);
            var earlierBillCredits = returned.Where(x => x.ReturnDocumentId != doc.Id).Sum(x => x.BillCredit);
            var outstanding = Math.Max(0, original.Total - settled - earlierBillCredits);
            if (input.Disposition == ReturnDisposition.Original && refund > outstanding) throw new BusinessException("Select a cash/bank refund or customer/supplier credit for the already-paid portion.");
            billCredit = Math.Min(refund, outstanding);
        }
        if (beforeInvoice is null) returned.Last().BillCredit = billCredit;
        doc.Total = -refund;
        doc.Detail = sale && categories is not null
            ? JsonSerializer.Serialize(new { Request = input, Net = net, Tax = tax, BillCredit = billCredit, RefundOrCredit = refund - billCredit, Taxable = thisTaxable,
                ZeroRated = returned.Where(x => x.ReturnDocumentId == doc.Id && categories.GetValueOrDefault(x.ProductId) == TaxCategory.ZeroRated).Sum(x => x.Net), Exempt = returned.Where(x => x.ReturnDocumentId == doc.Id && categories.GetValueOrDefault(x.ProductId) == TaxCategory.Exempt).Sum(x => x.Net) })
            : JsonSerializer.Serialize(new { Request = input, Net = net, Tax = tax, BillCredit = billCredit, RefundOrCredit = refund - billCredit });
        db.Documents.Add(doc);
        var posting = new List<Posting>();
        if (sale)
        {
            var destination = input.Disposition switch {
                ReturnDisposition.CashRefund => Account.Cash,
                ReturnDisposition.BankRefund => Account.Bank,
                ReturnDisposition.CustomerCredit => (Account)2400,
                _ => originalRequest.Payment switch { PaymentMethod.Cash => Account.Cash, PaymentMethod.BankTransfer => Account.Bank, PaymentMethod.Cheque => Account.ChequesInHand, _ => Account.Receivables }
            };
            posting.AddRange([new(Account.Sales, net, 0), new(Account.TaxPayable, tax, 0), new(Account.Receivables, 0, billCredit), new(destination, 0, refund - billCredit), new(Account.Inventory, cost, 0), new(Account.CostOfSales, 0, cost)]);
            if (destination == (Account)2400 && refund > billCredit) db.CustomerCredits.Add(new() { Id = Guid.NewGuid(), ReturnId = doc.Id, Amount = refund - billCredit });
            doc.Settlement = billCredit == refund ? "Bill credit" : input.Disposition == ReturnDisposition.Original ? original.Settlement : input.Disposition.ToString();
        }
        else
        {
            var destination = input.Disposition == ReturnDisposition.CashRefund ? Account.Cash : input.Disposition == ReturnDisposition.BankRefund ? Account.Bank : (Account)1300;
            posting.AddRange([new(beforeInvoice == true ? Grni.Account : Account.Payables, billCredit, 0), new(destination, net - billCredit, 0), new(Account.Inventory, 0, cost), new((Account)5100, Math.Max(0, cost - net), Math.Max(0, net - cost))]);
            if (destination == (Account)1300 && net > billCredit) db.SupplierCredits.Add(new() { Id = Guid.NewGuid(), ReturnId = doc.Id, Amount = net - billCredit });
            doc.Settlement = beforeInvoice == true ? "Returned before invoicing" : billCredit == refund ? "Purchase bill credit" : input.Disposition.ToString();
        }
        foreach (var p in AccountingEngine.Balanced(posting)) db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, Account = p.Account, Debit = p.Debit, Credit = p.Credit });
        db.Operations.Add(new() { Id = input.OperationId, ResultId = doc.Id, Branch = actor.Branch, Actor = actor.Id, Action = "document.return", Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = hash });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return doc.Id;
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}



