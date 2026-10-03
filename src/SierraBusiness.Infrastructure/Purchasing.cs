using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

/// <summary>Effective-dated reference rate: NLe per one unit of the foreign currency. Append-only.</summary>
public sealed class ExchangeRate { public Guid Id { get; set; } public string Branch { get; set; } = ""; public string Currency { get; set; } = ""; public DateOnly EffectiveFrom { get; set; } public decimal Rate { get; set; } public string Source { get; set; } = ""; public string CreatedBy { get; set; } = ""; }
/// <summary>A payable owed in a foreign currency. The bill document's Total is its NLe carrying amount at <see cref="Rate"/>.</summary>
public sealed class ForeignBill { public Guid Id { get; set; } public Guid DocumentId { get; set; } public string Currency { get; set; } = ""; public decimal Rate { get; set; } public decimal SourceAmount { get; set; } }
public sealed class ForeignSettlement { public Guid Id { get; set; } public Guid BillId { get; set; } public Guid VoucherId { get; set; } public decimal SourceAmount { get; set; } public decimal CarryingAmount { get; set; } public decimal PaidAmount { get; set; } }
public sealed class LandedCostAllocation { public Guid Id { get; set; } public Guid DocumentId { get; set; } public Guid ReceiptMovementId { get; set; } public LandedCostBasis Basis { get; set; } public decimal Weight { get; set; } public decimal Amount { get; set; } public decimal InventoryAmount { get; set; } public decimal CostOfSalesAmount { get; set; } }

public static partial class Fx
{
    public const string Local = "SLE";
    public const Account Gains = (Account)4200, Losses = (Account)5300;
    /// <summary>Null means local currency.</summary>
    public static string? Normalize(string? currency)
    {
        var code = currency?.Trim().ToUpperInvariant() ?? "";
        if (code is "" or Local or "NLE") return null;
        if (!Code().IsMatch(code)) throw new BusinessException("Use a three-letter currency code such as USD, GBP or EUR.");
        return code;
    }
    public static async Task<decimal> Rate(BusinessDb db, string branch, string currency, DateOnly date, CancellationToken ct = default)
    {
        var rate = await db.ExchangeRates.Where(x => x.Branch == branch && x.Currency == currency && x.EffectiveFrom <= date).OrderByDescending(x => x.EffectiveFrom).Select(x => (decimal?)x.Rate).FirstOrDefaultAsync(ct);
        return rate ?? throw new BusinessException($"No {currency} exchange rate applies on {date:dd MMM yyyy}. Add one under Purchasing & imports → Exchange rates.");
    }
    public static async Task<(decimal Source, decimal Carrying)> Outstanding(BusinessDb db, BusinessDocument bill, ForeignBill foreign)
    {
        var settled = (await db.BillAllocations.Where(x => x.InvoiceId == bill.Id && !db.Documents.Any(r => r.Reverses == x.VoucherId)).ToListAsync()).Sum(x => x.Amount);
        var returned = (await db.ReturnAllocations.Where(x => x.OriginalDocumentId == bill.Id && !db.Documents.Any(r => r.Reverses == x.ReturnDocumentId)).ToListAsync()).Sum(x => x.BillCredit);
        var carrying = bill.Total - settled - returned;
        var paidSource = (await db.ForeignSettlements.Where(x => x.BillId == bill.Id && !db.Documents.Any(r => r.Reverses == x.VoucherId)).ToListAsync()).Sum(x => x.SourceAmount);
        var source = foreign.SourceAmount - paidSource - AccountingEngine.Money(returned / foreign.Rate);
        return carrying <= 0 ? (0, 0) : (Math.Max(0, source), carrying);
    }
    public static async Task RejectLocalSettlement(BusinessDb db, Guid billId)
    {
        if (await db.ForeignBills.AnyAsync(x => x.DocumentId == billId)) throw new BusinessException("This bill is owed in a foreign currency. Pay it under Purchasing & imports → Foreign-currency bills so the exchange difference is recorded.");
    }
    [GeneratedRegex("^[A-Z]{3}$")] private static partial Regex Code();
}

