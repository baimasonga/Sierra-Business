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
    private void AsOwner() => session.Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet());
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
