using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Specification Skills 17 and 20 (0.14.0): serial receive → sell → install → warranty, return then resale, warranty lookup, accessory compatibility, installation completion.</summary>
public sealed class EquipmentTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-equipment-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private BusinessService business = null!; private EquipmentService equipment = null!; private ReturnService returns = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid xray, printer, toner;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options); equipment = new(factory, session, options); returns = new(factory, session, options);
        xray = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "XRAY-M1", "Mobile X-ray unit", "Medical", "unit", 250000, TrackingMode.Serial, MinimumPrice: 1));
        printer = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PRN-400", "Laser printer", "IT", "unit", 4000, TrackingMode.Serial, MinimumPrice: 1));
        toner = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "TNR-400", "Toner for PRN-400", "IT", "cartridge", 900, TrackingMode.Quantity, MinimumPrice: 1));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    private Task<Guid> Receive(Guid product, string serial, decimal cost, DateOnly? date = null) => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", date ?? today, [new(product, 1, cost, 1, serial)], null));
    private Task<Guid> Sell(Guid product, string serial, string customer) => business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, customer, today, [new(product, 1, 300000, 1, serial)], PaymentMethod.BankTransfer, "TT-1", "Rokel Bank", today));
    private Task<Guid> Terms(Guid product, int months, WarrantyStart start, bool install = false, int supplier = 0, params string[] checklist) => equipment.SaveTerms(new(Guid.NewGuid(), new(product, 0, months, start, supplier, install, checklist, "Parts and labour")));
    private async Task Verified() { await using var db = factory.CreateDbContext(); var report = await HistoryProtection.Verify(db); Assert.True(report.Ok, string.Join("; ", report.Problems)); }

    [Fact] public async Task SerialIsReceivedSoldInstalledWithChecklistAndWarrantyStartsAtInstallation()
    {
        await Terms(xray, 24, WarrantyStart.Installation, true, 12, "Power and earthing checked", "Calibration verified", "Operator training");
        await Receive(xray, "xr-1001", 180000, today.AddDays(-20)); await Receive(xray, "XR-1002", 180000);
        var sale = await Sell(xray, "XR-1001", "Connaught Hospital");
        Assert.Empty(await equipment.Warranties()); // starts at installation, not at sale

        var installable = (await equipment.Installable()).Single();
        Assert.Equal((sale, "Connaught Hospital", "XR-1001"), (installable.SaleId, installable.Customer, installable.Units.Single().Serial));
        Assert.Contains("not supplied", (await Assert.ThrowsAsync<BusinessException>(() => equipment.ScheduleInstallation(new(Guid.NewGuid(), sale, "Radiology", today, "Bangz", [new(xray, "XR-1002")])))).Message);
        var job = await equipment.ScheduleInstallation(new(Guid.NewGuid(), sale, "Radiology department, Connaught", today, "Bangz", [new(xray, "xr-1001")], "Bring earthing kit"));
        Assert.Empty(await equipment.Installable());
        await Assert.ThrowsAsync<BusinessException>(() => equipment.ScheduleInstallation(new(Guid.NewGuid(), sale, "Again", today, "Bangz", [new(xray, "XR-1001")])));
        var view = (await equipment.Installations()).Single();
        Assert.Equal(("Scheduled", 3), (view.Status, view.Checklist.Count));

        var missing = await Assert.ThrowsAsync<BusinessException>(() => equipment.CompleteInstallation(new(Guid.NewGuid(), job, today, ["Power and earthing checked"], "Dr Kamara")));
        Assert.Contains("Calibration verified", missing.Message);
        await Assert.ThrowsAsync<BusinessException>(() => equipment.CompleteInstallation(new(Guid.NewGuid(), job, today, [.. view.Checklist], ""))); // customer acceptance is required
        await equipment.CompleteInstallation(new(Guid.NewGuid(), job, today, [.. view.Checklist], "Dr Kamara, Head of Radiology", "Two radiographers"));
        Assert.Equal("Completed", (await equipment.Installations()).Single().Status);
        await Assert.ThrowsAsync<BusinessException>(() => equipment.CancelInstallation(new(Guid.NewGuid(), job, "Too late")));

        var warranty = (await equipment.Warranties()).Single();
        Assert.Equal(("XR-1001", "Connaught Hospital", today, today.AddMonths(24).AddDays(-1), "Active", "Installation"), (warranty.Serial, warranty.Customer, warranty.Start, warranty.End, warranty.Status, warranty.Source));

        var unit = (await equipment.Unit(xray, "xr-1001"))!;
        Assert.Equal(("With customer, installed", "Connaught Hospital", "MedEquip Ltd", 180000m), (unit.Status, unit.Customer, unit.Supplier, unit.Cost));
        Assert.True(unit.Coverage.Covered);
        Assert.Equal(today.AddDays(-20).AddMonths(12).AddDays(-1), unit.SupplierWarrantyEnd);
        Assert.Equal(["Receipt", "Sale", "Installation", "Installation", "Warranty"], unit.Timeline.Select(x => x.Kind));
        Assert.False(unit.CanStartWarranty);
        Assert.Equal("In stock (MAIN)", (await equipment.Unit(xray, "XR-1002"))!.Status);
        await Verified();
    }

    [Fact] public async Task ReturnVoidsTheWarrantyAndResaleStartsANewOne()
    {
        await Terms(printer, 12, WarrantyStart.Sale);
        await Receive(printer, "PRN-77", 2500);
        var first = await Sell(printer, "PRN-77", "Fourah Bay College");
        var warranty = (await equipment.Warranties()).Single();
        Assert.Equal(("Active", first, today.AddMonths(12).AddDays(-1)), (warranty.Status, (await Single()).SaleId, warranty.End));

        await returns.Post(new(Guid.NewGuid(), first, "Wrong model ordered", [new(printer, "PRN-77", null, null, 1)], ReturnDisposition.BankRefund));
        Assert.Equal("Void", (await equipment.Warranties()).Single().Status);
        var unit = (await equipment.Unit(printer, "PRN-77"))!;
        Assert.Equal(("In stock (MAIN)", false), (unit.Status, unit.Coverage.Covered));

        var second = await Sell(printer, "PRN-77", "Njala University");
        var all = await equipment.Warranties();
        Assert.Equal(2, all.Count);
        Assert.Equal(("Active", "Njala University"), (all[0].Status, all[0].Customer));
        Assert.Equal("Void", all[1].Status);
        unit = (await equipment.Unit(printer, "PRN-77"))!;
        Assert.Equal(("With customer", "Njala University", true), (unit.Status, unit.Customer, unit.Coverage.Covered));
        Assert.Equal(["Receipt", "Sale", "SalesReturn", "Sale", "Warranty", "Warranty"], unit.Timeline.Select(x => x.Kind).OrderBy(x => x == "Warranty").ToArray());

        await business.Reverse(session.Actor, new(Guid.NewGuid(), second, "Invoice raised in error"));
        Assert.All(await equipment.Warranties(), x => Assert.Equal("Void", x.Status));
        await Verified();
    }
    private async Task<Warranty> Single() { await using var db = factory.CreateDbContext(); return await db.Warranties.SingleAsync(); }

    [Fact] public async Task SalesBeforeTermsCanStartAWarrantyAndScheduledInstallationsBlockSaleReversal()
    {
        await Receive(printer, "PRN-90", 2500); await Receive(printer, "PRN-91", 2500);
        var sale = await Sell(printer, "PRN-90", "Ministry of Health");
        Assert.False((await equipment.Unit(printer, "PRN-90"))!.CanStartWarranty); // no terms yet
        await Assert.ThrowsAsync<BusinessException>(() => equipment.StartWarranty(new(Guid.NewGuid(), printer, "PRN-90")));
        await Terms(printer, 6, WarrantyStart.Sale);
        Assert.True((await equipment.Unit(printer, "PRN-90"))!.CanStartWarranty);
        await equipment.StartWarranty(new(Guid.NewGuid(), printer, "PRN-90"));
        Assert.Equal(today.AddMonths(6).AddDays(-1), (await equipment.Warranties()).Single().End);
        await Assert.ThrowsAsync<BusinessException>(() => equipment.StartWarranty(new(Guid.NewGuid(), printer, "PRN-90")));
        await Assert.ThrowsAsync<BusinessException>(() => equipment.StartWarranty(new(Guid.NewGuid(), printer, "PRN-91"))); // still in stock

        var job = await equipment.ScheduleInstallation(new(Guid.NewGuid(), sale, "Youyi Building", today.AddDays(3), "Field engineer", [new(printer, "PRN-90")]));
        Assert.Contains("Cancel the scheduled installation", (await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), sale, "Wrong customer")))).Message);
        await equipment.CancelInstallation(new(Guid.NewGuid(), job, "Customer postponed"));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), sale, "Wrong customer"));
        Assert.Equal("Cancelled", (await equipment.Installations()).Single().Status);
        await Verified();
    }

    [Fact] public async Task AccessoryCompatibilityAndUnitSpecificationsAreRecordedAndSearchable()
    {
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(toner, 8, 400)], null));
        await Receive(printer, "PRN-5", 2500);
        var link = await equipment.Link(new(Guid.NewGuid(), printer, toner, CompatibilityKind.Consumable, "Yields 3,000 pages"));
        await Assert.ThrowsAsync<BusinessException>(() => equipment.Link(new(Guid.NewGuid(), printer, toner, CompatibilityKind.Accessory)));
        await Assert.ThrowsAsync<BusinessException>(() => equipment.Link(new(Guid.NewGuid(), printer, printer, CompatibilityKind.Accessory)));
        var forPrinter = (await equipment.CompatibleWith(printer)).Single();
        Assert.Equal(("TNR-400", CompatibilityKind.Consumable, 8m, false), (forPrinter.Sku, forPrinter.Kind, forPrinter.OnHand, forPrinter.Reverse));
        var forToner = (await equipment.CompatibleWith(toner)).Single();
        Assert.Equal(("PRN-400", true), (forToner.Sku, forToner.Reverse)); // which equipment uses this consumable
        Assert.Single((await equipment.Unit(printer, "PRN-5"))!.Compatible);

        await equipment.SaveProfile(new(Guid.NewGuid(), printer, "prn-5", 0, [new("RAM", "512 MB"), new("Network", "Ethernet + Wi-Fi")], "USB cable, power cord"));
        await Assert.ThrowsAsync<BusinessException>(() => equipment.SaveProfile(new(Guid.NewGuid(), printer, "PRN-5", 0, [new("RAM", "1 GB")]))); // stale version
        await Assert.ThrowsAsync<BusinessException>(() => equipment.SaveProfile(new(Guid.NewGuid(), printer, "PRN-5", 1, [new("RAM", "1 GB"), new("ram", "2 GB")]))); // duplicate names
        await Assert.ThrowsAsync<BusinessException>(() => equipment.SaveProfile(new(Guid.NewGuid(), printer, "NOPE", 0, [new("RAM", "1 GB")])));
        var unit = (await equipment.Unit(printer, "PRN-5"))!;
        Assert.Equal((1, "512 MB", "USB cable, power cord"), (unit.ProfileVersion, unit.Specifications.Single(x => x.Name == "RAM").Value, unit.Accessories));

        var found = (await equipment.Search("prn-")).Single();
        Assert.Equal(("PRN-5", "In stock (MAIN)"), (found.Serial, found.Status));
        Assert.Single(await equipment.Search("Laser"));
        Assert.Empty(await equipment.Search("x"));

        await equipment.Unlink(new(Guid.NewGuid(), link, "Wrong cartridge"));
        Assert.Empty(await equipment.CompatibleWith(printer));
        await Assert.ThrowsAsync<BusinessException>(() => equipment.Unlink(new(Guid.NewGuid(), link, "Again")));
        await Verified();
    }

    [Fact] public async Task TermsAreVersionedValidatedAndPermissionsApply()
    {
        await Assert.ThrowsAsync<BusinessException>(() => Terms(xray, 24, WarrantyStart.Installation)); // needs installation required
        await Assert.ThrowsAsync<BusinessException>(() => Terms(xray, 200, WarrantyStart.Sale));
        await Assert.ThrowsAsync<BusinessException>(() => Terms(xray, 12, WarrantyStart.Sale, true, 0, "Check", "check"));
        await Terms(xray, 12, WarrantyStart.Sale);
        await Assert.ThrowsAsync<BusinessException>(() => Terms(xray, 18, WarrantyStart.Sale)); // stale version 0
        await equipment.SaveTerms(new(Guid.NewGuid(), new(xray, 1, 18, WarrantyStart.Sale)));
        Assert.Equal((2, 18), ((await equipment.Terms()).Single().Version, (await equipment.Terms()).Single().CustomerWarrantyMonths));

        await Receive(xray, "XR-9", 1000);
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read" } };
        Assert.NotNull(await equipment.Unit(xray, "XR-9")); // anyone may look up equipment
        Assert.Null((await equipment.Unit(xray, "XR-9"))!.Cost); // cost needs accounting.read
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Terms(xray, 6, WarrantyStart.Sale));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => equipment.SaveProfile(new(Guid.NewGuid(), xray, "XR-9", 0, [new("Tube", "Toshiba")])));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => equipment.StartWarranty(new(Guid.NewGuid(), xray, "XR-9")));
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
