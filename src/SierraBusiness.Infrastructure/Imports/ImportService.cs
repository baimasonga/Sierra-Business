using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure.Imports;

/// <summary>A validated upload. Staging only: business effects are created when it is committed.</summary>
public sealed class ImportBatch
{
    public Guid Id { get; set; }
    public string Branch { get; set; } = "";
    public ImportType Type { get; set; }
    public string FileName { get; set; } = "";
    public string FileSha256 { get; set; } = "";
    public string Sheet { get; set; } = "";
    public string HeadersJson { get; set; } = "[]";
    public string MappingJson { get; set; } = "{}";
    public string Key { get; set; } = "";
    public string Status { get; set; } = "Validated";
    public DateOnly? AsAt { get; set; }
    public string SessionName { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public string CommittedBy { get; set; } = "";
    public DateTimeOffset? Committed { get; set; }
    public int Total { get; set; }
    public int Errors { get; set; }
    public int Warnings { get; set; }
    public string SummaryJson { get; set; } = "";
    public string DocumentsJson { get; set; } = "[]";
}
public sealed class ImportRow
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public int RowNumber { get; set; }
    public string RawJson { get; set; } = "{}";
    public string ValuesJson { get; set; } = "{}";
    public string Status { get; set; } = "";
    public string MessagesJson { get; set; } = "[]";
    public Guid? EntityId { get; set; }
}

public sealed partial class ImportService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IImportService
{
    private const string Committed = "Committed", Validated = "Validated";
    private static ImportField F(string key, string label, bool required, string hint) => new(key, label, required, hint);
    private static readonly ImportField Sku = F("sku", "SKU", true, "Product code, exactly as in the catalogue.");
    private static readonly ImportField Warehouse = F("warehouse", "Warehouse", false, "Warehouse code. Leave empty for MAIN.");
    private static readonly ImportField UnitCost = F("unit_cost", "Unit cost", true, "Cost per base unit in NLe, without currency symbols.");
    private const string StockPermissions = "inventory.receive,accounting.write";
    private static readonly ImportTemplate[] All =
    [
        new(ImportType.Parties, "Customers and suppliers", "Create party masters with contact details, payment terms and credit limits.", "catalogue.write",
            [F("code","Party code",true,"Unique code, up to 40 characters."), F("name","Party name",true,"Unique name, up to 160 characters."), F("party_type","Party type",true,"Customer, Supplier or Both."), F("contact","Contact",false,"Main contact name."), F("phone","Phone",false,"Up to 60 characters."), F("email","Email",false,"Valid email address."), F("address","Address",false,"Up to 200 characters."), F("payment_days","Payment days",false,"0–3650, default zero."), F("credit_limit","Credit limit",false,"NLe, default zero, at most two decimal places."), F("category","Organisation type",false,"Optional, up to 40 characters."), F("tin","TIN",false,"Optional, up to 30 characters.")]),
        new(ImportType.Ledgers, "Ledger accounts", "Create custom accounts before importing vouchers. Control accounts are already supplied by the system.", "accounting.read,accounting.write",
            [F("account_code","Account code",true,"Custom code 6000–999999."), F("name","Account name",true,"Up to 160 characters."), F("group","Account type",true,"Asset, Liability, Equity, Income or Expense."), F("cash_bank","Cash or bank",false,"Yes or No. Only assets may be cash/bank accounts.")]),
        new(ImportType.Vouchers, "Accounting vouchers", "Import balanced vouchers, grouped by voucher reference. Each group is one journal, receipt, payment or contra. The batch commits together after every group passes domain checks.", "accounting.read,accounting.write",
            [F("voucher_key","Voucher reference",true,"Group key shared by all lines of one voucher."), F("date","Voucher date",true,"An open-period date, not in the future."), F("voucher_type","Voucher type",true,"Journal, Receipt, Payment or Contra."), F("narration","Narration",true,"Same narration on every line of the group."), F("account_code","Account code",true,"An existing ledger code."), F("debit","Debit",false,"NLe debit, at most two decimal places."), F("credit","Credit",false,"NLe credit, at most two decimal places."), F("cost_centre","Cost centre",false,"Income/expense allocation code."), F("bill_number","Allocated bill number",false,"Existing customer/supplier credit bill number, if settling trade controls."), F("bill_amount","Bill allocation amount",false,"Positive local amount against the bill.")]),
        new(ImportType.Products, "Product catalogue", "Create new products, with optional confidential minimum price and barcode.", "catalogue.write",
            [F("sku", "SKU", true, "Up to 40 characters. Must not already exist."), F("name", "Product name", true, "Up to 160 characters."), F("category", "Category", true, "For example Stationery."), F("unit", "Base unit", true, "Smallest unit you sell or count, e.g. piece, ream, box."),
             F("price", "Selling price", true, "Asking price per base unit in NLe."), F("tracking", "Tracking", false, "Quantity (default), Serial, Batch, or Batch+Expiry."),
             F("minimum_price", "Minimum price", false, "Confidential lowest price per base unit. Needs pricing permission. Without it the product cannot be sold until a manager sets one."),
             F("barcode", "Barcode", false, "Optional barcode printed on the item or pack."), F("barcode_units", "Units per barcode scan", false, "Base units one scan represents, e.g. 12 for a box of 12. Default 1.")]),
        new(ImportType.OpeningStock, "Opening stock", "Starting quantities and costs of quantity-tracked products, by warehouse. Posts inventory against opening balance equity.", StockPermissions,
            [Sku, Warehouse, F("quantity", "Quantity", true, "Quantity in the product's base unit."), UnitCost]),
        new(ImportType.SerialStock, "Serial inventory", "One row per serial number for serial-tracked products.", StockPermissions,
            [Sku, Warehouse, F("serial", "Serial number", true, "Each serial once; must not already exist in stock history."), UnitCost]),
        new(ImportType.BatchStock, "Batch and expiry inventory", "Opening quantities by batch, with expiry dates where the product requires them.", StockPermissions,
            [Sku, Warehouse, F("batch", "Batch", true, "Batch or lot number."), F("expiry", "Expiry date", false, "Required for Batch+Expiry products. dd/mm/yyyy or yyyy-mm-dd."), F("quantity", "Quantity", true, "Quantity in the product's base unit."), UnitCost]),
        new(ImportType.PriceUpdate, "Price update", "Change selling prices and/or confidential minimum prices of existing products.", "catalogue.write",
            [Sku, F("price", "New selling price", false, "Leave empty to keep the current price."), F("minimum_price", "New minimum price", false, "Leave empty to keep the current minimum. Needs pricing permission.")]),
        new(ImportType.StockCount, "Stock count", "A physical count sheet. Creates count sessions that a second authorised person approves before stock changes.", "inventory.receive",
            [Sku, Warehouse, F("serial", "Serial number", false, "Serial-tracked products: one row per serial, counted 1 (present) or 0 (missing)."), F("batch", "Batch", false, "Batch-tracked products."), F("expiry", "Expiry date", false, "Batch+Expiry products."),
             F("counted", "Counted quantity", true, "What was physically counted, in base units."), F("surplus_unit_cost", "Unit cost for surplus", false, "Needed when more is counted than the books show."), F("reason", "Reason", false, "Optional note, e.g. damaged.")])
    ];
    private static readonly Dictionary<string, string[]> Synonyms = new()
    {
        ["sku"] = ["itemcode", "productcode", "code", "item", "partnumber"], ["name"] = ["description", "productname", "itemname"], ["unit"] = ["uom", "unitofmeasure"],
        ["price"] = ["sellingprice", "retailprice", "salesprice", "newsellingprice"], ["minimum_price"] = ["minprice", "floorprice", "newminimumprice"], ["unit_cost"] = ["cost", "costprice", "purchaseprice"],
        ["quantity"] = ["qty", "stock", "onhand", "balance"], ["warehouse"] = ["location", "store", "branchstore"], ["serial"] = ["serialno", "sn", "imei"], ["batch"] = ["lot", "batchno", "lotnumber"],
        ["expiry"] = ["expirydate", "exp", "bestbefore", "expires"], ["counted"] = ["count", "countedqty", "physical", "physicalcount"], ["barcode_units"] = ["packsize", "unitsperpack"]
    };

