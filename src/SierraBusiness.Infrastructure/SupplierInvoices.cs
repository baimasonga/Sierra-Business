using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

/// <summary>Links a supplier invoice to the receipt line (first receipt movement of a product/warehouse/serial/batch group) it bills. Append-only.</summary>
public sealed class InvoiceMatch
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid ReceiptId { get; set; }
    public Guid ReceiptMovementId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Supplier's amount for the quantity, in the receipt currency.</summary>
    public decimal SourceAmount { get; set; }
    /// <summary>Amount expected from the receipt price for the quantity, in the receipt currency.</summary>
    public decimal ExpectedSource { get; set; }
    /// <summary>NLe cleared from goods received not invoiced (receipt carrying value).</summary>
    public decimal GoodsCleared { get; set; }
    /// <summary>NLe price difference at the receipt rate; positive when the supplier charged more.</summary>
    public decimal PriceVariance { get; set; }
    public decimal InventoryAmount { get; set; }
    public decimal CostOfSalesAmount { get; set; }
}
/// <summary>Currency of a purchase order (absent means NLe). Kept apart from the sealed order row so earlier seals stay valid.</summary>
public sealed class OrderCurrency { public Guid Id { get; set; } public Guid OrderId { get; set; } public string Currency { get; set; } = ""; }

/// <summary>Goods received not invoiced: receipts posted before the supplier's invoice arrives.</summary>
public static class Grni
{
    public const Account Account = (Account)2050;
    public const string Awaiting = "Awaiting supplier invoice";
    public static bool IsAwaiting(BusinessDocument receipt) => receipt.Kind == DocumentKind.Receipt && receipt.Settlement.StartsWith(Awaiting, StringComparison.Ordinal);
    /// <summary>NLe to clear for quantity <paramref name="quantity"/> after <paramref name="consumedBefore"/> units were invoiced or returned; the final unit clears the exact remainder.</summary>
    public static decimal Clear(decimal value, decimal received, decimal consumedBefore, decimal quantity, decimal clearedBefore)
    {
        var cumulative = consumedBefore + quantity;
        return cumulative >= received ? value - clearedBefore : AccountingEngine.Money(value * cumulative / received) - clearedBefore;
    }
    public static decimal Expected(decimal source, decimal received, decimal consumedBefore, decimal quantity)
    {
        var cumulative = consumedBefore + quantity;
        return (cumulative >= received ? source : AccountingEngine.Money(source * cumulative / received)) - AccountingEngine.Money(source * consumedBefore / received);
    }
    public static IEnumerable<Posting> Signed(Account account, decimal amount) => amount > 0 ? [new(account, amount, 0)] : amount < 0 ? [new(account, 0, -amount)] : [];

    /// <summary>A receipt line awaiting invoice: all receipt movements of one stock identity.</summary>
    public sealed record Line(BusinessDocument Receipt, Guid LineId, Guid ProductId, string Location, string? Serial, string? Batch, DateOnly? Expiry, decimal Received, decimal Value, decimal Source, string? Currency, decimal Rate,
        decimal Invoiced, decimal InvoicedCleared, decimal Returned, decimal ReturnedCleared)
    {
        public decimal Consumed => Invoiced + Returned;
        public decimal Outstanding => Received - Consumed;
        public decimal Cleared => InvoicedCleared + ReturnedCleared;
    }