/// <summary>One idempotent-command implementation for new services (retries replay; reused IDs with different data are refused).</summary>
internal static class Commands
{
    public static async Task<Guid> Run<T>(IDbContextFactory<BusinessDb> factory, Actor actor, Guid operationId, string action, T input, Func<BusinessDb, Task<Guid>> work)
    {
        if (operationId == Guid.Empty) throw new BusinessException("An operation ID is required.");
        var payload = JsonSerializer.Serialize(input);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + action + payload)));
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var existing = await db.Operations.SingleOrDefaultAsync(x => x.Id == operationId);
        if (existing is not null)
        {
            if (existing.Fingerprint != fingerprint && (existing.Branch != actor.Branch || existing.Actor != actor.Id || existing.Action != action || JsonSerializer.Serialize(JsonSerializer.Deserialize<T>(existing.Payload)) != payload))
                throw new BusinessException("Operation ID was already used with different data or identity.");
            return existing.ResultId;
        }
        var result = await work(db);
        db.Operations.Add(new() { Id = operationId, ResultId = result, Branch = actor.Branch, Actor = actor.Id, Action = action, Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = fingerprint });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return result;
    }
}

public sealed partial class PurchasingService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IPurchasingService
{
    private async Task<Actor> Authorize(params string[] permissions)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        foreach (var permission in permissions) actor.Demand(permission);
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Wrong branch.");
        return actor;
    }
    private async Task OpenDate(BusinessDb db, Actor actor, DateOnly date)
    {
        if (date <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough) || date > DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("The date must be in an open accounting period and not in the future.");
    }
    private static decimal Money2(decimal value, string field)
    {
        if (value <= 0 || value > 1_000_000_000 || AccountingEngine.Money(value) != value) throw new BusinessException($"{field} must be more than zero with at most two decimal places.");
        return value;
    }

    // ---------------- exchange rates ----------------

    public async Task<IReadOnlyList<ExchangeRateView>> Rates()
    {
        var actor = await Authorize(); // receivers need rates; rates are reference data, not confidential
        await using var db = await factory.CreateDbContextAsync();
        return (await db.ExchangeRates.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync()).OrderBy(x => x.Currency).ThenByDescending(x => x.EffectiveFrom).Select(x => new ExchangeRateView(x.Currency, x.EffectiveFrom, x.Rate, x.Source)).ToArray();
    }

    public async Task<Guid> SetRate(ExchangeRateCommand input)
    {
        var actor = await Authorize("accounting.read", "accounting.write");
        return await Commands.Run(factory, actor, input.OperationId, "fx.rate", input, async db =>
        {
            var currency = Fx.Normalize(input.Currency) ?? throw new BusinessException("NLe is the local currency and has no exchange rate.");
            if (input.Rate <= 0 || input.Rate > 1_000_000 || decimal.Round(input.Rate, 4) != input.Rate) throw new BusinessException("Enter the rate as NLe per one unit of the currency, with up to four decimal places.");
            if (input.EffectiveFrom > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(31)) throw new BusinessException("Rates can be entered at most 31 days ahead.");
            if (await db.ExchangeRates.AnyAsync(x => x.Branch == actor.Branch && x.Currency == currency && x.EffectiveFrom == input.EffectiveFrom)) throw new BusinessException($"A {currency} rate already starts on {input.EffectiveFrom:dd MMM yyyy}. Rates are kept as history; add one for another date.");
            var rate = new ExchangeRate { Id = Guid.NewGuid(), Branch = actor.Branch, Currency = currency, EffectiveFrom = input.EffectiveFrom, Rate = input.Rate, Source = Rules.Required(input.Source, "Rate source", 120), CreatedBy = actor.Id };
            db.ExchangeRates.Add(rate); return rate.Id;
        });
    }

    // ---------------- foreign-currency bills ----------------

    public async Task<IReadOnlyList<ForeignBillView>> ForeignBills()
    {
        var actor = await Authorize("accounting.read");
        await using var db = await factory.CreateDbContextAsync();
        var bills = await (from f in db.ForeignBills join d in db.Documents on f.DocumentId equals d.Id where d.Branch == actor.Branch && !db.Documents.Any(r => r.Reverses == d.Id) select new { f, d }).ToListAsync();
        var result = new List<ForeignBillView>();
        foreach (var x in bills)
        {
            var (source, carrying) = await Fx.Outstanding(db, x.d, x.f);
            if (carrying > 0) result.Add(new(x.d.Id, x.d.Number, x.d.Party, x.d.BusinessDate, x.f.Currency, x.f.Rate, x.f.SourceAmount, source, carrying));
        }
        return result.OrderBy(x => x.Date).ToArray();
    }

    public async Task<Guid> PayForeign(ForeignPaymentCommand input)
    {
        var actor = await Authorize("accounting.read", "accounting.write");
        return await Commands.Run(factory, actor, input.OperationId, "fx.payment", input, async db =>
        {
            await OpenDate(db, actor, input.Date);
            var bill = await db.Documents.SingleOrDefaultAsync(x => x.Id == input.BillId && x.Branch == actor.Branch && !db.Documents.Any(r => r.Reverses == x.Id)) ?? throw new BusinessException("Bill not found.");
            var foreign = await db.ForeignBills.SingleOrDefaultAsync(x => x.DocumentId == bill.Id) ?? throw new BusinessException("This bill is in NLe; pay it with a payment voucher under Accounting.");
            if (input.Date < bill.BusinessDate) throw new BusinessException("Payment cannot be dated before the bill.");
            Money2(input.SourceAmount, $"Amount in {foreign.Currency}"); Money2(input.LocalPaid, "NLe paid");
            if (!await db.LedgerAccounts.AnyAsync(x => x.Id == input.FundsAccount && x.CashOrBank)) throw new BusinessException("Pay from a cash or bank account.");
            var (sourceOutstanding, carryingOutstanding) = await Fx.Outstanding(db, bill, foreign);
            if (input.SourceAmount > sourceOutstanding) throw new BusinessException($"Only {foreign.Currency} {sourceOutstanding:N2} is outstanding on this bill.");
            // Final settlement clears the exact remaining carrying amount so no rounding residue is left on the payable.
            var carrying = input.SourceAmount == sourceOutstanding ? carryingOutstanding : Math.Min(AccountingEngine.Money(input.SourceAmount * foreign.Rate), carryingOutstanding);
            var difference = input.LocalPaid - carrying;
            var doc = new BusinessDocument
            {
                Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-FXP-{input.OperationId:N}", Kind = DocumentKind.Payment, Party = bill.Party, BusinessDate = input.Date, Total = input.LocalPaid,
                Settlement = $"Foreign bill payment ({foreign.Currency})", Detail = JsonSerializer.Serialize(new { Bill = bill.Id, bill.Number, foreign.Currency, BillRate = foreign.Rate, input.SourceAmount, Carrying = carrying, input.LocalPaid, PaymentRate = decimal.Round(input.LocalPaid / input.SourceAmount, 4), ExchangeDifference = difference, Reference = Rules.Required(input.Reference, "Payment reference", 100) })
            };
            db.Documents.Add(doc);
            db.BillAllocations.Add(new() { Id = Guid.NewGuid(), VoucherId = doc.Id, InvoiceId = bill.Id, Amount = carrying });
            db.ForeignSettlements.Add(new() { Id = Guid.NewGuid(), BillId = bill.Id, VoucherId = doc.Id, SourceAmount = input.SourceAmount, CarryingAmount = carrying, PaidAmount = input.LocalPaid });
            var postings = new List<Posting> { new(Account.Payables, carrying, 0), new((Account)input.FundsAccount, 0, input.LocalPaid) };
            if (difference > 0) postings.Add(new(Fx.Losses, difference, 0)); else if (difference < 0) postings.Add(new(Fx.Gains, 0, -difference));
            foreach (var p in AccountingEngine.Balanced(postings)) db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, Account = p.Account, Debit = p.Debit, Credit = p.Credit });
            return doc.Id;
        });
    }

    // ---------------- landed cost ----------------

    public async Task<IReadOnlyList<LandedReceiptView>> Receipts()
    {
        var actor = await Authorize("accounting.read");
        await using var db = await factory.CreateDbContextAsync();
        var receipts = await db.Documents.AsNoTracking().Where(x => x.Branch == actor.Branch && x.Kind == DocumentKind.Receipt && !db.Documents.Any(r => r.Reverses == x.Id)).OrderByDescending(x => x.BusinessDate).Take(200).ToListAsync();
        var ids = receipts.Select(x => x.Id).ToList();
        var lines = await (from m in db.Movements.AsNoTracking() join p in db.Products on m.ProductId equals p.Id where ids.Contains(m.DocumentId) && m.Quantity > 0 select new { m, p.Sku, p.Name }).ToListAsync();
        var foreign = await db.ForeignBills.AsNoTracking().Where(x => ids.Contains(x.DocumentId)).ToDictionaryAsync(x => x.DocumentId, x => x.Currency);
        return receipts.Select(r => new LandedReceiptView(r.Id, r.Number, r.Party, r.BusinessDate, r.Total, foreign.GetValueOrDefault(r.Id) ?? Fx.Local,
            lines.Where(l => l.m.DocumentId == r.Id).Select(l => new ReceiptLineView(l.m.Id, r.Id, r.Number, l.Sku, l.Name, l.m.Location, l.m.Serial, l.m.Batch, l.m.Quantity, l.m.Value)).ToArray())).ToArray();
    }

    private sealed record Plan(decimal LocalTotal, decimal SourceTotal, decimal Rate, string? Currency, List<(Movement Line, string Sku, decimal Weight, decimal Amount, decimal Inventory, decimal Cost)> Lines);

    private async Task<Plan> Allocate(BusinessDb db, Actor actor, LandedCostCommand input)
    {
        Rules.Required(input.Party, "Service provider");
        if (input.Receipts is null || input.Receipts.Count is < 1 or > 20 || input.Receipts.Distinct().Count() != input.Receipts.Count) throw new BusinessException("Choose 1 to 20 different goods receipts.");
        if (input.Charges is null || input.Charges.Count is < 1 or > 20) throw new BusinessException("Enter 1 to 20 charges.");
        foreach (var charge in input.Charges) { if (!Enum.IsDefined(charge.Type)) throw new BusinessException("Unknown charge type."); Money2(charge.Amount, $"{charge.Type} amount"); if ((charge.Description ?? "").Length > 120) throw new BusinessException("Charge descriptions are limited to 120 characters."); }
        if (!Enum.IsDefined(input.Basis)) throw new BusinessException("Choose how to share the costs.");
        var currency = Fx.Normalize(input.Currency);
        if (currency is not null && input.FundsAccount is not null) throw new BusinessException("Foreign-currency charges are recorded as owed to the provider; pay them later as a foreign-currency bill.");
        var rate = currency is null ? 1m : await Fx.Rate(db, actor.Branch, currency, input.Date);
        var sourceTotal = input.Charges.Sum(x => x.Amount);
        var localTotal = AccountingEngine.Money(sourceTotal * rate);
        var receipts = await db.Documents.Where(x => input.Receipts.Contains(x.Id) && x.Branch == actor.Branch && x.Kind == DocumentKind.Receipt && !db.Documents.Any(r => r.Reverses == x.Id)).ToListAsync();
        if (receipts.Count != input.Receipts.Count) throw new BusinessException("Choose posted, unreversed goods receipts from this branch.");
        if (receipts.Any(x => x.BusinessDate > input.Date)) throw new BusinessException("The landed cost cannot be dated before the goods were received.");
        var lines = await (from m in db.Movements join p in db.Products on m.ProductId equals p.Id where input.Receipts.Contains(m.DocumentId) && m.Quantity > 0 select new { m, p.Sku }).ToListAsync();
        if (lines.Count == 0) throw new BusinessException("The selected receipts have no stock lines.");
        var weights = lines.Select(l => input.Basis switch { LandedCostBasis.Value => l.m.Value, LandedCostBasis.Quantity => l.m.Quantity, _ => input.ManualShares?.GetValueOrDefault(l.m.Id) ?? 0 }).ToList();
        List<decimal> amounts;
        if (input.Basis == LandedCostBasis.Manual)
        {
            if (input.ManualShares is null || input.ManualShares.Keys.Any(k => lines.All(l => l.m.Id != k)) || input.ManualShares.Values.Any(v => v < 0 || AccountingEngine.Money(v) != v)) throw new BusinessException("Manual shares must be NLe amounts (two decimals) for lines of the selected receipts.");
            if (weights.Sum() != localTotal) throw new BusinessException($"Manual shares add up to NLe {weights.Sum():N2}; they must equal the NLe {localTotal:N2} total.");
            amounts = weights;
        }
        else
        {
            var total = weights.Sum(); if (total <= 0) throw new BusinessException("The selected lines have no value or quantity to share costs by.");
            amounts = weights.Select(w => AccountingEngine.Money(localTotal * w / total)).ToList();
            var largest = weights.IndexOf(weights.Max()); amounts[largest] += localTotal - amounts.Sum(); // rounding residue on the largest line
        }
        // Landed cost belongs in stock only for units still on hand; the share for units already sold goes to cost of sales.
        var identities = lines.Select((l, i) => (l, i)).GroupBy(x => (x.l.m.ProductId, x.l.m.Location, x.l.m.Serial, x.l.m.Batch, x.l.m.Expiry));
        var plan = new List<(Movement, string, decimal, decimal, decimal, decimal)>(new (Movement, string, decimal, decimal, decimal, decimal)[lines.Count]);
        foreach (var identity in identities)
        {
            var key = identity.Key;
            var onHand = await db.Movements.Where(x => x.Branch == actor.Branch && x.ProductId == key.ProductId && x.Location == key.Location && x.Serial == key.Serial && x.Batch == key.Batch && x.Expiry == key.Expiry).SumAsync(x => x.Quantity);
            var received = identity.Sum(x => x.l.m.Quantity);
            var ratio = received <= 0 ? 0 : Math.Clamp(onHand / received, 0, 1);
            foreach (var (l, i) in identity) { var toStock = AccountingEngine.Money(amounts[i] * ratio); plan[i] = (l.m, l.Sku, weights[i], amounts[i], toStock, amounts[i] - toStock); }
        }
        return new(localTotal, sourceTotal, rate, currency, plan);
    }

    public async Task<LandedCostPreview> PreviewLandedCost(LandedCostCommand input)
    {
        var actor = await Authorize("accounting.read", "accounting.write", "inventory.receive");
        await using var db = await factory.CreateDbContextAsync();
        var plan = await Allocate(db, actor, input);
        return new(plan.LocalTotal, plan.Rate, plan.Lines.Select(x => new LandedAllocationView(x.Line.Id, x.Sku, x.Line.Location, x.Weight, x.Amount, x.Inventory, x.Cost)).ToArray());
    }

    public async Task<Guid> PostLandedCost(LandedCostCommand input)
    {
        var actor = await Authorize("accounting.read", "accounting.write", "inventory.receive");
        return await Commands.Run(factory, actor, input.OperationId, "landed-cost.post", input, async db =>
        {
            await OpenDate(db, actor, input.Date);
            var plan = await Allocate(db, actor, input);
            var party = Rules.Required(input.Party, "Service provider");
            if (input.FundsAccount is not null && !await db.LedgerAccounts.AnyAsync(x => x.Id == input.FundsAccount && x.CashOrBank)) throw new BusinessException("Pay from a cash or bank account, or leave it owed to the provider.");
            var doc = new BusinessDocument
            {
                Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-LC-{input.OperationId:N}", Kind = DocumentKind.LandedCost, Party = party, BusinessDate = input.Date, DueDate = input.Date, Total = plan.LocalTotal,
                Settlement = input.FundsAccount is not null ? "Paid" : plan.Currency is null ? "Supplier payable" : $"Supplier payable ({plan.Currency})",
                Detail = JsonSerializer.Serialize(new { input.Receipts, input.Charges, Basis = input.Basis.ToString(), Currency = plan.Currency ?? Fx.Local, ExchangeRate = plan.Rate, SourceTotal = plan.SourceTotal, LocalTotal = plan.LocalTotal, input.Reference, Lines = plan.Lines.Select(x => new { Movement = x.Line.Id, x.Sku, x.Weight, x.Amount, x.Inventory, x.Cost }) })
            };
            db.Documents.Add(doc);
            foreach (var group in plan.Lines.Where(x => x.Inventory != 0).GroupBy(x => (x.Line.ProductId, x.Line.Location, x.Line.Serial, x.Line.Batch, x.Line.Expiry)))
                db.Movements.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, ProductId = group.Key.ProductId, Location = group.Key.Location, Serial = group.Key.Serial, Batch = group.Key.Batch, Expiry = group.Key.Expiry, Quantity = 0, Value = group.Sum(x => x.Inventory) });
            foreach (var x in plan.Lines) db.LandedCostAllocations.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, ReceiptMovementId = x.Line.Id, Basis = input.Basis, Weight = x.Weight, Amount = x.Amount, InventoryAmount = x.Inventory, CostOfSalesAmount = x.Cost });
            var postings = new List<Posting> { new(Account.Inventory, plan.Lines.Sum(x => x.Inventory), 0), new(Account.CostOfSales, plan.Lines.Sum(x => x.Cost), 0), new(input.FundsAccount is int funds ? (Account)funds : Account.Payables, 0, plan.LocalTotal) };
            foreach (var p in AccountingEngine.Balanced(postings)) db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, Account = p.Account, Debit = p.Debit, Credit = p.Credit });
            if (plan.Currency is not null) db.ForeignBills.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Currency = plan.Currency, Rate = plan.Rate, SourceAmount = plan.SourceTotal });
            return doc.Id;
        });
    }
}
