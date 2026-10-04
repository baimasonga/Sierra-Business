using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>0.24.0: price rules, bank statement layouts, cheques we issue, cash drawers per cashier, financial statements and year-end close, Excel exports, refund approvals, and payroll leave pay, end-of-service benefits, staff loans and bank files.</summary>
public sealed class SmallerGapsTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-gaps-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private BusinessService business = null!; private IOptions<TradingOptions> options = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid widget;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        options = Options.Create(new TradingOptions()); business = new(factory, options);
        widget = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "WID-1", "Widget", "Medical", "piece", 200, TrackingMode.Quantity, MinimumPrice: 180));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private void AsOwner() => session.Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet());
    private Task Stock(decimal qty = 50, DateOnly? date = null) => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", date ?? today.AddDays(-1), [new(widget, qty, 100)], null));
    private async Task<Dictionary<int, decimal>> Balances() { var s = await business.Read(session.Actor); return s.Ledger.GroupBy(x => (int)x.Account).ToDictionary(g => g.Key, g => g.Sum(x => x.Debit - x.Credit)); }
    private async Task Verified() { await using var db = factory.CreateDbContext(); var report = await HistoryProtection.Verify(db); Assert.True(report.Ok, string.Join("; ", report.Problems)); }
    private async Task AddUser(string id, params string[] permissions)
    {
        await using var db = factory.CreateDbContext();
        db.Users.Add(new IdentityUser { Id = id, UserName = $"{id}@example.sl", NormalizedUserName = $"{id.ToUpperInvariant()}@EXAMPLE.SL" });
        db.UserClaims.Add(new IdentityUserClaim<string> { UserId = id, ClaimType = "branch", ClaimValue = "FREETOWN" });
        foreach (var p in permissions) db.UserClaims.Add(new IdentityUserClaim<string> { UserId = id, ClaimType = "permission", ClaimValue = p });
        await db.SaveChangesAsync();
    }

    [Fact] public async Task PriceRulesGiveTheBestPriceWithoutDiscountApproval()
    {
        await Stock();
        var parties = new PartyService(factory, session, options);
        await parties.Save(new(Guid.NewGuid(), null, 0, "MOH", "Ministry of Health", PartyType.Customer, "", "", "", "", 30, 100_000_000, false, Category: "Government"));
        await TestParties.Register(factory, "Kissy Clinic");
        var pricing = new PricingService(factory, session, options);
        await pricing.Save(new(Guid.NewGuid(), null, 0, "gov10", "Government 10% off", PriceRuleKind.PercentOff, 10, null, "Medical", "Government", 0, today.AddDays(-5), null));
        await pricing.Save(new(Guid.NewGuid(), null, 0, "BULK", "Bulk widgets", PriceRuleKind.FixedPrice, 150, widget, "", "", 10, today.AddDays(-5), today.AddDays(5)));
        await pricing.Save(new(Guid.NewGuid(), null, 0, "OLD", "Expired", PriceRuleKind.FixedPrice, 50, widget, "", "", 0, today.AddDays(-30), today.AddDays(-10)));
        await Assert.ThrowsAsync<BusinessException>(() => pricing.Save(new(Guid.NewGuid(), null, 0, "BAD", "No product", PriceRuleKind.FixedPrice, 10, null, "", "", 0, today, null)));
        await Assert.ThrowsAsync<BusinessException>(() => pricing.Save(new(Guid.NewGuid(), null, 0, "GOV10", "Duplicate", PriceRuleKind.PercentOff, 5, null, "", "", 0, today, null)));
        Assert.Equal(3, (await pricing.Rules()).Count);

        Assert.Equal((180m, "GOV10"), (await pricing.Quote("Ministry of Health", today, [new(widget, 2, 0)])).Select(x => (x.Price, x.Rule.Split(' ')[0])).Single());
        Assert.Equal(150m, (await pricing.Quote("Ministry of Health", today, [new(widget, 12, 0)])).Single().Price); // bulk beats 10% off
        Assert.Empty(await pricing.Quote("Kissy Clinic", today, [new(widget, 2, 0)])); // not government, under the bulk quantity

        await new ControlService(factory, session, options).SavePolicy(new(Guid.NewGuid(), ControlPolicy.Recommended with { BackdateDays = null }));
        session.Actor = new("cashier", "FREETOWN", new HashSet<string> { "business.read", "sales.create" });
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Kissy Clinic", today, [new(widget, 12, 150)], PaymentMethod.Cash)); // below the minimum price, but a promotion applies
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Kissy Clinic", today, [new(widget, 2, 150)], PaymentMethod.Cash)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pricing.Save(new(Guid.NewGuid(), null, 0, "MINE", "Cashier rule", PriceRuleKind.PercentOff, 50, null, "", "", 0, today, null)));
    }

    [Fact] public void BankStatementLayoutsAreRead()
    {
        var debitCredit = StatementFormat.BuiltIn[1];
        var csv = "Date,Details,Debit,Credit,Balance\r\n,Opening balance,,,\"10,000.00\"\r\n03/09/2026,\"Cheque 000123, MedEquip\",\"1,250.50\",,\"8,749.50\"\r\n4/9/2026,Transfer from Kissy Clinic,,500.00,\"9,249.50\"\r\n05-Sep-2026,Bank charges,15.00 DR,,9234.50\r\n";
        var rows = BankStatementFormats.Parse(csv, debitCredit);
        Assert.Equal(3, rows.Count);
        Assert.Equal((new DateOnly(2026, 9, 3), "Cheque 000123, MedEquip", -1250.50m), (rows[0].Date, rows[0].Reference, rows[0].Amount));
        Assert.Equal(500m, rows[1].Amount); Assert.Equal(-15m, rows[2].Amount);
        var semicolon = new StatementFormat("Semicolon", ";", 2, 2, "yyyyMMdd", 3, 1);
        var other = BankStatementFormats.Parse("Bank export\nAmount;Date;Narrative\n(200.00);20260901;Rent\n1 000.00;20260902;Deposit\n", semicolon);
        Assert.Equal(new[] { -200m, 1000m }, other.Select(x => x.Amount));
        Assert.Throws<BusinessException>(() => BankStatementFormats.Parse("Date,Details,Amount\n31/02/2026,Bad,10\n", StatementFormat.BuiltIn[2]));
        Assert.Throws<BusinessException>(() => BankStatementFormats.Parse("Date,Details,Amount\n01/09/2026,Bad,ten\n", StatementFormat.BuiltIn[2]));
        Assert.Throws<BusinessException>(() => BankStatementFormats.Parse("a,b\n", StatementFormat.BuiltIn[0]));
    }

    [Fact] public async Task StatementsInABanksLayoutAreImportedAndLayoutsSaved()
    {
        var extras = new BankingExtrasService(factory, session, options);
        await extras.SaveFormat(new(Guid.NewGuid(), "Rokel Commercial Bank", new("RCB CSV", ";", 1, 1, "dd.MM.yyyy", 2, 0, 3, 4)));
        await Assert.ThrowsAsync<BusinessException>(() => extras.SaveFormat(new(Guid.NewGuid(), "Bad", new("Bad", ";;", 1))));
        var formats = await extras.Formats();
        var rcb = formats.Single(x => x.Bank == "Rokel Commercial Bank").Format;
        Assert.Equal(StatementFormat.BuiltIn.Count + 1, formats.Count);
        var csv = "Date;Narrative;Debit;Credit\n01.09.2026;Deposit;;1,000.00\n02.09.2026;Charges;12.50;\n";
        Assert.Equal(2, (await extras.Preview(csv, rcb)).Count);
        var banking = new BankingService(factory, session, options);
        await banking.Import(new(Guid.NewGuid(), 1010, "RCB-SEP", 0, 987.50m, csv, rcb));
        var snapshot = await banking.Read();
        Assert.Equal(new[] { 1000m, -12.50m }, snapshot.Rows.OrderBy(x => x.Line).Select(x => x.Amount));
    }

    [Fact] public async Task ChequesWeIssueArePostedTrackedAndCancelled()
    {
        var extras = new BankingExtrasService(factory, session, options);
        await Stock(10); // NLe 1,000 owed to MedEquip
        var bill = (await new FinanceService(factory, session, options).Read(DateOnly.MinValue, today)).Bills.Single(x => !x.Receivable);
        var op = Guid.NewGuid();
        var supplier = new IssueChequeCommand(op, 1010, "MedEquip Ltd", "000123", today, 600, [new(bill.Id, 600)]);
        var id = await extras.IssueCheque(supplier);
        Assert.Equal(id, await extras.IssueCheque(supplier)); // a retried request does not issue twice
        await Assert.ThrowsAsync<BusinessException>(() => extras.IssueCheque(supplier with { OperationId = Guid.NewGuid() })); // the number is used
        await Assert.ThrowsAsync<BusinessException>(() => extras.IssueCheque(new(Guid.NewGuid(), 1000, "Cash", "1", today, 10, [], 5100))); // not a bank account
        await Assert.ThrowsAsync<BusinessException>(() => extras.IssueCheque(new(Guid.NewGuid(), 1010, "X", "2", today, 10, [new(bill.Id, 5)])));
        var rent = await extras.IssueCheque(new(Guid.NewGuid(), 1010, "Landlord", "000124", today, 300, [], 5100, "September rent"));
        var b = await Balances();
        Assert.Equal(-900m, b[1010]); Assert.Equal(-400m, b[2000]); Assert.Equal(300m, b[5100]);
        var list = await extras.IssuedCheques();
        Assert.All(list, x => Assert.Equal(IssuedChequeState.Issued, x.State));

        await extras.ActOnIssuedCheque(new(Guid.NewGuid(), id, IssuedChequeState.Presented, today, ""));
        await Assert.ThrowsAsync<BusinessException>(() => extras.ActOnIssuedCheque(new(Guid.NewGuid(), id, IssuedChequeState.Cancelled, today, "Lost")));
        await Assert.ThrowsAsync<BusinessException>(() => extras.ActOnIssuedCheque(new(Guid.NewGuid(), rent, IssuedChequeState.Cancelled, today, ""))); // reason needed
        await extras.ActOnIssuedCheque(new(Guid.NewGuid(), rent, IssuedChequeState.Cancelled, today, "Stopped: wrong amount"));
        list = await extras.IssuedCheques();
        Assert.Equal(IssuedChequeState.Presented, list.Single(x => x.Number == "000123").State);
        Assert.Equal(IssuedChequeState.Cancelled, list.Single(x => x.Number == "000124").State);
        b = await Balances();
        Assert.Equal(-600m, b[1010]); Assert.Equal(0m, b.GetValueOrDefault(5100)); // the cancelled cheque's payment is reversed
        await Verified();
    }

    [Fact] public async Task CashDrawersCanBeLimitedToTheirCashiers()
    {
        await AddUser("amina", "business.read", "sales.create", "tills.use");
        await AddUser("bai", "business.read", "sales.create", "tills.use");
        var extras = new BankingExtrasService(factory, session, options);
        await extras.AssignDrawer(new(Guid.NewGuid(), 1000, "amina@example.sl", true));
        await Assert.ThrowsAsync<BusinessException>(() => extras.AssignDrawer(new(Guid.NewGuid(), 1000, "nobody@example.sl", true)));
        Assert.Equal(new[] { "amina@example.sl" }, (await extras.Drawers()).Single(x => x.AccountCode == 1000).Cashiers);
        session.Actor = new("bai", "FREETOWN", new HashSet<string> { "business.read", "sales.create", "tills.use" });
        var banking = new BankingService(factory, session, options);
        await Assert.ThrowsAsync<BusinessException>(() => banking.OpenTill(new(Guid.NewGuid(), 1000, 0)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new BankingExtrasService(factory, session, options).AssignDrawer(new(Guid.NewGuid(), 1000, "bai@example.sl", true)));
        session.Actor = new("amina", "FREETOWN", new HashSet<string> { "business.read", "sales.create", "tills.use" });
        await banking.OpenTill(new(Guid.NewGuid(), 1000, 0));
        AsOwner();
        await extras.AssignDrawer(new(Guid.NewGuid(), 1000, "amina@example.sl", false));
        Assert.Empty((await extras.Drawers()).Single(x => x.AccountCode == 1000).Cashiers);
    }

    [Fact] public async Task FinancialStatementsBalanceAndTheYearCanBeClosed()
    {
        var lastYear = new DateOnly(today.Year - 1, 6, 1); var yearEnd = new DateOnly(today.Year - 1, 12, 31);
        await TestParties.Register(factory, "Kissy Clinic");
        await Stock(10, lastYear);
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Kissy Clinic", lastYear.AddDays(5), [new(widget, 4, 250)], PaymentMethod.Cash));
        var finance = new FinanceService(factory, session, options);
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, lastYear.AddDays(10), "Rent", [new(5100, 100, 0), new(1000, 0, 100)], []));
        var statements = new StatementsService(factory, session, options);

        var pl = await statements.ProfitAndLoss(new(today.Year - 1, 1, 1), yearEnd);
        Assert.Equal(1000m, pl.Income); Assert.Equal(500m, pl.Expenses); Assert.Equal(500m, pl.Profit); // sales 1,000 − cost 400 − rent 100
        var bs = await statements.BalanceSheet(yearEnd);
        Assert.Equal(0m, bs.Difference); Assert.Equal(500m, bs.CurrentProfit);
        var cash = await statements.CashFlow(new(today.Year - 1, 1, 1), yearEnd);
        Assert.True(cash.Reconciled); Assert.Equal(900m, cash.ClosingCash); Assert.Equal(900m, cash.Operating);
        var funds = await statements.FundsFlow(new(today.Year - 1, 1, 1), yearEnd);
        Assert.Equal(funds.TotalSources, funds.TotalUses);

        await Assert.ThrowsAsync<BusinessException>(() => statements.CloseYear(new(Guid.NewGuid(), yearEnd, ""))); // reason
        await Assert.ThrowsAsync<BusinessException>(() => statements.CloseYear(new(Guid.NewGuid(), today, "Too early")));
        await statements.CloseYear(new(Guid.NewGuid(), yearEnd, "Accounts approved by the auditor"));
        await Assert.ThrowsAsync<BusinessException>(() => statements.CloseYear(new(Guid.NewGuid(), yearEnd, "Again")));
        Assert.Equal(500m, (await statements.YearEnds()).Single().Profit);
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, yearEnd, "Late rent", [new(5100, 10, 0), new(1000, 0, 10)], []))); // the year is locked
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Rent", [new(5100, 50, 0), new(1000, 0, 50)], []));
        bs = await statements.BalanceSheet(today);
        Assert.Equal((500m, -50m, 0m, yearEnd), (bs.RetainedEarnings, bs.CurrentProfit, bs.Difference, bs.LastYearEnd!.Value));

        foreach (var report in new[] { "Profit and loss", "Balance sheet", "Cash flow", "Funds flow", "Sales analysis", "Day book & ledgers", "Stock valuation", "Stock movements", "Receivables ageing", "Payables ageing" })
        {
            var bytes = await statements.Export(new(report, new(today.Year - 1, 1, 1), today, ProductId: widget));
            using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
            Assert.NotEmpty(doc.WorkbookPart!.Workbook!.Sheets!);
        }
        session.Actor = new("cashier", "FREETOWN", new HashSet<string> { "business.read", "sales.create" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => statements.BalanceSheet(today));
        await Verified();
    }

    [Fact] public async Task LargeRefundsWaitForApproval()
    {
        await Stock();
        var sale = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(widget, 5, 200)], PaymentMethod.Cash));
        var controls = new ControlService(factory, session, options);
        await controls.SavePolicy(new(Guid.NewGuid(), ControlPolicy.Recommended));
        await AddUser("cashier", "business.read", "sales.create", "documents.reverse");
        session.Actor = new("cashier", "FREETOWN", new HashSet<string> { "business.read", "sales.create", "documents.reverse" });
        var returns = new ReturnService(factory, session, options);
        await returns.Post(new(Guid.NewGuid(), sale, "Damaged", [new(widget, null, null, null, 1)])); // NLe 200: under the limit
        var big = new ReturnCommand(Guid.NewGuid(), sale, "Wrong model delivered", [new(widget, null, null, null, 3)]);
        var stopped = await Assert.ThrowsAsync<ApprovalRequiredException>(() => returns.Post(big));
        Assert.Contains("600.00", stopped.Message);
        AsOwner(); await controls.Decide(new(Guid.NewGuid(), stopped.RequestId, true, "Checked the goods"));
        session.Actor = new("cashier", "FREETOWN", new HashSet<string> { "business.read", "sales.create", "documents.reverse" });
        await controls.Post(new(stopped.RequestId));
        AsOwner();
        Assert.Contains((await controls.Exceptions(today.AddDays(-1), today)).Rows, x => x.Kind == "Refund");
        Assert.Equal(4m, Math.Abs((await business.Read(session.Actor)).Documents.Where(x => x.Kind == DocumentKind.SalesReturn).Sum(x => x.Total)) / 200);
    }

    [Fact] public async Task LeavePayEndOfServiceLoansAndBankFiles()
    {
        var payroll = new PayrollService(factory, session, options);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        await payroll.SaveRules(new(Guid.NewGuid(), PayrollRules.Recommended(month)));
        var a = await payroll.SaveEmployee(new(Guid.NewGuid(), null, 0, "E001", "Fatmata Kamara", "Sales", 3000, 0, 0, today.AddYears(-3), null, "TIN-1", "NAS-1", BankName: "Rokel Commercial Bank", BankAccount: "0021-334455"));
        var b = await payroll.SaveEmployee(new(Guid.NewGuid(), null, 0, "E002", "Mohamed Sesay", "Stores", 2000, 0, 0, today.AddYears(-5), month.AddDays(20), "TIN-2", "NAS-2"));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.GiveLoan(new(Guid.NewGuid(), a, 1000, 2000, month, PaymentMethod.Cash, "", "School fees"))); // instalment above the loan
        var loan = await payroll.GiveLoan(new(Guid.NewGuid(), a, 1000, 400, month, PaymentMethod.Cash, "", "School fees"));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.GiveLoan(new(Guid.NewGuid(), a, 100, 100, month, PaymentMethod.Cash, "", "More"))); // one loan at a time

        var preview = await payroll.Preview(new(Guid.NewGuid(), month, today, [a, b], [new(a, LeaveAllowance: 3500), new(b, EndOfService: 6000)]));
        var fa = preview.Lines.Single(x => x.Code == "E001").Calculation;
        Assert.Equal((3000m, 150m), (fa.LeaveTaxFree, fa.LeaveExcessTax)); // one month's basic tax-free, 30% on the 500 above it
        Assert.Equal(PayrollMath.Paye(2850, PayrollRules.Recommended(month).PayeBands) + 150m, fa.Paye);
        Assert.Equal(400m, fa.LoanRepayment); Assert.Equal(fa.Gross - fa.Paye - fa.NassitEmployee - 400m, fa.Net);
        var mo = preview.Lines.Single(x => x.Code == "E002").Calculation;
        Assert.Equal(6000m, mo.EndOfService); Assert.Equal(mo.Basic - mo.NassitEmployee, mo.Chargeable); // end-of-service is tax-free and has no NASSIT
        var skipped = await payroll.Preview(new(Guid.NewGuid(), month, today, [a], [new(a, SkipLoan: true)]));
        Assert.Equal(0m, skipped.Lines.Single().Calculation.LoanRepayment);

        await payroll.Post(new(Guid.NewGuid(), month, today, [a, b], [new(a, LeaveAllowance: 3500), new(b, EndOfService: 6000)]));
        var loans = await payroll.Loans();
        Assert.Equal((400m, 600m), (loans.Single().Repaid, loans.Single().Outstanding));
        Assert.Equal(600m, (await Balances())[1310]);
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), loan, "Wrong employee"))); // instalments already deducted

        var slips = (await payroll.Read()).Payslips;
        await Assert.ThrowsAsync<BusinessException>(() => payroll.BankFile(slips.Select(x => x.ItemId).ToList())); // E002 has no bank details
        var file = System.Text.Encoding.UTF8.GetString(await payroll.BankFile([slips.Single(x => x.EmployeeCode == "E001").ItemId]));
        Assert.Contains($"E001,Fatmata Kamara,ROKEL COMMERCIAL BANK,0021-334455,{fa.Net:0.00}", file);
        await Verified();
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
