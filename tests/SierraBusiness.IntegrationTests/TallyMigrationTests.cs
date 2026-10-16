using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.Infrastructure.Imports;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class TallyMigrationTests : IAsyncLifetime
{
    private readonly List<string> paths = []; private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private async Task<Factory> Company()
    {
        var path = Path.Combine(Path.GetTempPath(), "rokela-tally-" + Guid.NewGuid().ToString("N") + ".db"); paths.Add(path);
        var factory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=" + path + ";Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db); return factory;
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (var p in paths) File.Delete(p); return Task.CompletedTask; }
    private static async Task<ImportCommitResult> Import(ImportService imports, ImportType type, string xml, DateOnly? asAt = null, int errors = 0)
    {
        var workbook = await imports.ConvertExchange(new(type, "company.xml", System.Text.Encoding.UTF8.GetBytes(xml)));
        var sheet = (await imports.Inspect("company.xlsx", workbook)).Single(s => s.Name == "Data");
        var preview = await imports.Validate(new(type, "company.xlsx", workbook, "Data", imports.SuggestMapping(type, sheet.Headers), asAt));
        Assert.True(errors == preview.Errors, string.Join("; ", preview.Rows.SelectMany(r => r.Messages)));
        return await imports.Commit(new(Guid.NewGuid(), preview.BatchId));
    }
    private string CompanyXml(DateOnly billDate) => $"""
        <ENVELOPE><HEADER><TALLYREQUEST>Import Data</TALLYREQUEST></HEADER><BODY><IMPORTDATA><REQUESTDATA>
        <TALLYMESSAGE><GROUP NAME="Freetown Debtors"><PARENT>Sundry Debtors</PARENT></GROUP></TALLYMESSAGE>
        <TALLYMESSAGE><GROUP NAME="Admin Costs"><PARENT>Indirect Expenses</PARENT></GROUP></TALLYMESSAGE>
        <TALLYMESSAGE><LEDGER NAME="Rokel Commercial Bank"><PARENT>Bank Accounts</PARENT><OPENINGBALANCE>-5000.00</OPENINGBALANCE></LEDGER></TALLYMESSAGE>
        <TALLYMESSAGE><LEDGER NAME="Office Rent"><PARENT>Admin Costs</PARENT></LEDGER></TALLYMESSAGE>
        <TALLYMESSAGE><LEDGER NAME="Owner Capital Account"><PARENT>Capital Account</PARENT><OPENINGBALANCE>9000.00</OPENINGBALANCE></LEDGER></TALLYMESSAGE>
        <TALLYMESSAGE><LEDGER NAME="Kissy School"><PARENT>Freetown Debtors</PARENT><EMAIL>bursar@example.test</EMAIL><BILLCREDITPERIOD>30 Days</BILLCREDITPERIOD><CREDITLIMIT>-20000</CREDITLIMIT><OPENINGBALANCE>-3000.00</OPENINGBALANCE>
          <BILLALLOCATIONS.LIST><NAME>INV-77</NAME><BILLDATE>{billDate:yyyyMMdd}</BILLDATE><OPENINGBALANCE>-3000.00</OPENINGBALANCE><BILLCREDITPERIOD>30</BILLCREDITPERIOD></BILLALLOCATIONS.LIST></LEDGER></TALLYMESSAGE>
        <TALLYMESSAGE><LEDGER NAME="Paper Mills Ltd"><PARENT>Sundry Creditors</PARENT><OPENINGBALANCE>1000.00</OPENINGBALANCE></LEDGER></TALLYMESSAGE>
        <TALLYMESSAGE><LEDGER NAME="Profit &amp; Loss A/c"><PARENT></PARENT></LEDGER></TALLYMESSAGE>
        <TALLYMESSAGE><STOCKITEM NAME="A4 Paper Ream"><PARENT>Stationery</PARENT><BASEUNITS>Ream</BASEUNITS><OPENINGBALANCE> 10 Ream</OPENINGBALANCE><OPENINGVALUE>-500.00</OPENINGVALUE><OPENINGRATE>50.00/Ream</OPENINGRATE><STANDARDPRICELIST.LIST><RATE>80.00/Ream</RATE></STANDARDPRICELIST.LIST></STOCKITEM></TALLYMESSAGE>
        </REQUESTDATA></IMPORTDATA></BODY></ENVELOPE>
        """;

    [Fact] public async Task CompleteCompanyMastersStockAndOpeningBillsMigrateFromTally()
    {
        var factory = await Company(); var options = Options.Create(new TradingOptions()); var imports = new ImportService(factory, session, options); var finance = new FinanceService(factory, session, options);
        var xml = CompanyXml(today.AddDays(-10));
        await Assert.ThrowsAsync<BusinessException>(() => imports.ConvertExchange(new(ImportType.OpeningBalances, "company.xml", System.Text.Encoding.UTF8.GetBytes(xml))));
        await Assert.ThrowsAsync<BusinessException>(() => imports.ConvertExchange(new(ImportType.Ledgers, "company.xml", System.Text.Encoding.UTF8.GetBytes(xml.Replace("<PARENT>Admin Costs</PARENT>", "<PARENT>Unknown group</PARENT>")))));
        Assert.Equal(3, (await Import(imports, ImportType.Ledgers, xml)).Rows);
        var accounts = (await finance.Read(today, today)).Accounts; Assert.True(accounts.Single(a => a.Name == "Rokel Commercial Bank").CashOrBank); Assert.Equal(LedgerGroup.Expense, accounts.Single(a => a.Name == "Office Rent").Group);
        Assert.Equal(2, (await Import(imports, ImportType.Parties, xml)).Rows);
        await using (var db = factory.CreateDbContext()) { var school = await db.Parties.SingleAsync(p => p.Name == "Kissy School"); Assert.Equal(PartyType.Customer, school.Type); Assert.Equal(30, school.PaymentDays); Assert.Equal(20000, school.CreditLimit); Assert.Equal(PartyType.Supplier, (await db.Parties.SingleAsync(p => p.Name == "Paper Mills Ltd")).Type); }
        await Import(imports, ImportType.Products, xml);
        await Import(imports, ImportType.OpeningStock, xml, today);
        var business = new BusinessService(factory, options); var stock = Assert.Single((await business.Read(session.Actor)).Stock); Assert.Equal(10, stock.Quantity); Assert.Equal(500, stock.Value);
        Assert.Equal(80, (await business.Read(session.Actor)).Products.Single().Price);
        var balances = await Import(imports, ImportType.OpeningBalances, xml, today); Assert.Equal(3, balances.Documents.Count);
        var snapshot = await finance.Read(today.AddYears(-1), today);
        var invoice = snapshot.Bills.Single(b => b.Number.EndsWith("OB-INV-77")); Assert.True(invoice.Receivable); Assert.Equal(3000, invoice.Outstanding); Assert.Equal(today.AddDays(20), invoice.DueDate);
        Assert.Equal(1000, snapshot.Bills.Single(b => !b.Receivable).Outstanding);
        Assert.Equal(5000, snapshot.Balances.Single(b => b.Name == "Rokel Commercial Bank").Closing); Assert.Equal(-9000, snapshot.Balances.Single(b => b.Name == "Owner Capital Account").Closing);
        Assert.Equal(-(5000 + 3000 - 9000 - 1000) - 500, snapshot.Balances.Single(b => b.Code == 3200).Closing);
        var bank = accounts.Single(a => a.Name == "Rokel Commercial Bank").Code;
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, today, "School pays opening invoice", [new(bank, 3000, 0), new(1100, 0, 3000)], [new(invoice.Id, 3000)]));
        Assert.DoesNotContain((await finance.Read(today.AddYears(-1), today)).Bills, b => b.Id == invoice.Id && b.Outstanding > 0);
        var again = await imports.Validate(new(ImportType.OpeningBalances, "company.xlsx", await imports.ConvertExchange(new(ImportType.OpeningBalances, "company.xml", System.Text.Encoding.UTF8.GetBytes(xml.Replace("INV-77", "INV-78")))), "Data", imports.SuggestMapping(ImportType.OpeningBalances, (await imports.Inspect("company.xlsx", await imports.ConvertExchange(new(ImportType.OpeningBalances, "company.xml", System.Text.Encoding.UTF8.GetBytes(xml))))).Single().Headers), today));
        Assert.Equal(1, again.Errors); // the supplier's generated opening bill was already imported; the new customer bill is valid
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task OpeningBalancesRejectControlAccountsAndMismatchedParties()
    {
        var factory = await Company(); var imports = new ImportService(factory, session, Options.Create(new TradingOptions())); await TestParties.Register(factory, "School");
        static byte[] Book(params string[][] rows) => WorkbookWriter.Write([("Data", rows.Select(r => (IReadOnlyList<string>)r).ToArray())]);
        var bytes = Book(["Account code", "Debit", "Credit", "Customer or supplier", "Bill number"], ["1200", "10", "", "", ""], ["1100", "", "10", "School", "B-1"], ["2000", "", "10", "School", "B-2"], ["1100", "10", "", "Nobody", "B-3"], ["5100", "10", "", "School", "B-4"], ["1100", "10", "", "School", ""]);
        var preview = await imports.Validate(new(ImportType.OpeningBalances, "o.xlsx", bytes, "Data", imports.SuggestMapping(ImportType.OpeningBalances, (await imports.Inspect("o.xlsx", bytes))[0].Headers), today));
        Assert.Equal(6, preview.Errors);
        var headers = (await imports.Inspect("o.xlsx", bytes))[0].Headers;
        await Assert.ThrowsAsync<BusinessException>(() => imports.Validate(new(ImportType.OpeningBalances, "o.xlsx", bytes, "Data", imports.SuggestMapping(ImportType.OpeningBalances, headers))));
    }
    [Fact] public async Task TallyExportRoundTripsLedgersAndPlainVouchersIntoAnotherCompany()
    {
        var source = await Company(); var options = Options.Create(new TradingOptions()); var finance = new FinanceService(source, session, options);
        await finance.CreateLedger(new(Guid.NewGuid(), 6100, "Office supplies", LedgerGroup.Expense)); await finance.CreateLedger(new(Guid.NewGuid(), 6200, "Consulting income", LedgerGroup.Income));
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Paper", [new(6100, 125.5m, 0), new(1000, 0, 125.5m)], []));
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, today, "Advice", [new(1010, 400, 0), new(6200, 0, 400)], []));
        var xml = System.Text.Encoding.UTF8.GetString(await new ImportService(source, session, options).ExportTally(today, today));
        var parsed = System.Xml.Linq.XDocument.Parse(xml); Assert.Equal(2, parsed.Descendants("VOUCHER").Count()); Assert.Contains(parsed.Descendants("LEDGER"), l => l.Attribute("NAME")?.Value == "Office supplies" && l.Element("PARENT")?.Value == "Indirect Expenses");
        var target = await Company(); var imports = new ImportService(target, session, options);
        Assert.Equal(2, (await Import(imports, ImportType.Ledgers, xml)).Rows);
        Assert.Equal(2, (await Import(imports, ImportType.Vouchers, xml)).Documents.Count);
        var copied = await new FinanceService(target, session, options).Read(today, today);
        Assert.Equal(125.5m, copied.Balances.Single(b => b.Name == "Office supplies").Closing); Assert.Equal(-400, copied.Balances.Single(b => b.Name == "Consulting income").Closing);
        session.Actor = new("clerk", "FREETOWN", new HashSet<string> { "business.read" }); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => imports.ExportTally(today, today));
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
