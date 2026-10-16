using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class ConsolidationTests
{
    private readonly Session session = new();
    private static readonly DateOnly From = new(2026, 1, 1), To = DateOnly.FromDateTime(DateTime.UtcNow);
    private StatementsService Service => new(new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=:memory:").Options), session, Options.Create(new TradingOptions()));
    private static CompanyReportSnapshot Member(string name, decimal factor = 1) => new(1, Guid.NewGuid(), name, "FREETOWN", "NLe", From, To, DateTimeOffset.UtcNow,
        [new(1000, "Cash", LedgerGroup.Asset, 80 * factor, 180 * factor), new(2000, "Payables", LedgerGroup.Liability, -20 * factor, -20 * factor), new(3000, "Capital", LedgerGroup.Equity, 0, -100 * factor), new(4000, "Sales", LedgerGroup.Income, -100 * factor, -100 * factor), new(5000, "Costs", LedgerGroup.Expense, 40 * factor, 40 * factor)]);

    [Fact] public async Task ProfitAndBalanceSheetRetainEveryMemberContribution()
    {
        var members = new[] { Member("One"), Member("Two", 2) };
        var pl = await Service.Consolidate(new("Profit and loss", members, []));
        var profit = pl.Rows.Single(r => r.Section == "Result"); Assert.Equal(new decimal[] { 60, 120 }, profit.Contributions); Assert.Equal(180, profit.Amount);
        var bs = await Service.Consolidate(new("Balance sheet", members, [])); Assert.Equal(0, bs.BalanceDifference);
        Assert.Equal(540, bs.Rows.Single(r => r.Section == "Assets" && r.Total).Amount); Assert.Equal(480, bs.Rows.Single(r => r.Section == "Equity" && r.Total).Amount);
        Assert.Equal(180, bs.Rows.Single(r => r.Label == "Accumulated profit / loss").Amount);
    }
    [Fact] public async Task DifferingChartsNeedExplicitCompatibleMapping()
    {
        var one = Member("One"); var two = Member("Two", 2);
        two = two with { Accounts = two.Accounts.Select(a => a.Code == 4000 ? a with { Name = "Trading revenue", Code = 4010 } : a).ToArray() };
        var result = await Service.Consolidate(new("Profit and loss", [one, two], [new(two.SourceId, 4010, 4000, "Sales")]));
        Assert.Equal(300, result.Rows.Single(r => r.AccountCode == 4000).Amount);
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Profit and loss", [one, two], [new(two.SourceId, 4010, 1000, "Cash")])));
        var renamed = one with { SourceId = Guid.NewGuid(), Company = "Other", Accounts = one.Accounts.Select(a => a.Code == 4000 ? a with { Name = "Other revenue" } : a).ToArray() };
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Profit and loss", [one, renamed], [])));
    }
    [Fact] public async Task DuplicateLineageAndMismatchedDatesAreRejected()
    {
        var one = Member("One"); var two = Member("Two");
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Balance sheet", [one, one with { Company = "Renamed clone" }], [])));
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Balance sheet", [one, two with { From = From.AddDays(1) }], [])));
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Cash flow", [one, two], [])));
    }
    [Fact] public async Task IntercompanyReviewOnlyProposesExactReciprocalBalances()
    {
        var debit = Member("Buying member", 2.5m); var credit = Member("Selling member");
        var query = new ConsolidationRequest("Profit and loss", [debit, credit], []);
        var pair = new IntercompanyPair("IC-1", debit.SourceId, 5000, credit.SourceId, 4000, "Dedicated reciprocal trading accounts reviewed");
        var proposal = Assert.Single(await Service.ReviewIntercompany(new(query, [pair]))); Assert.True(proposal.Matched); Assert.NotNull(proposal.Adjustment);
        var result = await Service.Consolidate(query with { Adjustments = [proposal.Adjustment!] }); Assert.Equal((await Service.Consolidate(query)).Rows.Single(r => r.Section == "Result").Amount, result.Rows.Single(r => r.Section == "Result").Amount);
        var unmatched = query with { Members = [Member("Buying member", 2) with { SourceId = debit.SourceId }, credit] };
        Assert.Null(Assert.Single(await Service.ReviewIntercompany(new(unmatched, [pair]))).Adjustment);
        await Assert.ThrowsAsync<BusinessException>(() => Service.ReviewIntercompany(new(query, [pair, pair with { Reference = "IC-2" }])));
    }
    [Fact] public async Task PortableCompanySearchRespectsLineageLimitsAndOwnerPermission()
    {
        var index = new CompanySearchIndex(1,Guid.NewGuid(),"Company A","FREETOWN",DateTimeOffset.UtcNow,[new("Product","PAPER","Office paper",null,null),new("Document","INV-1","Paper customer",To,50)]);
        var second = index with { SourceId = Guid.NewGuid(), Company = "Company B", Entries = [new("Party","CUS-1","Paper shop",null,null)] };
        var hits = await Service.SearchCompanies(new("paper",[index,second])); Assert.Equal(3,hits.Count); Assert.Equal(2,hits.Select(h => h.SourceId).Distinct().Count());
        await Assert.ThrowsAsync<BusinessException>(() => Service.SearchCompanies(new("paper",[index,index with { Company = "Renamed" }])));
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "accounting.read","business.read" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.SearchCompanies(new("paper",[index])));
    }
    [Fact] public async Task ReviewedEliminationsRemainSeparateAndBalanceBothStatements()
    {
        var members = new[] { Member("One"),Member("Two",2) };
        var adjustments = new[] { new ConsolidationAdjustment("BALANCE-ELIM",To,"Eliminate reviewed reciprocal balances",[new(2000,20,0),new(1000,0,20)]),new ConsolidationAdjustment("TRADE-ELIM",To,"Eliminate reviewed internal revenue and expense",[new(4000,50,0),new(5000,0,50)]) };
        var query = new ConsolidationRequest("Balance sheet",members,[],adjustments);
        var report = await Service.Consolidate(query); Assert.True(report.HasAdjustments); Assert.Equal(0,report.BalanceDifference); var assets = report.Rows.Single(r => r.Section == "Assets" && r.Total); Assert.Equal(new decimal[] {180,360,-20},assets.Contributions); Assert.Equal(520,assets.Amount);
        var pl = await Service.Consolidate(query with { Statement = "Profit and loss" }); Assert.Equal(-50,pl.Rows.Single(r => r.AccountCode == 4000).Contributions.Last()); Assert.Equal(180,pl.Rows.Single(r => r.Section == "Result").Amount);
        Assert.Equal(180,members[0].Accounts.Single(a => a.Code == 1000).ClosingBalance);
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(query with { Adjustments = [adjustments[0],adjustments[0]] }));
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(query with { Adjustments = [adjustments[0] with { Lines = [new(2000,21,0),new(1000,0,20)] }] }));
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(query with { Adjustments = [adjustments[0] with { Lines = [new(8888,20,0),new(1000,0,20)] }] }));
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(query with { Adjustments = [adjustments[0] with { Date = From.AddDays(-1) }] }));
    }
    [Fact] public async Task PartlyOwnedMembersShowNonControllingInterests()
    {
        var one = Member("Parent"); var two = Member("Subsidiary", 2);
        var terms = new ConsolidationMemberTerms[] { new(two.SourceId, 60) };
        var bs = await Service.Consolidate(new("Balance sheet", [one, two], [], null, terms));
        Assert.Equal(0, bs.BalanceDifference); Assert.True(bs.HasNonControllingInterest);
        var nci = bs.Rows.Single(r => r.Section == "Equity attributable to" && r.Label == "Non-controlling interests"); Assert.Equal(new decimal[] { 0, 128 }, nci.Contributions);
        var owners = bs.Rows.Single(r => r.Section == "Equity attributable to" && r.Label == "Owners of the parent"); Assert.Equal(480 - 128, owners.Amount);
        Assert.Equal(bs.Rows.Single(r => r.Section == "Equity" && r.Total).Amount, bs.Rows.Single(r => r.Section == "Equity attributable to" && r.Total).Amount);
        var pl = await Service.Consolidate(new("Profit and loss", [one, two], [], null, terms));
        Assert.Equal(48, pl.Rows.Single(r => r.Section == "Profit attributable to" && r.Label == "Non-controlling interests").Amount);
        Assert.Equal(132, pl.Rows.Single(r => r.Section == "Profit attributable to" && r.Label == "Owners of the parent").Amount);
        Assert.DoesNotContain((await Service.Consolidate(new("Profit and loss", [one, two], []))).Rows, r => r.Section == "Profit attributable to");
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Balance sheet", [one, two], [], null, [new(two.SourceId, 0)])));
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Balance sheet", [one, two], [], null, [new(two.SourceId, 100.5m)])));
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Balance sheet", [one, two], [], null, [new(two.SourceId, 60), new(two.SourceId, 70)])));
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Balance sheet", [one, two], [], null, [new(Guid.NewGuid(), 60)])));
    }
    [Fact] public async Task ForeignMembersAreTranslatedWithAReserveAndStillBalance()
    {
        var one = Member("Parent"); var foreign = Member("Dollar subsidiary") with { Currency = "USD" };
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Balance sheet", [one, foreign], [])));
        var terms = new ConsolidationMemberTerms[] { new(foreign.SourceId, 100, 20, 18, 15) };
        var bs = await Service.Consolidate(new("Balance sheet", [one, foreign], [], null, terms));
        Assert.Equal(0, bs.BalanceDifference); Assert.True(bs.HasTranslation);
        Assert.Equal(new decimal[] { 180, 3600 }, bs.Rows.Single(r => r.AccountCode == 1000).Contributions);
        Assert.Equal(new decimal[] { 0, 620 }, bs.Rows.Single(r => r.Label == "Foreign currency translation reserve").Contributions);
        Assert.Equal(new decimal[] { 100, 1500 }, bs.Rows.Single(r => r.AccountCode == 3000).Contributions);
        var pl = await Service.Consolidate(new("Profit and loss", [one, foreign], [], null, terms));
        Assert.Equal(new decimal[] { 60, 1080 }, pl.Rows.Single(r => r.Section == "Result").Contributions);
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Balance sheet", [one, foreign], [], null, [new(foreign.SourceId, 100, 0, 18, 15)])));
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Balance sheet", [one, foreign], [], null, [..terms, new(one.SourceId, 100, 2, 1, 1)])));
        var both = await Service.Consolidate(new("Balance sheet", [one, foreign], [], null, [new(foreign.SourceId, 75, 20, 18, 15)]));
        var equity = both.Rows.Single(r => r.Section == "Equity" && r.Total).Contributions[1];
        Assert.Equal(decimal.Round(equity * 0.25m, 2), both.Rows.Single(r => r.Label == "Non-controlling interests").Amount);
        var bytes = await Service.ExportConsolidation(new("Balance sheet", [one, foreign], [], null, terms));
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes)); using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet2.xml")!.Open()); Assert.Contains("USD", reader.ReadToEnd());
    }
    [Fact] public async Task IntercompanyMatchingUsesTranslatedBalances()
    {
        var parent = Member("Parent", 20); var foreign = Member("Dollar subsidiary") with { Currency = "USD" };
        var query = new ConsolidationRequest("Profit and loss", [parent, foreign], [], null, [new(foreign.SourceId, 100, 20, 20, 20)]);
        // Subsidiary sales of 100 USD at 20 are 2,000 NLe: parent costs of 800 NLe do not match; 2,000 NLe do.
        Assert.False(Assert.Single(await Service.ReviewIntercompany(new(query, [new("IC-FX", parent.SourceId, 5000, foreign.SourceId, 4000, "Reviewed")]))).Matched);
        var matching = query with { Members = [Member("Parent", 50) with { SourceId = parent.SourceId }, foreign] };
        Assert.True(Assert.Single(await Service.ReviewIntercompany(new(matching, [new("IC-FX", parent.SourceId, 5000, foreign.SourceId, 4000, "Reviewed")]))).Matched);
    }
    [Fact] public async Task IntercompanyTransactionsMatchAcrossCompanyBooks()
    {
        var paths = new[] { Path.Combine(Path.GetTempPath(), "rokela-ic-a-" + Guid.NewGuid().ToString("N") + ".db"), Path.Combine(Path.GetTempPath(), "rokela-ic-b-" + Guid.NewGuid().ToString("N") + ".db") };
        try
        {
            var factories = new List<Factory>(); foreach (var p in paths) { var f = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=" + p + ";Pooling=False").Options); await using (var db = f.CreateDbContext()) await HistoryProtection.MigrateAsync(db); factories.Add(f); }
            var options = Options.Create(new TradingOptions()); var day = To.AddDays(-5);
            var a = new BusinessService(factories[0], options); var b = new BusinessService(factories[1], options);
            await TestParties.Register(factories[0], "Company B"); await TestParties.Register(factories[1], "Company A", PartyType.Supplier);
            var aItem = await a.AddProduct(session.Actor, new(Guid.NewGuid(), "IC-ITEM", "Intercompany item", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: 1)); var bItem = await b.AddProduct(session.Actor, new(Guid.NewGuid(), "IC-ITEM", "Intercompany item", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: 1));
            await a.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Maker", day.AddDays(-1), [new(aItem, 10, 50)], null));
            var sale = await a.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Company B", day, [new(aItem, 2, 100)], null));
            var receipt = await b.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Company A", day.AddDays(1), [new(bItem, 2, 100)], null));
            await new FinanceService(factories[0], session, options).PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, day.AddDays(2), "Company B", [new(1000, 50, 0), new(1100, 0, 50)], [new(sale, 50)]));
            await new FinanceService(factories[1], session, options).PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, day.AddDays(2), "Company A", [new(2000, 50, 0), new(1000, 0, 50)], [new(receipt, 50)]));
            await a.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Company B", day.AddDays(3), [new(aItem, 1, 30)], null));
            var left = await new StatementsService(factories[0], session, options).ExportIntercompanyLedger("Company A", "company b", From, To);
            var right = await new StatementsService(factories[1], session, options).ExportIntercompanyLedger("Company B", "Company A", From, To);
            Assert.Equal(3, left.Transactions.Count); Assert.Equal(2, right.Transactions.Count); Assert.Equal(200, left.Transactions[0].Amount); Assert.Equal(-200, right.Transactions[0].Amount);
            var report = await Service.MatchIntercompanyTransactions(new(left, right));
            Assert.Equal(2, report.Matched.Count); Assert.Equal(30, Assert.Single(report.LeftOnly).Amount); Assert.Empty(report.RightOnly); Assert.Equal(30, report.Difference); Assert.Equal(1, report.Matched[0].DaysApart);
            var strict = await Service.MatchIntercompanyTransactions(new(left, right, 0)); Assert.Single(strict.Matched); Assert.Equal(2, strict.LeftOnly.Count);
            await Assert.ThrowsAsync<BusinessException>(() => Service.MatchIntercompanyTransactions(new(left, left)));
            await Assert.ThrowsAsync<BusinessException>(() => Service.MatchIntercompanyTransactions(new(left, right with { From = From.AddDays(1) })));
            await Assert.ThrowsAsync<BusinessException>(() => Service.MatchIntercompanyTransactions(new(left, right with { Transactions = [.. right.Transactions, new("X", To, "Sale", 0)] })));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (var p in paths) File.Delete(p); }
    }
    [Fact] public async Task MalformedSnapshotsAndMappingsCannotProduceReports()
    {
        var one = Member("One"); var two = Member("Two");
        await Assert.ThrowsAsync<BusinessException>(() => Service.ValidateCompanySnapshot(one with { Accounts = one.Accounts.Skip(1).ToArray() }));
        await Assert.ThrowsAsync<BusinessException>(() => Service.ValidateCompanySnapshot(one with { Currency = "usd" }));
        await Service.ValidateCompanySnapshot(one with { Currency = "USD" });
        await Assert.ThrowsAsync<BusinessException>(() => Service.ValidateCompanySnapshot(one with { Accounts = [.. one.Accounts, one.Accounts[0]] }));
        await Assert.ThrowsAsync<BusinessException>(() => Service.ValidateCompanySnapshot(one with { Accounts = one.Accounts.Select(a => a with { ClosingBalance = decimal.MinValue }).ToArray() }));
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Profit and loss", [one, two], [new(one.SourceId, 9999, 4000, "Sales")])));
        await Assert.ThrowsAsync<BusinessException>(() => Service.Consolidate(new("Profit and loss", [one, two], [new(one.SourceId, 4000, 4000, "Sales"), new(one.SourceId, 4000, 4000, "Sales")])));
    }
    [Fact] public async Task RevokedPermissionsAndOtherBranchBlockReportsAndExports()
    {
        var query = new ConsolidationRequest("Profit and loss", [Member("One"), Member("Two")], []);
        session.Actor = new("reader", "FREETOWN", new HashSet<string> { "business.read" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.Consolidate(query)); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.ExportConsolidation(query));
        session.Actor = new("owner", "OTHER", BusinessPermissions.All.ToHashSet()); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.CompanySnapshot("Other", From, To));
    }
    [Fact] public async Task WorkbookIncludesSourcesAndMemberTotals()
    {
        var bytes = await Service.ExportConsolidation(new("Profit and loss", [Member("One"), Member("Two")], []));
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes)); Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml")); Assert.NotNull(zip.GetEntry("xl/worksheets/sheet2.xml"));
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()); var text = reader.ReadToEnd(); Assert.Contains("Group total", text); Assert.Contains("intercompany eliminations", text);
    }
    [Fact] public async Task SnapshotUsesPostedHistoryAndStableSourceDespiteRenaming()
    {
        var path = Path.Combine(Path.GetTempPath(), "rokela-group-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var factory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=" + path + ";Pooling=False").Options);
            await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
            var options = Options.Create(new TradingOptions()); var finance = new FinanceService(factory, session, options); var service = new StatementsService(factory, session, options);
            await finance.CreateLedger(new(Guid.NewGuid(), 6001, "Other income", LedgerGroup.Income));
            await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Journal, From.AddDays(-1), "Opening", [new(1000, 100, 0), new(3000, 0, 100)], []));
            var sale = await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, To, "Revenue", [new(1000, 60, 0), new(6001, 0, 60)], []));
            var snapshot = await service.CompanySnapshot("One", From, To); var cash = snapshot.Accounts.Single(a => a.Code == 1000); Assert.Equal(60, cash.PeriodBalance); Assert.Equal(160, cash.ClosingBalance);
            Assert.Equal(snapshot.SourceId, (await service.CompanySnapshot("Renamed", From, To)).SourceId);
            await new BusinessService(factory, options).Reverse(session.Actor, new(Guid.NewGuid(), sale, "Undo revenue"));
            var reversed = await service.CompanySnapshot("One", From, To); Assert.Equal(0, reversed.Accounts.Single(a => a.Code == 6001).PeriodBalance);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
    [Fact] public void ScreenLoadsMembersBuildsReportsAndClearsStaleResults()
    {
        using var context = new BunitContext(); context.Services.AddSingleton<IStatementsService>(Service); context.JSInterop.Mode = JSRuntimeMode.Loose;
        var screen = context.Render<ConsolidationWorkspace>();
        void Upload(CompanyReportSnapshot snapshot) => screen.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText(System.Text.Json.JsonSerializer.Serialize(snapshot), "member.json"));
        Upload(Member("One")); screen.WaitForAssertion(() => Assert.Contains("One loaded", screen.Markup));
        Upload(Member("Two", 2)); screen.WaitForAssertion(() => Assert.Contains("Two loaded", screen.Markup));
        screen.FindAll("button").Single(b => b.TextContent == "Build group report").Click(); screen.WaitForAssertion(() => Assert.Contains("180.00", screen.Markup));
        screen.Find("select").Change("Balance sheet"); Assert.DoesNotContain("Group total (NLe)", screen.Markup);
        screen.FindAll("button").Single(b => b.TextContent == "Build group report").Click(); screen.WaitForAssertion(() => Assert.Contains("Balance sheet difference: 0.00", screen.Markup));
        screen.FindAll("button").Single(b => b.TextContent == "Remove Two").Click(); Assert.DoesNotContain("Group total (NLe)", screen.Markup);
    }
    [Fact] public void ScreenCollectsOwnershipAndTranslationRates()
    {
        using var context = new BunitContext(); context.Services.AddSingleton<IStatementsService>(Service); context.JSInterop.Mode = JSRuntimeMode.Loose;
        var screen = context.Render<ConsolidationWorkspace>();
        void Upload(CompanyReportSnapshot snapshot) => screen.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText(System.Text.Json.JsonSerializer.Serialize(snapshot), "member.json"));
        Upload(Member("Parent")); screen.WaitForAssertion(() => Assert.Contains("Parent loaded", screen.Markup));
        Upload(Member("Dollar subsidiary") with { Currency = "USD" }); screen.WaitForAssertion(() => Assert.Contains("Closing rate (NLe per USD)", screen.Markup));
        screen.FindAll("button").Single(b => b.TextContent == "Build group report").Click(); screen.WaitForAssertion(() => Assert.Contains("Exchange rates must be positive", screen.Markup));
        foreach (var label in new[] { "Closing rate", "Average rate", "Historical equity rate" }) screen.FindAll("label").Single(l => l.TextContent.StartsWith(label)).QuerySelector("input")!.Change("20");
        screen.FindAll("label").Single(l => l.TextContent.StartsWith("Parent ownership % for Dollar subsidiary")).QuerySelector("input")!.Change("80");
        screen.FindAll("button").Single(b => b.TextContent == "Build group report").Click();
        screen.WaitForAssertion(() => { Assert.Contains("Non-controlling interests", screen.Markup); Assert.DoesNotContain("Foreign currency translation reserve", screen.Markup); });
    }
    [Fact] public void ScreenSavesAndRestoresMappedGroupThroughDesktopFileExport()
    {
        using var context = new BunitContext(); var export = new Export(); context.Services.AddSingleton<IStatementsService>(Service); context.Services.AddSingleton<IFileExport>(export); context.JSInterop.Mode = JSRuntimeMode.Loose;
        var screen = context.Render<ConsolidationWorkspace>();
        void Upload(string json) => screen.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText(json, "group.json"));
        var one = Member("One"); var two = Member("Two", 2);
        var query = new ConsolidationRequest("Profit and loss", [one, two], [new(two.SourceId, 4000, 4000, "Sales")]);
        Upload(System.Text.Json.JsonSerializer.Serialize(query)); screen.WaitForAssertion(() => Assert.Contains("Saved group and mappings loaded", screen.Markup));
        screen.FindAll("button").Single(b => b.TextContent == "Save group and mappings").Click(); screen.WaitForAssertion(() => Assert.Contains("Saved to", screen.Markup)); Assert.Equal("group-reporting-set.json", export.Name);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ConsolidationRequest>(export.Bytes)!; Assert.Equal(2, restored.Members.Count); Assert.Contains(restored.Mappings, m => m.SourceId == two.SourceId && m.SourceCode == 4000 && m.GroupName == "Sales");
        Upload("{bad"); screen.WaitForAssertion(() => Assert.Contains("not a valid company snapshot", screen.Markup));
        screen.FindAll("button").Single(b => b.TextContent == "Build group report").Click(); screen.WaitForAssertion(() => Assert.Contains("180.00", screen.Markup));
        screen.FindAll("button").Single(b => b.TextContent == "Export group report to Excel").Click(); screen.WaitForAssertion(() => Assert.EndsWith(".xlsx", export.Name)); Assert.DoesNotContain(context.JSInterop.Invocations, i => i.Identifier == "sierraDownload");
    }
    private sealed class Export : IFileExport { public string Name = ""; public byte[] Bytes = []; public Task<string> Save(string name, byte[] bytes) { Name = name; Bytes = bytes; return Task.FromResult("C:/Reports/" + name); } }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
