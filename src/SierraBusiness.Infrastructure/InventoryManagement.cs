using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

// Append-only revisions keep management changes auditable without changing posted stock.
public sealed class InventoryRevision
{
    public Guid Id { get; set; }
    public string Branch { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Key { get; set; } = "";
    public int Version { get; set; }
    public string Payload { get; set; } = "";
}

public sealed class InventoryManagement(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IInventoryManagement
{
    private async Task<Actor> Authorize(string permission = "business.read")
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read"); actor.Demand(permission);
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException();
        return actor;
    }
    internal static async Task<T?> Latest<T>(BusinessDb db, string branch, string kind, string key) where T : class
    {
        var row = await db.InventoryRevisions.AsNoTracking().Where(x => x.Branch == branch && x.Kind == kind && x.Key == key).OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        return row is null ? null : JsonSerializer.Deserialize<T>(row.Payload);
    }
    private static async Task Append<T>(BusinessDb db, string branch, string kind, string key, int expected, T data)
    {
        var version = await db.InventoryRevisions.Where(x => x.Branch == branch && x.Kind == kind && x.Key == key).MaxAsync(x => (int?)x.Version) ?? 0;
        if (version != expected) throw new BusinessException("This record changed. Refresh before saving.");
        db.InventoryRevisions.Add(new() { Id = Guid.NewGuid(), Branch = branch, Kind = kind, Key = key, Version = version + 1, Payload = JsonSerializer.Serialize(data) });
    }
    internal static async Task Active(BusinessDb db, string branch, Guid product)
    {
        if (await Latest<ProductSettings>(db, branch, "product", product.ToString()) is { Active: false }) throw new BusinessException("This product is inactive. Reactivate it in Product settings before trading.");
    }
    internal static async Task SerialAvailable(BusinessDb db, string branch, string? serial)
    {
        if (serial is not null && await Latest<SerialServiceState>(db, branch, "serial", serial) is { InService: true }) throw new BusinessException("This serial is in service. Release it in the serial register before moving or selling it.");
    }
    public Task<Guid> SetSerialService(SerialServiceCommand input) => Execute(input.OperationId, "inventory.receive", "inventory.serial.service", input, async (db, actor) =>
    {
        actor.Demand("documents.reverse"); Rules.Required(input.Reason, "Service reason");
        var serial = Rules.Required(input.Serial, "Serial", 100).ToUpperInvariant();
        var rows = await db.Movements.Where(x => x.Branch == actor.Branch && x.ProductId == input.ProductId && x.Serial == serial).ToListAsync();
        if (rows.Sum(x => x.Quantity) != 1) throw new BusinessException("Only an on-hand serial can enter or leave service.");
        var current = await Latest<SerialServiceState>(db, actor.Branch, "serial", serial);
        if ((current?.InService ?? false) == input.InService) throw new BusinessException("This serial is already in the requested state.");
        foreach (var location in rows.GroupBy(x => x.Location))
            if (await CommerceRules.Reserved(db, actor.Branch, input.ProductId, location.Key, serial, null, null) > 0) throw new BusinessException("Release this serial's reservation before placing it in service.");
        await Append(db, actor.Branch, "serial", serial, input.ExpectedVersion, new SerialServiceState(input.ProductId, serial, input.InService, input.ExpectedVersion + 1)); return input.ProductId;
    });
    public async Task<IReadOnlyList<SerialRegisterView>> Serials()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var moves = await db.Movements.AsNoTracking().Where(x => x.Branch == actor.Branch && x.Serial != null).ToListAsync();
        var documents = await db.Documents.AsNoTracking().Where(x => x.Branch == actor.Branch).ToDictionaryAsync(x => x.Id);
        var ops = await db.Operations.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync();
        var result = new List<SerialRegisterView>();
        foreach (var serial in moves.GroupBy(x => new { x.ProductId, x.Serial }))
        {
            var state = await Latest<SerialServiceState>(db, actor.Branch, "serial", serial.Key.Serial!);
            var quantity = serial.Sum(x => x.Quantity); var location = serial.GroupBy(x => x.Location).FirstOrDefault(x => x.Sum(m => m.Quantity) > 0)?.Key ?? "";
            var effective = serial.Where(x => documents[x.DocumentId].Kind != DocumentKind.Reversal && !documents.Values.Any(d => d.Reverses == x.DocumentId)).ToList();
            var latest = effective.OrderByDescending(x => ops.FirstOrDefault(o => o.ResultId == x.DocumentId)?.Time ?? DateTimeOffset.MinValue).FirstOrDefault();
            var kind = latest is null ? "" : documents[latest.DocumentId].Kind.ToString();
            var status = quantity == 0 ? (kind is "Sale" or "Delivery" ? "Sold" : "Retired") : state?.InService == true ? "InService" : await CommerceRules.Reserved(db, actor.Branch, serial.Key.ProductId, location, serial.Key.Serial, null, null) > 0 ? "Reserved" : kind == "StockTransfer" ? "Transferred" : kind == "SalesReturn" ? "Returned" : "InStock";
            result.Add(new(serial.Key.ProductId, serial.Key.Serial!, location, status, serial.Sum(x => x.Value), state?.Version ?? 0));
        }
        return result.OrderBy(x => x.Serial).ToArray();
    }
    private static string BatchKey(Guid product, string batch, DateOnly? expiry) => $"{product}/{batch}/{expiry:yyyy-MM-dd}";
    public Task<Guid> SaveBatch(SaveBatchMetadata input) => Execute(input.OperationId, "inventory.receive", "inventory.batch.metadata", input, async (db, actor) =>
    {
        var m = input.Metadata ?? throw new BusinessException("Batch metadata required."); var batch = Rules.Required(m.Batch, "Batch", 100).ToUpperInvariant();
        if (!await db.Movements.AnyAsync(x => x.Branch == actor.Branch && x.ProductId == m.ProductId && x.Batch == batch && x.Expiry == m.Expiry)) throw new BusinessException("Record this batch's stock before adding metadata.");
        if (m.Manufactured > m.Expiry || m.Manufactured > DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("Manufacture date must not be after expiry or today.");
        if (m.SupplierLot is null || m.SupplierLot.Length > 100 || m.Notes is null || m.Notes.Length > 2000) throw new BusinessException("Supplier lot is limited to 100 characters and notes to 2000.");
        await Append(db, actor.Branch, "batch", BatchKey(m.ProductId, batch, m.Expiry), m.Version, m with { Batch = batch, Version = m.Version + 1 }); return m.ProductId;
    });
    public async Task<IReadOnlyList<BatchRegisterView>> Batches()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var rows = await (from m in db.Movements.AsNoTracking() join d in db.Documents on m.DocumentId equals d.Id where m.Branch == actor.Branch && m.Batch != null select new { m, d.BusinessDate }).ToListAsync();
        var result = new List<BatchRegisterView>();
        foreach (var lot in rows.GroupBy(x => new { x.m.ProductId, x.m.Batch, x.m.Expiry, x.m.Location }))
        {
            var key = lot.Key; var metadata = await Latest<BatchMetadata>(db, actor.Branch, "batch", BatchKey(key.ProductId, key.Batch!, key.Expiry)) ?? new BatchMetadata(key.ProductId, key.Batch!, key.Expiry);
            var received = lot.Where(x => x.m.Quantity > 0).Select(x => (DateOnly?)x.BusinessDate).Min();
            result.Add(new(metadata, key.Location, lot.Sum(x => x.m.Quantity), await CommerceRules.Reserved(db, actor.Branch, key.ProductId, key.Location, null, key.Batch, key.Expiry), lot.Sum(x => x.m.Value), received, key.Expiry < DateOnly.FromDateTime(DateTime.UtcNow)));
        }
        return result.OrderBy(x => x.Metadata.Expiry is null).ThenBy(x => x.Metadata.Expiry).ThenBy(x => x.Metadata.Batch).ToArray();
    }
    public async Task<IReadOnlyList<ProductSettings>> Products()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var rows = await db.InventoryRevisions.AsNoTracking().Where(x => x.Branch == actor.Branch && x.Kind == "product").ToListAsync();
        var latest = rows.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => JsonSerializer.Deserialize<ProductSettings>(x.MaxBy(r => r.Version)!.Payload)!);
        return (await db.Products.AsNoTracking().ToListAsync()).Select(x => latest.GetValueOrDefault(x.Id.ToString()) ?? new ProductSettings(x.Id)).ToArray();
    }
    public Task<Guid> SaveProduct(SaveProductSettings input) => Execute(input.OperationId, "catalogue.write", "inventory.product", input, async (db, actor) =>
    {
        var s = input.Settings ?? throw new BusinessException("Product settings required.");
        var product = await db.Products.FindAsync(s.ProductId) ?? throw new BusinessException("Product not found.");
        Rules.Positive(s.PurchaseConversion, "Purchase conversion"); Rules.Positive(s.SalesConversion, "Sales conversion");
        if (product.Tracking == TrackingMode.Serial && (s.PurchaseConversion != 1 || s.SalesConversion != 1)) throw new BusinessException("Serial units must have a conversion of one.");
        if (string.IsNullOrWhiteSpace(s.PurchaseUnit) && s.PurchaseConversion != 1 || string.IsNullOrWhiteSpace(s.SalesUnit) && s.SalesConversion != 1) throw new BusinessException("Name each alternative unit.");
        foreach (var value in new[] { s.Brand, s.Model, s.Family, s.PurchaseUnit, s.SalesUnit }) if (value is null || value.Length > 100) throw new BusinessException("Names must contain at most 100 characters.");
        if (s.Attributes is null || s.Compatibility is null || s.Attributes.Length > 2000 || s.Compatibility.Length > 2000) throw new BusinessException("Variant and compatibility details are limited to 2000 characters.");
        await Append(db, actor.Branch, "product", s.ProductId.ToString(), s.Version, s with { Version = s.Version + 1 }); return s.ProductId;
    });
    public Task<Guid> SaveReorder(SaveReorderRule input) => Execute(input.OperationId, "catalogue.write", "inventory.reorder", input, async (db, actor) =>
    {
        var r = input.Rule ?? throw new BusinessException("Reorder rule required.");
        if (!await db.Products.AnyAsync(x => x.Id == r.ProductId) || !await db.StockLocations.AnyAsync(x => x.Code == r.Location)) throw new BusinessException("Select a product and warehouse.");
        if (r.Minimum < 0 || r.Target < r.Minimum || r.Target > 1_000_000_000 || decimal.Round(r.Minimum, 4) != r.Minimum || decimal.Round(r.Target, 4) != r.Target) throw new BusinessException("Use non-negative reorder quantities with up to four decimals and a target at least equal to the minimum.");
        if (r.SupplierId is not null && !await db.Parties.AnyAsync(x => x.Id == r.SupplierId && x.Branch == actor.Branch && (x.Type == PartyType.Supplier || x.Type == PartyType.Both))) throw new BusinessException("Preferred supplier must be a supplier in this branch.");
        await Append(db, actor.Branch, "reorder", $"{r.ProductId}/{r.Location}", r.Version, r with { Version = r.Version + 1 }); return r.ProductId;
    });
    public async Task<IReadOnlyList<ReorderView>> Reorder()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var rows = await db.InventoryRevisions.AsNoTracking().Where(x => x.Branch == actor.Branch && x.Kind == "reorder").ToListAsync();
        var stock = await db.Movements.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync(); var result = new List<ReorderView>(); var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var group in rows.GroupBy(x => x.Key))
        {
            var r = JsonSerializer.Deserialize<ReorderRule>(group.MaxBy(x => x.Version)!.Payload)!;
            var lots = stock.Where(x => x.ProductId == r.ProductId && x.Location == r.Location).GroupBy(x => new { x.Serial, x.Batch, x.Expiry });
            decimal onHand = 0, expired = 0, reserved = 0;
            foreach (var lot in lots)
            {
                var qty = lot.Sum(x => x.Quantity); onHand += qty;
                if (lot.Key.Expiry < today) expired += qty;
                else reserved += await CommerceRules.Reserved(db, actor.Branch, r.ProductId, r.Location, lot.Key.Serial, lot.Key.Batch, lot.Key.Expiry);
            }
            var available = onHand - expired - reserved;
            var active = await Latest<ProductSettings>(db, actor.Branch, "product", r.ProductId.ToString());
            result.Add(new(r, onHand, reserved, expired, active?.Active != false && available <= r.Minimum ? Math.Max(0, r.Target - available) : 0));
        }
        return result;
    }
    public async Task<IReadOnlyList<CountSessionView>> Counts()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var rows = await db.InventoryRevisions.AsNoTracking().Where(x => x.Branch == actor.Branch && x.Kind == "count").ToListAsync();
        return rows.GroupBy(x => x.Key).Select(x => JsonSerializer.Deserialize<CountSessionView>(x.MaxBy(r => r.Version)!.Payload)!).OrderByDescending(x => x.Time).ToArray();
    }
    public Task<Guid> SubmitCount(CountSessionCommand input) => Execute(input.OperationId, "inventory.receive", "inventory.count.submit", input, async (db, actor) =>
    {
        if (input.Lines is null || input.Lines.Count is < 1 or > 200 || input.Lines.Any(x => x is null)) throw new BusinessException("A count session needs 1–200 lines.");
        var lines = input.Lines.Select(x => x with { Serial = string.IsNullOrWhiteSpace(x.Serial) ? null : x.Serial.Trim().ToUpperInvariant(), Batch = string.IsNullOrWhiteSpace(x.Batch) ? null : x.Batch.Trim().ToUpperInvariant() }).ToArray();
        if (lines.Select(x => (x.ProductId, x.Location, x.Serial, x.Batch, x.Expiry)).Distinct().Count() != lines.Length || lines.Select(x => x.OperationId).Distinct().Count() != lines.Length || lines.Any(x => x.OperationId == Guid.Empty)) throw new BusinessException("Each count identity and line operation must be unique.");
        foreach (var line in lines)
        {
            if (!await db.Products.AnyAsync(x => x.Id == line.ProductId) || !await db.StockLocations.AnyAsync(x => x.Code == line.Location)) throw new BusinessException("Unknown count product or warehouse.");
            if (line.CountedQuantity < 0 || line.CountedQuantity > 1_000_000_000 || decimal.Round(line.CountedQuantity, 4) != line.CountedQuantity) throw new BusinessException("Invalid counted quantity.");
            Rules.Required(line.Reason, "Count reason");
        }
        var id = Guid.NewGuid(); await Append(db, actor.Branch, "count", id.ToString(), 0, new CountSessionView(id, Rules.Required(input.Name, "Session name"), actor.Id, DateTimeOffset.UtcNow, "Pending approval", lines, [], "")); return id;
    });
    public Task<Guid> DecideCount(CountDecision input) => Execute(input.OperationId, "documents.reverse", "inventory.count.decide", input, async (db, actor) =>
    {
        actor.Demand("inventory.receive"); var reason = Rules.Required(input.Reason, "Decision reason");
        var count = await Latest<CountSessionView>(db, actor.Branch, "count", input.SessionId.ToString()) ?? throw new BusinessException("Count session not found.");
        if (count.Status != "Pending approval") throw new BusinessException("This count session was already decided.");
        var documents = new List<Guid>();
        if (input.Approve)
        {
            if (DateOnly.FromDateTime(DateTime.UtcNow) <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough)) throw new BusinessException("Current accounting period is closed.");
            var inventory = new InventoryService(factory, session, options);
            foreach (var line in count.Lines)
            {
                if (line.ExpectedQuantity == line.CountedQuantity)
                {
                    var rows = await db.Movements.Where(x => x.Branch == actor.Branch && x.ProductId == line.ProductId && x.Location == line.Location && x.Serial == line.Serial && x.Batch == line.Batch && x.Expiry == line.Expiry).ToListAsync();
                    if (rows.Sum(x => x.Quantity) != line.ExpectedQuantity) throw new BusinessException("Stock changed since this count was prepared. Refresh and recount.");
                    continue;
                }
                documents.Add(await inventory.CountWithin(db, actor, line));
                await db.SaveChangesAsync(); // Still inside the outer transaction: later failures roll every line back.
            }
        }
        await Append(db, actor.Branch, "count", count.Id.ToString(), 1, count with { Status = input.Approve ? "Approved" : "Rejected", Documents = documents, DecisionReason = reason }); return count.Id;
    });
    internal static async Task<IReadOnlyList<ManagedBarcode>> ReadBarcodes(BusinessDb db)
    {
        var rows = await db.InventoryRevisions.AsNoTracking().Where(x => x.Branch == "*" && x.Kind == "barcode").ToListAsync();
        var latest = rows.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => JsonSerializer.Deserialize<ManagedBarcode>(x.MaxBy(r => r.Version)!.Payload)!);
        return (await db.ProductBarcodes.AsNoTracking().OrderBy(x => x.Code).ToListAsync()).Select(x => latest.GetValueOrDefault(x.Id.ToString()) ?? new ManagedBarcode(x.Id, x.Code, x.ProductId, x.Conversion, 0, false)).ToArray();
    }
    public async Task<IReadOnlyList<ManagedBarcode>> Barcodes() { await Authorize(); await using var db = await factory.CreateDbContextAsync(); return await ReadBarcodes(db); }
    public Task<Guid> ChangeBarcode(BarcodeChange input) => Execute(input.OperationId, "catalogue.write", "inventory.barcode.change", input, async (db, actor) =>
    {
        Rules.Required(input.Reason, "Change reason"); Rules.Positive(input.Conversion, "Conversion");
        var original = await db.ProductBarcodes.FindAsync(input.BarcodeId) ?? throw new BusinessException("Barcode not found.");
        var product = await db.Products.FindAsync(input.ProductId) ?? throw new BusinessException("Product not found.");
        if (product.Tracking == TrackingMode.Serial && input.Conversion != 1) throw new BusinessException("Serial barcodes represent one base unit.");
        await Append(db, "*", "barcode", original.Id.ToString(), input.ExpectedVersion, new ManagedBarcode(original.Id, original.Code, product.Id, input.Conversion, input.ExpectedVersion + 1, input.Retired)); return original.Id;
    });
    public async Task<IReadOnlyList<InventoryTrace>> Trace(Guid productId)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var rows = await (from m in db.Movements.AsNoTracking() join d in db.Documents on m.DocumentId equals d.Id where m.Branch == actor.Branch && m.ProductId == productId select new { m, d }).ToListAsync();
        var ops = await db.Operations.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync();
        var counts = await Counts();
        return rows.OrderByDescending(x => x.d.BusinessDate).Select(x =>
        {
            var count = counts.FirstOrDefault(c => c.Documents.Contains(x.d.Id));
            var op = ops.FirstOrDefault(o => o.ResultId == x.d.Id) ?? (count is null ? null : ops.FirstOrDefault(o => o.ResultId == count.Id && o.Action == "inventory.count.decide"));
            return new InventoryTrace(x.d.Id, x.d.Number, x.d.BusinessDate, x.d.Kind.ToString(), x.m.ProductId, x.m.Location, x.m.Serial, x.m.Batch, x.m.Expiry, x.m.Quantity, x.m.Value, op?.Actor ?? "", op?.Time);
        }).ToArray();
    }
    private async Task<Guid> Execute<T>(Guid operation, string permission, string action, T input, Func<BusinessDb, Actor, Task<Guid>> work)
    {
        var actor = await Authorize(permission); if (operation == Guid.Empty) throw new BusinessException("Operation ID is required.");
        var payload = JsonSerializer.Serialize(input); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + action + payload)));
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var prior = await db.Operations.SingleOrDefaultAsync(x => x.Id == operation);
        if (prior is not null) { if (prior.Fingerprint != hash) throw new BusinessException("Operation ID already used with different data or identity."); return prior.ResultId; }
        var id = await work(db, actor); db.Operations.Add(new() { Id = operation, ResultId = id, Branch = actor.Branch, Actor = actor.Id, Action = action, Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = hash }); await db.SaveChangesAsync(); await tx.CommitAsync(); return id;
    }
}
