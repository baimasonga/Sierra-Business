using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Specification Skill 20 (0.17.0): service jobs, parts consumption once only, warranty claims, chargeable invoices and preventive maintenance.</summary>
public sealed class ServiceTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-service-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private BusinessService business = null!; private EquipmentService equipment = null!; private ServiceDesk desk = null!; private FinanceService finance = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly HashSet<string> Clerk = ["business.read", "sales.create", "inventory.receive"];
    private Guid analyser, fuses, sensor;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options); equipment = new(factory, session, options); desk = new(factory, session, options); finance = new(factory, session, options);
        analyser = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "HEM-5", "Haematology analyser", "Medical", "unit", 150000, TrackingMode.Serial, MinimumPrice: 100000));
        fuses = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "FUS-2A", "Fuse 2A", "Spares", "piece", 500, TrackingMode.Quantity, MinimumPrice: 400));
        sensor = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "SNS-T1", "Temperature sensor", "Spares", "piece", 2000, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(fuses, 10, 10), new(sensor, 5, 800)], null));
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    private async Task Sold(string serial, string customer, int warrantyMonths = 0)
    {
        if (warrantyMonths > 0 && (await equipment.Terms()).All(x => x.ProductId != analyser)) await equipment.SaveTerms(new(Guid.NewGuid(), new(analyser, 0, warrantyMonths, WarrantyStart.Sale, 6, false, [], "Parts and labour")));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "MedEquip Ltd", today, [new(analyser, 1, 100000, 1, serial)], null));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, customer, today, [new(analyser, 1, 150000, 1, serial)], PaymentMethod.BankTransfer, "TT-1", "Rokel Bank", today));
    }
    private async Task<ServiceJobView> Job(Guid id) => (await desk.Read()).Jobs.Single(x => x.Id == id);
    private async Task<decimal> OnHand(Guid product) { await using var db = factory.CreateDbContext(); return await db.Movements.Where(x => x.ProductId == product).SumAsync(x => x.Quantity); }
    private async Task<decimal> Balance(int account) { await using var db = factory.CreateDbContext(); var lines = await db.Journal.Where(x => x.Account == (Account)account).ToListAsync(); return lines.Sum(x => x.Debit - x.Credit); }
    private async Task Verified() { await using var db = factory.CreateDbContext(); var report = await HistoryProtection.Verify(db); Assert.True(report.Ok, string.Join("; ", report.Problems)); }
    private async Task<Guid> Worked(Guid job, string acceptedBy = "Sister Kamara")
    {
        await desk.Complete(new(Guid.NewGuid(), job, today, "Replaced the failed part and tested", acceptedBy, [.. (await Job(job)).Checklist]));
        return job;
    }

    [Fact] public async Task WarrantyJobConsumesPartsOnceAndAbsorbsTheirCost()
    {
        await Sold("A-1", "Connaught Hospital", 12);
        var id = await desk.Log(new(Guid.NewGuid(), "", "Laboratory", "Lab manager 076 000000", analyser, "a-1", "", "Analyser shows error E12 on start-up", ServicePriority.High, today, "Musa"));
        var job = await Job(id);
        Assert.Equal(("Connaught Hospital", ServiceStatus.Assigned, "Musa", "A-1", "Haematology analyser"), (job.Customer, job.Status, job.Engineer, job.Serial, job.Equipment));
        Assert.True(job.WarrantyActive); Assert.Contains("Under customer warranty", job.WarrantyHint);
        await desk.Diagnose(new(Guid.NewGuid(), id, "Blown fuses on the power board"));

        var issue = new IssuePartCommand(Guid.NewGuid(), id, today, fuses, 3);
        var doc = await desk.IssuePart(issue);
        Assert.Equal(doc, await desk.IssuePart(issue)); // the same issue retried does not use stock twice
        Assert.Equal(7, await OnHand(fuses));
        Assert.Equal(30, await Balance(1260));
        await Assert.ThrowsAsync<BusinessException>(() => desk.IssuePart(new(Guid.NewGuid(), id, today, fuses, 50)));
        Assert.Contains("service job", (await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), doc, "Wrong part")))).Message);
        await Assert.ThrowsAsync<BusinessException>(() => desk.ReturnPart(new(Guid.NewGuid(), id, doc, 4, "Too many")));
        await desk.ReturnPart(new(Guid.NewGuid(), id, doc, 1, "Not needed"));
        Assert.Equal(8, await OnHand(fuses));
        await desk.AddLabour(new(Guid.NewGuid(), id, today, "Musa", 2, 5000, "Board repair"));

        job = await Job(id);
        Assert.Equal((ServiceStatus.InProgress, 2m, 1m, 20m), (job.Status, job.Parts.Single().Quantity - job.Parts.Single().Returned, job.Parts.Single().Returned, job.PartsCost!.Value));
        await Assert.ThrowsAsync<BusinessException>(() => desk.Complete(new(Guid.NewGuid(), id, today, "Fixed", "", []))); // customer acceptance is required
        await Worked(id);
        await Assert.ThrowsAsync<BusinessException>(() => desk.AddLabour(new(Guid.NewGuid(), id, today, "Musa", 1, 5000, "More"))); // completed work needs reopening
        Assert.Contains("Decide who pays", (await Assert.ThrowsAsync<BusinessException>(() => desk.Close(new(Guid.NewGuid(), id, today)))).Message);
        await desk.DecideCoverage(new(Guid.NewGuid(), id, ServiceBilling.Warranty));
        await Assert.ThrowsAsync<BusinessException>(() => desk.Close(new(Guid.NewGuid(), id, today, PaymentMethod.Cash))); // nothing to pay
        var closing = await desk.Close(new(Guid.NewGuid(), id, today));

        job = await Job(id);
        Assert.Equal((ServiceStatus.Closed, "No charge", 0m, 0m), (job.Status, job.Settlement, job.PartsCharge, job.LabourCharge));
        Assert.Equal(closing, job.CloseDocumentId);
        Assert.Equal((20m, 0m, 0m), (await Balance(5400), await Balance(1260), await Balance(4300)));
        Assert.Equal(8, await OnHand(fuses)); // closing does not move stock again
        await Assert.ThrowsAsync<BusinessException>(() => desk.ReturnPart(new(Guid.NewGuid(), id, doc, 1, "After closing")));
        var unit = (await equipment.Unit(analyser, "A-1"))!;
        Assert.Contains(unit.Timeline, x => x.Kind == "Service" && x.Description.Contains("Warranty claim accepted"));
        Assert.Contains(unit.Timeline, x => x.Description.StartsWith("Service completed", StringComparison.Ordinal));
        await Verified();
    }

    [Fact] public async Task ChargeableJobIsInvoicedOnCreditAndCollectedLikeASale()
    {
        var id = await desk.Log(new(Guid.NewGuid(), "Kissy Clinic", "Theatre", "", null, "", "Autoclave (bought elsewhere)", "Does not reach temperature", ServicePriority.Urgent, today));
        Assert.Contains("No serial", (await Job(id)).WarrantyHint);
        await Assert.ThrowsAsync<BusinessException>(() => desk.Complete(new(Guid.NewGuid(), id, today, "Fixed", "Matron", []))); // no engineer yet
        await desk.Assign(new(Guid.NewGuid(), id, "Fatmata"));
        await desk.IssuePart(new(Guid.NewGuid(), id, today, sensor, 2, UnitPrice: 2500));
        await desk.AddLabour(new(Guid.NewGuid(), id, today, "Fatmata", 1.5m, 4000, "Replace sensor and test cycle"));
        await Worked(id, "Matron Conteh");
        session.Actor = session.Actor with { Permissions = Clerk };
        await Assert.ThrowsAsync<BusinessException>(() => desk.DecideCoverage(new(Guid.NewGuid(), id, ServiceBilling.Warranty))); // no warranty to claim against
        await desk.DecideCoverage(new(Guid.NewGuid(), id, ServiceBilling.Chargeable));
        Assert.Contains("registered customer", (await Assert.ThrowsAsync<BusinessException>(() => desk.Close(new(Guid.NewGuid(), id, today)))).Message);
        await TestParties.Register(factory, "Kissy Clinic", creditLimit: 10000);
        Assert.Contains("credit limit", (await Assert.ThrowsAsync<BusinessException>(() => desk.Close(new(Guid.NewGuid(), id, today)))).Message);
        session.Actor = session.Actor with { Permissions = BusinessPermissions.All.ToHashSet() };
        var parties = new PartyService(factory, session, Options.Create(new TradingOptions()));
        var kissy = (await parties.Read()).Single(x => x.Name == "Kissy Clinic");
        await parties.Save(new(Guid.NewGuid(), kissy.Id, kissy.Version, kissy.Code, kissy.Name, PartyType.Customer, "", "", "", "", 30, 50000, false));
        var invoice = await desk.Close(new(Guid.NewGuid(), id, today));

        var job = await Job(id);
        Assert.Equal((ServiceStatus.Closed, "Credit", 11000m, 5000m, 6000m), (job.Status, job.Settlement, job.Invoiced, job.PartsCharge, job.LabourCharge));
        Assert.Equal((11000m, -11000m, 1600m, 0m), (await Balance(1100), await Balance(4300), await Balance(5000), await Balance(1260)));
        var bill = (await finance.Read(today.AddDays(-1), today)).Bills.Single(x => x.Id == invoice);
        Assert.Equal((true, 11000m, today.AddDays(30)), (bill.Receivable, bill.Outstanding, bill.DueDate));
        await finance.PostVoucher(new(Guid.NewGuid(), VoucherType.Receipt, today, "Part payment", [new(1000, 4000, 0), new(1100, 0, 4000)], [new(invoice, 4000)]));
        Assert.Equal(7000m, (await finance.Read(today.AddDays(-1), today)).Bills.Single(x => x.Id == invoice).Outstanding);
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), invoice, "Wrong customer")));
        await Verified();
    }

    [Fact] public async Task CoverageDecisionsAreExplicitAndAReversedInvoiceReturnsTheJobForBilling()
    {
        await Sold("B-1", "Choithram Hospital");
        var noWarranty = await desk.Log(new(Guid.NewGuid(), "", "", "", analyser, "B-1", "", "Noisy pump", ServicePriority.Normal, today, "Musa"));
        Assert.Contains("No customer warranty", (await Job(noWarranty)).WarrantyHint);
        session.Actor = session.Actor with { Permissions = Clerk };
        Assert.Contains("warranty exception", (await Assert.ThrowsAsync<BusinessException>(() => desk.DecideCoverage(new(Guid.NewGuid(), noWarranty, ServiceBilling.Warranty)))).Message);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => desk.DecideCoverage(new(Guid.NewGuid(), noWarranty, ServiceBilling.Goodwill, Reason: "Good customer")));
        await Assert.ThrowsAsync<BusinessException>(() => desk.DecideCoverage(new(Guid.NewGuid(), noWarranty, ServiceBilling.Contract)));
        session.Actor = session.Actor with { Permissions = BusinessPermissions.All.ToHashSet() };
        await Assert.ThrowsAsync<BusinessException>(() => desk.DecideCoverage(new(Guid.NewGuid(), noWarranty, ServiceBilling.Warranty))); // an exception needs a reason
        await desk.DecideCoverage(new(Guid.NewGuid(), noWarranty, ServiceBilling.Warranty, Reason: "Fault found within days of the sale"));
        var coverage = (await Job(noWarranty)).Coverage!;
        Assert.Equal((ServiceBilling.Warranty, true), (coverage.Billing, coverage.Exception));

        await Sold("B-2", "Choithram Hospital", 12);
        var id = await desk.Log(new(Guid.NewGuid(), "", "", "", analyser, "B-2", "", "Display flickers", ServicePriority.Low, today, "Musa"));
        await desk.DecideCoverage(new(Guid.NewGuid(), id, ServiceBilling.Warranty, CoversParts: true, CoversLabour: false, Reason: "Call-out labour not covered"));
        await desk.IssuePart(new(Guid.NewGuid(), id, today, fuses, 2));
        await desk.AddLabour(new(Guid.NewGuid(), id, today, "Musa", 1, 3000, "Call-out"));
        await Worked(id);
        var invoice = await desk.Close(new(Guid.NewGuid(), id, today, PaymentMethod.Cash));
        var job = await Job(id);
        Assert.Equal((3000m, 0m, 3000m, "Cash"), (job.Invoiced, job.PartsCharge, job.LabourCharge, job.Settlement));
        Assert.Equal((3000m, 20m, 200000m), (await Balance(1000), await Balance(5400), await Balance(5000))); // cost of sales holds only the two analysers sold: covered parts are not charged

        await business.Reverse(session.Actor, new(Guid.NewGuid(), invoice, "Customer disputes the call-out charge"));
        job = await Job(id);
        Assert.Equal((ServiceStatus.Completed, null, 20m), (job.Status, job.CloseDocumentId, await Balance(1260)));
        Assert.Contains("invoice was reversed", (await Assert.ThrowsAsync<BusinessException>(() => desk.Reopen(new(Guid.NewGuid(), id, "Redo")))).Message);
        await desk.DecideCoverage(new(Guid.NewGuid(), id, ServiceBilling.Goodwill, Reason: "Waived after dispute"));
        await desk.Close(new(Guid.NewGuid(), id, today));
        Assert.Equal((ServiceStatus.Closed, "No charge", 0m), ((await Job(id)).Status, (await Job(id)).Settlement, await Balance(1260)));
        Assert.Equal((0m, 20m), (await Balance(1000), await Balance(5400)));
        await Verified();
    }

    [Fact] public async Task PreventiveMaintenancePlansGenerateEachVisitOnce()
    {
        await Sold("C-1", "Connaught Hospital");
        await Assert.ThrowsAsync<BusinessException>(() => desk.CreatePlan(new(Guid.NewGuid(), analyser, "NOPE-1", "Lab", 3, today, [])));
        var first = today.AddDays(-100);
        var plan = await desk.CreatePlan(new(Guid.NewGuid(), analyser, "c-1", "Main laboratory", 3, first, ["Clean optics", "Calibrate with control samples"], "Musa", "Bring control kit"));
        await Assert.ThrowsAsync<BusinessException>(() => desk.CreatePlan(new(Guid.NewGuid(), analyser, "C-1", "Lab", 6, today, [])));

        await desk.GenerateDue(new(Guid.NewGuid(), today.AddDays(30)));
        await desk.GenerateDue(new(Guid.NewGuid(), today.AddDays(30))); // a second run adds nothing
        var view = await desk.Read();
        var visits = view.Jobs.Where(x => x.Source == "Preventive").OrderBy(x => x.PlanDue).ToList();
        Assert.Equal([first, first.AddMonths(3)], visits.Select(x => x.PlanDue!.Value));
        Assert.All(visits, v => Assert.Equal((ServiceStatus.Assigned, "Musa", "Connaught Hospital", ServiceBilling.Contract, 2), (v.Status, v.Engineer, v.Customer, v.Coverage!.Billing, v.Checklist.Count)));
        var p = view.Plans.Single();
        Assert.Equal((first.AddMonths(6), 2, true), (p.NextDue, p.Visits, p.Active));

        var visit = visits[0].Id;
        Assert.Contains("Calibrate", (await Assert.ThrowsAsync<BusinessException>(() => desk.Complete(new(Guid.NewGuid(), visit, today, "Serviced", "Lab manager", ["Clean optics"])))).Message);
        await Worked(visit, "Lab manager");
        await desk.Close(new(Guid.NewGuid(), visit, today));
        Assert.Equal((ServiceStatus.Closed, null), ((await Job(visit)).Status, (await Job(visit)).CloseDocumentId)); // no parts, no charge: nothing to post
        Assert.Equal(today, (await desk.Read()).Plans.Single().LastVisit);

        await desk.EndPlan(new(Guid.NewGuid(), plan, "Contract not renewed"));
        await desk.GenerateDue(new(Guid.NewGuid(), today.AddDays(90)));
        Assert.Equal(2, (await desk.Read()).Jobs.Count(x => x.Source == "Preventive"));
        Assert.False((await desk.Read()).Plans.Single().Active);
        await Assert.ThrowsAsync<BusinessException>(() => desk.GenerateDue(new(Guid.NewGuid(), today.AddDays(120))));
        await Verified();
    }

    [Fact] public async Task CancellingNeedsPartsBackClaimsAreTrackedAndViewersCannotChangeJobs()
    {
        await Sold("D-1", "Connaught Hospital", 12);
        var id = await desk.Log(new(Guid.NewGuid(), "", "", "", analyser, "D-1", "", "Dead on arrival", ServicePriority.Urgent, today, "Musa"));
        var issue = await desk.IssuePart(new(Guid.NewGuid(), id, today, sensor, 1));
        Assert.Contains("Return the parts", (await Assert.ThrowsAsync<BusinessException>(() => desk.Cancel(new(Guid.NewGuid(), id, "Unit replaced instead")))).Message);
        await desk.SupplierClaim(new(Guid.NewGuid(), id, "MedEquip Ltd", "RMA-7781", "Dead on arrival"));
        await Assert.ThrowsAsync<BusinessException>(() => desk.SupplierClaim(new(Guid.NewGuid(), id, "MedEquip Ltd", "RMA-7782")));
        await desk.SupplierClaimOutcome(new(Guid.NewGuid(), id, "Replaced by supplier", "New unit shipped"));
        Assert.Contains("Replaced by supplier", (await Job(id)).SupplierClaim);
        Assert.NotNull((await Job(id)).SupplierWarrantyEnd);
        await desk.ReturnPart(new(Guid.NewGuid(), id, issue, 1, "Not used"));
        await desk.Cancel(new(Guid.NewGuid(), id, "Unit replaced by supplier"));
        Assert.Equal((ServiceStatus.Cancelled, 5m, 0m), ((await Job(id)).Status, await OnHand(sensor), await Balance(1260)));
        await Assert.ThrowsAsync<BusinessException>(() => desk.IssuePart(new(Guid.NewGuid(), id, today, sensor, 1)));

        var open = await desk.Log(new(Guid.NewGuid(), "", "", "", analyser, "D-1", "", "Check after replacement", ServicePriority.Low, today, "Musa"));
        await desk.IssuePart(new(Guid.NewGuid(), open, today, fuses, 1));
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => desk.Log(new(Guid.NewGuid(), "X", "", "", null, "", "Fridge", "Warm", ServicePriority.Low, today)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => desk.Note(new(Guid.NewGuid(), open, "Hello")));
        var hidden = await Job(open);
        Assert.Equal((null, null, false), (hidden.PartsCost, hidden.Parts.Single().Cost, hidden.CostsVisible));
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
