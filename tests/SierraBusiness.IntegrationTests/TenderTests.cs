using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Specification Skill 16 acceptance: tender creation, bid revision/version, approval, won tender conversion, lost tender closure, attachment authorization.</summary>
public sealed class TenderTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-tenders-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private BusinessService business = null!; private TenderService tenders = null!; private DocumentLibrary library = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid analyser, reagent;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions { TaxRate = .15m });
        business = new(factory, options); tenders = new(factory, session, options); library = new(factory, session, options, new DatabaseFileStore());
        analyser = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "HEM-5", "Haematology analyser 5-part", "Medical", "unit", 150000, TrackingMode.Serial, MinimumPrice: 100000));
        reagent = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "REAG-1", "Diluent 20 L", "Medical", "box", 2000, TrackingMode.Quantity, MinimumPrice: 1500));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    private Task<Guid> Create(string reference = "NHSL/RFQ/2026/31") => tenders.Create(new(Guid.NewGuid(), reference, "National Hospital Services", "Supply of haematology analysers and reagents", today.AddDays(12), "12:00", "Bangz", "Hand delivery to procurement office"));
    private static List<TenderLineInput> Lines(decimal analyserPrice = 160000) =>
    [
        new(null, "5-part haematology analyser with UPS", 2, "unit", 4000, "USD", 23, 12, 30, analyserPrice), // placeholder product added later
        new(null, "Diluent 20 L", 10, "box", 1200, "SLE", 1, 0, 40, 1680),
        new(null, "Alternative: 3-part analyser", 2, "unit", 2500, "USD", 23, 12, 30, 105000, AlternativeTo: 1)
    ];
    private async Task<TenderView> View(Guid id) => (await tenders.List()).Single(x => x.Id == id);
    private async Task Verified() { await using var db = factory.CreateDbContext(); var report = await HistoryProtection.Verify(db); Assert.True(report.Ok, string.Join("; ", report.Problems)); }

    [Fact] public async Task TenderIsCostedVersionedApprovedSubmittedAndWonIntoASalesOrder()
    {
        var id = await Create();
        await Assert.ThrowsAsync<BusinessException>(() => Create()); // same reference and institution
        Assert.Equal((TenderStatus.Draft, 12), ((await View(id)).Status, (await View(id)).DaysLeft));

        await tenders.SaveBid(new(Guid.NewGuid(), id, 0, Lines()));
        var bid = (await View(id)).Bids.Single();
        var line = bid.Lines[0];
        Assert.Equal((103040m, 133952m, 320000m), (line.LocalUnitCost, line.SuggestedPrice, line.LineTotal)); // 4,000 × 23 × 1.12; +30%
        Assert.Equal((336800m, 50520m, 387320m), (bid.Subtotal, bid.Tax, bid.Total)); // alternative excluded; 15% tax
        Assert.Equal(218080m, bid.CostTotal);
        Assert.Equal(TenderStatus.BidPreparation, (await View(id)).Status);

        await Assert.ThrowsAsync<BusinessException>(() => tenders.Submit(new(Guid.NewGuid(), id, 1, today))); // needs approval
        await Assert.ThrowsAsync<BusinessException>(() => tenders.SaveBid(new(Guid.NewGuid(), id, 1, Lines(150000)))); // a revision needs a reason
        await Assert.ThrowsAsync<BusinessException>(() => tenders.SaveBid(new(Guid.NewGuid(), id, 0, Lines(150000), "Stale"))); // stale version
        await tenders.SaveBid(new(Guid.NewGuid(), id, 1, LinesWithProducts(150000), "Supplier discount confirmed"));
        await Assert.ThrowsAsync<BusinessException>(() => tenders.Approve(new(Guid.NewGuid(), id, 1))); // only the latest version
        await tenders.Approve(new(Guid.NewGuid(), id, 2, "Margin acceptable"));
        var view = await View(id);
        Assert.Equal((TenderStatus.Approved, 2), (view.Status, view.ApprovedVersion));
        Assert.Contains("self-approved", view.Events.Last().Reason); // no other pricing approver exists

        await tenders.Submit(new(Guid.NewGuid(), id, 2, today, "Receipt PR-778"));
        await Assert.ThrowsAsync<BusinessException>(() => tenders.SaveBid(new(Guid.NewGuid(), id, 2, LinesWithProducts(140000), "Silent change"))); // submitted bid is locked
        await tenders.Clarify(new(Guid.NewGuid(), id, "Ministry asked for a 12-month warranty statement and revised price"));
        await tenders.SaveBid(new(Guid.NewGuid(), id, 2, LinesWithProducts(148000), "Revised after clarification"));
        await tenders.Approve(new(Guid.NewGuid(), id, 3));
        await tenders.Submit(new(Guid.NewGuid(), id, 3, today));
        view = await View(id);
        Assert.Equal((TenderStatus.Submitted, 3, 3), (view.Status, view.SubmittedVersion, view.Bids.Count));
        Assert.True(view.Bids.Single(x => x.Version == 2).Submitted); // earlier submitted version kept

        await Assert.ThrowsAsync<BusinessException>(() => tenders.Win(new(Guid.NewGuid(), id, "AW-1", today, today.AddDays(30), [new(3, 2)]))); // alternative line has no product
        await Assert.ThrowsAsync<BusinessException>(() => tenders.Win(new(Guid.NewGuid(), id, "AW-1", today, today.AddDays(30), [new(1, 3)]))); // more than bid
        var order = await tenders.Win(new(Guid.NewGuid(), id, "MOH/AW/2026/14", today, today.AddDays(45), [new(1, 1), new(2, 10)]));
        view = await View(id);
        Assert.Equal((TenderStatus.Won, order), (view.Status, view.OrderId));
        var sales = (await new OrderService(factory, session, Options.Create(new TradingOptions())).Read()).Single(x => x.Id == order);
        Assert.Equal((OrderType.SalesOrder, "National Hospital Services"), (sales.Type, sales.Party));
        Assert.Contains("NHSL/RFQ/2026/31", sales.Reference);
        Assert.Equal([(analyser, 1m, 148000m), (reagent, 10m, 1680m)], sales.Lines.OrderBy(x => x.Sku).Select(x => (x.ProductId, x.Quantity, x.UnitPrice)));
        await Assert.ThrowsAsync<BusinessException>(() => tenders.Cancel(new(Guid.NewGuid(), id, "Too late")));
        Assert.Equal(["Created", "BidSaved", "BidSaved", "Approved", "Submitted", "Clarification", "BidSaved", "Approved", "Submitted", "Won"], view.Events.Select(x => x.Kind));
        await Verified();
    }
    private List<TenderLineInput> LinesWithProducts(decimal analyserPrice) => [.. Lines(analyserPrice).Select((l, i) => i switch { 0 => l with { ProductId = analyser }, 1 => l with { ProductId = reagent }, _ => l })];

    [Fact] public async Task LostAndCancelledTendersCloseWithReasonsAndClosingCanBeExtended()
    {
        var lost = await Create("RFQ-A");
        await tenders.SaveBid(new(Guid.NewGuid(), lost, 0, Lines()));
        await Assert.ThrowsAsync<BusinessException>(() => tenders.Lose(new(Guid.NewGuid(), lost, "Not submitted"))); // only submitted bids are lost
        await tenders.Approve(new(Guid.NewGuid(), lost, 1));
        await tenders.ChangeClosing(new(Guid.NewGuid(), lost, today.AddDays(30), "15:00", "Addendum 2 extended the closing date"));
        Assert.Equal((today.AddDays(30), "15:00"), ((await View(lost)).Closing, (await View(lost)).ClosingTime));
        await tenders.Submit(new(Guid.NewGuid(), lost, 1, today));
        await tenders.Lose(new(Guid.NewGuid(), lost, "Price about 8% higher than the winner", "MedSupply SL", 355000));
        var view = await View(lost);
        Assert.Equal(TenderStatus.Lost, view.Status);
        Assert.Contains("MedSupply SL at NLe 355,000.00", view.Outcome);
        await Assert.ThrowsAsync<BusinessException>(() => tenders.SaveBid(new(Guid.NewGuid(), lost, 1, Lines(), "After loss")));

        var cancelled = await Create("RFQ-B");
        await Assert.ThrowsAsync<BusinessException>(() => tenders.Cancel(new(Guid.NewGuid(), cancelled, "")));
        await tenders.Cancel(new(Guid.NewGuid(), cancelled, "Decided not to bid: delivery period too short"));
        Assert.Equal(TenderStatus.Cancelled, (await View(cancelled)).Status);
        Assert.Equal(new[] { lost, cancelled }.OrderBy(x => x).ToArray(), (await tenders.List()).Select(x => x.Id).OrderBy(x => x).ToArray());
        await Verified();
    }

    [Fact] public async Task ApprovalNeedsAnotherPricingApproverWhenOneExistsAndCostsFollowPermissions()
    {
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(new IdentityUser { Id = "manager", UserName = "manager@example.sl", NormalizedUserName = "MANAGER@EXAMPLE.SL" });
            db.UserClaims.AddRange(new IdentityUserClaim<string> { UserId = "manager", ClaimType = "branch", ClaimValue = "FREETOWN" }, new IdentityUserClaim<string> { UserId = "manager", ClaimType = "permission", ClaimValue = "pricing.manage" });
            await db.SaveChangesAsync();
        }
        var id = await Create();
        await tenders.SaveBid(new(Guid.NewGuid(), id, 0, Lines()));
        Assert.Contains("Another person", (await Assert.ThrowsAsync<BusinessException>(() => tenders.Approve(new(Guid.NewGuid(), id, 1)))).Message);
        session.Actor = session.Actor with { Id = "manager" };
        await tenders.Approve(new(Guid.NewGuid(), id, 1, "Checked against supplier quote"));
        Assert.Equal("manager@example.sl", (await View(id)).Bids.Single().ApprovedBy);

        session.Actor = session.Actor with { Id = "viewer", Permissions = new HashSet<string> { "business.read" } };
        var hidden = (await View(id)).Bids.Single();
        Assert.Equal((0m, 0m, 160000m), (hidden.CostTotal, hidden.Lines[0].SupplierCost, hidden.Lines[0].UnitPrice)); // prices yes, costs no
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create("RFQ-C"));
        session.Actor = session.Actor with { Id = "clerk", Permissions = new HashSet<string> { "business.read", "sales.create" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tenders.Approve(new(Guid.NewGuid(), id, 1)));
    }

    [Fact] public async Task TenderDocumentsAreLinkedAndConfidentialOnesStayHidden()
    {
        var id = await Create();
        var pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();
        var doc = await library.Add(new(Guid.NewGuid(), new(LibraryDocumentType.TenderDocument, "Tender dossier NHSL/RFQ/2026/31", "NHSL/RFQ/2026/31", "National Hospital Services", DueDate: today.AddDays(12)), new("dossier.pdf", pdf), [new(DocumentLinkTarget.Tender, id.ToString())]));
        var priced = await library.Add(new(Guid.NewGuid(), new(LibraryDocumentType.TenderDocument, "Our priced bid", Confidential: true), new("bid.pdf", [.. pdf, 32]), [new(DocumentLinkTarget.Tender, id.ToString())]));
        Assert.Equal(2, (await library.Search(new(Target: DocumentLinkTarget.Tender, Key: id.ToString()))).Count);
        Assert.Contains((await library.Targets(DocumentLinkTarget.Tender, "RFQ")), x => x.Key == id.ToString());
        await Assert.ThrowsAsync<BusinessException>(() => library.Link(new(Guid.NewGuid(), doc, DocumentLinkTarget.Tender, Guid.NewGuid().ToString())));

        session.Actor = session.Actor with { Id = "clerk", Permissions = new HashSet<string> { "business.read", "sales.create" } };
        var visible = await library.Search(new(Target: DocumentLinkTarget.Tender, Key: id.ToString()));
        Assert.Equal(doc, visible.Single().Id); // the confidential priced bid is hidden from a clerk
        await Assert.ThrowsAsync<BusinessException>(() => library.Get(priced));
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
