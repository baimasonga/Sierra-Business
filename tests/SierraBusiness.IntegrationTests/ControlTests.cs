using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.Infrastructure.Imports;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Specification Skill 22 (0.19.0): discount, below-cost, back-date, credit, reversal and stock-adjustment approvals; no self-approval when another approver exists; exception report and audit export permissions.</summary>
public sealed class ControlTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-controls-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private BusinessService business = null!; private ControlService controls = null!; private InventoryService inventory = null!; private FinanceService finance = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly HashSet<string> Cashier = ["business.read", "sales.create", "documents.reverse", "inventory.receive"];
    private static readonly HashSet<string> Manager = ["business.read", "sales.create", "pricing.manage", "accounting.read", "accounting.write", "documents.reverse", "audit.read"];
    private Guid widget;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options); controls = new(factory, session, options); inventory = new(factory, session, options); finance = new(factory, session, options);
        widget = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 200, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today.AddDays(-10), [new(widget, 20, 100)], null));
        await TestParties.Register(factory, "Kissy Clinic", creditLimit: 500);
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    private void As(string id, HashSet<string> permissions) => session.Actor = new(id, "FREETOWN", permissions);
    private void AsOwner() => session.Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet());
    private async Task AddUser(string id, params string[] permissions)
    {
        await using var db = factory.CreateDbContext();
        db.Users.Add(new IdentityUser { Id = id, UserName = $"{id}@example.sl", NormalizedUserName = $"{id.ToUpperInvariant()}@EXAMPLE.SL" });
        db.UserClaims.Add(new IdentityUserClaim<string> { UserId = id, ClaimType = "branch", ClaimValue = "FREETOWN" });
        foreach (var p in permissions) db.UserClaims.Add(new IdentityUserClaim<string> { UserId = id, ClaimType = "permission", ClaimValue = p });
        await db.SaveChangesAsync();
    }
    private async Task Policy(ControlPolicy policy) { AsOwner(); await controls.SavePolicy(new(Guid.NewGuid(), policy with { Version = (await controls.Policy()).Version })); }
    private TradeCommand Sale(decimal price, decimal quantity = 1, string party = "Walk-in", PaymentMethod? payment = PaymentMethod.Cash, DateOnly? date = null) =>
        new(Guid.NewGuid(), DocumentKind.Sale, party, date ?? today, [new(widget, quantity, price)], payment);
    private async Task<int> Sales() { await using var db = factory.CreateDbContext(); return await db.Documents.CountAsync(x => x.Kind == DocumentKind.Sale); }
    private async Task Verified() { await using var db = factory.CreateDbContext(); var report = await HistoryProtection.Verify(db); Assert.True(report.Ok, string.Join("; ", report.Problems)); }

    [Fact] public async Task LargeDiscountWaitsForAnotherPersonsApprovalAndPostsOnce()
    {
        await AddUser("cashier", [.. Cashier]); await AddUser("manager", [.. Manager]);
        await Policy(ControlPolicy.Recommended);
        As("cashier", Cashier);
        await business.Post(session.Actor, Sale(185)); // 7.5% off: within the 10% limit
        var sale = Sale(150); // 25% off
        var stopped = await Assert.ThrowsAsync<ApprovalRequiredException>(() => business.Post(session.Actor, sale));
        Assert.Contains("25% below its list price", stopped.Message);
        Assert.Equal(1, await Sales()); // nothing posted
        var again = await Assert.ThrowsAsync<ApprovalRequiredException>(() => business.Post(session.Actor, sale));
        Assert.Equal(stopped.RequestId, again.RequestId); // trying again does not create a second request

        var mine = (await controls.Approvals()).Single();
        Assert.Equal((ApprovalState.Pending, true, false, false), (mine.State, mine.Mine, mine.CanDecide, mine.CanPost));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controls.Decide(new(Guid.NewGuid(), mine.Id, true, ""))); // cashier lacks pricing authority

        As("manager", Manager);
        Assert.True((await controls.Approvals()).Single().CanDecide);
        await Assert.ThrowsAsync<BusinessException>(() => controls.Decide(new(Guid.NewGuid(), mine.Id, false, ""))); // a rejection needs a reason
        await controls.Decide(new(Guid.NewGuid(), mine.Id, true, "Long-standing customer"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controls.Post(new(mine.Id))); // only the requester posts it

        As("cashier", Cashier);
        Assert.True((await controls.Approvals()).Single().CanPost);
        await Assert.ThrowsAsync<ApprovalRequiredException>(() => business.Post(session.Actor, sale with { OperationId = Guid.NewGuid(), Lines = [new(widget, 1, 140)] })); // different details are not covered
        var posted = await business.Post(session.Actor, sale); // the identical sale now goes through
        Assert.Equal(posted, await business.Post(session.Actor, sale)); // and a retry does not post twice
        Assert.Equal(2, await Sales());
        var used = (await controls.Approvals()).Single(x => x.Id == mine.Id);
        Assert.Equal(ApprovalState.Used, used.State); Assert.NotEmpty(used.UsedDocument);
        await Assert.ThrowsAsync<BusinessException>(() => controls.Post(new(mine.Id)));
        await Verified();
    }

    [Fact] public async Task ManagersCannotApproveTheirOwnRequestsUnlessAlone()
    {
        await Policy(ControlPolicy.Recommended);
        As("manager", Manager);
        var alone = await Assert.ThrowsAsync<ApprovalRequiredException>(() => business.Post(session.Actor, Sale(100)));
        Assert.Contains("50% below its list price", alone.Message); // selling at cost is not below cost
        var request = (await controls.Approvals()).Single();
        Assert.True(request.CanDecide); // nobody else can approve: self-approval is allowed and recorded
        await controls.Decide(new(Guid.NewGuid(), request.Id, true, ""));
        Assert.True((await controls.Approvals()).Single().SelfApproved);
        await controls.Post(new(request.Id));
        Assert.Equal(1, await Sales());

        await AddUser("auditor", [.. Manager]);
        await Assert.ThrowsAsync<ApprovalRequiredException>(() => business.Post(session.Actor, Sale(90)));
        var second = (await controls.Approvals()).First(x => x.State == ApprovalState.Pending);
        Assert.False(second.CanDecide);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controls.Decide(new(Guid.NewGuid(), second.Id, true, "")));
        await controls.Withdraw(new(Guid.NewGuid(), second.Id, "Customer paid list price"));
        Assert.Equal(ApprovalState.Withdrawn, (await controls.Approvals()).Single(x => x.Id == second.Id).State);

        await Policy(ControlPolicy.Recommended with { SelfApprovalWhenAlone = false });
        await using (var db = factory.CreateDbContext()) { db.Users.Single(x => x.Id == "auditor").LockoutEnd = DateTimeOffset.UtcNow.AddYears(1); await db.SaveChangesAsync(); }
        As("manager", Manager);
        await Assert.ThrowsAsync<ApprovalRequiredException>(() => business.Post(session.Actor, Sale(80)));
        Assert.False((await controls.Approvals()).First(x => x.State == ApprovalState.Pending).CanDecide); // policy forbids self-approval
    }

    [Fact] public async Task BelowCostBackdatedAndCreditLimitSalesNeedApproval()
    {
        await Policy(ControlPolicy.Recommended with { MaxDiscountPercent = null });
        As("cashier", Cashier);
        Assert.Contains("below its stock cost NLe 100.00", (await Assert.ThrowsAsync<ApprovalRequiredException>(() => business.Post(session.Actor, Sale(90)))).Message);
        Assert.Contains("5 days ago", (await Assert.ThrowsAsync<ApprovalRequiredException>(() => business.Post(session.Actor, Sale(200, date: today.AddDays(-5))))).Message);
        var credit = Sale(200, 3, "Kissy Clinic", null);
        var stopped = await Assert.ThrowsAsync<ApprovalRequiredException>(() => business.Post(session.Actor, credit));
        Assert.Contains("credit limit of NLe 500.00", stopped.Message);
        AsOwner(); await controls.Decide(new(Guid.NewGuid(), stopped.RequestId, true, "Order from the Ministry, payment due"));
        As("cashier", Cashier);
        await business.Post(session.Actor, credit);
        await Policy(ControlPolicy.Recommended with { MaxDiscountPercent = null, CreditOverride = false });
        As("cashier", Cashier);
        Assert.Contains("exceeds the customer's credit limit", (await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, Sale(200, 1, "Kissy Clinic", null)))).Message); // without the override the limit is absolute

        AsOwner();
        await Assert.ThrowsAsync<ApprovalRequiredException>(() => finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Payment, today.AddDays(-9), "Old rent", [new(5100, 50, 0), new(1000, 0, 50)], [])));
    }

    [Fact] public async Task ReversalsAndStockAdjustmentsAreApprovedAndReported()
    {
        As("cashier", Cashier);
        var sale = await business.Post(session.Actor, Sale(200, 2));
        await Policy(ControlPolicy.Recommended);
        As("cashier", Cashier);
        var reverse = new ReverseCommand(Guid.NewGuid(), sale, "Customer changed their mind");
        var stopped = await Assert.ThrowsAsync<ApprovalRequiredException>(() => business.Reverse(session.Actor, reverse));
        Assert.Contains("Customer changed their mind", stopped.Message);
        AsOwner(); await controls.Decide(new(Guid.NewGuid(), stopped.RequestId, true, ""));
        As("cashier", Cashier); await controls.Post(new(stopped.RequestId));

        var small = new StockCountCommand(Guid.NewGuid(), widget, "MAIN", 20, 19, 0, "Damaged in store"); // NLe 100: under the NLe 500 threshold
        await inventory.Count(small);
        var large = new StockCountCommand(Guid.NewGuid(), widget, "MAIN", 19, 10, 0, "Missing at count");
        var count = await Assert.ThrowsAsync<ApprovalRequiredException>(() => inventory.Count(large));
        Assert.Contains("NLe -900.00", count.Message);
        AsOwner(); await controls.Decide(new(Guid.NewGuid(), count.RequestId, true, "Theft reported to police"));
        As("cashier", Cashier); await inventory.Count(large);

        As("cashier", Cashier);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controls.Exceptions(today, today));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controls.Export(today, today));
        AsOwner();
        var report = await controls.Exceptions(today.AddDays(-30), today);
        Assert.Contains(report.Rows, x => x.Kind == "Reversal" && x.Detail.Contains("Customer changed their mind") && x.Approval.StartsWith("APR-"));
        Assert.Equal(2, report.Rows.Count(x => x.Kind == "Stock adjustment"));
        Assert.Contains(report.Rows, x => x.Kind == "Self-approved" || x.Kind == "Approved");
        var cashier = report.Users.Single(x => x.Actor == "cashier");
        Assert.Equal((1, 2), (cashier.Reversals, cashier.StockAdjustments));
        var workbook = await controls.Export(today.AddDays(-30), today);
        var sheets = WorkbookReader.Read(workbook).Select(x => x.Name).ToList();
        Assert.Equal(["Exceptions", "By user", "Approvals", "Audit trail"], sheets);
        Assert.NotEmpty(await controls.AuditTrail(today, today));
        await Verified();
    }

    [Fact] public async Task OnlyAdministratorsChangeThePolicyAndWithoutOneNothingIsStopped()
    {
        As("cashier", Cashier);
        await business.Post(session.Actor, Sale(10)); // no policy saved: controls are off
        Assert.False((await controls.Policy()).Active);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controls.SavePolicy(new(Guid.NewGuid(), ControlPolicy.Recommended)));
        AsOwner();
        await Assert.ThrowsAsync<BusinessException>(() => controls.SavePolicy(new(Guid.NewGuid(), ControlPolicy.Recommended with { MaxDiscountPercent = 150 })));
        await Assert.ThrowsAsync<BusinessException>(() => controls.SavePolicy(new(Guid.NewGuid(), ControlPolicy.Recommended with { WorkStart = "19:00", WorkEnd = "07:00" })));
        await controls.SavePolicy(new(Guid.NewGuid(), ControlPolicy.Recommended));
        await Assert.ThrowsAsync<BusinessException>(() => controls.SavePolicy(new(Guid.NewGuid(), ControlPolicy.Recommended))); // stale version
        Assert.Equal((1, 10m, true), ((await controls.Policy()).Version, (await controls.Policy()).MaxDiscountPercent!.Value, (await controls.Policy()).Active));
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
