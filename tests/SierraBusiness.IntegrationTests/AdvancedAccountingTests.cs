using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class AdvancedAccountingTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "rokela-advanced-" + Guid.NewGuid().ToString("N") + ".db"); private Factory factory = null!; private FinanceService finance = null!; private BusinessService business = null!; private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=" + path + ";Pooling=False").Options); await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions()); finance = new(factory, session, options); business = new(factory, options);
        await finance.CreateLedger(new(Guid.NewGuid(), 6001, "Operating expense", LedgerGroup.Expense)); await finance.CreateLedger(new(Guid.NewGuid(), 6002, "Consulting revenue", LedgerGroup.Income));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    [Fact] public async Task NestedGroupsRetainNatureAndReconcileWithoutDoubleCounting()
    {
        var root = await finance.SaveAccountGroup(new(Guid.NewGuid(), null, 0, "Operations", LedgerGroup.Expense, null));
        var child = await finance.SaveAccountGroup(new(Guid.NewGuid(), null, 0, "Local operations", LedgerGroup.Expense, root));
        await finance.AssignLedgerGroup(new(Guid.NewGuid(), 6001, 0, child));
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Expense", [new(6001, 100, 0), new(1000, 0, 100)], []));
        var report = await finance.StructureReport(today, today); Assert.Equal(100, report.Groups.Single(g => g.Id == root).Amount); Assert.Equal(100, report.Groups.Single(g => g.Id == child).Amount); Assert.Equal(1, report.Groups.Single(g => g.Id == child).Depth);
        await Assert.ThrowsAsync<BusinessException>(() => finance.SaveAccountGroup(new(Guid.NewGuid(), root, 1, "Operations", LedgerGroup.Expense, child)));
        await Assert.ThrowsAsync<BusinessException>(() => finance.SaveAccountGroup(new(Guid.NewGuid(), root, 1, "Operations", LedgerGroup.Asset, null)));
        await Assert.ThrowsAsync<BusinessException>(() => finance.AssignLedgerGroup(new(Guid.NewGuid(), 6002, 0, child)));
        await Assert.ThrowsAsync<BusinessException>(() => finance.AssignLedgerGroup(new(Guid.NewGuid(), 6001, 0, null)));
    }
    [Fact] public async Task CentreAllocationRoundsToAmountPostsOnceAndKeepsLegacyCentres()
    {
        var a = await finance.SaveCostCentre(new(Guid.NewGuid(), null, 0, "A", "Administration", null, true)); var b = await finance.SaveCostCentre(new(Guid.NewGuid(), null, 0, "B", "Field work", a, true));
        var input = new AllocateCentreCommand(Guid.NewGuid(), today, "Split operating cost", 6001, 1000, false, 100.01m, [new(a, 33.33m), new(b, 66.67m)]);
        var preview = await finance.PreviewCentreAllocation(input); Assert.Equal(100.01m, preview.Lines.Sum(l => l.Debit)); Assert.Equal(100.01m, preview.Lines.Sum(l => l.Credit)); Assert.Equal(33.33m, preview.Lines[0].Debit); Assert.Equal(66.68m, preview.Lines[1].Debit);
        var id = await finance.PostCentreAllocation(input); Assert.Equal(id, await finance.PostCentreAllocation(input));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostCentreAllocation(input with { Amount = 200 }));
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Legacy", [new(6001, 20, 0, "Legacy department"), new(1000, 0, 20)], []));
        var report = await finance.StructureReport(today, today); Assert.Equal(33.33m, report.Centres.Single(c => c.Code == "A").Expenses); Assert.Equal(66.68m, report.Centres.Single(c => c.Code == "B").Expenses); Assert.Equal(20, report.Centres.Single(c => c.Code == "Legacy department").Expenses);
        await Assert.ThrowsAsync<BusinessException>(() => finance.SaveCostCentre(new(Guid.NewGuid(), a, 1, "A", "Administration", b, true)));
        await finance.SaveCostCentre(new(Guid.NewGuid(), a, 1, "A", "Administration", null, false));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PreviewCentreAllocation(input));
        Assert.Equal(id, await finance.PostCentreAllocation(input));
    }
    [Fact] public async Task ScenariosAndMemorandaNeverChangeActualJournals()
    {
        var before = await business.Read(session.Actor);
        var scenario = new ScenarioCommand(Guid.NewGuid(), null, 0, "Future revenue", ScenarioKind.Adjustment, today, "Planning only", [new(1000, 250, 0), new(6002, 0, 250)], true);
        var id = await finance.SaveScenario(scenario); Assert.Equal(id, await finance.SaveScenario(scenario));
        var memo = await finance.SaveScenario(new(Guid.NewGuid(), null, 0, "Review rent", ScenarioKind.Memorandum, today, "Discuss terms", [], true));
        var projected = await finance.ProjectScenarios(today, today, [id, memo]); Assert.Equal(0, projected.ActualProfit); Assert.Equal(250, projected.ProjectedProfit); Assert.Equal(250, projected.Balances.Single(b => b.Code == 6002).Projected);
        var after = await business.Read(session.Actor); Assert.Equal(before.Documents.Count, after.Documents.Count); Assert.Equal(before.Ledger.Sum(l => l.Debit), after.Ledger.Sum(l => l.Debit));
        await Assert.ThrowsAsync<BusinessException>(() => finance.SaveScenario(scenario with { OperationId = Guid.NewGuid(), Lines = [new(1000, 251, 0), new(6002, 0, 250)] }));
        await finance.SaveScenario(scenario with { OperationId = Guid.NewGuid(), Id = id, ExpectedVersion = 1, Active = false });
        await Assert.ThrowsAsync<BusinessException>(() => finance.ProjectScenarios(today, today, [id]));
        await Assert.ThrowsAsync<BusinessException>(() => finance.SaveScenario(scenario with { OperationId = Guid.NewGuid(), Id = id, ExpectedVersion = 1 }));
    }
    [Fact] public async Task InterestUsesDailyClosingBalancesAndReversalDates()
    {
        await TestParties.Register(factory, "School"); var date = today.AddDays(-10);
        var product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "INT-TEST", "Credit item", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", date, [new(product, 10, 50)], null));
        var bill = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "School", date, [new(product, 10, 100)], null, DueDate: date));
        var payment = await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, date.AddDays(5), "Partial payment", [new(1000, 500, 0), new(1100, 0, 500)], [new(bill, 500)]));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), payment, "Payment reversed today"));
        var report = await finance.Interest(new(date.AddDays(1), today, 36.5m, 0, InterestBasis.Actual365));
        var row = Assert.Single(report.Rows); Assert.Equal(10, row.ChargedDays); Assert.Equal(7.50m, row.Interest); Assert.Equal(1000, row.EndBalance);
        var historical = await finance.Interest(new(date.AddDays(1), today.AddDays(-1), 36.5m, 0, InterestBasis.Actual365)); Assert.Equal(6.50m, historical.Total);
        Assert.Equal(5.50m, (await finance.Interest(new(date, today, 36.5m, 2, InterestBasis.Actual365))).Total);
        Assert.Equal(7.60m, (await finance.Interest(new(date.AddDays(1), today, 36.5m, 0, InterestBasis.Actual360))).Total);
        var count = (await business.Read(session.Actor)).Documents.Count; await finance.ExportAdvanced(date, today, [], report.Request); Assert.Equal(count, (await business.Read(session.Actor)).Documents.Count);
    }
    [Fact] public async Task ReviewedInterestChargesBecomeCustomerBillsOncePerPeriod()
    {
        await TestParties.Register(factory, "School"); var date = today.AddDays(-10);
        var product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "INT-CHG", "Credit item", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", date, [new(product, 10, 50)], null));
        var bill = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "School", date, [new(product, 10, 100)], null, DueDate: date));
        var request = new InterestRequest(date.AddDays(1), today, 36.5m, 0, InterestBasis.Actual365);
        Assert.Equal(10m, Assert.Single((await finance.Interest(request)).Rows).Interest);
        var command = new InterestChargeCommand(Guid.NewGuid(), request, bill, 10m, today, 6002, "Agreed late-payment terms reviewed", 14);
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostInterestCharge(command with { OperationId = Guid.NewGuid(), Amount = 10.01m }));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostInterestCharge(command with { OperationId = Guid.NewGuid(), IncomeAccount = 6001 }));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostInterestCharge(command with { OperationId = Guid.NewGuid(), Request = request with { Receivable = false } }));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostInterestCharge(command with { OperationId = Guid.NewGuid(), Date = today.AddDays(-1) }));
        var charge = await finance.PostInterestCharge(command); Assert.Equal(charge, await finance.PostInterestCharge(command));
        await using (var db = factory.CreateDbContext())
        {
            var doc = await db.Documents.SingleAsync(d => d.Id == charge); Assert.Equal(DocumentKind.InterestCharge, doc.Kind); Assert.Equal("School", doc.Party); Assert.Equal(today.AddDays(14), doc.DueDate);
            var lines = await db.Journal.Where(j => j.DocumentId == charge).ToListAsync(); Assert.Equal(10m, lines.Single(l => l.Account == Account.Receivables).Debit); Assert.Equal(10m, lines.Single(l => (int)l.Account == 6002).Credit);
        }
        Assert.Contains((await finance.Read(date, today)).Bills, b => b.Id == charge && b.Outstanding == 10m);
        var view = Assert.Single(await finance.InterestCharges()); Assert.Equal(bill, view.BillId); Assert.Equal(10m, view.Calculated); Assert.False(view.Reversed);
        // No interest on interest, and the same days cannot be charged twice.
        Assert.Single((await finance.Interest(request)).Rows);
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostInterestCharge(command with { OperationId = Guid.NewGuid(), Amount = 5m }));
        var payment = await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, today, "Interest paid", [new(1000, 10, 0), new(1100, 0, 10)], [new(charge, 10)]));
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), charge, "Waived")));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), payment, "Refunded"));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), charge, "Waived after review"));
        Assert.True(Assert.Single(await finance.InterestCharges()).Reversed);
        await finance.PostInterestCharge(command with { OperationId = Guid.NewGuid(), Amount = 4m });
        session.Actor = new("reader", "FREETOWN", new HashSet<string> { "accounting.read", "business.read" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => finance.PostInterestCharge(command with { OperationId = Guid.NewGuid() }));
    }
    [Fact] public async Task ReadOnlyUsersMayAnalyzeButCannotChangeStructures()
    {
        session.Actor = new("reader", "FREETOWN", new HashSet<string> { "accounting.read", "business.read" }); Assert.Empty((await finance.Structure()).Groups);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => finance.SaveAccountGroup(new(Guid.NewGuid(), null, 0, "Forbidden", LedgerGroup.Expense, null)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => finance.SaveScenario(new(Guid.NewGuid(), null, 0, "Forbidden", ScenarioKind.Memorandum, today, "", [], true)));
        session.Actor = new("owner", "OTHER", BusinessPermissions.All.ToHashSet()); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => finance.Structure());
    }
    [Fact] public async Task ReusableAllocationRejectsStaleClassesAndReplaysAfterEdits()
    {
        var a = await finance.SaveCostCentre(new(Guid.NewGuid(), null, 0, "CLASS-A", "A", null, true));
        var b = await finance.SaveCostCentre(new(Guid.NewGuid(), null, 0, "CLASS-B", "B", null, true));
        var command = new AllocationClassCommand(Guid.NewGuid(), null, 0, "Regular split", [new(a, 33.33m), new(b, 66.67m)], true);
        var id = await finance.SaveAllocationClass(command);
        var use = new UseAllocationClassCommand(Guid.NewGuid(), id, 1, today, "Reusable split", 6001, 1000, false, 100.01m);
        Assert.Equal(66.68m, (await finance.PreviewAllocationClass(use)).Lines[1].Debit);
        var voucher = await finance.PostAllocationClass(use);
        await finance.SaveAllocationClass(command with { OperationId = Guid.NewGuid(), Id = id, ExpectedVersion = 1, Active = false });
        Assert.Equal(voucher, await finance.PostAllocationClass(use));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostAllocationClass(use with { OperationId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostAllocationClass(use with { Amount = 200 }));
        Assert.Single((await finance.Read(today, today)).Entries.Select(e => e.DocumentId).Distinct());
    }
    [Fact] public async Task ParallelCategoriesReconcileSeparatelyAndReverseWithSource()
    {
        var a = await finance.SaveCostCentre(new(Guid.NewGuid(), null, 0, "DIM-A", "A", null, true)); var b = await finance.SaveCostCentre(new(Guid.NewGuid(), null, 0, "DIM-B", "B", null, true));
        var geography = await finance.SaveCostCategory(new(Guid.NewGuid(), null, 0, "Geography", true)); var department = await finance.SaveCostCategory(new(Guid.NewGuid(), null, 0, "Department", true));
        var doc = await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Category cost", [new(6001, 100.01m, 0), new(1000, 0, 100.01m)], []));
        var allocation = new CategoryAllocationCommand(Guid.NewGuid(), geography, 1, doc, [new(a, 33.33m), new(b, 66.67m)]);
        await finance.AllocateCategory(allocation); await finance.AllocateCategory(new(Guid.NewGuid(), department, 1, doc, [new(a, 100)]));
        var report = await finance.CategoryAnalysis(today, today); Assert.Equal(100.01m, report.Rows.Where(r => r.CategoryId == geography).Sum(r => r.Expenses)); Assert.Equal(100.01m, report.Rows.Where(r => r.CategoryId == department).Sum(r => r.Expenses));
        Assert.Equal(66.68m, report.Rows.Single(r => r.CategoryId == geography && r.Centre == "DIM-B").Expenses);
        await Assert.ThrowsAsync<BusinessException>(() => finance.AllocateCategory(allocation with { OperationId = Guid.NewGuid() }));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), doc, "Cancel category expense"));
        Assert.All((await finance.CategoryAnalysis(today, today)).Rows, r => Assert.Equal(0, r.Expenses));
    }
    [Fact] public async Task ProjectProfitabilityIncludesActualCancellationAndPreservesCentreIdentity()
    {
        var centre = await finance.SaveCostCentre(new(Guid.NewGuid(), null, 0, "PROJECT", "Installation project", null, true));
        var input = new ProjectCommand(Guid.NewGuid(), null, 0, "P-1", "Commercial installation", centre, today, null, 500, 200, true);
        var id = await finance.SaveProject(input);
        var expense = await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today, "Project expense", [new(6001, 120, 0, "PROJECT"), new(1000, 0, 120)], []));
        var revenue = await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, today, "Project revenue", [new(1000, 300, 0), new(6002, 0, 300)], []));
        await finance.AssignProjectDocument(new(Guid.NewGuid(),id,1,revenue,"Revenue belongs to this commercial project"));
        await Assert.ThrowsAsync<BusinessException>(() => finance.AssignProjectDocument(new(Guid.NewGuid(),id,1,revenue,"Cannot count twice")));
        var row = Assert.Single((await finance.ProjectProfitability(today, today)).Projects); Assert.Equal(180, row.Profit); Assert.Equal(-200, row.RevenueVariance); Assert.Equal(80, row.ExpenseVariance);
        await business.Reverse(session.Actor, new(Guid.NewGuid(), expense, "Cancelled project expense"));
        Assert.Equal(300, Assert.Single((await finance.ProjectProfitability(today, today)).Projects).Profit);
        await Assert.ThrowsAsync<BusinessException>(() => finance.SaveProject(input with { OperationId = Guid.NewGuid(), Id = id, ExpectedVersion = 1, Code = "CHANGED" }));
        await Assert.ThrowsAsync<BusinessException>(() => finance.SaveProject(input with { OperationId = Guid.NewGuid(), Code = "P-2" }));
    }
    [Fact] public async Task ScheduledJournalPostsEachStageOnceAndHonoursOpenDates()
    {
        var command = new ScheduledJournalCommand(Guid.NewGuid(), null, 0, "Accrual", today.AddDays(-1), today, [new(6001, 75, 0), new(6002, 0, 75)], true);
        var id = await finance.ScheduleJournal(command); Assert.Empty((await finance.Read(today.AddDays(-1), today)).Entries);
        var first = new PostScheduledJournalCommand(Guid.NewGuid(), id, 1, false);
        var posted = await finance.PostScheduledJournal(first); Assert.Equal(posted, await finance.PostScheduledJournal(first));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostScheduledJournal(first with { OperationId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<BusinessException>(() => finance.ScheduleJournal(command with { OperationId = Guid.NewGuid(), Id = id, ExpectedVersion = 1 }));
        var reversal = new PostScheduledJournalCommand(Guid.NewGuid(), id, 1, true); var reversed = await finance.PostScheduledJournal(reversal); Assert.Equal(reversed, await finance.PostScheduledJournal(reversal));
        Assert.Equal(0, (await finance.Read(today.AddDays(-1), today)).Balances.Single(b => b.Code == 6001).Closing);
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostScheduledJournal(reversal with { OperationId = Guid.NewGuid() }));
        var future = await finance.ScheduleJournal(command with { OperationId = Guid.NewGuid(), Date = today.AddDays(1), ReverseOn = today.AddDays(2) });
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostScheduledJournal(new(Guid.NewGuid(), future, 1, false)));
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
