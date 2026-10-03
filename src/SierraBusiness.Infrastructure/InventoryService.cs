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
public sealed class StockLocation { public string Code { get; set; } = ""; public string Name { get; set; } = ""; }
public sealed class InventoryService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IInventoryService
{
    private async Task<Actor> Authorize(bool write = false)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read"); if (write) { actor.Demand("inventory.receive"); actor.Demand("documents.reverse"); }
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch."); return actor;
    }
    public async Task<IReadOnlyList<LocationView>> Locations()
    {
        await Authorize(); await using var db = await factory.CreateDbContextAsync(); return await db.StockLocations.AsNoTracking().OrderBy(x => x.Code).Select(x => new LocationView(x.Code, x.Name)).ToListAsync();
    }
    public async Task<IReadOnlyList<BatchPick>> SuggestBatches(BatchSuggestionCommand input)
    {
        var actor = await Authorize(); actor.Demand("sales.create");
        Rules.Positive(input.Quantity, "Requested base quantity");
        if (input.Date == default) throw new BusinessException("Sale date is required.");
        var draft = input.DraftAllocations ?? [];
        if (draft.Count > 200) throw new BusinessException("A draft may contain at most 200 batch allocations.");
        foreach (var line in draft)
        {
            if (line is null) throw new BusinessException("Draft batch allocation is required.");
            Rules.Required(line.Batch, "Draft batch", 160); Rules.Positive(line.Quantity, "Draft base quantity");
        }
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Location(db, input.Location);
        var product = await db.Products.FindAsync(input.ProductId) ?? throw new BusinessException("Product not found.");
        if (product.Tracking is not (TrackingMode.Batch or TrackingMode.BatchExpiry)) throw new BusinessException("Earliest-expiry suggestions require a batch-controlled product.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow); var cutoff = input.Date > today ? input.Date : today;
        var movements = await db.Movements.AsNoTracking().Where(x => x.Branch == actor.Branch && x.Location == input.Location && x.ProductId == product.Id && x.Serial == null && x.Batch != null).ToListAsync();
        var lots = movements.GroupBy(x => new { x.Batch, x.Expiry })
            .Where(x => (x.Key.Expiry is null || x.Key.Expiry >= cutoff) && (product.Tracking != TrackingMode.BatchExpiry || x.Key.Expiry is not null))
            .OrderBy(x => x.Key.Expiry is null).ThenBy(x => x.Key.Expiry).ThenBy(x => x.Key.Batch, StringComparer.Ordinal);
        var remaining = input.Quantity; var picks = new List<BatchPick>();
        foreach (var lot in lots)
        {
            var pending = draft.Where(x => x.Batch.Trim().ToUpperInvariant() == lot.Key.Batch && x.Expiry == lot.Key.Expiry).Sum(x => x.Quantity);
            var available = lot.Sum(x => x.Quantity) - pending;
            if (available <= 0) continue;
            available -= await CommerceRules.Reserved(db, actor.Branch, product.Id, input.Location, null, lot.Key.Batch, lot.Key.Expiry);
            if (available <= 0) continue;
            var take = Math.Min(available, remaining); picks.Add(new(lot.Key.Batch!, lot.Key.Expiry, take)); remaining -= take;
            if (picks.Count > 200) throw new BusinessException("This quantity needs more than 200 batch lines. Split the sale.");
            if (remaining == 0) return picks;
        }
        throw new BusinessException($"Insufficient unexpired, unreserved stock after draft allocations. Short by {remaining} base units. No suggestions were added.");
    }
    public Task<Guid> CreateLocation(LocationCommand input) => Execute(input.OperationId, "location.create", input, async (db, actor) =>
    {
        var code = Rules.Required(input.Code, "Warehouse code", 30).ToUpperInvariant();
        if (!Regex.IsMatch(code, "^[A-Z0-9_-]+$")) throw new BusinessException("Warehouse codes use letters, digits, hyphens or underscores.");
        if (await db.StockLocations.AnyAsync(x => x.Code == code)) throw new BusinessException("Warehouse code already exists.");
        db.StockLocations.Add(new() { Code = code, Name = Rules.Required(input.Name, "Warehouse name") }); return Guid.NewGuid();
    });
    public Task<Guid> Transfer(TransferCommand input) => Execute(input.OperationId, "stock.transfer", input, async (db, actor) =>
    {
        Rules.Positive(input.Quantity, "Transfer quantity");
        if (input.From == input.To) throw new BusinessException("Select two different warehouses.");
        await Location(db, input.From); await Location(db, input.To);
        var product = await db.Products.FindAsync(input.ProductId) ?? throw new BusinessException("Product not found.");
        var serial = string.IsNullOrWhiteSpace(input.Serial) ? null : input.Serial.Trim().ToUpperInvariant();
        var batch = string.IsNullOrWhiteSpace(input.Batch) ? null : input.Batch.Trim().ToUpperInvariant();
        Rules.Tracking(product.Tracking, input.Quantity, serial, batch, input.Expiry, DateOnly.FromDateTime(DateTime.UtcNow), false);
        await InventoryManagement.SerialAvailable(db, actor.Branch, serial);
        var matches = await db.Movements.Where(x => x.Branch == actor.Branch && x.Location == input.From && x.ProductId == product.Id && x.Serial == serial && x.Batch == batch && x.Expiry == input.Expiry).ToListAsync();
        var available = matches.Sum(x => x.Quantity);
        if (available - await CommerceRules.Reserved(db, actor.Branch, product.Id, input.From, serial, batch, input.Expiry) < input.Quantity) throw new BusinessException("Insufficient stock in the source warehouse and tracking identity.");
        var cost = available == input.Quantity ? matches.Sum(x => x.Value) : AccountingEngine.Money(matches.Sum(x => x.Value) / available * input.Quantity);
        var doc = Document(actor, input.OperationId, DocumentKind.StockTransfer, input.Reason, input);
        db.Documents.Add(doc);
        db.Movements.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, ProductId = product.Id, Branch = actor.Branch, Location = input.From, Serial = serial, Batch = batch, Expiry = input.Expiry, Quantity = -input.Quantity, Value = -cost });
        db.Movements.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, ProductId = product.Id, Branch = actor.Branch, Location = input.To, Serial = serial, Batch = batch, Expiry = input.Expiry, Quantity = input.Quantity, Value = cost });
        return doc.Id;
    });
    public async Task<Guid> Count(StockCountCommand input)
    {
        var actor = await session.CurrentActor();
        return await Controls.Guard(factory, actor, () => Execute(input.OperationId, "stock.count", input, async (db, actor) =>
        {
            var id = await CountWithin(db, actor, input);
            var adjustment = db.Documents.Local.Single(x => x.Id == id).Total;
            var policy = await Controls.Current(db, actor.Branch);
            var triggers = policy.StockAdjustmentAbove is decimal above && Math.Abs(adjustment) >= above
                ? new List<ApprovalTrigger> { new("StockAdjustment", $"stock count adjustment of NLe {adjustment:N2} ({input.Reason.Trim()})", "accounting.write") } : [];
            await Controls.Gate(db, actor, Controls.Count, input with { ApprovalId = null }, input.ApprovalId, input.OperationId, triggers, $"Stock count · {input.Location} · counted {input.CountedQuantity:0.####} against {input.ExpectedQuantity:0.####} · NLe {adjustment:N2} · {input.Reason.Trim()}");
            return id;
        }));
    }
    internal async Task<Guid> CountWithin(BusinessDb db, Actor actor, StockCountCommand input)
    {
        await Location(db, input.Location); var product = await db.Products.FindAsync(input.ProductId) ?? throw new BusinessException("Product not found.");
        if (product.Tracking is not (TrackingMode.Quantity or TrackingMode.Serial or TrackingMode.Batch or TrackingMode.BatchExpiry)) throw new BusinessException("This product tracking mode does not support counts.");
        var serial = string.IsNullOrWhiteSpace(input.Serial) ? null : Rules.Required(input.Serial, "Serial", 100).ToUpperInvariant();
        if (product.Tracking != TrackingMode.Serial && serial is not null) throw new BusinessException("Serial details require a serial-controlled product.");
        if (product.Tracking == TrackingMode.Serial && input.CountedQuantity is not (0 or 1)) throw new BusinessException("Count each serial as absent (0) or present (1).");
        var batch = string.IsNullOrWhiteSpace(input.Batch) ? null : Rules.Required(input.Batch, "Batch", 160).ToUpperInvariant();
        if (product.Tracking is TrackingMode.Quantity or TrackingMode.Serial && (batch is not null || input.Expiry is not null)) throw new BusinessException("Quantity and serial products cannot have batch or expiry details in a count.");
        Rules.Tracking(product.Tracking, 1, serial, batch, input.Expiry, DateOnly.FromDateTime(DateTime.UtcNow), false);
        if (input.CountedQuantity < 0 || input.CountedQuantity > 1_000_000_000 || decimal.Round(input.CountedQuantity, 4) != input.CountedQuantity) throw new BusinessException("Counted quantity must be non-negative with up to four decimals.");
        await InventoryManagement.SerialAvailable(db, actor.Branch, serial);
        var matches = await db.Movements.Where(x => x.Branch == actor.Branch && x.Location == input.Location && x.ProductId == product.Id && x.Serial == serial && x.Batch == batch && x.Expiry == input.Expiry).ToListAsync();
        var available = matches.Sum(x => x.Quantity); var value = matches.Sum(x => x.Value);
        if (available != input.ExpectedQuantity) throw new BusinessException("Stock changed since this count was prepared. Refresh and recount before posting.");
        if (input.CountedQuantity < await CommerceRules.Reserved(db, actor.Branch, product.Id, input.Location, serial, batch, input.Expiry)) throw new BusinessException("Release reservations before reducing stock below reserved quantities.");
        var difference = input.CountedQuantity - available; if (difference == 0) throw new BusinessException("The count matches the book quantity. No adjustment is needed.");
        if (serial is not null && difference > 0 && await db.Movements.AnyAsync(x => x.Serial == serial)) throw new BusinessException("Serial already exists in stock history. Use a transfer, return or reversal instead of a surplus count.");
        decimal adjustment;
        if (difference > 0) { Rules.Positive(input.SurplusUnitCost, "Surplus unit cost"); adjustment = AccountingEngine.Money(difference * input.SurplusUnitCost); }
        else adjustment = input.CountedQuantity == 0 ? -value : -AccountingEngine.Money(-difference * value / available);
        var doc = Document(actor, input.OperationId, DocumentKind.StockCount, input.Reason, input); doc.Total = adjustment; db.Documents.Add(doc);
        db.Movements.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, ProductId = product.Id, Branch = actor.Branch, Location = input.Location, Serial = serial, Batch = batch, Expiry = input.Expiry, Quantity = difference, Value = adjustment });
        if (adjustment != 0)
        {
            db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, Account = Account.Inventory, Debit = Math.Max(0, adjustment), Credit = Math.Max(0, -adjustment) });
            db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, Account = (Account)5100, Debit = Math.Max(0, -adjustment), Credit = Math.Max(0, adjustment) });
        }
        return doc.Id;
    }
    private static async Task Location(BusinessDb db, string code) { if (!await db.StockLocations.AnyAsync(x => x.Code == code)) throw new BusinessException("Warehouse not found."); }
    private BusinessDocument Document<T>(Actor actor, Guid operation, DocumentKind kind, string reason, T input) => new() { Id = Guid.NewGuid(), Number = $"{actor.Branch}-{options.Value.Device}-I-{operation:N}", Branch = actor.Branch, Kind = kind, Party = Rules.Required(reason, "Reason"), BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow), Settlement = "Inventory control", Detail = JsonSerializer.Serialize(input) };
    private async Task<Guid> Execute<T>(Guid operation, string action, T input, Func<BusinessDb, Actor, Task<Guid>> work)
    {
        var actor = await Authorize(true);
        if (operation == Guid.Empty) throw new BusinessException("Operation ID is required."); var payload = JsonSerializer.Serialize(input); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + action + payload)));
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.Operations.SingleOrDefaultAsync(x => x.Id == operation); if (previous is not null) { if (hash != previous.Fingerprint) throw new BusinessException("Operation ID already used with different data or identity."); return previous.ResultId; }
        if (DateOnly.FromDateTime(DateTime.UtcNow) <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough)) throw new BusinessException("Current accounting period is closed.");
        var id = await work(db, actor); db.Operations.Add(new() { Id = operation, ResultId = id, Branch = actor.Branch, Actor = actor.Id, Action = action, Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = hash }); await db.SaveChangesAsync(); await tx.CommitAsync(); return id;
    }
}

