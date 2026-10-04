using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>0.22.0. Skill 19: organisation types, institution contacts, statements of account with partial payments and ageing. Skill 18: furniture colour/size variants with separate stock, bundle offers priced as a package.</summary>
public sealed class CustomerMerchandiseTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-crm-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private BusinessService business = null!; private PartyService parties = null!; private FinanceService finance = null!; private MerchandiseService merchandise = null!; private ControlService controls = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid widget;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options); parties = new(factory, session, options); finance = new(factory, session, options); merchandise = new(factory, session, options); controls = new(factory, session, options);
        widget = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "WID-1", "Widget", "General", "piece", 100, TrackingMode.Quantity, MinimumPrice: 50));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today.AddDays(-60), [new(widget, 100, 40)], null));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private async Task<decimal> OnHand(Guid product) { await using var db = factory.CreateDbContext(); return (await db.Movements.Where(x => x.ProductId == product).Select(x => x.Quantity).ToListAsync()).Sum(); }
    private async Task Verified() { await using var db = factory.CreateDbContext(); var report = await HistoryProtection.Verify(db); Assert.True(report.Ok, string.Join("; ", report.Problems)); }

    [Fact] public async Task InstitutionsHaveTypesContactsAndAStatementOfAccount()
    {
        var id = await parties.Save(new(Guid.NewGuid(), null, 0, "CONN", "Connaught Hospital", PartyType.Customer, "Dr Sesay", "+232 76 111111", "", "Freetown", 30, 50_000, false,
            "Hospital / clinic", "tin-778899", [new("Procurement", "Fatmata Koroma", "+232 76 222222", "procurement@connaught.sl"), new("Finance", "Ibrahim Bah"), new("Technical / biomedical", "Musa Conteh")], "Pays by cheque"));
        var party = (await parties.Read()).Single();
        Assert.Equal(("Hospital / clinic", "TIN-778899", 3), (party.Category, party.Tin, party.People.Count));
        await Assert.ThrowsAsync<BusinessException>(() => parties.Save(new(Guid.NewGuid(), id, party.Version, "CONN", "Connaught Hospital", PartyType.Customer, "", "", "", "", 30, 50_000, false, Contacts: [new("Finance", "Bad", Email: "not-an-email")])));
        await parties.Save(new(Guid.NewGuid(), id, party.Version, "CONN", "Connaught Hospital", PartyType.Customer, "Dr Sesay", "", "", "Freetown", 30, 50_000, false)); // older screens leave contacts unchanged
        Assert.Equal(3, (await parties.Read()).Single().People.Count);

        var before = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Connaught Hospital", today.AddDays(-50), [new(widget, 5, 100)], null)); // 500, now 50 days old
        var invoice = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Connaught Hospital", today.AddDays(-10), [new(widget, 10, 100)], null)); // 1,000
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, today.AddDays(-5), "Cheque 004512", [new(1000, 400, 0), new(1100, 0, 400)], [new(invoice, 400)])); // partial payment
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Kissy Clinic", today, [new(widget, 1, 100)], PaymentMethod.Cash)); // another customer

        var st = await parties.Statement(id, today.AddDays(-20), today);
        Assert.Equal(500m, st.Opening);
        Assert.Equal(2, st.Lines.Count);
        Assert.Equal((1000m, 0m, 1500m), (st.Lines[0].Debit, st.Lines[0].Credit, st.Lines[0].Balance));
        Assert.Equal((0m, 400m, 1100m), (st.Lines[1].Debit, st.Lines[1].Credit, st.Lines[1].Balance));
        Assert.Equal(1100m, st.Closing);
        Assert.Equal(1100m, st.Current + st.Days30 + st.Days60 + st.Days90 + st.Over90); // ageing agrees with the balance
        Assert.Equal(500m, st.Days30 + st.Days60); // the older invoice is overdue
        var workbook = await parties.ExportStatement(id, today.AddDays(-20), today);
        using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(new MemoryStream(workbook), false)) Assert.Equal(2, doc.WorkbookPart!.Workbook!.Sheets!.Count());

        session.Actor = new("clerk", "FREETOWN", new HashSet<string> { "business.read", "sales.create" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => parties.Statement(id, today.AddDays(-20), today));
        _ = before; await Verified();
    }

    [Fact] public async Task FurnitureVariantsKeepTheirOwnStock()
    {
        var group = await merchandise.CreateVariants(new(Guid.NewGuid(), "CHR-EXE", "Executive chair", "Furniture", "piece", 1500, [new("Colour", ["Black", "Brown"]), new("Size", ["Standard", "XL"])], NeedsAssembly: true, MinimumPrice: 1200));
        var view = (await merchandise.Variants()).Single();
        Assert.Equal(4, view.Variants.Count);
        var black = view.Variants.Single(v => v.Values["Colour"] == "Black" && v.Values["Size"] == "XL");
        var brown = view.Variants.Single(v => v.Values["Colour"] == "Brown" && v.Values["Size"] == "XL");
        Assert.Equal("CHR-EXE-BLA-XL", black.Sku); Assert.Equal("Executive chair – Black, XL", black.Name);
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Office World", today, [new(black.ProductId, 5, 900), new(brown.ProductId, 3, 900)], null));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(black.ProductId, 2, 1500)], PaymentMethod.Cash));
        Assert.Equal(3m, await OnHand(black.ProductId)); Assert.Equal(3m, await OnHand(brown.ProductId)); // selling black leaves brown untouched
        view = (await merchandise.Variants()).Single();
        Assert.Equal(6m, view.OnHand);

        await merchandise.AddVariant(new(Guid.NewGuid(), group, new Dictionary<string, string> { ["Colour"] = "Grey", ["Size"] = "XL" }, 1600));
        await Assert.ThrowsAsync<BusinessException>(() => merchandise.AddVariant(new(Guid.NewGuid(), group, new Dictionary<string, string> { ["Colour"] = "grey", ["Size"] = "xl" }, 1600)));
        await Assert.ThrowsAsync<BusinessException>(() => merchandise.AddVariant(new(Guid.NewGuid(), group, new Dictionary<string, string> { ["Colour"] = "Red" }, 1600)));
        await Assert.ThrowsAsync<BusinessException>(() => merchandise.CreateVariants(new(Guid.NewGuid(), "CHR-EXE", "Duplicate", "Furniture", "piece", 1, [new("Colour", ["Red"])])));
        Assert.Equal(5, (await merchandise.Variants()).Single().Variants.Count);
        session.Actor = new("clerk", "FREETOWN", new HashSet<string> { "business.read", "sales.create" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => merchandise.CreateVariants(new(Guid.NewGuid(), "TBL", "Table", "Furniture", "piece", 1, [new("Colour", ["Oak"])])));
    }

    [Fact] public async Task BundlesSellAtThePackagePriceWithoutTrippingDiscountControls()
    {
        var desk = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "DSK-1", "Office desk", "Furniture", "piece", 2000, TrackingMode.Quantity, MinimumPrice: 1900));
        var chair = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "CHR-1", "Office chair", "Furniture", "piece", 1000, TrackingMode.Quantity, MinimumPrice: 950));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Office World", today, [new(desk, 10, 1200), new(chair, 10, 500)], null));
        await controls.SavePolicy(new(Guid.NewGuid(), ControlPolicy.Recommended)); // 10% discount limit
        var bundleId = await merchandise.SaveBundle(new(Guid.NewGuid(), null, 0, "office-set", "Desk and chair", 2500, [new(desk, 1), new(chair, 1)], true, today.AddDays(30)));
        var bundle = (await merchandise.Bundles()).Single();
        Assert.Equal(("OFFICE-SET", 3000m, 500m), (bundle.Code, bundle.ListValue, bundle.Saving));

        var lines = (await merchandise.Expand(bundleId, 3)).ToList();
        Assert.Equal(7500m, lines.Sum(l => AccountingEngine.Money(l.Quantity * l.UnitPrice)));
        var sale = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, lines, PaymentMethod.Cash)); // 17% off list, below minimum prices, yet no approval: the bundle price is approved
        await using (var db = factory.CreateDbContext()) Assert.Equal(7500m, (await db.Documents.SingleAsync(x => x.Id == sale)).Total);
        Assert.Equal(7m, await OnHand(desk)); Assert.Equal(7m, await OnHand(chair));

        var tampered = lines.Select((l, i) => i == 0 ? l with { UnitPrice = l.UnitPrice - 100 } : l).ToList();
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, tampered, PaymentMethod.Cash)));
        var partial = lines.Take(1).ToList();
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, partial, PaymentMethod.Cash)));
        await merchandise.SaveBundle(new(Guid.NewGuid(), bundleId, bundle.Version, "OFFICE-SET", "Desk and chair", 2500, [new(desk, 1), new(chair, 1)], false));
        await Assert.ThrowsAsync<BusinessException>(() => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, lines, PaymentMethod.Cash)));
        await Assert.ThrowsAsync<BusinessException>(() => merchandise.SaveBundle(new(Guid.NewGuid(), null, 0, "SOLO", "One item", 100, [new(desk, 1)])));
        await Verified();
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