    public IReadOnlyList<ImportTemplate> Templates() => All.OrderBy(t => (int)t.Type).ToArray();
    private static ImportTemplate Get(ImportType type) => All.SingleOrDefault(x => x.Type == type) ?? throw new BusinessException("Unknown import type.");
    private static bool IsStock(ImportType type) => type is ImportType.OpeningStock or ImportType.SerialStock or ImportType.BatchStock;

    private async Task<Actor> Authorize(ImportType? type = null)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        if (type is not null) foreach (var permission in Get(type.Value).Permission.Split(',')) actor.Demand(permission);
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Wrong branch.");
        return actor;
    }

    public async Task<byte[]> Template(ImportType type)
    {
        await Authorize(); var t = Get(type);
        var instructions = new List<IReadOnlyList<string>> { new[] { t.Title }, new[] { t.Description }, new[] { "" }, new[] { "Column", "Required", "What to enter" } };
        instructions.AddRange(t.Fields.Select(f => (IReadOnlyList<string>)[f.Label, f.Required ? "Yes" : "No", f.Hint]));
        instructions.Add([""]); instructions.Add(["Enter data on the Data sheet below the header row. Keep the header names, or match columns during import."]);
        instructions.Add(["Dates: dd/mm/yyyy or yyyy-mm-dd. Numbers: no currency symbols. Up to 10,000 rows and 10 MB per file. Save as .xlsx (not .xls or .xlsm)."]);
        return WorkbookWriter.Write([("Data", [t.Fields.Select(f => f.Label).ToArray()]), ("Instructions", instructions)]);
    }

    public async Task<IReadOnlyList<SheetInfo>> Inspect(string fileName, byte[] content)
    {
        await Authorize(); CheckFileName(fileName);
        return WorkbookReader.Read(content).Select(x => new SheetInfo(x.Name, x.Rows.Count, x.Headers)).ToArray();
    }
    private static void CheckFileName(string fileName)
    {
        if (!string.Equals(Path.GetExtension(fileName ?? ""), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Choose an Excel workbook saved as .xlsx. Older .xls files, .xlsm macro files and CSV files are not accepted; use Save As → Excel Workbook.");
    }

    private static string Norm(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    public IReadOnlyDictionary<string, string> SuggestMapping(ImportType type, IReadOnlyList<string> headers)
    {
        var result = new Dictionary<string, string>();
        foreach (var field in Get(type).Fields)
        {
            var names = new[] { field.Label, field.Key }.Concat(Synonyms.GetValueOrDefault(field.Key) ?? []).Select(Norm).ToHashSet();
            var match = headers.FirstOrDefault(h => names.Contains(Norm(h)) && !result.ContainsValue(h));
            if (match is not null) result[field.Key] = match;
        }
        return result;
    }

    // ---------------- validation ----------------

    private sealed class Line
    {
        public int Row;
        public Dictionary<string, string> Values = [];
        public readonly List<string> Errors = [], Warnings = [];
        public string Status => Errors.Count > 0 ? "Error" : Warnings.Count > 0 ? "Warning" : "Valid";
        public Product? Product; public string Location = "MAIN"; public string? Serial, Batch, Barcode; public DateOnly? Expiry;
        public decimal Quantity, Cost, BarcodeUnits = 1, Expected; public decimal? Price, Minimum, SurplusCost; public TrackingMode Tracking;
        public Guid? EntityId;
        public string V(string key) => Values.GetValueOrDefault(key) ?? "";
    }

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "yyyy/MM/dd", "d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy"];
    private static DateOnly? ParseDate(Line line, string key, string label)
    {
        var text = line.V(key); if (text.Length == 0) return null;
        if (DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)) { line.Values[key] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); return date; }
        line.Errors.Add($"{label} \"{text}\" is not a date. Use dd/mm/yyyy or yyyy-mm-dd."); return null;
    }
    private static decimal? ParseNumber(Line line, string key, string label, bool required, bool allowZero = false)
    {
        var text = line.V(key).Replace("NLe", "", StringComparison.OrdinalIgnoreCase).Replace("SLE", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (text.Length == 0) { if (required) line.Errors.Add($"{label} is required."); return null; }
        if (!decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var value)) { line.Errors.Add($"{label} \"{line.V(key)}\" is not a number."); return null; }
        if (value < 0 || (!allowZero && value == 0) || value > 1_000_000_000 || decimal.Round(value, 4) != value) { line.Errors.Add($"{label} must be {(allowZero ? "zero or more" : "more than zero")}, at most one billion, with up to four decimal places."); return null; }
        line.Values[key] = value.ToString("0.############################", CultureInfo.InvariantCulture); return value; // 10.0 and 10 are the same data
    }
    private static string? Text(Line line, string key, string label, bool required, int max)
    {
        var text = line.V(key).Trim();
        if (text.Length == 0) { if (required) line.Errors.Add($"{label} is required."); return null; }
        if (text.Length > max) { line.Errors.Add($"{label} must be at most {max} characters."); return null; }
        return text;
    }
    private static void Duplicates(IEnumerable<Line> lines, Func<Line, string?> key, string what)
    {
        foreach (var group in lines.Select(l => (Line: l, Key: key(l))).Where(x => !string.IsNullOrEmpty(x.Key)).GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var item in group) item.Line.Errors.Add($"{what} appears more than once in this file (rows {string.Join(", ", group.Select(x => x.Line.Row))}).");
    }
    private static TrackingMode? ParseTracking(Line line)
    {
        var text = Norm(line.V("tracking"));
        TrackingMode? mode = text switch
        {
            "" or "quantity" or "qty" or "none" => TrackingMode.Quantity, "serial" or "serialised" or "serialized" or "serialnumber" => TrackingMode.Serial,
            "batch" or "lot" => TrackingMode.Batch, "batchexpiry" or "expiry" or "batchandexpiry" or "lotexpiry" => TrackingMode.BatchExpiry,
            "variant" or "compatibility" => null, _ => null
        };
        if (mode is null) line.Errors.Add(text is "variant" or "compatibility" ? "Variant and compatibility tracking are not available. Use a separate quantity-tracked SKU for each variant." : $"Tracking \"{line.V("tracking")}\" is not recognised. Use Quantity, Serial, Batch or Batch+Expiry.");
        else line.Values["tracking"] = mode.ToString()!;
        return mode;
    }

    private async Task<List<Line>> Check(BusinessDb db, Actor actor, ImportType type, List<Line> lines)
    {
        foreach (var line in lines)
            foreach (var (key, value) in line.Values.ToArray())
            {
                if (value == WorkbookReader.FormulaWithoutValue) { line.Errors.Add($"The {Label(type, key)} cell holds a formula with no saved result. Open the file in Excel, save it, then upload again."); line.Values[key] = ""; }
                else if (value.StartsWith("#ERROR", StringComparison.Ordinal)) { line.Errors.Add($"The {Label(type, key)} cell contains an Excel error ({value[7..]})."); line.Values[key] = ""; }
            }
        var products = (await db.Products.ToListAsync()).ToDictionary(x => x.Sku, StringComparer.OrdinalIgnoreCase);
        var locations = (await db.StockLocations.AsNoTracking().Select(x => x.Code).ToListAsync()).ToDictionary(x => x, StringComparer.OrdinalIgnoreCase);
        var active = new Dictionary<Guid, string?>();
        async Task<Product?> FindProduct(Line line)
        {
            var sku = Text(line, "sku", "SKU", true, 40)?.ToUpperInvariant(); if (sku is null) return null; line.Values["sku"] = sku;
            if (!products.TryGetValue(sku, out var product)) { line.Errors.Add($"SKU {sku} is not in the catalogue. Import or create the product first."); return null; }
            if (!active.TryGetValue(product.Id, out var inactive)) { try { await InventoryManagement.Active(db, actor.Branch, product.Id); inactive = null; } catch (BusinessException ex) { inactive = ex.Message; } active[product.Id] = inactive; }
            if (inactive is not null && type != ImportType.PriceUpdate) line.Errors.Add($"{sku}: {inactive}");
            line.Product = product; return product;
        }
        void FindLocation(Line line)
        {
            var code = line.V("warehouse").Trim(); if (code.Length == 0) code = "MAIN";
            if (!locations.TryGetValue(code, out var stored)) { line.Location = code.ToUpperInvariant(); line.Errors.Add($"Warehouse {code} does not exist. Create it under Warehouse controls first."); }
            else { line.Location = stored; line.Values["warehouse"] = stored; }
        }
        switch (type)
        {
            case ImportType.Parties: await CheckParties(db, actor, lines); break;
            case ImportType.Ledgers: await CheckLedgers(db, lines); break;
            case ImportType.Vouchers: await CheckVouchers(db, actor, lines); break;
            case ImportType.Products:
            {
                var barcodes = (await db.ProductBarcodes.AsNoTracking().Select(x => x.Code).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines)
                {
                    var sku = Text(line, "sku", "SKU", true, 40)?.ToUpperInvariant();
                    if (sku is not null) { line.Values["sku"] = sku; if (products.ContainsKey(sku) || barcodes.Contains(sku)) line.Errors.Add($"SKU {sku} already exists. Use the Price update import to change existing products."); }
                    Text(line, "name", "Product name", true, 160); Text(line, "category", "Category", true, 160); Text(line, "unit", "Base unit", true, 30);
                    line.Price = ParseNumber(line, "price", "Selling price", true);
                    line.Tracking = ParseTracking(line) ?? TrackingMode.Quantity;
                    line.Minimum = ParseNumber(line, "minimum_price", "Minimum price", false);
                    if (line.Minimum is null && line.V("minimum_price").Length == 0) line.Warnings.Add("No minimum price: the product cannot be sold until a manager sets one under Sales controls.");
                    if (line.Minimum is not null && !actor.Permissions.Contains("pricing.manage")) line.Errors.Add("Setting minimum prices needs the pricing.manage permission. Remove the column or ask a manager to import.");
                    if (line.Minimum > line.Price) line.Warnings.Add("Minimum price is above the selling price.");
                    var barcode = Text(line, "barcode", "Barcode", false, 80)?.ToUpperInvariant();
                    if (barcode is not null)
                    {
                        line.Values["barcode"] = barcode; line.Barcode = barcode;
                        if (barcode.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) line.Errors.Add("Barcode must not contain spaces.");
                        if (barcodes.Contains(barcode) || products.ContainsKey(barcode)) line.Errors.Add($"Barcode {barcode} is already assigned to a product.");
                        line.BarcodeUnits = ParseNumber(line, "barcode_units", "Units per barcode scan", false) ?? 1;
                        if (line.Tracking == TrackingMode.Serial && line.BarcodeUnits != 1) line.Errors.Add("A serial-tracked product's barcode must represent one unit.");
                    }
                    else if (line.V("barcode_units").Length > 0) line.Warnings.Add("Units per barcode scan is ignored because no barcode is given.");
                }
                Duplicates(lines, l => l.Values.GetValueOrDefault("sku"), "This SKU");
                Duplicates(lines, l => l.Barcode, "This barcode");
                var skus = lines.Select(l => l.Values.GetValueOrDefault("sku")).Where(x => !string.IsNullOrEmpty(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Where(l => l.Barcode is not null && skus.Contains(l.Barcode))) line.Errors.Add($"Barcode {line.Barcode} is the same as a SKU in this file.");
                break;
            }
            case ImportType.OpeningStock or ImportType.SerialStock or ImportType.BatchStock:
            {
                var movements = await db.Movements.AsNoTracking().Where(x => x.Branch == actor.Branch).Select(x => new { x.ProductId, x.Location, x.Serial, x.Batch, x.Expiry, x.Quantity }).ToListAsync();
                var serials = (await db.Movements.AsNoTracking().Where(x => x.Serial != null).Select(x => x.Serial!).Distinct().ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                foreach (var line in lines)
                {
                    var product = await FindProduct(line); FindLocation(line);
                    line.Cost = ParseNumber(line, "unit_cost", "Unit cost", true) ?? 0;
                    if (type == ImportType.SerialStock)
                    {
                        line.Quantity = 1; line.Serial = Text(line, "serial", "Serial number", true, 100)?.ToUpperInvariant();
                        if (line.Serial is not null) { line.Values["serial"] = line.Serial; if (serials.Contains(line.Serial)) line.Errors.Add($"Serial {line.Serial} already exists in stock history. Use a return, transfer or count instead."); }
                        if (product is not null && product.Tracking != TrackingMode.Serial) line.Errors.Add($"{product.Sku} is not serial-tracked. Use the {(product.Tracking == TrackingMode.Quantity ? "Opening stock" : "Batch and expiry inventory")} template.");
                    }
                    else
                    {
                        line.Quantity = ParseNumber(line, "quantity", "Quantity", true) ?? 0;
                        if (type == ImportType.OpeningStock && product is not null && product.Tracking != TrackingMode.Quantity) line.Errors.Add($"{product.Sku} is {product.Tracking}-tracked. Use the {(product.Tracking == TrackingMode.Serial ? "Serial inventory" : "Batch and expiry inventory")} template.");
                        if (type == ImportType.BatchStock)
                        {
                            line.Batch = Text(line, "batch", "Batch", true, 100)?.ToUpperInvariant(); if (line.Batch is not null) line.Values["batch"] = line.Batch;
                            line.Expiry = ParseDate(line, "expiry", "Expiry date");
                            if (product is not null && product.Tracking is not (TrackingMode.Batch or TrackingMode.BatchExpiry)) line.Errors.Add($"{product.Sku} is not batch-tracked. Use the {(product.Tracking == TrackingMode.Serial ? "Serial inventory" : "Opening stock")} template.");
                            if (product?.Tracking == TrackingMode.BatchExpiry && line.Expiry is null && line.V("expiry").Length == 0) line.Errors.Add("Expiry date is required for this product.");
                            if (product?.Tracking == TrackingMode.Batch && line.Expiry is not null) line.Errors.Add("This product is tracked by batch without expiry; leave the expiry date empty.");
                            if (line.Expiry < today) line.Warnings.Add("This batch has expired. It will be recorded but cannot be sold.");
                        }
                    }
                    if (product is not null && line.Errors.Count == 0)
                    {
                        var existing = movements.Where(x => x.ProductId == product.Id && x.Location == line.Location && x.Serial == line.Serial && x.Batch == line.Batch && x.Expiry == line.Expiry).Sum(x => x.Quantity);
                        if (existing != 0) line.Warnings.Add($"{existing.ToString(CultureInfo.InvariantCulture)} {product.Unit} already recorded here; this adds to it.");
                    }
                }
                Duplicates(lines, l => l.Product is null ? null : $"{l.Product.Sku}|{l.Location}|{l.Serial}|{l.Batch}|{l.Expiry}", type == ImportType.SerialStock ? "This serial" : "This product and warehouse" + (type == ImportType.BatchStock ? " and batch" : ""));
                if (type == ImportType.SerialStock) Duplicates(lines, l => l.Serial, "This serial number");
                break;
            }
            case ImportType.PriceUpdate:
            {
                var policies = await db.PricePolicies.AsNoTracking().ToListAsync();
                foreach (var line in lines)
                {
                    var product = await FindProduct(line);
                    line.Price = ParseNumber(line, "price", "New selling price", false); line.Minimum = ParseNumber(line, "minimum_price", "New minimum price", false);
                    if (line.V("price").Length == 0 && line.V("minimum_price").Length == 0) line.Errors.Add("Give a new selling price, a new minimum price, or both.");
                    if (line.Minimum is not null && !actor.Permissions.Contains("pricing.manage")) line.Errors.Add("Changing minimum prices needs the pricing.manage permission.");
                    if (product is not null)
                    {
                        var minimum = line.Minimum ?? policies.Where(x => x.ProductId == product.Id).OrderByDescending(x => x.Version).FirstOrDefault()?.MinimumPrice;
                        if (minimum > (line.Price ?? product.Price)) line.Warnings.Add($"The selling price ({line.Price ?? product.Price:N2}) would be below the minimum ({minimum:N2}).");
                        if (line.Price == product.Price && line.Minimum is null) line.Warnings.Add("The selling price is unchanged.");
                    }
                }
                Duplicates(lines, l => l.Values.GetValueOrDefault("sku"), "This SKU");
                break;
            }
            case ImportType.StockCount:
            {
                var movements = await db.Movements.AsNoTracking().Where(x => x.Branch == actor.Branch).Select(x => new { x.ProductId, x.Location, x.Serial, x.Batch, x.Expiry, x.Quantity }).ToListAsync();
                foreach (var line in lines)
                {
                    var product = await FindProduct(line); FindLocation(line);
                    line.Serial = Text(line, "serial", "Serial number", false, 100)?.ToUpperInvariant(); if (line.Serial is not null) line.Values["serial"] = line.Serial;
                    line.Batch = Text(line, "batch", "Batch", false, 100)?.ToUpperInvariant(); if (line.Batch is not null) line.Values["batch"] = line.Batch;
                    line.Expiry = ParseDate(line, "expiry", "Expiry date");
                    line.Quantity = ParseNumber(line, "counted", "Counted quantity", true, allowZero: true) ?? 0;
                    line.SurplusCost = ParseNumber(line, "surplus_unit_cost", "Unit cost for surplus", false);
                    line.Values["reason"] = Text(line, "reason", "Reason", false, 160) ?? "Imported count";
                    if (product is null) continue;
                    if (product.Tracking == TrackingMode.Serial && (line.Serial is null || line.Quantity is not (0 or 1))) line.Errors.Add("Serial-tracked products need a serial number with a count of 1 (present) or 0 (missing).");
                    if (product.Tracking != TrackingMode.Serial && line.Serial is not null) line.Errors.Add($"{product.Sku} is not serial-tracked; remove the serial number.");
                    if (product.Tracking is TrackingMode.Batch or TrackingMode.BatchExpiry && line.Batch is null) line.Errors.Add("A batch is required for this product.");
                    if (product.Tracking is not (TrackingMode.Batch or TrackingMode.BatchExpiry) && (line.Batch is not null || line.Expiry is not null)) line.Errors.Add($"{product.Sku} is not batch-tracked; remove the batch and expiry.");
                    if (product.Tracking == TrackingMode.BatchExpiry && line.Expiry is null && line.V("expiry").Length == 0) line.Errors.Add("Expiry date is required for this product.");
                    line.Expected = movements.Where(x => x.ProductId == product.Id && x.Location == line.Location && x.Serial == line.Serial && x.Batch == line.Batch && x.Expiry == line.Expiry).Sum(x => x.Quantity);
                    if (line.Quantity > line.Expected && line.SurplusCost is null) line.Errors.Add($"More counted than recorded ({line.Expected.ToString(CultureInfo.InvariantCulture)}): give a unit cost for the surplus.");
                    if (line.Quantity != line.Expected) line.Warnings.Add($"Difference {(line.Quantity - line.Expected).ToString("+0.####;-0.####", CultureInfo.InvariantCulture)} {product.Unit} against the books.");
                }
                Duplicates(lines, l => l.Product is null ? null : $"{l.Product.Sku}|{l.Location}|{l.Serial}|{l.Batch}|{l.Expiry}", "This product, warehouse and identity");
                break;
            }
        }
        return lines;
    }
    private static string Label(ImportType type, string key) => Get(type).Fields.FirstOrDefault(x => x.Key == key)?.Label ?? key;

    private static string KeyFor(ImportType type, string branch, DateOnly? asAt, string sessionName, IEnumerable<Line> lines)
    {
        var canonical = JsonSerializer.Serialize(new { type, branch, asAt, sessionName, rows = lines.Select(l => l.Values.OrderBy(x => x.Key, StringComparer.Ordinal).Where(x => x.Value.Length > 0).Select(x => x.Key + "=" + x.Value)) });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    private static Guid Derived(string seed) => new(SHA256.HashData(Encoding.UTF8.GetBytes(seed)).AsSpan(0, 16));

    public async Task<ImportPreview> Validate(ImportValidateCommand input)
    {
        var actor = await Authorize(input.Type); CheckFileName(input.FileName);
        var template = Get(input.Type);
        var sheet = WorkbookReader.Read(input.Content, input.Sheet).Single();
        var mapping = new Dictionary<string, string>();
        foreach (var field in template.Fields)
        {
            var header = input.Mapping?.GetValueOrDefault(field.Key)?.Trim() ?? "";
            if (header.Length > 0 && !sheet.Headers.Contains(header)) throw new BusinessException($"The column \"{header}\" chosen for {field.Label} is not in this sheet.");
            if (header.Length == 0 && field.Required) throw new BusinessException($"Choose the column that holds {field.Label}.");
            if (header.Length > 0) mapping[field.Key] = header;
        }
        if (mapping.Values.GroupBy(x => x).Any(g => g.Count() > 1)) throw new BusinessException("Each column can be used for only one field.");
        if (sheet.Rows.Count == 0) throw new BusinessException("The sheet has no data rows below the header row.");
        await using var db = await factory.CreateDbContextAsync();
        DateOnly? asAt = null;
        if (IsStock(input.Type))
        {
            asAt = input.AsAt ?? throw new BusinessException("Choose the date the opening stock applies from.");
            if (asAt <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough) || asAt > DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("The opening stock date must be in an open accounting period and not in the future.");
        }
        var sessionName = input.Type == ImportType.StockCount ? (input.SessionName ?? "").Trim() is { Length: > 0 } name ? Rules.Required(name, "Count session name", 120) : $"Imported count {DateTime.UtcNow:yyyy-MM-dd}" : "";
        var index = sheet.Headers.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);
        var lines = sheet.Rows.Select(r => new Line { Row = r.Number, Values = mapping.ToDictionary(m => m.Key, m => r.Cells[index[m.Value]]) }).ToList();
        await Check(db, actor, input.Type, lines);
        if (IsStock(input.Type)) await CheckStockCostDates(db,actor,lines,asAt!.Value);
        var key = KeyFor(input.Type, actor.Branch, asAt, sessionName, lines);
        var previous = await db.ImportBatches.AsNoTracking().Where(x => x.Branch == actor.Branch && x.Key == key && x.Status == Committed).FirstOrDefaultAsync();

        // Uncommitted staging older than a day is discarded (SQLite cannot compare offsets in SQL, so filter in memory).
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        var stale = (await db.ImportBatches.Where(x => x.Branch == actor.Branch && x.CreatedBy == actor.Id && x.Status == Validated).ToListAsync()).Where(x => x.Created < cutoff).ToList();
        var staleIds = stale.Select(x => x.Id).ToList();
        db.ImportRows.RemoveRange(await db.ImportRows.Where(r => staleIds.Contains(r.BatchId)).ToListAsync()); db.ImportBatches.RemoveRange(stale);
        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(), Branch = actor.Branch, Type = input.Type, FileName = Path.GetFileName(input.FileName), FileSha256 = Convert.ToHexString(SHA256.HashData(input.Content)), Sheet = sheet.Name,
            HeadersJson = JsonSerializer.Serialize(sheet.Headers), MappingJson = JsonSerializer.Serialize(mapping), Key = key, AsAt = asAt, SessionName = sessionName, CreatedBy = actor.Id, Created = DateTimeOffset.UtcNow,
            Total = lines.Count, Errors = lines.Count(x => x.Errors.Count > 0), Warnings = lines.Count(x => x.Errors.Count == 0 && x.Warnings.Count > 0)
        };
        db.ImportBatches.Add(batch);
        var raw = sheet.Rows.ToDictionary(r => r.Number);
        db.ImportRows.AddRange(lines.Select(l => new ImportRow
        {
            Id = Guid.NewGuid(), BatchId = batch.Id, RowNumber = l.Row, Status = l.Status, ValuesJson = JsonSerializer.Serialize(l.Values),
            RawJson = JsonSerializer.Serialize(raw[l.Row].Cells), MessagesJson = JsonSerializer.Serialize(l.Errors.Concat(l.Warnings))
        }));
        await db.SaveChangesAsync();
        return new(batch.Id, input.Type, batch.Total, batch.Total - batch.Errors - batch.Warnings, batch.Warnings, batch.Errors,
            lines.Select(l => new ImportRowView(l.Row, l.Status, l.Values, l.Errors.Concat(l.Warnings).ToArray())).ToArray(),
            previous is null ? "" : $"This exact data was already imported on {previous.Committed:dd MMM yyyy HH:mm} UTC. Committing again will not create anything.");
    }

    public async Task<byte[]> ErrorWorkbook(Guid batchId)
    {
        var actor = await Authorize();
        await using var db = await factory.CreateDbContextAsync();
        var batch = await db.ImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == batchId && x.Branch == actor.Branch) ?? throw new BusinessException("Import not found.");
        var headers = JsonSerializer.Deserialize<List<string>>(batch.HeadersJson) ?? [];
        var rows = await db.ImportRows.AsNoTracking().Where(x => x.BatchId == batchId).OrderBy(x => x.RowNumber).ToListAsync();
        var data = new List<IReadOnlyList<string>> { headers.Concat(["Excel row", "Import status", "What to fix"]).ToArray() };
        data.AddRange(rows.Select(r => (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(r.RawJson) ?? []).Select(x => x == WorkbookReader.FormulaWithoutValue ? "" : x).Concat([r.RowNumber.ToString(CultureInfo.InvariantCulture), r.Status, string.Join(" ", JsonSerializer.Deserialize<List<string>>(r.MessagesJson) ?? [])]).ToArray()));
        var summary = new List<IReadOnlyList<string>> { new[] { "File", batch.FileName }, new[] { "Sheet", batch.Sheet }, new[] { "Import", Get(batch.Type).Title }, new[] { "Rows", batch.Total.ToString(CultureInfo.InvariantCulture) }, new[] { "Rows with errors", batch.Errors.ToString(CultureInfo.InvariantCulture) }, new[] { "Rows with warnings", batch.Warnings.ToString(CultureInfo.InvariantCulture) }, new[] { "Fix the rows marked Error on the Rows sheet, delete the three added columns, save as .xlsx and import again." } };
        return WorkbookWriter.Write([("Rows", data), ("Summary", summary)]);
    }

    // ---------------- commit ----------------

    public async Task<ImportCommitResult> Commit(ImportCommitCommand input)
    {
        if (input.OperationId == Guid.Empty) throw new BusinessException("An operation ID is required.");
        var actor = await Authorize();
        ImportBatch batch;
        await using (var lookup = await factory.CreateDbContextAsync())
            batch = await lookup.ImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.BatchId && x.Branch == actor.Branch) ?? throw new BusinessException("Import not found. Validate the file again.");
        actor = await Authorize(batch.Type);
        if (batch.Type == ImportType.StockCount) return await CommitCount(actor, batch, input.OperationId);

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var replay = await Replay(db, actor, batch, input.OperationId); if (replay is not null) return replay;
        batch = await db.ImportBatches.SingleAsync(x => x.Id == batch.Id);
        var (rows, lines) = await Recheck(db, actor, batch);
        var documents = new List<Guid>(); string summary;
        switch (batch.Type)
        {
            case ImportType.Parties:
                foreach (var line in lines) { var party = await PartyService.SaveCore(db, actor, PartyInput(line)); line.EntityId = party.Id; }
                summary = $"Created {lines.Count} customers/suppliers."; break;
            case ImportType.Ledgers:
                foreach (var line in lines) await FinanceService.CreateLedgerCore(db, LedgerInputFor(line));
                summary = $"Created {lines.Count} ledger accounts."; break;
            case ImportType.Vouchers:
                var finance = new FinanceService(factory, session, options);
                foreach (var group in lines.GroupBy(l => l.V("voucher_key")))
                {
                    var command = await VoucherInput(db, actor, group.ToList(), Derived(batch.Key + "|voucher|" + group.Key));
                    var id = await finance.PostVoucherCore(db, actor, command); documents.Add(id); foreach (var line in group) line.EntityId = id;
                    var payload = JsonSerializer.Serialize(command); var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + "voucher.post" + payload)));
                    db.Operations.Add(Audit(command.OperationId,id,actor,"voucher.post",command,fingerprint));
                    await db.SaveChangesAsync(); // Visible to later groups in this transaction; any failure rolls back the entire batch.
                }
                summary = $"Posted {documents.Count} balanced accounting vouchers."; break;
            case ImportType.Products:
            {
                foreach (var line in lines)
                {
                    var product = await BusinessService.CreateProduct(db, actor, new(Guid.Empty, line.V("sku"), line.V("name"), line.V("category"), line.V("unit"), line.Price!.Value, line.Tracking, line.Minimum), uniquenessVerified: true); // Recheck verified SKUs/barcodes in this transaction
                    if (line.Barcode is not null) db.ProductBarcodes.Add(new() { Id = Guid.NewGuid(), ProductId = product.Id, Code = line.Barcode, Conversion = line.BarcodeUnits });
                    line.EntityId = product.Id;
                }
                summary = $"Created {lines.Count} products ({lines.Count(x => x.Barcode is not null)} with barcodes; {lines.Count(x => x.Minimum is null)} still need a minimum price).";
                break;
            }
            case ImportType.PriceUpdate:
            {
                var changes = new List<object>();
                foreach (var line in lines)
                {
                    var product = line.Product!; var oldPrice = product.Price;
                    if (line.Price is not null) product.Price = line.Price.Value;
                    if (line.Minimum is not null)
                    {
                        var version = (await db.PricePolicies.Where(x => x.ProductId == product.Id).Select(x => (long?)x.Version).MaxAsync() ?? 0) + 1;
                        db.PricePolicies.Add(new() { Id = Guid.NewGuid(), ProductId = product.Id, MinimumPrice = line.Minimum.Value, Version = version });
                    }
                    changes.Add(new { product.Sku, OldPrice = oldPrice, NewPrice = line.Price, NewMinimum = line.Minimum is null ? (decimal?)null : line.Minimum });
                    line.EntityId = product.Id;
                }
                summary = $"Updated {lines.Count(x => x.Price is not null)} selling prices and {lines.Count(x => x.Minimum is not null)} minimum prices.";
                db.Operations.Add(Audit(Derived(batch.Key + "|prices"), batch.Id, actor, "import.price-changes", new { Batch = batch.Id, Changes = changes }, batch.Key));
                break;
            }
            default:
            {
                foreach (var group in lines.GroupBy(x => x.Location))
                {
                    var id = Derived(batch.Key + "|document|" + group.Key);
                    var items = group.Select(l => new { l.Product!.Sku, l.Serial, l.Batch, l.Expiry, l.Quantity, UnitCost = l.Cost, Value = AccountingEngine.Money(l.Quantity * l.Cost), Line = l }).ToList();
                    var total = items.Sum(x => x.Value);
                    var doc = new BusinessDocument
                    {
                        Id = id, Number = $"{actor.Branch}-{options.Value.Device}-OPEN-{id:N}", Branch = actor.Branch, Kind = DocumentKind.OpeningStock, Party = "Opening balance", BusinessDate = batch.AsAt!.Value, Total = total, Settlement = "Opening balance equity",
                        Detail = JsonSerializer.Serialize(new { Import = batch.Id, File = batch.FileName, Warehouse = group.Key, Lines = items.Select(x => new { x.Sku, x.Serial, x.Batch, x.Expiry, x.Quantity, x.UnitCost, x.Value }) })
                    };
                    db.Documents.Add(doc);
                    foreach (var item in items)
                    {
                        db.Movements.Add(new() { Id = Guid.NewGuid(), DocumentId = id, ProductId = item.Line.Product!.Id, Branch = actor.Branch, Location = group.Key, Serial = item.Serial, Batch = item.Batch, Expiry = item.Expiry, Quantity = item.Quantity, Value = item.Value });
                        item.Line.EntityId = id;
                    }
                    foreach (var posting in AccountingEngine.Balanced([new(Account.Inventory, total, 0), new((Account)3200, 0, total)]))
                        db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = id, Branch = actor.Branch, Account = posting.Account, Debit = posting.Debit, Credit = posting.Credit });
                    db.Operations.Add(Audit(Derived(batch.Key + "|operation|" + group.Key), id, actor, "import.opening-stock", new { Batch = batch.Id, Document = id, Warehouse = group.Key }, batch.Key));
                    documents.Add(id);
                }
                summary = $"Posted opening stock worth NLe {lines.Sum(x => AccountingEngine.Money(x.Quantity * x.Cost)):N2} in {documents.Count} warehouse document(s), dated {batch.AsAt:dd MMM yyyy}.";
                break;
            }
        }
        await Finish(db, actor, batch, rows, lines, documents, summary, input.OperationId);
        await tx.CommitAsync();
        return new(batch.Id, lines.Count, documents, false, summary);
    }

    private async Task<ImportCommitResult> CommitCount(Actor actor, ImportBatch batch, Guid operationId)
    {
        List<ImportRow> rows; List<Line> lines;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var replay = await Replay(db, actor, batch, operationId); if (replay is not null) return replay;
            (rows, lines) = await Recheck(db, actor, batch);
        }
        var management = new InventoryManagement(factory, session, options);
        var chunks = lines.Chunk(200).ToArray(); var sessions = new List<Guid>();
        for (var i = 0; i < chunks.Length; i++)
        {
            var name = chunks.Length == 1 ? batch.SessionName : $"{batch.SessionName} (part {i + 1} of {chunks.Length})";
            // Derived operation IDs make a retried commit reuse sessions that were already submitted.
            var id = await management.SubmitCount(new(Derived(batch.Key + "|count|" + i), name, chunks[i].Select(l => new StockCountCommand(Derived(batch.Key + "|line|" + l.Row), l.Product!.Id, l.Location, l.Expected, l.Quantity, l.SurplusCost ?? 0, l.V("reason"), l.Batch, l.Expiry, l.Serial)).ToArray()));
            sessions.Add(id); foreach (var l in chunks[i]) l.EntityId = id;
        }
        await using var write = await factory.CreateDbContextAsync();
        await using var tx = await write.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var again = await Replay(write, actor, batch, operationId); if (again is not null) return again;
        var tracked = await write.ImportBatches.SingleAsync(x => x.Id == batch.Id);
        var trackedRows = await write.ImportRows.Where(x => x.BatchId == batch.Id).OrderBy(x => x.RowNumber).ToListAsync();
        var summary = $"Created {sessions.Count} count session(s) with {lines.Count} lines, awaiting approval under Count sessions.";
        await Finish(write, actor, tracked, trackedRows, lines, sessions, summary, operationId);
        await tx.CommitAsync();
        return new(batch.Id, lines.Count, sessions, false, summary);
    }

    private async Task<(List<ImportRow> Rows, List<Line> Lines)> Recheck(BusinessDb db, Actor actor, ImportBatch batch)
    {
        var rows = await db.ImportRows.Where(x => x.BatchId == batch.Id).OrderBy(x => x.RowNumber).ToListAsync();
        var lines = rows.Select(r => new Line { Row = r.RowNumber, Values = JsonSerializer.Deserialize<Dictionary<string, string>>(r.ValuesJson) ?? [] }).ToList();
        await Check(db, actor, batch.Type, lines);
        if (IsStock(batch.Type)) await CheckStockCostDates(db,actor,lines,batch.AsAt!.Value);
        var failed = lines.Where(x => x.Errors.Count > 0).ToList();
        if (failed.Count > 0)
            throw new BusinessException(batch.Errors > 0
                ? $"{failed.Count} row(s) have errors. Download the error workbook, fix the rows marked Error and import again."
                : $"Records changed since this file was validated (row {failed[0].Row}: {failed[0].Errors[0]}). Validate the file again.");
        if (IsStock(batch.Type) && batch.AsAt <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough)) throw new BusinessException("The opening stock date is now in a closed period. Validate again with an open date.");
        return (rows, lines);
    }

    private static async Task<ImportCommitResult?> Replay(BusinessDb db, Actor actor, ImportBatch batch, Guid operationId)
    {
        var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == operationId);
        if (prior is not null && (prior.ResultId != batch.Id || prior.Actor != actor.Id)) throw new BusinessException("Operation ID was already used for a different import.");
        var done = await db.ImportBatches.AsNoTracking().Where(x => x.Branch == batch.Branch && x.Key == batch.Key && x.Status == Committed).FirstOrDefaultAsync();
        if (done is null) return null;
        return new(done.Id, done.Total, JsonSerializer.Deserialize<List<Guid>>(done.DocumentsJson) ?? [], true, $"Already imported on {done.Committed:dd MMM yyyy HH:mm} UTC; nothing was created again. {done.SummaryJson}");
    }

    private static Operation Audit(Guid id, Guid result, Actor actor, string action, object payload, string fingerprint) => new() { Id = id, ResultId = result, Branch = actor.Branch, Actor = actor.Id, Action = action, Time = DateTimeOffset.UtcNow, Payload = JsonSerializer.Serialize(payload), Fingerprint = fingerprint };

    private static async Task Finish(BusinessDb db, Actor actor, ImportBatch batch, List<ImportRow> rows, List<Line> lines, List<Guid> documents, string summary, Guid operationId)
    {
        batch.Status = Committed; batch.Committed = DateTimeOffset.UtcNow; batch.CommittedBy = actor.Id; batch.SummaryJson = summary; batch.DocumentsJson = JsonSerializer.Serialize(documents);
        foreach (var (row, line) in rows.Zip(lines)) { row.Status = Committed; row.EntityId = line.EntityId; }
        db.Operations.Add(Audit(operationId, batch.Id, actor, $"import.{batch.Type.ToString().ToLowerInvariant()}", new { Batch = batch.Id, batch.FileName, batch.FileSha256, batch.Sheet, Rows = lines.Count, Documents = documents, Summary = summary }, batch.Key));
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ImportHistoryView>> History()
    {
        var actor = await Authorize();
        await using var db = await factory.CreateDbContextAsync();
        var batches = await db.ImportBatches.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync();
        return batches.OrderByDescending(x => x.Committed ?? x.Created).Take(50).Select(x => new ImportHistoryView(x.Id, x.Type, x.FileName, x.Status, x.Created, x.CommittedBy.Length > 0 ? x.CommittedBy : x.CreatedBy, x.Total, x.Errors, x.Committed, x.SummaryJson)).ToArray();
    }
}
