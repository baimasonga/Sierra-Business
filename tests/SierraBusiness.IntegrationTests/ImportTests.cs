using System.Diagnostics;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.Infrastructure.Imports;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Specification Skill 10 acceptance criteria, plus security and consistency checks, on real workbooks and a real database.</summary>
public sealed class ImportTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-import-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private ImportService imports = null!; private BusinessService business = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        imports = new(factory, session, options); business = new(factory, options);
        await new InventoryService(factory, session, options).CreateLocation(new(Guid.NewGuid(), "SECOND", "Second warehouse"));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    private static byte[] Xlsx(params string[][] rows) => WorkbookWriter.Write([("Data", rows.Select(r => (IReadOnlyList<string>)r).ToArray())]);
    private async Task<ImportPreview> Validate(ImportType type, byte[] file, DateOnly? asAt = null, string name = "")
    {
        var sheet = (await imports.Inspect("stock.xlsx", file)).First();
        return await imports.Validate(new(type, "stock.xlsx", file, sheet.Name, imports.SuggestMapping(type, sheet.Headers), asAt ?? (type is ImportType.OpeningStock or ImportType.SerialStock or ImportType.BatchStock ? today : null), name));
    }
    private Task<ImportCommitResult> Commit(ImportPreview preview) => imports.Commit(new(Guid.NewGuid(), preview.BatchId));
    private async Task<Guid> Product(string sku, TrackingMode mode = TrackingMode.Quantity) => await business.AddProduct(session.Actor, new(Guid.NewGuid(), sku, sku, "Goods", "unit", 100, mode, MinimumPrice: 1));
    private async Task<BusinessSnapshot> Snapshot() => await business.Read(session.Actor);

    [Fact] public async Task EveryTemplateRoundTripsAndMapsItself()
    {
        foreach (var template in imports.Templates())
        {
            var sheets = await imports.Inspect("template.xlsx", await imports.Template(template.Type));
            Assert.Equal(["Data", "Instructions"], sheets.Select(x => x.Name));
            Assert.Equal(template.Fields.Select(x => x.Label), sheets[0].Headers);
            Assert.Equal(template.Fields.Count, imports.SuggestMapping(template.Type, sheets[0].Headers).Count);
        }
        var custom = imports.SuggestMapping(ImportType.OpeningStock, ["Item Code", "Location", "Qty", "Cost Price"]);
        Assert.Equal("Item Code", custom["sku"]); Assert.Equal("Qty", custom["quantity"]); Assert.Equal("Cost Price", custom["unit_cost"]); Assert.Equal("Location", custom["warehouse"]);
    }

    [Fact] public async Task ThousandPlusProductsImportInOneAtomicCommit()
    {
        var rows = new List<string[]> { new[] { "SKU", "Product name", "Category", "Base unit", "Selling price", "Tracking", "Minimum price", "Barcode", "Units per barcode scan" } };
        rows.AddRange(Enumerable.Range(1, 1200).Select(i => new[] { $"P{i:0000}", $"Product {i}", i % 2 == 0 ? "Stationery" : "Hardware", "piece", $"{10 + i % 50}.50", i % 100 == 0 ? "Serial" : "Quantity", "5", i % 3 == 0 ? $"600{i:000000}" : "", i % 3 == 0 && i % 100 != 0 ? "12" : "" }));
        var watch = Stopwatch.StartNew();
        var preview = await Validate(ImportType.Products, Xlsx(rows.ToArray()));
        Assert.Equal(1200, preview.Total); Assert.Equal(0, preview.Errors);
        var result = await Commit(preview);
        Assert.True(watch.Elapsed < TimeSpan.FromMinutes(2), $"Import took {watch.Elapsed}.");
        var snapshot = await Snapshot();
        Assert.Equal(1200, snapshot.Products.Count); Assert.Equal(TrackingMode.Serial, snapshot.Products.Single(x => x.Sku == "P0100").Tracking);
        await using var db = factory.CreateDbContext();
        Assert.Equal(400, await db.ProductBarcodes.CountAsync()); Assert.Equal(1200, await db.PricePolicies.CountAsync());
        Assert.Contains("Created 1200 products", result.Summary);
        Assert.True((await new IntegrityService(factory, session, Options.Create(new TradingOptions())).Verify()).Ok);
    }

    [Fact] public async Task ProductRowErrorsBlockTheWholeCommit()
    {
        await Product("EXISTS");
        var preview = await Validate(ImportType.Products, Xlsx(["SKU", "Product name", "Category", "Base unit", "Selling price", "Tracking"], ["NEW1", "Good", "A", "pc", "10", ""], ["EXISTS", "Dup of catalogue", "A", "pc", "10", ""], ["NEW2", "Bad price", "A", "pc", "-3", ""], ["NEW3", "Variant", "A", "pc", "5", "Variant"], ["NEW1", "Dup in file", "A", "pc", "10", ""]));
        Assert.Equal(5, preview.Errors + preview.Warnings + preview.Valid);
        Assert.Contains(preview.Rows.Single(x => x.Row == 3).Messages, m => m.Contains("already exists"));
        Assert.Contains(preview.Rows.Single(x => x.Row == 4).Messages, m => m.Contains("more than zero"));
        Assert.Contains(preview.Rows.Single(x => x.Row == 5).Messages, m => m.Contains("Variant"));
        Assert.Contains(preview.Rows.Single(x => x.Row == 6).Messages, m => m.Contains("more than once"));
        Assert.Contains("error workbook", (await Assert.ThrowsAsync<BusinessException>(() => Commit(preview))).Message);
        Assert.Single((await Snapshot()).Products);
    }

    [Fact] public async Task DuplicateSerialsAreRejected()
    {
        var phone = await Product("PHONE", TrackingMode.Serial);
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(phone, 1, 500, 1, "SN-OLD")], null));
        var preview = await Validate(ImportType.SerialStock, Xlsx(["SKU", "Warehouse", "Serial number", "Unit cost"], ["PHONE", "", "SN-1", "500"], ["PHONE", "", "sn-1", "500"], ["PHONE", "", "sn-old", "500"], ["PHONE", "", "SN-2", "500"]));
        Assert.Equal(3, preview.Errors);
        Assert.All(preview.Rows.Where(x => x.Row is 2 or 3), r => Assert.Contains(r.Messages, m => m.Contains("more than once")));
        Assert.Contains(preview.Rows.Single(x => x.Row == 4).Messages, m => m.Contains("already exists in stock history"));
        await Assert.ThrowsAsync<BusinessException>(() => Commit(preview));
        Assert.Equal(1, (await Snapshot()).Stock.Sum(x => x.Quantity));
    }

    [Fact] public async Task OpeningStockAcrossWarehousesPostsBalancedDocumentsAndReconciles()
    {
        var paper = await Product("PAPER"); var pens = await Product("PENS");
        var preview = await Validate(ImportType.OpeningStock, Xlsx(["SKU", "Warehouse", "Quantity", "Unit cost"], ["paper", "main", "100", "45.5"], ["PENS", "", "250", "2.25"], ["PAPER", "second", "40", "45.5"]));
        Assert.Equal(0, preview.Errors);
        var result = await Commit(preview);
        Assert.Equal(2, result.Documents.Count);
        var snapshot = await Snapshot();
        Assert.Equal(100, snapshot.Stock.Single(x => x.ProductId == paper && x.Location == "MAIN").Quantity);
        Assert.Equal(40, snapshot.Stock.Single(x => x.ProductId == paper && x.Location == "SECOND").Quantity);
        Assert.Equal(250, snapshot.Stock.Single(x => x.ProductId == pens).Quantity);
        var value = 100 * 45.5m + 250 * 2.25m + 40 * 45.5m;
        Assert.Equal(value, snapshot.Stock.Sum(x => x.Value));
        Assert.Equal(value, snapshot.Ledger.Single(x => x.Account == Account.Inventory).Debit);
        Assert.Equal(value, snapshot.Ledger.Single(x => (int)x.Account == 3200).Credit);
        Assert.All(snapshot.Documents.Where(x => x.Kind == DocumentKind.OpeningStock), d => Assert.Equal(today, d.BusinessDate));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(paper, 5, 100)], PaymentMethod.Cash, Location: "SECOND")); // stock is tradeable
    }

    [Fact] public async Task BatchAndExpiryImportHandlesExcelDatesTextDatesAndExpiredWarnings()
    {
        var drug = await Product("DRUG", TrackingMode.BatchExpiry); var cement = await Product("CEMENT", TrackingMode.Batch);
        var file = DatedWorkbook(["SKU", "Batch", "Expiry date", "Quantity", "Unit cost"],
            [["DRUG", "L1", today.AddDays(200), "30", "12"], ["DRUG", "L2", $"{today.AddDays(-3):dd/MM/yyyy}", "5", "12"], ["CEMENT", "C9", null, "80", "95"], ["DRUG", "L3", null, "1", "12"], ["CEMENT", "C10", today.AddDays(30), "2", "95"]]);
        var preview = await Validate(ImportType.BatchStock, file);
        Assert.Equal(today.AddDays(200).ToString("yyyy-MM-dd"), preview.Rows.Single(x => x.Row == 2).Values["expiry"]);
        Assert.Contains(preview.Rows.Single(x => x.Row == 3).Messages, m => m.Contains("expired")); Assert.Equal("Warning", preview.Rows.Single(x => x.Row == 3).Status);
        Assert.Contains(preview.Rows.Single(x => x.Row == 5).Messages, m => m.Contains("Expiry date is required"));
        Assert.Contains(preview.Rows.Single(x => x.Row == 6).Messages, m => m.Contains("leave the expiry date empty"));

        var fixedFile = DatedWorkbook(["SKU", "Batch", "Expiry date", "Quantity", "Unit cost"], [["DRUG", "L1", today.AddDays(200), "30", "12"], ["DRUG", "L2", $"{today.AddDays(-3):dd/MM/yyyy}", "5", "12"], ["CEMENT", "C9", null, "80", "95"]]);
        await Commit(await Validate(ImportType.BatchStock, fixedFile));
        var stock = (await Snapshot()).Stock;
        Assert.Equal(30, stock.Single(x => x.Batch == "L1" && x.Expiry == today.AddDays(200)).Quantity);
        Assert.Equal(80, stock.Single(x => x.ProductId == cement && x.Batch == "C9" && x.Expiry is null).Quantity);
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(drug, 1, 100, 1, null, "L2", today.AddDays(-3))], PaymentMethod.Cash))); // expired stock still cannot be sold
    }

    [Fact] public async Task MalformedAndUnsafeWorkbooksAreRejectedClearly()
    {
        Assert.Contains("not a readable Excel workbook", (await Assert.ThrowsAsync<BusinessException>(() => imports.Inspect("broken.xlsx", "this is not a zip file"u8.ToArray()))).Message);
        Assert.Contains(".xlsx", (await Assert.ThrowsAsync<BusinessException>(() => imports.Inspect("old.xls", Xlsx(["SKU"])))).Message);
        Assert.Contains("Macro-enabled", (await Assert.ThrowsAsync<BusinessException>(() => imports.Inspect("macro.xlsx", MacroWorkbook()))).Message);
        Assert.Contains("empty", (await Assert.ThrowsAsync<BusinessException>(() => imports.Inspect("empty.xlsx", []))).Message);
        Assert.Contains("10 MB", (await Assert.ThrowsAsync<BusinessException>(() => imports.Inspect("huge.xlsx", new byte[11 * 1024 * 1024]))).Message);
        Assert.Contains("no data rows", (await Assert.ThrowsAsync<BusinessException>(() => Validate(ImportType.Products, Xlsx(["SKU", "Product name", "Category", "Base unit", "Selling price"])))).Message);
        Assert.Contains("Choose the column", (await Assert.ThrowsAsync<BusinessException>(() => imports.Validate(new(ImportType.Products, "p.xlsx", Xlsx(["SKU"], ["A"]), "Data", new Dictionary<string, string> { ["sku"] = "SKU" })))).Message);
    }

    [Fact] public async Task FormulaCellsUseSavedResultsAndRejectUnsavedOnes()
    {
        await Product("PAPER");
        var preview = await Validate(ImportType.OpeningStock, FormulaWorkbook());
        Assert.Equal("12", preview.Rows.Single(x => x.Row == 2).Values["quantity"]); // =3*4 with cached result 12
        Assert.Contains(preview.Rows.Single(x => x.Row == 3).Messages, m => m.Contains("formula with no saved result"));
    }

    [Fact] public async Task RetryingACommittedImportNeverDuplicates()
    {
        await Product("PAPER");
        var file = Xlsx(["SKU", "Quantity", "Unit cost"], ["PAPER", "10", "50"]);
        var first = await Validate(ImportType.OpeningStock, file);
        var operation = Guid.NewGuid();
        var committed = await imports.Commit(new(operation, first.BatchId));
        var again = await imports.Commit(new(operation, first.BatchId));
        Assert.True(again.Replayed); Assert.Equal(committed.Documents, again.Documents);
        Assert.True((await imports.Commit(new(Guid.NewGuid(), first.BatchId))).Replayed);
        var resaved = await Validate(ImportType.OpeningStock, Xlsx(["SKU", "Quantity", "Unit cost"], ["paper", "10.0", "50"])); // same data, re-saved file
        Assert.Contains("already imported", resaved.AlreadyImported);
        Assert.True((await Commit(resaved)).Replayed);
        Assert.Equal(10, (await Snapshot()).Stock.Single().Quantity);
        Assert.Single((await Snapshot()).Documents, x => x.Kind == DocumentKind.OpeningStock);
        Assert.Contains(await imports.History(), x => x.Status == "Committed" && x.Rows == 1);
    }

    [Fact] public async Task ErrorWorkbookListsEveryRowWithWhatToFix()
    {
        await Product("PAPER");
        var preview = await Validate(ImportType.OpeningStock, Xlsx(["SKU", "Warehouse", "Quantity", "Unit cost"], ["PAPER", "", "10", "50"], ["NOPE", "", "1", "1"], ["PAPER", "ATTIC", "x", "1"]));
        Assert.Equal(2, preview.Errors);
        var sheet = WorkbookReader.Read(await imports.ErrorWorkbook(preview.BatchId), "Rows").Single();
        Assert.Equal(["SKU", "Warehouse", "Quantity", "Unit cost", "Excel row", "Import status", "What to fix"], sheet.Headers);
        Assert.Equal(3, sheet.Rows.Count);
        Assert.Equal("Valid", sheet.Rows[0].Cells[5]);
        Assert.Contains("not in the catalogue", sheet.Rows[1].Cells[6]);
        Assert.Contains("ATTIC does not exist", sheet.Rows[2].Cells[6]); Assert.Contains("not a number", sheet.Rows[2].Cells[6]);
        Assert.Equal("ATTIC", sheet.Rows[2].Cells[1]); // original values kept so the user can correct in place
    }

    [Fact] public async Task CommitRevalidatesAgainstRecordsChangedAfterValidation()
    {
        var phone = await Product("PHONE", TrackingMode.Serial);
        var preview = await Validate(ImportType.SerialStock, Xlsx(["SKU", "Serial number", "Unit cost"], ["PHONE", "SN-9", "500"]));
        Assert.Equal(0, preview.Errors);
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(phone, 1, 500, 1, "SN-9")], null));
        Assert.Contains("Records changed", (await Assert.ThrowsAsync<BusinessException>(() => Commit(preview))).Message);
        Assert.Equal(1, (await Snapshot()).Stock.Sum(x => x.Quantity));
    }

    [Fact] public async Task OpeningStockCanBeReversedThroughTheNormalWorkflow()
    {
        await Product("PAPER");
        var result = await Commit(await Validate(ImportType.OpeningStock, Xlsx(["SKU", "Quantity", "Unit cost"], ["PAPER", "10", "50"])));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), result.Documents.Single(), "Wrong opening count"));
        var snapshot = await Snapshot();
        Assert.Equal(0, snapshot.Stock.Sum(x => x.Quantity));
        Assert.All(snapshot.Ledger, x => Assert.Equal(x.Debit, x.Credit));
    }

    [Fact] public async Task OpeningStockDateAndPermissionsAreEnforced()
    {
        await Product("PAPER");
        var file = Xlsx(["SKU", "Quantity", "Unit cost"], ["PAPER", "1", "1"]);
        await Assert.ThrowsAsync<BusinessException>(() => Validate(ImportType.OpeningStock, file, today.AddDays(1)));
        await Assert.ThrowsAsync<BusinessException>(() => Validate(ImportType.OpeningStock, file, new DateOnly(1999, 1, 1)));
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read", "inventory.receive", "catalogue.write" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Validate(ImportType.OpeningStock, file)); // posting to the ledger needs accounting.write
        var noPricing = await Validate(ImportType.Products, Xlsx(["SKU", "Product name", "Category", "Base unit", "Selling price", "Minimum price"], ["NEW", "New", "A", "pc", "10", "5"]));
        Assert.Contains(noPricing.Rows.Single().Messages, m => m.Contains("pricing.manage"));
    }

    [Fact] public async Task PriceUpdateChangesPricesAndVersionsMinimums()
    {
        var paper = await Product("PAPER");
        var preview = await Validate(ImportType.PriceUpdate, Xlsx(["SKU", "New selling price", "New minimum price"], ["PAPER", "120", "80"]));
        await Commit(preview);
        Assert.Equal(120, (await Snapshot()).Products.Single().Price);
        await using var db = factory.CreateDbContext();
        Assert.Equal(80, (await db.PricePolicies.Where(x => x.ProductId == paper).OrderByDescending(x => x.Version).FirstAsync()).MinimumPrice);
        Assert.Equal(2, await db.PricePolicies.CountAsync(x => x.ProductId == paper));
        Assert.Contains(await db.Operations.ToListAsync(), x => x.Action == "import.price-changes" && x.Payload.Contains("\"OldPrice\":100"));
    }

    [Fact] public async Task StockCountImportCreatesSessionsForSecondPersonApproval()
    {
        var paper = await Product("PAPER");
        await Commit(await Validate(ImportType.OpeningStock, Xlsx(["SKU", "Quantity", "Unit cost"], ["PAPER", "10", "50"])));
        var preview = await Validate(ImportType.StockCount, Xlsx(["SKU", "Counted quantity", "Unit cost for surplus", "Reason"], ["PAPER", "8", "", "Damaged"]), name: "Month end");
        Assert.Contains(preview.Rows.Single().Messages, m => m.Contains("-2"));
        var result = await Commit(preview);
        Assert.Equal(10, (await Snapshot()).Stock.Single().Quantity); // nothing moves until approval
        var management = new InventoryManagement(factory, session, Options.Create(new TradingOptions()));
        var count = (await management.Counts()).Single();
        Assert.Equal("Month end", count.Name); Assert.Equal(10, count.Lines.Single().ExpectedQuantity); Assert.Equal(result.Documents.Single(), count.Id);
        await management.DecideCount(new(Guid.NewGuid(), count.Id, true, "Checked"));
        Assert.Equal(8, (await Snapshot()).Stock.Single(x => x.ProductId == paper).Quantity);

        var many = new List<string[]> { new[] { "SKU", "Warehouse", "Counted quantity", "Unit cost for surplus" } };
        for (var i = 0; i < 250; i++) { var sku = $"C{i:000}"; await Product(sku); many.Add([sku, "MAIN", "0", ""]); }
        var big = await Commit(await Validate(ImportType.StockCount, Xlsx(many.ToArray()), name: "Big count"));
        Assert.Equal(2, big.Documents.Count);
        Assert.Contains(await management.Counts(), x => x.Name == "Big count (part 2 of 2)" && x.Lines.Count == 50);
    }

    private static byte[] DatedWorkbook(string[] headers, object?[][] rows)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart(); workbook.Workbook = new Workbook();
            workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = new Stylesheet(new Fonts(new Font()), new Fills(new Fill(), new Fill()), new Borders(new Border()), new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }));
            var part = workbook.AddNewPart<WorksheetPart>(); var data = new SheetData(); part.Worksheet = new Worksheet(data);
            Cell Text(string value) => new() { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value)) };
            data.Append(new Row(headers.Select(Text)) { RowIndex = 1 });
            uint index = 2;
            foreach (var row in rows)
                data.Append(new Row(row.Select(v => v switch
                {
                    DateOnly d => new Cell { CellValue = new CellValue(d.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture)), StyleIndex = 1 },
                    null => new Cell(),
                    _ => Text(v.ToString()!)
                })) { RowIndex = index++ });
            workbook.Workbook.AppendChild(new Sheets(new Sheet { Name = "Data", SheetId = 1, Id = workbook.GetIdOfPart(part) }));
        }
        return stream.ToArray();
    }
    private static byte[] FormulaWorkbook()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart(); workbook.Workbook = new Workbook();
            var part = workbook.AddNewPart<WorksheetPart>(); var data = new SheetData(); part.Worksheet = new Worksheet(data);
            Cell Text(string value, string reference) => new() { CellReference = reference, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value)) };
            data.Append(new Row(Text("SKU", "A1"), Text("Quantity", "B1"), Text("Unit cost", "C1")) { RowIndex = 1 });
            data.Append(new Row(Text("PAPER", "A2"), new Cell { CellReference = "B2", CellFormula = new CellFormula("3*4"), CellValue = new CellValue("12") }, new Cell { CellReference = "C2", CellValue = new CellValue("5") }) { RowIndex = 2 });
            data.Append(new Row(Text("PAPER", "A3"), new Cell { CellReference = "B3", CellFormula = new CellFormula("Z99*2") }, new Cell { CellReference = "C3", CellValue = new CellValue("5") }) { RowIndex = 3 });
            workbook.Workbook.AppendChild(new Sheets(new Sheet { Name = "Data", SheetId = 1, Id = workbook.GetIdOfPart(part) }));
        }
        return stream.ToArray();
    }
    private static byte[] MacroWorkbook()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.MacroEnabledWorkbook))
        {
            var workbook = document.AddWorkbookPart(); workbook.Workbook = new Workbook();
            var part = workbook.AddNewPart<WorksheetPart>(); part.Worksheet = new Worksheet(new SheetData());
            workbook.Workbook.AppendChild(new Sheets(new Sheet { Name = "Data", SheetId = 1, Id = workbook.GetIdOfPart(part) }));
        }
        return stream.ToArray();
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
