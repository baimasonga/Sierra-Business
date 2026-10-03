using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed class ProductBarcode { public Guid Id { get; set; } public Guid ProductId { get; set; } public string Code { get; set; } = ""; public decimal Conversion { get; set; } }
public sealed class HeldSale { public Guid Id { get; set; } public string Branch { get; set; } = ""; public string Cashier { get; set; } = ""; public string Label { get; set; } = ""; public DateTimeOffset Time { get; set; } public string Payload { get; set; } = ""; }
public sealed class HeldSaleEnd { public Guid Id { get; set; } public Guid HeldSaleId { get; set; } public Guid? DocumentId { get; set; } public string Reason { get; set; } = ""; }
public sealed class PosService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IPosService
{
    private async Task<Actor> Authorize(string permission) { var actor = await session.CurrentActor(); actor.Demand(permission); if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException(); return actor; }
    public async Task<IReadOnlyList<BarcodeView>> Barcodes() { await Authorize("business.read"); await using var db = await factory.CreateDbContextAsync(); return (await InventoryManagement.ReadBarcodes(db)).Where(x => !x.Retired).Select(x => new BarcodeView(x.Id, x.ProductId, x.Code, x.Conversion)).ToArray(); }
    public Task<Guid> AddBarcode(BarcodeCommand input) => Execute(input.OperationId, "catalogue.write", "barcode.add", input, async (db, actor) => {
        var code = Rules.Required(input.Code, "Barcode", 80).ToUpperInvariant(); if (code.Any(char.IsControl) || code.Any(char.IsWhiteSpace)) throw new BusinessException("Barcode must not contain spaces or control characters."); Rules.Positive(input.Conversion, "Base units per scan");
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == input.ProductId) ?? throw new BusinessException("Product not found.");
        if (product.Tracking == TrackingMode.Serial && input.Conversion != 1) throw new BusinessException("A serial-controlled barcode must represent one base unit.");
        if (await db.ProductBarcodes.AnyAsync(x => x.Code == code) || await db.Products.AnyAsync(x => x.Sku == code)) throw new BusinessException("This barcode or SKU is already assigned. SKU scanning is available without a barcode mapping.");
        var record = new ProductBarcode { Id = Guid.NewGuid(), ProductId = product.Id, Code = code, Conversion = input.Conversion }; db.ProductBarcodes.Add(record); return record.Id;
    });
    public async Task<IReadOnlyList<HeldSaleView>> HeldSales() { var actor = await Authorize("sales.create"); await using var db = await factory.CreateDbContextAsync(); var records = await db.HeldSales.AsNoTracking().Where(x => x.Branch == actor.Branch && !db.HeldSaleEnds.Any(e => e.HeldSaleId == x.Id)).ToListAsync(); return records.OrderByDescending(x => x.Time).Select(x => new HeldSaleView(x.Id, x.Label, x.Cashier, x.Time, JsonSerializer.Deserialize<TradeCommand>(x.Payload)!)).ToArray(); }
    public Task<Guid> Hold(HoldSaleCommand input) => Execute(input.OperationId, "sales.create", "sale.hold", input, async (db, actor) => {
        var draft = input.Draft ?? throw new BusinessException("Sale draft required.");
        if (draft.Kind != DocumentKind.Sale || draft.OrderId is not null || draft.DeliveryId is not null || draft.HeldSaleId is not null || draft.Lines is null || draft.Lines.Count is < 1 or > 200) throw new BusinessException("Hold a counter sale with 1–200 lines; linked orders/deliveries use their own workflows.");
        Rules.Required(draft.Party, "Customer"); if (draft.Payment is not null && !Enum.IsDefined(draft.Payment.Value)) throw new BusinessException("Unknown payment method.");
        if (!await db.StockLocations.AnyAsync(x => x.Code == draft.Location)) throw new BusinessException("Warehouse not found.");
        foreach (var line in draft.Lines) { Rules.Positive(line.Quantity, "Quantity"); Rules.Positive(line.UnitPrice, "Agreed price"); Rules.Positive(line.Conversion, "Conversion"); if (!await db.Products.AnyAsync(x => x.Id == line.ProductId)) throw new BusinessException("Product not found."); }
        var payload = JsonSerializer.Serialize(draft); if (payload.Length > 100_000) throw new BusinessException("Sale draft is too large.");
        if (input.ReplaceId is not null) { await Active(db, actor, input.ReplaceId.Value); db.HeldSaleEnds.Add(new() { Id = Guid.NewGuid(), HeldSaleId = input.ReplaceId.Value, Reason = "Replaced by updated held sale" }); }
        var record = new HeldSale { Id = Guid.NewGuid(), Branch = actor.Branch, Cashier = actor.Id, Time = DateTimeOffset.UtcNow, Label = Rules.Required(input.Label, "Hold label", 100), Payload = payload }; db.HeldSales.Add(record); return record.Id;
    });
    public Task<Guid> Cancel(CancelHeldSaleCommand input) => Execute(input.OperationId, "sales.create", "sale.hold.cancel", input, async (db, actor) => { await Active(db, actor, input.HeldSaleId); var end = new HeldSaleEnd { Id = Guid.NewGuid(), HeldSaleId = input.HeldSaleId, Reason = Rules.Required(input.Reason, "Reason") }; db.HeldSaleEnds.Add(end); return end.Id; });
    internal static async Task Active(BusinessDb db, Actor actor, Guid id) { if (!await db.HeldSales.AnyAsync(x => x.Id == id && x.Branch == actor.Branch) || await db.HeldSaleEnds.AnyAsync(x => x.HeldSaleId == id)) throw new BusinessException("This held sale was already completed, replaced or cancelled. Refresh the held-sale list."); }
    private async Task<Guid> Execute<T>(Guid operation, string permission, string action, T input, Func<BusinessDb, Actor, Task<Guid>> work)
    {
        var actor = await Authorize(permission); if (operation == Guid.Empty) throw new BusinessException("Operation ID is required."); var payload = JsonSerializer.Serialize(input); var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + action + payload)));
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable); var prior = await db.Operations.SingleOrDefaultAsync(x => x.Id == operation); if (prior is not null) { if (prior.Fingerprint != fingerprint) throw new BusinessException("Operation ID already used with different data or identity."); return prior.ResultId; }
        var id = await work(db, actor); db.Operations.Add(new() { Id = operation, ResultId = id, Branch = actor.Branch, Actor = actor.Id, Action = action, Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = fingerprint }); await db.SaveChangesAsync(); await tx.CommitAsync(); return id;
    }
}
