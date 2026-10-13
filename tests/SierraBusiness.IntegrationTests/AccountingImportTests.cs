using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.Infrastructure.Imports;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class AccountingImportTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(),"rokela-account-import-"+Guid.NewGuid().ToString("N")+".db"); private readonly Session session = new(); private Factory factory = null!; private ImportService imports = null!;
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync() { factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source="+path+";Pooling=False").Options); await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db); imports = new(factory,session,Options.Create(new TradingOptions())); }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private async Task<ImportPreview> Validate(ImportType type, params string[][] rows) { var bytes = WorkbookWriter.Write([("Data",rows.Select(r => (IReadOnlyList<string>)r).ToArray())]); var sheet = (await imports.Inspect("onboarding.xlsx",bytes))[0]; return await imports.Validate(new(type,"onboarding.xlsx",bytes,sheet.Name,imports.SuggestMapping(type,sheet.Headers))); }
    [Fact] public async Task PartyAndLedgerPreviewCreatesNothingAndCommitUsesDomainRules()
    {
        var party = await Validate(ImportType.Parties,["Party code","Party name","Party type","Email","Payment days","Credit limit"],["C1","School","Customer","office@example.test","30","1000"]);
        Assert.Equal(0,party.Errors); await using (var db = factory.CreateDbContext()) Assert.Empty(await db.Parties.ToListAsync());
        var result = await imports.Commit(new(Guid.NewGuid(),party.BatchId)); Assert.Equal(1,result.Rows);
        var ledger = await Validate(ImportType.Ledgers,["Account code","Account name","Account type","Cash or bank"],["6001","Imported income","Income","No"]); Assert.Equal(0,ledger.Errors);
        await using (var db = factory.CreateDbContext()) Assert.False(await db.LedgerAccounts.AnyAsync(a => a.Id == 6001));
        await imports.Commit(new(Guid.NewGuid(),ledger.BatchId));
        var duplicate = await Validate(ImportType.Parties,["Party code","Party name","Party type"],["C1","Different name","Customer"]); Assert.Equal(1,duplicate.Errors);
        var invalid = await Validate(ImportType.Ledgers,["Account code","Account name","Account type","Cash or bank"],["6002","Not an asset","Expense","Yes"]); Assert.Equal(1,invalid.Errors);
        session.Actor = new("reader","FREETOWN",new HashSet<string>{"business.read"}); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => imports.Commit(new(Guid.NewGuid(),ledger.BatchId)));
    }
    [Fact] public async Task MultipleVoucherGroupsRollBackTogetherWhenCombinedBillAllocationsExceedOutstanding()
    {
        var options = Options.Create(new TradingOptions()); var business = new BusinessService(factory,options); var product = await business.AddProduct(session.Actor,new(Guid.NewGuid(),"IMPORT-BILL","Import stock","General","piece",20,TrackingMode.Quantity,MinimumPrice:1));
        var billId = await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",today,[new(product,10,10)],null)); var bill = (await business.Read(session.Actor)).Documents.Single(d => d.Id == billId);
        var headers = new[] { "Voucher reference","Voucher date","Voucher type","Narration","Account code","Debit","Credit","Allocated bill number","Bill allocation amount" };
        string[][] Rows(string second) => [headers,["PAY1",today.ToString("yyyy-MM-dd"),"Payment","First payment","2000","60","",bill.Number,"60"],["PAY1",today.ToString("yyyy-MM-dd"),"Payment","First payment","1010","","60","",""],["PAY2",today.ToString("yyyy-MM-dd"),"Payment","Second payment","2000",second,"",bill.Number,second],["PAY2",today.ToString("yyyy-MM-dd"),"Payment","Second payment","1010","",second,"",""]];
        var over = await Validate(ImportType.Vouchers,Rows("60")); Assert.Equal(0,over.Errors); await using (var db = factory.CreateDbContext()) Assert.Single(await db.Documents.ToListAsync());
        await Assert.ThrowsAsync<BusinessException>(() => imports.Commit(new(Guid.NewGuid(),over.BatchId)));
        await using (var db = factory.CreateDbContext()) { Assert.Single(await db.Documents.ToListAsync()); Assert.Empty(await db.BillAllocations.ToListAsync()); Assert.True((await HistoryProtection.Verify(db)).Ok); }
        var valid = await Validate(ImportType.Vouchers,Rows("40")); Assert.Equal(0,valid.Errors); var command = new ImportCommitCommand(Guid.NewGuid(),valid.BatchId); var result = await imports.Commit(command); Assert.Equal(2,result.Documents.Count); Assert.True((await imports.Commit(command)).Replayed);
        var finance = new FinanceService(factory,session,options); Assert.DoesNotContain((await finance.Read(today,today)).Bills,b => b.Id == billId);
        var reordered = Rows("40"); var reversedRows = new[] { headers }.Concat(reordered.Skip(1).Reverse()).ToArray(); var duplicate = await Validate(ImportType.Vouchers,reversedRows); Assert.True(duplicate.Errors > 0);
        var unicode = new VoucherCommand(Guid.NewGuid(),VoucherType.Payment,today,"Unicode source reference",[new(5100,1,0),new(1010,0,1)],[],ExternalReference:"REF-Å");
        await finance.PostVoucher(unicode); await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(unicode with { OperationId = Guid.NewGuid() }));
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task ExchangeAdaptersStageReviewedDataAndRefuseUnsupportedDetails()
    {
        var finance = new FinanceService(factory,session,Options.Create(new TradingOptions()));
        var json = System.Text.Encoding.UTF8.GetBytes("""{"Version":1,"Type":"Parties","Rows":[{"code":"EX-1","name":"Exchange customer","party_type":"Customer"}]}""");
        var workbook = await imports.ConvertExchange(new(ImportType.Parties,"parties.json",json)); var sheets = await imports.Inspect("exchange.xlsx",workbook);
        var preview = await imports.Validate(new(ImportType.Parties,"exchange.xlsx",workbook,"Data",imports.SuggestMapping(ImportType.Parties,sheets.Single(s => s.Name == "Data").Headers))); Assert.Equal(0,preview.Errors);
        await using (var db = factory.CreateDbContext()) Assert.Empty(await db.Parties.ToListAsync());
        await imports.Commit(new(Guid.NewGuid(),preview.BatchId));
        var unknown = System.Text.Encoding.UTF8.GetBytes("""{"Version":1,"Type":"Parties","Rows":[{"code":"EX-2","name":"Other","party_type":"Customer","unrecognised":"discard me"}]}""");
        await Assert.ThrowsAsync<BusinessException>(() => imports.ConvertExchange(new(ImportType.Parties,"bad.json",unknown)));
        await finance.CreateLedger(new(Guid.NewGuid(),6201,"Migration expense",LedgerGroup.Expense));
        var xml = $"<ENVELOPE><BODY><VOUCHER VCHTYPE=\"Payment\"><DATE>{today:yyyyMMdd}</DATE><VOUCHERNUMBER>XML-1</VOUCHERNUMBER><NARRATION>XML expense</NARRATION><ALLLEDGERENTRIES.LIST><LEDGERNAME>Migration expense</LEDGERNAME><ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE><AMOUNT>-10</AMOUNT></ALLLEDGERENTRIES.LIST><ALLLEDGERENTRIES.LIST><LEDGERNAME>Cash</LEDGERNAME><ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE><AMOUNT>10</AMOUNT></ALLLEDGERENTRIES.LIST></VOUCHER></BODY></ENVELOPE>";
        await using (var db = factory.CreateDbContext()) { var cash = await db.LedgerAccounts.SingleAsync(a => a.Id == 1000); xml = xml.Replace("<LEDGERNAME>Cash</LEDGERNAME>","<LEDGERNAME>" + cash.Name + "</LEDGERNAME>",StringComparison.Ordinal); }
        var converted = await imports.ConvertExchange(new(ImportType.Vouchers,"tally.xml",System.Text.Encoding.UTF8.GetBytes(xml))); Assert.NotEmpty(converted);
        await Assert.ThrowsAsync<BusinessException>(() => imports.ConvertExchange(new(ImportType.Vouchers,"tally.xml",System.Text.Encoding.UTF8.GetBytes(xml.Replace("<NARRATION>","<ALLINVENTORYENTRIES.LIST /><NARRATION>",StringComparison.Ordinal)))));
        await Assert.ThrowsAsync<BusinessException>(() => imports.ConvertExchange(new(ImportType.Vouchers,"external.xml",System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE ENVELOPE [<!ENTITY external SYSTEM 'file:///secret'>]><ENVELOPE>&external;</ENVELOPE>"))));
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner","FREETOWN",BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
