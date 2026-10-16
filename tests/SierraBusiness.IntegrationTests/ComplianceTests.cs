using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Specification Skill 23 (0.20.0): effective-dated tax categories and rates, historical invoices keep their tax, returns refund only the tax charged,
/// the tax return reconciles to the ledger, versioned company details and templates, and fiscal registration that never blocks a sale.</summary>
public sealed class ComplianceTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-compliance-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private BusinessService business = null!; private ReturnService returns = null!; private PeriodService periods = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private IOptions<TradingOptions> options = null!;
    private Guid widget, drug, food;
    private static readonly HashSet<string> Cashier = ["business.read", "sales.create"];
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        options = Options.Create(new TradingOptions());
        business = new(factory, options); returns = new(factory, session, options); periods = new(factory, session, options);
        await periods.SetTax(new(Guid.NewGuid(), today.AddDays(-20), .15m, "GST 15%"));
        widget = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 200, TrackingMode.Quantity, MinimumPrice: 1));
        drug = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "AMX-500", "Amoxicillin 500 mg", "Medicines", "pack", 50, TrackingMode.Quantity, MinimumPrice: 1));
        food = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "RICE-50", "Rice 50 kg", "Food", "bag", 100, TrackingMode.Quantity, MinimumPrice: 1));
        foreach (var p in new[] { widget, drug, food }) await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today.AddDays(-20), [new(p, 50, 10)], null));
        // Categories that took effect in the past (the service only accepts today or later, so these are recorded directly).
        await using (var db = factory.CreateDbContext())
        {
            db.TaxAssignments.Add(new() { Id = Guid.NewGuid(), ProductId = drug, From = today.AddDays(-15), Category = TaxCategory.Exempt, Reason = "GST Act schedule: medicines", CreatedBy = "owner", Time = DateTimeOffset.UtcNow });
            db.TaxAssignments.Add(new() { Id = Guid.NewGuid(), ProductId = food, From = today.AddDays(-15), Category = TaxCategory.ZeroRated, Reason = "Finance Act: staple food", CreatedBy = "owner", Time = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    private ComplianceService Compliance(IFiscalIntegration? fiscal = null) => new(factory, session, options, fiscal ?? new NoFiscalDevice());
    [Fact] public async Task ExpenseInputTaxRequiresEvidencePostsOnceAndReversesBeforeSource()
    {
        var service = Compliance(); await service.SaveProfile(new(Guid.NewGuid(), new(LegalName: "Tax company", TaxRegistered: true, Identifiers: [new("TIN", "123456")])));
        var finance = new FinanceService(factory, session, options);
        await finance.CreateLedger(new(Guid.NewGuid(), 6101, "Taxable expenses", LedgerGroup.Expense));
        await finance.CreateLedger(new(Guid.NewGuid(), 6102, "Input GST recovery", LedgerGroup.Asset));
        var paid = await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Supplier GST invoice", [new(6101, 115, 0), new(1000, 0, 115)], []));
        var claim = new InputTaxCommand(Guid.NewGuid(), paid, today, "Supplier", "TIN-1", "INV-1", false, 100, 15, 12, 6101, 6102, "Reviewed 80 percent business taxable use");
        var id = await service.RecordInputTax(claim); Assert.Equal(id, await service.RecordInputTax(claim));
        var report = await service.NetTaxReturn(today, today); Assert.Equal(12, report.AllowableInputTax); Assert.Equal(-12, report.NetPayable); Assert.Equal(100, report.LocalNet); Assert.Single(report.Inputs);
        Assert.Equal(103, (await finance.Read(today, today)).Balances.Single(b => b.Code == 6101).Closing);
        Assert.NotEmpty(await service.ExportNetTaxReturn(today, today));
        await Assert.ThrowsAsync<BusinessException>(() => service.RecordInputTax(claim with { OperationId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<BusinessException>(() => service.RecordInputTax(claim with { AllowableTax = 16 }));
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), paid, "Cancel source")));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), id, "Cancel tax review"));
        Assert.Equal(0, (await service.NetTaxReturn(today, today)).AllowableInputTax);
        await business.Reverse(session.Actor, new(Guid.NewGuid(), paid, "Cancel source after claim")); await Verified();
    }
    private void AsOwner() => session.Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet());
    [Fact] public async Task RecipientReliefTracksNetCeilingAndReturnsWithoutExemptingProductsGlobally()
    {
        var service = Compliance(); await service.SaveProfile(new(Guid.NewGuid(),new(LegalName:"Relief seller",TaxRegistered:true,Identifiers:[new("TIN","123456")]))); await TestParties.Register(factory,"Relief recipient"); Guid customer; await using (var db = factory.CreateDbContext()) customer = await db.Parties.Where(p => p.Name == "Relief recipient").Select(p => p.Id).SingleAsync();
        var grant = await service.RecordReliefGrant(new(Guid.NewGuid(),customer,"NRA-RELIEF-1",today,today.AddDays(30),300,"Certificate and this branch's approved allocation reviewed"));
        var sale = await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Sale,"Relief recipient",today,[new(widget,1,100)],PaymentMethod.Cash,ReliefGrantId:grant)); Assert.Equal(0,Field(await Doc(sale),"Tax")); Assert.Equal(100,Field(await Doc(sale),"Relieved")); Assert.Equal(200,Assert.Single(await service.ReliefGrants()).Remaining);
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Sale,"Wrong buyer",today,[new(widget,1,100)],PaymentMethod.Cash,ReliefGrantId:grant)));
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Sale,"Relief recipient",today,[new(widget,1,400)],PaymentMethod.Cash,ReliefGrantId:grant)));
        await Assert.ThrowsAsync<BusinessException>(() => service.SetTaxCategory(new(Guid.NewGuid(),widget,TaxCategory.Relieved,today,"Relief cannot be assigned to all buyers")));
        Assert.Equal(100,(await service.TaxReturn(today,today)).Lines.Last().Relieved);
        await returns.Post(new(Guid.NewGuid(),sale,"Recipient returned relieved goods",[new(widget,null,null,null,1)])); Assert.Equal(300,Assert.Single(await service.ReliefGrants()).Remaining); Assert.Equal(0,(await service.TaxReturn(today,today)).Lines.Last().Relieved); await Verified();
    }
    [Fact] public async Task MonthlyGstRecordReconcilesLocksPeriodAndPostsOnlyOnce()
    {
        var service = Compliance(); await service.SaveProfile(new(Guid.NewGuid(),new(LegalName:"GST filer",TaxRegistered:true,Identifiers:[new("TIN","123456")]))); var finance = new FinanceService(factory,session,options); await finance.CreateLedger(new(Guid.NewGuid(),6501,"GST carry forward",LedgerGroup.Asset)); var to = new DateOnly(today.Year,today.Month,1).AddDays(-1); var from = new DateOnly(to.Year,to.Month,1); await MixedSale(to);
        var preview = await service.PreviewTaxFiling(from,to); var command = new TaxFilingCommand(Guid.NewGuid(),from,to,preview.CalculationFingerprint,"NRA-REVIEWED-1","All invoices and branch sources reconciled",1010,6501,0,true); var id = await service.RecordTaxFiling(command); Assert.Equal(id,await service.RecordTaxFiling(command)); var filing = Assert.Single(await service.TaxFilings()); Assert.Equal(preview.Report.NetPayable,filing.Paid); Assert.Equal(to,(await periods.Read()).ClosedThrough);
        await Assert.ThrowsAsync<BusinessException>(() => service.RecordTaxFiling(command with { OperationId = Guid.NewGuid() })); await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor,new(Guid.NewGuid(),id,"Declared returns cannot be erased"))); Assert.Equal(0,(await finance.Read(from,today)).Balances.Single(a => a.Code == 2100).Closing); await Verified();
    }
    [Fact] public async Task MonthlyGstRoundingAndCarryCreditBalanceWithoutRewritingTheWorksheet()
    {
        var service = Compliance(); await service.SaveProfile(new(Guid.NewGuid(),new(LegalName:"GST filer",TaxRegistered:true,Identifiers:[new("TIN","123456")])));
        var finance = new FinanceService(factory,session,options); await finance.CreateLedger(new(Guid.NewGuid(),6501,"GST carry forward",LedgerGroup.Asset));
        await finance.PostVoucher(new(Guid.NewGuid(),VoucherType.Journal,today,"Prior reviewed GST credit",[new(6501,10,0),new(5100,0,10)],[]));
        var to = new DateOnly(today.Year,today.Month,1).AddDays(-1); var from = new DateOnly(to.Year,to.Month,1); await MixedSale(to); var preview = await service.PreviewTaxFiling(from,to);
        var declared = preview.Report.NetPayable + .25m; var command = new TaxFilingCommand(Guid.NewGuid(),from,to,preview.CalculationFingerprint,"ROUND-1","Reviewed rounding and brought-forward credit",1010,6501,10,true,declared,5100);
        await Assert.ThrowsAsync<BusinessException>(() => service.RecordTaxFiling(command with { DeclaredNetTax = preview.Report.NetPayable+1.01m }));
        await service.RecordTaxFiling(command); var filing = Assert.Single(await service.TaxFilings()); Assert.Equal(declared-10,filing.Paid); Assert.Equal(declared,filing.NetTax); Assert.Equal(preview.Report.NetPayable,(await service.NetTaxReturn(from,to)).NetPayable);
        var balances = (await finance.Read(from,today)).Balances; Assert.Equal(0,balances.Single(a => a.Code == 6501).Closing); Assert.Equal(0,balances.Single(a => a.Code == 2100).Closing); await Verified();
    }
    [Fact] public async Task StockInputTaxReducesCarryingValueWithoutChangingQuantityAndProtectsSupplierReturn()
    {
        var service = Compliance(); await service.SaveProfile(new(Guid.NewGuid(),new(LegalName:"Stock tax company",TaxRegistered:true,Identifiers:[new("TIN","123456")]))); var finance = new FinanceService(factory,session,options); await finance.CreateLedger(new(Guid.NewGuid(),6401,"Input GST recovery",LedgerGroup.Asset));
        var receipt = await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Tax supplier",today,[new(widget,1,115)],null)); var before = (await business.Read(session.Actor)).Stock.Where(s => s.ProductId == widget).Sum(s => s.Value);
        var claim = new InputTaxCommand(Guid.NewGuid(),receipt,today,"Tax supplier","TIN-SUP","GST-INV-1",false,100,15,12,1200,6401,"Reviewed stock invoice; allowable share excludes private use",true); var id = await service.RecordInputTax(claim); Assert.Equal(before-12,(await business.Read(session.Actor)).Stock.Where(s => s.ProductId == widget).Sum(s => s.Value));
        await Assert.ThrowsAsync<BusinessException>(() => returns.Post(new(Guid.NewGuid(),receipt,"Cannot return claimed GST without correction",[new(widget,null,null,null,1)],ReturnDisposition.SupplierCredit)));
        await business.Reverse(session.Actor,new(Guid.NewGuid(),id,"Correct allowable-tax review")); Assert.Equal(before,(await business.Read(session.Actor)).Stock.Where(s => s.ProductId == widget).Sum(s => s.Value)); await Verified();
    }
    [Fact] public async Task StockInputTaxAfterIssueSplitsBetweenStockAndIssuedGoods()
    {
        var service = Compliance(); await service.SaveProfile(new(Guid.NewGuid(),new(LegalName:"Stock tax company",TaxRegistered:true,Identifiers:[new("TIN","123456")]))); var finance = new FinanceService(factory,session,options); await finance.CreateLedger(new(Guid.NewGuid(),6401,"Input GST recovery",LedgerGroup.Asset));
        var taxed = await business.AddProduct(session.Actor,new(Guid.NewGuid(),"TAXED","Taxed stock","General","piece",200,TrackingMode.Quantity,MinimumPrice:1));
        var receipt = await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Tax supplier",today,[new(taxed,2,115)],null));
        await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Sale,"Cash customer",today,[new(taxed,1,200)],PaymentMethod.Cash));
        var preview = await service.PreviewStockInputTax(receipt,30); Assert.True(preview.LaterIssues); Assert.Equal(15,preview.SuggestedIssued); Assert.Equal(15,preview.SuggestedOnHand);
        var claim = new InputTaxCommand(Guid.NewGuid(),receipt,today,"Tax supplier","TIN-SUP","GST-ISSUED",false,200,30,30,1200,6401,"Reviewed after partial sale",true);
        await Assert.ThrowsAsync<BusinessException>(() => service.RecordInputTax(claim));
        await Assert.ThrowsAsync<BusinessException>(() => service.RecordInputTax(claim with { IssuedTax = 31,IssuedAccount = 5000 }));
        await Assert.ThrowsAsync<BusinessException>(() => service.RecordInputTax(claim with { IssuedTax = 15,IssuedAccount = 1000 }));
        var id = await service.RecordInputTax(claim with { IssuedTax = 15,IssuedAccount = 5000 });
        Assert.Equal(100,(await business.Read(session.Actor)).Stock.Where(s => s.ProductId == taxed).Sum(s => s.Value));
        await using (var db = factory.CreateDbContext()) { var lines = await db.Journal.Where(j => j.DocumentId == id).ToListAsync(); Assert.Equal(30,lines.Single(l => (int)l.Account == 6401).Debit); Assert.Equal(15,lines.Single(l => l.Account == Account.CostOfSales).Credit); Assert.Equal(15,lines.Single(l => l.Account == Account.Inventory).Credit); }
        Assert.Equal(30,(await service.NetTaxReturn(today,today)).AllowableInputTax);
        await business.Reverse(session.Actor,new(Guid.NewGuid(),id,"Allowability corrected")); Assert.Equal(115,(await business.Read(session.Actor)).Stock.Where(s => s.ProductId == taxed).Sum(s => s.Value)); await Verified();
    }
    [Fact] public async Task StandardCostStockInputTaxReducesPurchaseVariance()
    {
        var service = Compliance(); await service.SaveProfile(new(Guid.NewGuid(),new(LegalName:"Stock tax company",TaxRegistered:true,Identifiers:[new("TIN","123456")]))); var finance = new FinanceService(factory,session,options); await finance.CreateLedger(new(Guid.NewGuid(),6401,"Input GST recovery",LedgerGroup.Asset));
        var standard = await business.AddProduct(session.Actor,new(Guid.NewGuid(),"STD-TAX","Standard taxed","General","piece",200,TrackingMode.Quantity,MinimumPrice:1));
        await new InventoryService(factory,session,options).SetCostPolicy(new(Guid.NewGuid(),standard,0,InventoryCostMethod.Standard,90,today,"Standard"));
        var receipt = await business.Post(session.Actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Tax supplier",today,[new(standard,1,115)],null));
        var preview = await service.PreviewStockInputTax(receipt,15); Assert.Equal(15,preview.StandardVariance); Assert.False(preview.LaterIssues);
        var id = await service.RecordInputTax(new(Guid.NewGuid(),receipt,today,"Tax supplier","TIN-SUP","GST-STD",false,100,15,15,1200,6401,"Standard-cost receipt reviewed",true));
        Assert.Equal(90,(await business.Read(session.Actor)).Stock.Where(s => s.ProductId == standard).Sum(s => s.Value));
        await using (var db = factory.CreateDbContext()) Assert.Equal(15,(await db.Journal.SingleAsync(j => j.DocumentId == id && (int)j.Account == 5100)).Credit);
        await Verified();
    }
    [Fact] public async Task RecordedReturnsAreCorrectedSeparatelyWithPaymentCreditAndPenalty()
    {
        var service = Compliance(); await service.SaveProfile(new(Guid.NewGuid(),new(LegalName:"GST filer",TaxRegistered:true,Identifiers:[new("TIN","123456")]))); var finance = new FinanceService(factory,session,options);
        await finance.CreateLedger(new(Guid.NewGuid(),6501,"GST carry forward",LedgerGroup.Asset)); await finance.CreateLedger(new(Guid.NewGuid(),6502,"GST corrections",LedgerGroup.Expense)); await finance.CreateLedger(new(Guid.NewGuid(),6503,"Tax penalties",LedgerGroup.Expense));
        var to = new DateOnly(today.Year,today.Month,1).AddDays(-1); var from = new DateOnly(to.Year,to.Month,1); await MixedSale(to);
        var preview = await service.PreviewTaxFiling(from,to); var filing = await service.RecordTaxFiling(new(Guid.NewGuid(),from,to,preview.CalculationFingerprint,"NRA-1","Reconciled",1010,6501,0,true));
        var more = new TaxFilingCorrectionCommand(Guid.NewGuid(),filing,"NRA-1-A1","Missed invoice found on review",20,0,6502,1010,6501,0,5,6503);
        await Assert.ThrowsAsync<BusinessException>(() => service.RecordTaxFilingCorrection(more with { FilingId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<BusinessException>(() => service.RecordTaxFilingCorrection(more with { AdjustmentAccount = 2100 }));
        await Assert.ThrowsAsync<BusinessException>(() => service.RecordTaxFilingCorrection(more with { CarryCreditUsed = 5 }));
        var id = await service.RecordTaxFilingCorrection(more); Assert.Equal(id,await service.RecordTaxFilingCorrection(more));
        await Assert.ThrowsAsync<BusinessException>(() => service.RecordTaxFilingCorrection(more with { OperationId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor,new(Guid.NewGuid(),id,"Declared corrections cannot be erased")));
        var less = await service.RecordTaxFilingCorrection(new(Guid.NewGuid(),filing,"NRA-1-A2","Input invoice omitted",0,8,6502,1010,6501,0));
        var views = await service.TaxFilingCorrections(); Assert.Equal(2,views.Count); var first = views.Single(v => v.Id == id); Assert.Equal(20,first.Paid); Assert.Equal(5,first.Penalty); Assert.Equal(from,first.From);
        Assert.Equal(8,views.Single(v => v.Id == less).NewCredit);
        var used = await service.RecordTaxFilingCorrection(new(Guid.NewGuid(),filing,"NRA-1-A3","Late output adjustment",10,0,6502,1010,6501,8));
        Assert.Equal(2,(await service.TaxFilingCorrections()).Single(v => v.Id == used).Paid);
        var balances = (await finance.Read(from,today)).Balances; Assert.Equal(0,balances.Single(a => a.Code == 6501).Closing); Assert.Equal(22,balances.Single(a => a.Code == 6502).Closing); Assert.Equal(5,balances.Single(a => a.Code == 6503).Closing);
        Assert.Equal(preview.Report.NetPayable,(await service.NetTaxReturn(from,to)).NetPayable); await Verified();
    }
    [Fact] public async Task CancellationsAffectTheirOwnTaxPeriodAndDoNotRewriteEarlierOutputOrInput()
    {
        var past = new DateOnly(today.Year,today.Month,1).AddDays(-1); var start = new DateOnly(past.Year,past.Month,1); var invoice = await MixedSale(past); var before = await Compliance().TaxReturn(start,past); Assert.True(before.OutputTax > 0);
        await business.Reverse(session.Actor,new(Guid.NewGuid(),invoice,"Invoice cancelled in the next tax period")); Assert.Equal(before.OutputTax,(await Compliance().TaxReturn(start,past)).OutputTax); var current = await Compliance().TaxReturn(new(today.Year,today.Month,1),today); Assert.Equal(-before.OutputTax,current.OutputTax); Assert.True(current.Check.Ok);
        var service = Compliance(); await service.SaveProfile(new(Guid.NewGuid(),new(LegalName:"Tax company",TaxRegistered:true,Identifiers:[new("TIN","123456")]))); var finance = new FinanceService(factory,session,options); await finance.CreateLedger(new(Guid.NewGuid(),6402,"Tax expense",LedgerGroup.Expense)); await finance.CreateLedger(new(Guid.NewGuid(),6403,"Input recovery",LedgerGroup.Asset));
        var paid = await finance.PostVoucher(new(Guid.NewGuid(),VoucherType.Payment,past,"Taxed source expense",[new(6402,115,0),new(1000,0,115)],[])); var claim = await service.RecordInputTax(new(Guid.NewGuid(),paid,past,"Supplier","TIN-1","INV-OLD",false,100,15,15,6402,6403,"Allowability review")); await business.Reverse(session.Actor,new(Guid.NewGuid(),claim,"Input allowance corrected next month"));
        Assert.Equal(15,(await service.NetTaxReturn(start,past)).AllowableInputTax); Assert.Equal(-15,(await service.NetTaxReturn(new(today.Year,today.Month,1),today)).AllowableInputTax); await Verified();
    }
    private async Task<BusinessDocument> Doc(Guid id) { await using var db = factory.CreateDbContext(); return await db.Documents.AsNoTracking().SingleAsync(x => x.Id == id); }
    private static decimal Field(BusinessDocument d, string name) => JsonDocument.Parse(d.Detail).RootElement.GetProperty(name).GetDecimal();
    private Task<Guid> MixedSale(DateOnly date) => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Connaught Hospital", date, [new(widget, 1, 200), new(drug, 2, 50), new(food, 1, 100)], PaymentMethod.Cash));
    private async Task Verified() { await using var db = factory.CreateDbContext(); var report = await HistoryProtection.Verify(db); Assert.True(report.Ok, string.Join("; ", report.Problems)); }

    [Fact] public async Task OnlyStandardRatedLinesAreTaxedAndPostedInvoicesKeepTheirTax()
    {
        var first = await MixedSale(today.AddDays(-10));
        var d = await Doc(first);
        Assert.Equal(430m, d.Total); // 15% of the widget only: medicines exempt, rice zero-rated
        Assert.Equal(200m, Field(d, "Taxable")); Assert.Equal(100m, Field(d, "Exempt")); Assert.Equal(100m, Field(d, "ZeroRated")); Assert.Equal(30m, Field(d, "Tax"));

        await periods.SetTax(new(Guid.NewGuid(), today.AddDays(-5), .2m, "GST 20%")); // a rate change is a dated setting, not a code change
        var compliance = Compliance();
        await compliance.SetTaxCategory(new(Guid.NewGuid(), food, TaxCategory.Standard, today, "Zero rating withdrawn"));
        var second = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Connaught Hospital", today, [new(widget, 1, 200), new(food, 1, 100)], PaymentMethod.Cash));
        Assert.Equal(360m, (await Doc(second)).Total); // both standard-rated at 20%
        Assert.Equal(430m, (await Doc(first)).Total); // the earlier invoice is unchanged

        await compliance.SetTaxCategory(new(Guid.NewGuid(), widget, TaxCategory.Exempt, today.AddDays(3), "Scheduled exemption"));
        var view = (await compliance.ProductTax()).Single(x => x.ProductId == widget);
        Assert.Equal(TaxCategory.Standard, view.Current); Assert.Equal(TaxCategory.Exempt, view.Next); Assert.Equal(today.AddDays(3), view.NextFrom);
        Assert.Equal(TaxCategory.Exempt, (await compliance.ProductTax()).Single(x => x.ProductId == drug).Current);

        await Assert.ThrowsAsync<BusinessException>(() => compliance.SetTaxCategory(new(Guid.NewGuid(), widget, TaxCategory.ZeroRated, today.AddDays(-1), "Back-dated"))); // posted history cannot be re-taxed
        await Assert.ThrowsAsync<BusinessException>(() => compliance.SetTaxCategory(new(Guid.NewGuid(), widget, TaxCategory.ZeroRated, today.AddDays(3), "Same day twice")));
        await Assert.ThrowsAsync<BusinessException>(() => compliance.SetTaxCategory(new(Guid.NewGuid(), widget, TaxCategory.ZeroRated, today.AddDays(4), " ")));
        session.Actor = new("cashier", "FREETOWN", Cashier);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => compliance.SetTaxCategory(new(Guid.NewGuid(), widget, TaxCategory.ZeroRated, today.AddDays(5), "Not allowed")));
        AsOwner(); await Verified();
    }

    [Fact] public async Task ReturnsRefundOnlyTheTaxThatWasChargedAndTheTaxReturnReconciles()
    {
        var sale = await MixedSale(today.AddDays(-10));
        var exemptReturn = await returns.Post(new(Guid.NewGuid(), sale, "Expired packs", [new(drug, null, null, null, 2)], ReturnDisposition.CashRefund));
        var r1 = await Doc(exemptReturn);
        Assert.Equal(0m, Field(r1, "Tax")); Assert.Equal(-100m, r1.Total); // net 100 refunded, no tax on exempt medicines
        var standardReturn = await returns.Post(new(Guid.NewGuid(), sale, "Damaged", [new(widget, null, null, null, 1)], ReturnDisposition.CashRefund));
        Assert.Equal(30m, Field(await Doc(standardReturn), "Tax")); // the full 30 charged on the widget

        var report = await Compliance().TaxReturn(today.AddDays(-30), today);
        Assert.True(report.Check.Ok, $"{report.Check.Report} vs {report.Check.Source}");
        var sales = report.Lines[0]; var back = report.Lines[1]; var net = report.Lines[2];
        Assert.Equal((200m, 100m, 100m, 30m), (sales.Standard, sales.ZeroRated, sales.Exempt, sales.Tax));
        Assert.Equal((-200m, 0m, -100m, -30m), (back.Standard, back.ZeroRated, back.Exempt, back.Tax));
        Assert.Equal(0m, report.OutputTax); Assert.Equal(100m, net.ZeroRated);
        Assert.Equal(3, report.Documents.Count);

        session.Actor = new("cashier", "FREETOWN", Cashier);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Compliance().TaxReturn(today.AddDays(-30), today));
        AsOwner(); await Verified();
    }

    [Fact] public async Task CompanyDetailsAndTemplatesAreVersionedValidatedAndReadableForPrinting()
    {
        var compliance = Compliance();
        var profile = new CompanyProfile(LegalName: "Index Medical Supplies Ltd", Address: "12 Siaka Stevens Street, Freetown", Identifiers: [new("TIN", "1000123456"), new("GST No.", "GST-0099"), new("Reg. No.", "C-2019-77", false)], TaxRegistered: true);
        await compliance.SaveProfile(new(Guid.NewGuid(), profile));
        var saved = await compliance.Profile();
        Assert.Equal(1, saved.Version); Assert.Equal("owner", saved.SavedBy); Assert.Equal(3, saved.Ids.Count);
        Assert.Equal("NLe 1,234.50", saved.Money(1234.5m));
        Assert.Equal("1,234.50 Le", (saved with { CurrencySymbol = "Le", SymbolAfter = true }).Money(1234.5m));

        await Assert.ThrowsAsync<BusinessException>(() => compliance.SaveProfile(new(Guid.NewGuid(), saved with { Version = 0, LegalName = "Stale copy" }))); // someone else saved first
        await Assert.ThrowsAsync<BusinessException>(() => compliance.SaveProfile(new(Guid.NewGuid(), saved with { Identifiers = [new("TIN", "1", false)] }))); // registered: an identifier must print
        await Assert.ThrowsAsync<BusinessException>(() => compliance.SaveProfile(new(Guid.NewGuid(), saved with { CurrencyCode = "LEONE" })));
        await Assert.ThrowsAsync<BusinessException>(() => compliance.SaveProfile(new(Guid.NewGuid(), saved with { Identifiers = [new("TIN", "1"), new("tin", "2")] })));
        await compliance.SaveProfile(new(Guid.NewGuid(), saved with { Phone = "+232 76 000000" }));
        Assert.Equal(2, (await compliance.Profile()).Version);

        const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        await compliance.SaveTemplate(new(Guid.NewGuid(), new DocumentTemplate(Logo: png, InvoiceTerms: "Payment within 30 days.", BankDetails: "Rokel Commercial Bank · 001-234567", ReceiptWidth: "80mm")));
        await Assert.ThrowsAsync<BusinessException>(() => compliance.SaveTemplate(new(Guid.NewGuid(), new DocumentTemplate(Version: 1, Logo: "data:text/html;base64,PGI+"))));
        await Assert.ThrowsAsync<BusinessException>(() => compliance.SaveTemplate(new(Guid.NewGuid(), new DocumentTemplate(Version: 1, AccentColor: "red"))));
        await Assert.ThrowsAsync<BusinessException>(() => compliance.SaveTemplate(new(Guid.NewGuid(), new DocumentTemplate(Version: 1, ReceiptWidth: "58mm"))));

        session.Actor = new("cashier", "FREETOWN", Cashier);
        var setup = await compliance.PrintSetup(); // anyone who can print reads the letterhead
        Assert.Equal("Index Medical Supplies Ltd", setup.Company.LegalName); Assert.Equal(png, setup.Template.Logo); Assert.Equal("80mm", setup.Template.ReceiptWidth);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => compliance.SaveProfile(new(Guid.NewGuid(), setup.Company with { LegalName = "Changed" })));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => compliance.SaveTemplate(new(Guid.NewGuid(), setup.Template)));
        AsOwner(); await Verified();
    }

    [Fact] public async Task FiscalRegistrationNeverBlocksASaleAndPendingInvoicesAreSentLater()
    {
        var before = await MixedSale(today.AddDays(-10)); // before fiscal registration started
        var compliance = Compliance();
        await compliance.SaveProfile(new(Guid.NewGuid(), new CompanyProfile(LegalName: "Index Medical Supplies Ltd", Identifiers: [new("TIN", "1000123456")], TaxRegistered: true, FiscalFrom: today)));
        var sale = await MixedSale(today); // posts even though no fiscal device is connected
        var reversed = await MixedSale(today);
        await business.Reverse(session.Actor, new(Guid.NewGuid(), reversed, "Keyed twice"));

        var status = await compliance.Fiscal();
        Assert.True(status.Enabled); Assert.False(status.ConnectorAvailable); Assert.Equal(1, status.Pending);
        Assert.Null(await compliance.FiscalFor(before));
        Assert.Equal(FiscalState.Pending, (await compliance.FiscalFor(sale))!.State);
        var offline = await compliance.RegisterPending();
        Assert.Equal(1, offline.StillPending); Assert.Contains("not available", offline.Message);

        var device = new FakeDevice(new(true, "SL-FISC-000001"));
        var connected = Compliance(device);
        var run = await connected.RegisterPending();
        Assert.Equal(1, run.Registered);
        var invoice = Assert.Single(device.Sent);
        Assert.Equal(430m, invoice.Total); Assert.Equal(3, invoice.Lines.Count); Assert.Contains(invoice.Lines, l => l.Category == TaxCategory.Exempt); Assert.Equal("TIN", invoice.Seller.Single().Label);
        var registered = (await connected.FiscalFor(sale))!;
        Assert.Equal(FiscalState.Registered, registered.State); Assert.Equal("SL-FISC-000001", registered.Code);
        Assert.Equal(0, (await connected.RegisterPending()).Tried); // registered once only

        var refused = await MixedSale(today);
        var rejecting = Compliance(new FakeDevice(new(false, Message: "Buyer TIN invalid", Permanent: true)));
        Assert.Equal(1, (await rejecting.RegisterPending()).Rejected);
        Assert.Equal(FiscalState.Rejected, (await rejecting.FiscalFor(refused))!.State);
        var report = await connected.TaxReturn(today, today);
        Assert.Contains(report.Documents, d => d.Fiscal == "Registered"); Assert.Contains(report.Documents, d => d.Fiscal == "Rejected");
        await Verified();
    }

    [Fact] public void AmountsAreWrittenInWords()
    {
        Assert.Equal("One million two hundred and thirty-four thousand five hundred and sixty-seven Leones and fifty cents only", AmountWords.Write(1_234_567.50m, "Leones", "cents"));
        Assert.Equal("One thousand and five Leones only", AmountWords.Write(1005m, "Leones", "cents"));
        Assert.Equal("Zero Leones and one cents only", AmountWords.Write(0.01m, "Leones", "cents"));
        Assert.Equal("Twenty Leones only", AmountWords.Write(19.999m, "Leones", "cents"));
    }

    private sealed class FakeDevice(FiscalResult result) : IFiscalIntegration
    {
        public List<FiscalInvoice> Sent { get; } = [];
        public string Name => "Test device"; public bool Available => true;
        public Task<FiscalResult> Register(FiscalInvoice invoice, CancellationToken cancellationToken = default) { Sent.Add(invoice); return Task.FromResult(result); }
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