    public static async Task<List<Line>> Lines(BusinessDb db, string branch, IReadOnlyCollection<Guid>? receiptIds = null, CancellationToken ct = default)
    {
        var query = db.Documents.Where(x => x.Branch == branch && x.Kind == DocumentKind.Receipt && x.Settlement.StartsWith(Awaiting) && !db.Documents.Any(r => r.Reverses == x.Id));
        if (receiptIds is not null) query = query.Where(x => receiptIds.Contains(x.Id));
        var receipts = await query.ToListAsync(ct);
        var ids = receipts.Select(x => x.Id).ToList();
        var movements = await db.Movements.Where(x => ids.Contains(x.DocumentId) && x.Quantity > 0).ToListAsync(ct);
        var matches = await db.InvoiceMatches.Where(x => ids.Contains(x.ReceiptId) && !db.Documents.Any(r => r.Reverses == x.InvoiceId)).ToListAsync(ct);
        var returns = await db.ReturnAllocations.Where(x => ids.Contains(x.OriginalDocumentId) && !db.Documents.Any(r => r.Reverses == x.ReturnDocumentId)).ToListAsync(ct);
        var result = new List<Line>();
        foreach (var receipt in receipts)
        {
            using var detail = JsonDocument.Parse(receipt.Detail);
            var root = detail.RootElement;
            var currency = root.TryGetProperty("Currency", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var rate = root.TryGetProperty("ExchangeRate", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDecimal() : 1m;
            var sources = root.TryGetProperty("ReceiptLines", out var rl) ? rl.EnumerateArray().ToDictionary(x => x.GetProperty("Movement").GetGuid(), x => x.GetProperty("Source").GetDecimal()) : [];
            foreach (var group in movements.Where(x => x.DocumentId == receipt.Id).GroupBy(x => (x.ProductId, x.Location, x.Serial, x.Batch, x.Expiry)))
            {
                var first = group.OrderBy(x => x.Id).First();
                var groupIds = group.Select(x => x.Id).ToHashSet();
                var matched = matches.Where(x => groupIds.Contains(x.ReceiptMovementId)).ToList();
                var returned = returns.Where(x => x.ProductId == group.Key.ProductId && x.Serial == group.Key.Serial && x.Batch == group.Key.Batch && x.Expiry == group.Key.Expiry).ToList();
                var value = group.Sum(x => x.Value);
                result.Add(new(receipt, first.Id, group.Key.ProductId, group.Key.Location, group.Key.Serial, group.Key.Batch, group.Key.Expiry, group.Sum(x => x.Quantity), value,
                    group.Sum(x => sources.GetValueOrDefault(x.Id, AccountingEngine.Money(x.Value / rate))), currency, rate,
                    matched.Sum(x => x.Quantity), matched.Sum(x => x.GoodsCleared), returned.Sum(x => x.Quantity), returned.Sum(x => x.BillCredit)));
            }
        }
        return result;
    }
}

public sealed partial class PurchasingService
{
    public async Task<IReadOnlyList<AwaitingInvoiceLine>> AwaitingInvoice()
    {
        var actor = await Authorize("accounting.read");
        await using var db = await factory.CreateDbContextAsync();
        var lines = (await Grni.Lines(db, actor.Branch)).Where(x => x.Outstanding > 0).ToList();
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return lines.OrderBy(x => x.Receipt.BusinessDate).ThenBy(x => x.Receipt.Number).Select(x =>
        {
            var p = products[x.ProductId];
            return new AwaitingInvoiceLine(x.LineId, x.Receipt.Id, x.Receipt.Number, x.Receipt.BusinessDate, x.Receipt.Party, p.Sku, p.Name, p.Unit, x.Location, x.Serial, x.Batch,
                x.Received, x.Invoiced, x.Returned, x.Outstanding, x.Currency ?? Fx.Local, Grni.Expected(x.Source, x.Received, x.Consumed, x.Outstanding), x.Value - x.Cleared, today.DayNumber - x.Receipt.BusinessDate.DayNumber);
        }).ToArray();
    }

    private sealed record MatchPlan(string? Currency, decimal Rate, decimal SourceTotal, decimal LocalTotal, decimal Exchange, bool NeedsReason, string Supplier, List<MatchLine> Lines);
    private sealed record MatchLine(Grni.Line Line, string Sku, decimal Quantity, decimal Expected, decimal Amount, decimal Cleared, decimal Variance, decimal ToInventory, decimal ToCost);

    private async Task<MatchPlan> Match(BusinessDb db, Actor actor, SupplierInvoiceCommand input)
    {
        var supplier = Rules.Required(input.Supplier, "Supplier");
        if (input.Lines is null || input.Lines.Count is < 1 or > 200 || input.Lines.Select(x => x.LineId).Distinct().Count() != input.Lines.Count) throw new BusinessException("Enter 1 to 200 different receipt lines.");
        var movementIds = input.Lines.Select(x => x.LineId).ToList();
        var receiptIds = await db.Movements.Where(x => movementIds.Contains(x.Id)).Select(x => x.DocumentId).Distinct().ToListAsync();
        var available = await Grni.Lines(db, actor.Branch, receiptIds);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var planned = new List<MatchLine>();
        foreach (var entry in input.Lines)
        {
            var line = available.SingleOrDefault(x => x.LineId == entry.LineId) ?? throw new BusinessException("Choose receipt lines that are awaiting a supplier invoice in this branch.");
            if (!line.Receipt.Party.Equals(supplier, StringComparison.OrdinalIgnoreCase)) throw new BusinessException($"Receipt {line.Receipt.Number} is from {line.Receipt.Party}, not {supplier}.");
            if (line.Receipt.BusinessDate > input.Date) throw new BusinessException("The invoice cannot be dated before the goods were received.");
            Rules.Positive(entry.Quantity, "Invoiced quantity");
            if (entry.Quantity > line.Outstanding) throw new BusinessException($"Only {line.Outstanding:0.####} {products[line.ProductId].Unit} of {products[line.ProductId].Sku} on {line.Receipt.Number} is still awaiting an invoice.");
            Money2(entry.Amount, $"Invoice amount for {products[line.ProductId].Sku}");
            var cleared = Grni.Clear(line.Value, line.Received, line.Consumed, entry.Quantity, line.Cleared);
            var expected = Grni.Expected(line.Source, line.Received, line.Consumed, entry.Quantity);
            planned.Add(new(line, products[line.ProductId].Sku, entry.Quantity, expected, entry.Amount, cleared, AccountingEngine.Money((entry.Amount - expected) * line.Rate), 0, 0));
        }
        var currencies = planned.Select(x => x.Line.Currency).Distinct().ToList();
        if (currencies.Count != 1) throw new BusinessException("An invoice is in one currency. Match receipts in NLe and receipts in foreign currency on separate invoices.");
        var currency = currencies[0];
        var rate = currency is null ? 1m : await Fx.Rate(db, actor.Branch, currency, input.Date);
        var sourceTotal = planned.Sum(x => x.Amount);
        var localTotal = AccountingEngine.Money(sourceTotal * rate);
        var exchange = localTotal - planned.Sum(x => x.Cleared) - planned.Sum(x => x.Variance); // rate movement between receipt and invoice (positive = loss)

        // A price difference belongs in stock for units still on hand; the share for units already sold goes to cost of sales.
        var result = new List<MatchLine>();
        foreach (var identity in planned.GroupBy(x => (x.Line.ProductId, x.Line.Location, x.Line.Serial, x.Line.Batch, x.Line.Expiry)))
        {
            var k = identity.Key;
            var stock = await db.Movements.Where(x => x.Branch == actor.Branch && x.ProductId == k.ProductId && x.Location == k.Location && x.Serial == k.Serial && x.Batch == k.Batch && x.Expiry == k.Expiry).Select(x => new { x.Quantity, x.Value }).ToListAsync();
            var invoiced = identity.Sum(x => x.Quantity);
            var ratio = Math.Clamp(stock.Sum(x => x.Quantity) / invoiced, 0, 1);
            var lines = identity.Select(x => { var toStock = AccountingEngine.Money(x.Variance * ratio); return x with { ToInventory = toStock, ToCost = x.Variance - toStock }; }).ToList();
            var shortfall = stock.Sum(x => x.Value) + lines.Sum(x => x.ToInventory); // a price reduction cannot take stock value below zero
            if (shortfall < 0 && lines.FindIndex(x => x.ToInventory < 0) is var i and >= 0) { lines[i] = lines[i] with { ToInventory = lines[i].ToInventory - shortfall, ToCost = lines[i].ToCost + shortfall }; }
            result.AddRange(lines);
        }
        return new(currency, rate, sourceTotal, localTotal, exchange, result.Any(x => x.Amount != x.Expected), result[0].Line.Receipt.Party, result);
    }

    public async Task<SupplierInvoicePreview> PreviewSupplierInvoice(SupplierInvoiceCommand input)
    {
        var actor = await Authorize("accounting.read", "accounting.write");
        await using var db = await factory.CreateDbContextAsync();
        var plan = await Match(db, actor, input);
        return new(plan.Currency ?? Fx.Local, plan.Rate, plan.SourceTotal, plan.LocalTotal, plan.Lines.Sum(x => x.Cleared), plan.Lines.Sum(x => x.Variance), plan.Exchange, plan.NeedsReason,
            plan.Lines.Select(x => new InvoiceMatchView(x.Line.LineId, x.Line.Receipt.Number, x.Sku, x.Quantity, x.Expected, x.Amount, x.Cleared, x.Variance, x.ToInventory, x.ToCost)).ToArray());
    }

    public async Task<Guid> PostSupplierInvoice(SupplierInvoiceCommand input)
    {
        var actor = await Authorize("accounting.read", "accounting.write");
        return await Commands.Run(factory, actor, input.OperationId, "supplier-invoice.post", input, async db =>
        {
            await OpenDate(db, actor, input.Date);
            var plan = await Match(db, actor, input);
            var number = Rules.Required(input.InvoiceNumber, "Supplier's invoice number", 60);
            var reason = (input.VarianceReason ?? "").Trim();
            if (plan.NeedsReason && reason.Length == 0) throw new BusinessException("The invoice differs from the receipt prices. Enter the reason for the difference (for example the supplier's price change).");
            if (reason.Length > 200) throw new BusinessException("The reason is limited to 200 characters.");
            var normalized = plan.Supplier.ToUpperInvariant();
            var earlier = await db.Documents.Where(x => x.Branch == actor.Branch && x.Kind == DocumentKind.SupplierInvoice && x.Party.ToUpper() == normalized && !db.Documents.Any(r => r.Reverses == x.Id)).Select(x => x.Detail).ToListAsync();
            if (earlier.Any(d => { using var j = JsonDocument.Parse(d); return string.Equals(j.RootElement.GetProperty("InvoiceNumber").GetString(), number, StringComparison.OrdinalIgnoreCase); })) throw new BusinessException($"Invoice {number} from {plan.Supplier} is already recorded.");
            var party = await db.Parties.SingleOrDefaultAsync(x => x.Branch == actor.Branch && x.NormalizedName == normalized);
            var due = input.DueDate ?? input.Date.AddDays(party?.PaymentDays ?? 0);
            if (due < input.Date) throw new BusinessException("The due date cannot be before the invoice date.");
            var doc = new BusinessDocument
            {
                Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-SI-{input.OperationId:N}", Kind = DocumentKind.SupplierInvoice, Party = plan.Supplier, BusinessDate = input.Date, DueDate = due, Total = plan.LocalTotal,
                Settlement = plan.Currency is null ? "Supplier payable" : $"Supplier payable ({plan.Currency})",
                Detail = JsonSerializer.Serialize(new { InvoiceNumber = number, Currency = plan.Currency ?? Fx.Local, ExchangeRate = plan.Rate, SourceTotal = plan.SourceTotal, LocalTotal = plan.LocalTotal, GoodsCleared = plan.Lines.Sum(x => x.Cleared), PriceVariance = plan.Lines.Sum(x => x.Variance), ExchangeDifference = plan.Exchange, VarianceReason = reason,
                    Lines = plan.Lines.Select(x => new { Receipt = x.Line.Receipt.Id, ReceiptNumber = x.Line.Receipt.Number, Line = x.Line.LineId, x.Sku, x.Quantity, x.Expected, Invoiced = x.Amount, x.Cleared, x.Variance, x.ToInventory, x.ToCost }) })
            };
            db.Documents.Add(doc);
            foreach (var group in plan.Lines.Where(x => x.ToInventory != 0).GroupBy(x => (x.Line.ProductId, x.Line.Location, x.Line.Serial, x.Line.Batch, x.Line.Expiry)))
                if (group.Sum(x => x.ToInventory) != 0) db.Movements.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, ProductId = group.Key.ProductId, Location = group.Key.Location, Serial = group.Key.Serial, Batch = group.Key.Batch, Expiry = group.Key.Expiry, Quantity = 0, Value = group.Sum(x => x.ToInventory) });
            foreach (var x in plan.Lines) db.InvoiceMatches.Add(new() { Id = Guid.NewGuid(), InvoiceId = doc.Id, ReceiptId = x.Line.Receipt.Id, ReceiptMovementId = x.Line.LineId, Quantity = x.Quantity, SourceAmount = x.Amount, ExpectedSource = x.Expected, GoodsCleared = x.Cleared, PriceVariance = x.Variance, InventoryAmount = x.ToInventory, CostOfSalesAmount = x.ToCost });
            var postings = new List<Posting>();
            postings.AddRange(Grni.Signed(Grni.Account, plan.Lines.Sum(x => x.Cleared)));
            postings.AddRange(Grni.Signed(Account.Inventory, plan.Lines.Sum(x => x.ToInventory)));
            postings.AddRange(Grni.Signed(Account.CostOfSales, plan.Lines.Sum(x => x.ToCost)));
            postings.AddRange(Grni.Signed(plan.Exchange > 0 ? Fx.Losses : Fx.Gains, plan.Exchange));
            postings.AddRange(Grni.Signed(Account.Payables, -plan.LocalTotal));
            foreach (var p in AccountingEngine.Balanced(postings)) db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, Account = p.Account, Debit = p.Debit, Credit = p.Credit });
            if (plan.Currency is not null) db.ForeignBills.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Currency = plan.Currency, Rate = plan.Rate, SourceAmount = plan.SourceTotal });
            return doc.Id;
        });
    }
}
