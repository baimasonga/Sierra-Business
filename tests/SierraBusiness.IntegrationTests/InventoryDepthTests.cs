using Microsoft.EntityFrameworkCore;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class InventoryDepthTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(),"rokela-inventory-depth-"+Guid.NewGuid().ToString("N")+".db"); private readonly Session session = new(); private Factory factory = null!; private BusinessService business = null!; private InventoryService inventory = null!; private FinanceService finance = null!; private Guid product; private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source="+path+";Pooling=False").Options); await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db); await TestParties.Register(factory,"School");
        var options = Options.Create(new TradingOptions()); business = new(factory,options); inventory = new(factory,session,options); finance = new(factory,session,options); product = await business.AddProduct(session.Actor,new(Guid.NewGuid(),"DEPTH","Inventory depth item","General","piece",100,TrackingMode.Quantity,MinimumPrice:1));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private Task<Guid> Receipt(decimal quantity,decimal cost,DateOnly date) => business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",date,[new(product,quantity,cost)],null));
    private Task<Guid> Sale(decimal quantity,string location = "MAIN") => business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Sale,"Cash customer",today,[new(product,quantity,100)],PaymentMethod.Cash,Location:location));
    [Fact] public async Task FifoCostsPreserveLayersThroughTransfersReversalsAndMultiPriceReceipt()
    {
        await inventory.SetCostPolicy(new(Guid.NewGuid(),product,0,InventoryCostMethod.Fifo,0,today.AddDays(-2),"Initial FIFO policy"));
        await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",today.AddDays(-2),[new(product,10,10),new(product,10,20)],null));
        await inventory.CreateLocation(new(Guid.NewGuid(),"SECOND","Second warehouse")); await inventory.Transfer(new(Guid.NewGuid(),product,"MAIN","SECOND",3,null,null,null,"Move oldest stock"));
        var sale = await Sale(5); await Sale(1,"SECOND");
        Assert.Equal(240,(await business.Read(session.Actor)).Stock.Sum(s => s.Value));
        await business.Reverse(session.Actor,new(Guid.NewGuid(),sale,"Undo sale")); var next = await Sale(1);
        await using (var db = factory.CreateDbContext()) Assert.Equal(10,(await db.Journal.Where(j => j.DocumentId == next && j.Account == Account.CostOfSales).ToListAsync()).Sum(j => j.Debit));
        Assert.Equal(280,(await business.Read(session.Actor)).Stock.Sum(s => s.Value));
        await Assert.ThrowsAsync<BusinessException>(() => Receipt(1,10,today.AddDays(-1)));
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task ProspectiveCostingTransitionPreservesValueAndBlocksEarlierHistoryReversal()
    {
        var receipt = await Receipt(10,10,today.AddDays(-2)); await Receipt(10,20,today.AddDays(-1)); await Sale(5);
        var before = Assert.Single((await business.Read(session.Actor)).Stock).Value;
        await inventory.SetCostPolicy(new(Guid.NewGuid(),product,0,InventoryCostMethod.Fifo,0,today,"Prospective FIFO using existing carrying values"));
        Assert.Equal(before,Assert.Single((await business.Read(session.Actor)).Stock).Value);
        await Sale(1); await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor,new(Guid.NewGuid(),receipt,"Cannot rewrite pre-transition history")));
        await Assert.ThrowsAsync<BusinessException>(() => Receipt(1,10,today.AddDays(-1)));
        await inventory.SetCostPolicy(new(Guid.NewGuid(),product,1,InventoryCostMethod.WeightedAverage,0,today,"Prospective average"));
        await Assert.ThrowsAsync<BusinessException>(() => Receipt(1,10,today.AddDays(-1)));
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task StandardCostsPostPurchaseVarianceAndAlternativeValuesLeaveBooksUnchanged()
    {
        await inventory.SetCostPolicy(new(Guid.NewGuid(),product,0,InventoryCostMethod.Standard,12,today.AddDays(-1),"Initial standard cost")); await Receipt(3,10,today.AddDays(-1)); await Sale(1);
        var state = await business.Read(session.Actor); Assert.Equal(24,Assert.Single(state.Stock).Value); Assert.Equal(6,state.Ledger.Single(l => (int)l.Account == 5100).Credit);
        var report = await inventory.Valuation(new(today,InventoryValuationMethod.Market,new Dictionary<Guid,decimal>{{product,30}})); Assert.Equal(60,report.SelectedValue); Assert.Equal(24,report.BookValue); Assert.Equal(24,Assert.Single((await business.Read(session.Actor)).Stock).Value);
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Valuation(new(today,InventoryValuationMethod.Standard)));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.Count(new(Guid.NewGuid(),product,"MAIN",2,3,10,"Wrong standard cost")));
    }
    [Fact] public async Task ExistingStockMigratesToStandardRevisesAndLeavesStandardProspectively()
    {
        await finance.CreateLedger(new(Guid.NewGuid(),6100,"Standard cost revaluation",LedgerGroup.Expense));
        var receipt = await Receipt(10,10,today.AddDays(-2)); await Receipt(10,20,today.AddDays(-1)); await Sale(5);
        await Assert.ThrowsAsync<BusinessException>(() => inventory.SetCostPolicy(new(Guid.NewGuid(),product,0,InventoryCostMethod.Standard,12,today,"Not through the plain policy")));
        var preview = await inventory.PreviewStandardMigration(product,12); Assert.Equal(15,preview.Quantity); Assert.Equal(225,preview.BookValue); Assert.Equal(180,preview.StandardValue); Assert.Equal(-45,preview.Difference);
        var command = new StandardCostMigrationCommand(Guid.NewGuid(),product,0,12,today,225,6100,"Reviewed standard from supplier price list");
        await Assert.ThrowsAsync<BusinessException>(() => inventory.MigrateToStandard(command with { OperationId = Guid.NewGuid(),ExpectedBookValue = 224 }));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.MigrateToStandard(command with { OperationId = Guid.NewGuid(),ExpectedVersion = 1 }));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.MigrateToStandard(command with { OperationId = Guid.NewGuid(),DifferenceAccount = 1000 }));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.MigrateToStandard(command with { OperationId = Guid.NewGuid(),From = today.AddDays(-1) }));
        var migration = await inventory.MigrateToStandard(command); Assert.Equal(migration,await inventory.MigrateToStandard(command));
        Assert.Equal(180,Assert.Single((await business.Read(session.Actor)).Stock).Value);
        var policy = Assert.Single(await inventory.CostPolicies()); Assert.Equal(InventoryCostMethod.Standard,policy.Method); Assert.Equal(12,policy.StandardUnitCost); Assert.Equal(1,policy.Version);
        await using (var db = factory.CreateDbContext()) { var lines = await db.Journal.Where(j => j.DocumentId == migration).ToListAsync(); Assert.Equal(45,lines.Single(l => l.Account == Account.Inventory).Credit); Assert.Equal(45,lines.Single(l => (int)l.Account == 6100).Debit); }
        var sale = await Sale(1); await using (var db = factory.CreateDbContext()) Assert.Equal(12,(await db.Journal.Where(j => j.DocumentId == sale && j.Account == Account.CostOfSales).ToListAsync()).Sum(j => j.Debit));
        await Receipt(1,10,today); Assert.Equal(180,Assert.Single((await business.Read(session.Actor)).Stock).Value);
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor,new(Guid.NewGuid(),receipt,"Earlier costing history")));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.MigrateToStandard(new(Guid.NewGuid(),product,1,12,today,180,6100,"Same standard")));
        var revision = await inventory.MigrateToStandard(new(Guid.NewGuid(),product,1,15,today,180,6100,"Annual standard revision"));
        Assert.Equal(225,Assert.Single((await business.Read(session.Actor)).Stock).Value); Assert.Equal(15,Assert.Single(await inventory.CostPolicies()).StandardUnitCost);
        await using (var db = factory.CreateDbContext()) Assert.Equal(45,(await db.Journal.SingleAsync(j => j.DocumentId == revision && j.Account == Account.Inventory)).Debit);
        await inventory.SetCostPolicy(new(Guid.NewGuid(),product,2,InventoryCostMethod.WeightedAverage,0,today,"Back to average at standard carrying value"));
        Assert.Equal(225,Assert.Single((await business.Read(session.Actor)).Stock).Value); await Receipt(15,25,today); Assert.Equal(600,Assert.Single((await business.Read(session.Actor)).Stock).Value);
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task StandardAllowanceRaisesAndReleasesWithoutChangingStandardValue()
    {
        await finance.CreateLedger(new(Guid.NewGuid(),6200,"Stock impairment",LedgerGroup.Expense)); await finance.CreateLedger(new(Guid.NewGuid(),6300,"Inventory allowance",LedgerGroup.Asset)); await finance.CreateLedger(new(Guid.NewGuid(),6301,"Other allowance",LedgerGroup.Asset));
        await inventory.SetCostPolicy(new(Guid.NewGuid(),product,0,InventoryCostMethod.Standard,12,today.AddDays(-1),"Initial standard")); await Receipt(10,12,today.AddDays(-1));
        var command = new StandardAllowanceCommand(Guid.NewGuid(),120,new Dictionary<Guid,decimal> { { product,9 } },6200,6300,"Damaged packaging; reviewed selling price");
        await Assert.ThrowsAsync<BusinessException>(() => inventory.ReviewStandardAllowance(command with { OperationId = Guid.NewGuid(),ExpectedStandardValue = 100 }));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.ReviewStandardAllowance(command with { OperationId = Guid.NewGuid(),UnitValues = new Dictionary<Guid,decimal>() }));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.ReviewStandardAllowance(command with { OperationId = Guid.NewGuid(),AllowanceAccount = 1000 }));
        var raised = await inventory.ReviewStandardAllowance(command);
        var view = Assert.Single(await inventory.StandardAllowances()); Assert.Equal(30,view.Allowance); Assert.Equal(90,view.NetValue); Assert.Equal(120,Assert.Single((await business.Read(session.Actor)).Stock).Value);
        await using (var db = factory.CreateDbContext()) { var lines = await db.Journal.Where(j => j.DocumentId == raised).ToListAsync(); Assert.Equal(30,lines.Single(l => (int)l.Account == 6200).Debit); Assert.Equal(30,lines.Single(l => (int)l.Account == 6300).Credit); }
        await Assert.ThrowsAsync<BusinessException>(() => inventory.ReviewStandardAllowance(command with { OperationId = Guid.NewGuid(),AllowanceAccount = 6301 }));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.ReviewStandardAllowance(command with { OperationId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<BusinessException>(() => inventory.SetCostPolicy(new(Guid.NewGuid(),product,1,InventoryCostMethod.WeightedAverage,0,today,"Leave standard with allowance")));
        var released = await inventory.ReviewStandardAllowance(command with { OperationId = Guid.NewGuid(),UnitValues = new Dictionary<Guid,decimal> { { product,11 } } });
        Assert.Equal(10,Assert.Single(await inventory.StandardAllowances()).Allowance);
        await using (var db = factory.CreateDbContext()) Assert.Equal(20,(await db.Journal.SingleAsync(j => j.DocumentId == released && (int)j.Account == 6300)).Debit);
        for (var i = 0; i < 10; i++) await Sale(1);
        await inventory.ReviewStandardAllowance(new(Guid.NewGuid(),0,new Dictionary<Guid,decimal>(),6200,6300,"Goods sold; release remaining allowance"));
        Assert.Equal(0,Assert.Single(await inventory.StandardAllowances()).Allowance);
        await inventory.SetCostPolicy(new(Guid.NewGuid(),product,1,InventoryCostMethod.WeightedAverage,0,today,"Leave standard"));
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task ScreenMigratesToStandardAndPostsAllowance()
    {
        await finance.CreateLedger(new(Guid.NewGuid(),6100,"Standard cost revaluation",LedgerGroup.Expense)); await finance.CreateLedger(new(Guid.NewGuid(),6200,"Stock impairment",LedgerGroup.Expense)); await finance.CreateLedger(new(Guid.NewGuid(),6300,"Inventory allowance",LedgerGroup.Asset));
        await Receipt(10,10,today.AddDays(-1));
        using var context = new BunitContext(); context.Services.AddSingleton<IInventoryService>(inventory); context.Services.AddSingleton<IBusinessService>(business); context.JSInterop.Mode = JSRuntimeMode.Loose;
        var screen = context.Render<InventoryValuationWorkspace>(p => p.Add(x => x.Actor,session.Actor));
        AngleSharp.Dom.IElement Field(string label) => screen.FindAll("label").First(l => l.TextContent.Trim().StartsWith(label,StringComparison.Ordinal)).QuerySelector("select,input")!;
        void Click(string text) => screen.FindAll("button").Single(b => b.TextContent == text).Click();
        screen.WaitForAssertion(() => Assert.Contains("Migrate existing stock to standard cost",screen.Markup));
        Field("Migration product").Change(product.ToString()); Field("New standard unit cost").Change("12"); Click("Preview standard-cost migration");
        screen.WaitForAssertion(() => Assert.Contains("difference 20.00 NLe",screen.Markup)); Field("Migration review reason").Change("Reviewed standard");
        Click("Post reviewed standard-cost migration"); screen.WaitForAssertion(() => Assert.Contains("Standard-cost migration posted",screen.Markup));
        Assert.Equal(120,Assert.Single((await business.Read(session.Actor)).Stock).Value);
        Field("Valuation basis").Change("Market"); screen.FindAll("input").Single(i => i.GetAttribute("aria-label") == "Reference price for DEPTH").Change("9"); Click("Build valuation comparison");
        screen.WaitForAssertion(() => Assert.Contains("Post reviewed standard-cost allowance",screen.Markup)); Field("Allowance review reason").Change("Damaged packaging");
        Click("Post reviewed standard-cost allowance"); screen.WaitForAssertion(() => Assert.Contains("Reviewed standard-cost allowance posted",screen.Markup));
        Assert.Contains("Standard-cost allowances",screen.Markup); Assert.Equal(30,Assert.Single(await inventory.StandardAllowances()).Allowance);
    }
    [Fact] public async Task StandardReceiptsSupportForeignBillsAndLaterInvoicesWithoutChangingStandardValue()
    {
        await inventory.SetCostPolicy(new(Guid.NewGuid(),product,0,InventoryCostMethod.Standard,12,today,"Initial standard"));
        var purchasing = new PurchasingService(factory,session,Options.Create(new TradingOptions()));
        await purchasing.SetRate(new(Guid.NewGuid(),"USD",today,2,"Reviewed exchange rate"));
        var foreign = await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",today,[new(product,1,50)],null,Currency:"USD"));
        await using (var db = factory.CreateDbContext()) { Assert.Equal(12,await db.Movements.Where(m => m.DocumentId == foreign).Select(m => m.Value).SingleAsync()); Assert.Equal(100,(await db.ForeignBills.SingleAsync(b => b.DocumentId == foreign)).SourceAmount * 2); }
        await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",today,[new(product,1,10)],null,InvoiceLater:true));
        var line = Assert.Single(await purchasing.AwaitingInvoice()); var input = new SupplierInvoiceCommand(Guid.NewGuid(),"Supplier","STD-LATER",today,null,[new(line.LineId,1,10)]);
        var preview = await purchasing.PreviewSupplierInvoice(input); Assert.Equal(-2,preview.PriceVariance); Assert.Equal(0,preview.ExchangeDifference); Assert.Equal(0,Assert.Single(preview.Lines).ToInventory);
        await purchasing.PostSupplierInvoice(input); Assert.Equal(24,Assert.Single((await business.Read(session.Actor)).Stock).Value);
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task FifoAndLifoComparisonsReconstructDifferentIssueOrdersWithoutPosting()
    {
        await Receipt(10,10,today.AddDays(-2)); await Receipt(10,20,today.AddDays(-1)); await Sale(5);
        var fifo = await inventory.Valuation(new(today,InventoryValuationMethod.Fifo)); var lifo = await inventory.Valuation(new(today,InventoryValuationMethod.Lifo));
        Assert.Equal(225,fifo.BookValue); Assert.Equal(250,fifo.SelectedValue); Assert.Equal(200,lifo.SelectedValue); Assert.Equal(225,Assert.Single((await business.Read(session.Actor)).Stock).Value);
        session.Actor = new("reader","FREETOWN",new HashSet<string>{"business.read"}); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inventory.Valuation(new(today,InventoryValuationMethod.Book)));
    }
    [Fact] public async Task SupplierCustodyIsOffBalanceUntilAcceptanceAndOwnedConsignmentRetainsAssets()
    {
        await inventory.SaveConsignmentSite(new(Guid.NewGuid(),"CON-SUP01","Supplier",ConsignmentOwnership.SupplierOwned));
        var origin = await inventory.ReceiveCustody(new(Guid.NewGuid(),"CON-SUP01",today,product,10,20,"Supplier custody")); Assert.Empty((await business.Read(session.Actor)).Stock);
        await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",today,[new(product,4,20)],null,CustodyReceiveId:origin));
        await inventory.ReturnCustody(new(Guid.NewGuid(),origin,today,3,"Return unneeded units")); var custody = Assert.Single(await inventory.CustodyStock()); Assert.Equal(3,custody.Remaining); Assert.Equal(4,custody.Accepted); Assert.Equal(3,custody.Returned);
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",today,[new(product,4,20)],null,CustodyReceiveId:origin)));
        await inventory.SaveConsignmentSite(new(Guid.NewGuid(),"CON-AGENT","Agent",ConsignmentOwnership.CompanyOwned)); await inventory.Transfer(new(Guid.NewGuid(),product,"MAIN","CON-AGENT",2,null,null,null,"Owned goods at agent"));
        Assert.Equal(80,(await business.Read(session.Actor)).Stock.Sum(s => s.Value)); await Sale(1,"CON-AGENT"); Assert.Equal(60,(await business.Read(session.Actor)).Stock.Sum(s => s.Value)); Assert.Equal(80,Assert.Single((await finance.Read(today,today)).Bills).Outstanding);
        await using var db = factory.CreateDbContext(); Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task PreInvoiceRejectionRestoresStockReopensOrderAndInvoicesOnlyAcceptedQuantity()
    {
        await Receipt(10,20,today.AddDays(-1)); var options = Options.Create(new TradingOptions()); var orders = new OrderService(factory,session,options);
        var order = await orders.Create(new(Guid.NewGuid(),OrderType.SalesOrder,"School",today,today,"Order",[new(product,5,100)]));
        var delivery = await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Delivery,"School",today,[new(product,5,100)],null,OrderId:order));
        var rejection = await inventory.RejectDelivery(new(Guid.NewGuid(),delivery,today,"Two rejected",[new(product,null,null,null,2)])); Assert.Equal(7,Assert.Single((await business.Read(session.Actor)).Stock).Quantity); Assert.Equal(3,Assert.Single((await orders.Read()).Single(o => o.Id == order).Lines).Fulfilled);
        var invoice = await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Sale,"School",today,[new(product,3,100)],null,OrderId:order,DeliveryId:delivery));
        Assert.Empty(await inventory.PendingDeliveries()); Assert.Equal(7,Assert.Single((await business.Read(session.Actor)).Stock).Quantity);
        await Assert.ThrowsAsync<BusinessException>(() => inventory.RejectDelivery(new(Guid.NewGuid(),delivery,today,"Too late",[new(product,null,null,null,1)])));
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor,new(Guid.NewGuid(),rejection,"Cannot change invoiced remainder")));
        var returns = new ReturnService(factory,session,options); Assert.Equal(3,Assert.Single((await returns.Read()).Single(d => d.Id == invoice).Lines).OriginalQuantity);
        await returns.Post(new(Guid.NewGuid(),invoice,"One accepted unit returned",[new(product,null,null,null,1)]));
        Assert.Equal(8,Assert.Single((await business.Read(session.Actor)).Stock).Quantity); Assert.Equal(200,(await finance.Read(today,today)).Bills.Single(b => b.Id == invoice).Outstanding);
        await using var db = factory.CreateDbContext(); Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task CostingAndValuationScreenConfiguresNewItemAndBuildsReadOnlyReport()
    {
        using var context = new BunitContext(); context.Services.AddSingleton<IInventoryService>(inventory); context.Services.AddSingleton<IBusinessService>(business); context.JSInterop.Mode = JSRuntimeMode.Loose;
        var screen = context.Render<InventoryValuationWorkspace>(p => p.Add(x => x.Actor,session.Actor));
        AngleSharp.Dom.IElement Field(string label) => screen.FindAll("label").First(l => l.TextContent.Trim().StartsWith(label,StringComparison.Ordinal)).QuerySelector("select,input")!;
        screen.WaitForAssertion(() => Assert.Contains("Inventory depth item",screen.Markup)); Field("Costing product").Change(product.ToString()); Field("Book costing method").Change("Fifo"); Field("Costing policy from").Change(today.AddDays(-1).ToString("yyyy-MM-dd")); Field("Costing reason").Change("Initial FIFO");
        screen.FindAll("button").Single(b => b.TextContent == "Save reviewed costing policy").Click(); screen.WaitForAssertion(() => Assert.Contains("policy saved",screen.Markup));
        await Receipt(10,10,today.AddDays(-1)); Field("Valuation basis").Change("Fifo"); screen.FindAll("button").Single(b => b.TextContent == "Build valuation comparison").Click(); screen.WaitForAssertion(() => Assert.Contains("selected value 100.00",screen.Markup));
    }
    [Fact] public async Task CustodyScreenReceivesAndAcceptsSupplierUnits()
    {
        using var context = new BunitContext(); context.Services.AddSingleton<IInventoryService>(inventory); context.Services.AddSingleton<IBusinessService>(business); context.JSInterop.Mode = JSRuntimeMode.Loose;
        var screen = context.Render<ConsignmentWorkspace>(p => p.Add(x => x.Actor,session.Actor));
        AngleSharp.Dom.IElement Field(string label) => screen.FindAll("label").First(l => l.TextContent.Trim().StartsWith(label,StringComparison.Ordinal)).QuerySelector("select,input")!;
        screen.WaitForAssertion(() => Assert.Contains("Inventory depth item",screen.Markup)); Field("Consignment site code").Change("CON-UI"); Field("Consignment partner").Change("Supplier"); Field("Stock ownership").Change("SupplierOwned"); screen.FindAll("button").Single(b => b.TextContent == "Register consignment site").Click(); screen.WaitForAssertion(() => Assert.Contains("site registered",screen.Markup));
        Field("Supplier custody site").Change("CON-UI"); Field("Custody product").Change(product.ToString()); Field("Custody quantity").Change("10"); Field("Reference unit cost").Change("20"); Field("Custody reference").Change("Screen receipt"); screen.FindAll("button").Single(b => b.TextContent == "Record supplier custody receipt").Click(); screen.WaitForAssertion(() => Assert.Contains("without an inventory asset",screen.Markup));
        var origin = Assert.Single(await inventory.CustodyStock()); Field("Remaining custody lot").Change(origin.ReceiveId.ToString()); Field("Accept / return quantity").Change("4"); screen.FindAll("button").Single(b => b.TextContent == "Accept and post supplier bill").Click(); screen.WaitForAssertion(() => Assert.Contains("posted as owned stock",screen.Markup)); Assert.Equal(4,Assert.Single((await business.Read(session.Actor)).Stock).Quantity);
    }
    [Fact] public async Task ReviewedWriteDownPreservesQuantityRejectsStaleValueAndReverses()
    {
        await Receipt(10,20,today); var input = new InventoryWriteDownCommand(Guid.NewGuid(),200,new Dictionary<Guid,decimal>{{product,12}},5100,"Reviewed damaged packaging and market evidence");
        var id = await inventory.WriteDown(input); Assert.Equal(id,await inventory.WriteDown(input));
        var stock = Assert.Single((await business.Read(session.Actor)).Stock); Assert.Equal(10,stock.Quantity); Assert.Equal(120,stock.Value);
        await Assert.ThrowsAsync<BusinessException>(() => inventory.WriteDown(input with { OperationId = Guid.NewGuid() }));
        await business.Reverse(session.Actor,new(Guid.NewGuid(),id,"Market assessment corrected")); Assert.Equal(200,Assert.Single((await business.Read(session.Actor)).Stock).Value);
        await using (var db = factory.CreateDbContext()) Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner","FREETOWN",BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
