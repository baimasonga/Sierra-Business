using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Document library (0.15.0): capture and storage of tenders, cheques and other business papers with versions, links, confidentiality and tamper detection.</summary>
public sealed class DocumentLibraryTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-library-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private BusinessService business = null!; private DocumentLibrary library = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static byte[] Pdf(string text) => Encoding.ASCII.GetBytes("%PDF-1.4\n% " + text + "\n%%EOF");
    private static byte[] Jpeg(int seed) => [0xFF, 0xD8, 0xFF, 0xE0, (byte)seed, 1, 2, 3, 4, 5, 6, 7, 0xFF, 0xD9];

    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        var options = Options.Create(new TradingOptions());
        business = new(factory, options); library = new(factory, session, options, new DatabaseFileStore());
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private Task<Guid> Add(string title, byte[] content, string file = "tender.pdf", LibraryDocumentType type = LibraryDocumentType.TenderDocument, DateOnly? due = null, string text = "", bool confidential = false, List<DocumentLinkInput>? links = null) =>
        library.Add(new(Guid.NewGuid(), new(type, title, "MOH/RFQ/2026/014", "Ministry of Health", today, 125000, "SLE", due, "tender, medical", "", confidential), new(file, content, text, text.Length > 0 ? 1 : 0, .9f), links));
    private async Task Verified() { await using var db = factory.CreateDbContext(); var report = await HistoryProtection.Verify(db); Assert.True(report.Ok, string.Join("; ", report.Problems)); }

    [Fact] public async Task TenderDocumentIsStoredSearchableVersionedAndListedByDeadline()
    {
        var original = Pdf("Invitation to bid for X-ray units");
        var id = await Add("Supply of mobile X-ray units", original, due: today.AddDays(10), text: "Invitation to bid. Closing date: 13 October 2026. Bid security required.");
        Assert.Contains("already stored", (await Assert.ThrowsAsync<BusinessException>(() => Add("Same file again", original))).Message);
        Assert.Equal(id, (await library.FindDuplicate(original))!.Id);

        Assert.Single(await library.Search(new("bid security"))); // text inside the file
        Assert.Single(await library.Search(new("RFQ/2026", LibraryDocumentType.TenderDocument)));
        Assert.Empty(await library.Search(new("", LibraryDocumentType.Cheque)));
        var deadline = (await library.Deadlines()).Single();
        Assert.Equal((id, 10), (deadline.Id, deadline.DaysLeft));

        var view = await library.Get(id);
        Assert.Equal(("Supply of mobile X-ray units", 1, 1), (view.Item.Details.Title, view.Item.Version, view.Files.Count));
        var download = await library.Download(view.Files.Single().Id);
        Assert.Equal(original, download.Content); Assert.Equal(("application/pdf", "tender.pdf"), (download.ContentType, download.FileName));

        await Assert.ThrowsAsync<BusinessException>(() => library.AddVersion(new(Guid.NewGuid(), id, new("addendum.pdf", Pdf("Addendum 1")), ""))); // reason required
        await library.AddVersion(new(Guid.NewGuid(), id, new("addendum.pdf", Pdf("Addendum 1: deadline extended")), "Addendum 1 issued by the procuring entity"));
        view = await library.Get(id);
        Assert.Equal((2, "addendum.pdf"), (view.Files.Count, view.Item.LatestFileName));
        Assert.Equal(original, (await library.Download(view.Files.Last().Id)).Content); // earlier version kept

        await library.Update(new(Guid.NewGuid(), id, 1, view.Item.Details with { DueDate = today.AddDays(20) }, "Deadline extended by addendum 1"));
        await Assert.ThrowsAsync<BusinessException>(() => library.Update(new(Guid.NewGuid(), id, 1, view.Item.Details, "Stale"))); // someone else changed it
        Assert.Equal(20, (await library.Deadlines()).Single().DaysLeft);
        Assert.Equal(["Details updated", "Created"], (await library.Get(id)).History.Select(x => x.Action));

        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read", "sales.create" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => library.Void(new(Guid.NewGuid(), id, 2, "Not needed")));
        session.Actor = session.Actor with { Permissions = BusinessPermissions.All.ToHashSet() };
        await library.Void(new(Guid.NewGuid(), id, 2, "Tender cancelled by the ministry"));
        Assert.Empty(await library.Search(new()));
        Assert.True((await library.Search(new(IncludeVoided: true))).Single().Voided);
        Assert.Empty(await library.Deadlines());
        Assert.Equal("Tender cancelled by the ministry", (await library.Get(id)).VoidReason);
        await Assert.ThrowsAsync<BusinessException>(() => library.AddVersion(new(Guid.NewGuid(), id, new("late.pdf", Pdf("late")), "Late")));
        await Add("Re-filed after voiding", original); // a voided document no longer blocks its file
        await Verified();
    }

    [Fact] public async Task ChequePhotoWithoutTextIsLinkedToTheChequeTransactionAndCustomer()
    {
        var product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(product, 10, 40)], null));
        var sale = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "St Edward's School", today, [new(product, 2, 100)], PaymentMethod.Cheque, "000451", "Rokel Commercial Bank", today));
        await TestParties.Register(factory, "St Edward's School");
        var cheque = (await library.Targets(DocumentLinkTarget.Cheque, "000451")).Single();
        var party = (await library.Targets(DocumentLinkTarget.Party, "edward")).Single();
        Assert.Contains("ROKEL", cheque.Label);

        var id = await Add("Cheque 000451 front", Jpeg(1), "cheque-000451.jpg", LibraryDocumentType.Cheque,
            links: [new(DocumentLinkTarget.Cheque, cheque.Key), new(DocumentLinkTarget.Transaction, sale.ToString()), new(DocumentLinkTarget.Party, party.Key)]);
        Assert.Equal(3, (await library.Get(id)).Links.Count);
        Assert.Single(await library.Search(new(Target: DocumentLinkTarget.Cheque, Key: cheque.Key)));
        Assert.Single(await library.Search(new(Target: DocumentLinkTarget.Transaction, Key: sale.ToString())));
        Assert.Equal("image/jpeg", (await library.Download((await library.Get(id)).Files.Single().Id)).ContentType);

        await Assert.ThrowsAsync<BusinessException>(() => library.Link(new(Guid.NewGuid(), id, DocumentLinkTarget.Cheque, cheque.Key))); // already linked
        await Assert.ThrowsAsync<BusinessException>(() => library.Link(new(Guid.NewGuid(), id, DocumentLinkTarget.Transaction, Guid.NewGuid().ToString())));
        var link = (await library.Get(id)).Links.Single(x => x.Target == DocumentLinkTarget.Party);
        await library.Unlink(new(Guid.NewGuid(), link.Id, "Wrong customer"));
        Assert.Empty(await library.Search(new(Target: DocumentLinkTarget.Party, Key: party.Key)));
        await library.Link(new(Guid.NewGuid(), id, DocumentLinkTarget.Party, party.Key)); // can be linked again
        await Verified();
    }

    [Fact] public async Task FilesAreCheckedAndConfidentialDocumentsAreHidden()
    {
        await Assert.ThrowsAsync<BusinessException>(() => Add("Fake PDF", Encoding.ASCII.GetBytes("MZ not a pdf")));
        await Assert.ThrowsAsync<BusinessException>(() => Add("Program", [0x4D, 0x5A, 0x90], "setup.exe"));
        await Assert.ThrowsAsync<BusinessException>(() => Add("Too big", [.. Pdf("x"), .. new byte[20 * 1024 * 1024]]));
        await Assert.ThrowsAsync<BusinessException>(() => Add("", Pdf("no title")));
        Assert.NotEqual(Guid.Empty, await Add("Price list", Encoding.UTF8.GetBytes("SKU,Price\nPAPER,100\n"), "prices.csv", LibraryDocumentType.Other));
        Assert.NotEqual(Guid.Empty, await Add("Signed contract", [0x50, 0x4B, 0x03, 0x04, 9, 9], "contract.docx", LibraryDocumentType.Contract, confidential: true));

        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read", "sales.create" } };
        var visible = await library.Search(new());
        Assert.Equal(["Price list"], visible.Select(x => x.Details.Title));
        await Assert.ThrowsAsync<BusinessException>(() => Add("Mine", Pdf("secret"), confidential: true)); // only accounting/admin may mark confidential
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Add("Read-only user", Pdf("ro")));
        session.Actor = session.Actor with { Permissions = BusinessPermissions.All.ToHashSet() };
        Assert.Equal(2, (await library.Search(new())).Count);
    }

    [Fact] public async Task AlteredFilesAreDetectedOnDownloadAndByTheIntegrityCheck()
    {
        var id = await Add("Bank guarantee", Pdf("Bank guarantee 2026"), "guarantee.pdf", LibraryDocumentType.Certificate);
        var file = (await library.Get(id)).Files.Single();
        await using (var db = factory.CreateDbContext())
        {
            await HistoryProtection.SuspendGuards(db);
            await db.Database.ExecuteSqlRawAsync("UPDATE FileBlobs SET Content = X'255044462D312E340A25464F52474544'");
            await HistoryProtection.ApplyGuards(db);
        }
        Assert.Contains("does not match the fingerprint", (await Assert.ThrowsAsync<BusinessException>(() => library.Download(file.Id))).Message);
        await using var check = factory.CreateDbContext();
        Assert.False((await HistoryProtection.Verify(check)).Ok);
    }

    [Fact] public async Task EarlierScanAndCaptureDocumentsMoveIntoTheLibraryOnUpgrade()
    {
        var upgradePath = Path.Combine(Path.GetTempPath(), $"sierra-library-upgrade-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={upgradePath};Pooling=False").Options;
        var original = Pdf("Invoice INV-77 from Global Supplies");
        try
        {
            await using (var db = new BusinessDb(options))
            {
                await db.GetService<IMigrator>().MigrateAsync("20261003100406_EquipmentLifecycle");
                var capture = new CapturedDocument { Id = Guid.NewGuid(), Branch = "FREETOWN", FileName = "inv-77.pdf", Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)), Original = original, Payload = "Invoice No: INV-77", Pages = 1, Confidence = .95f, Actor = "owner", Created = DateTimeOffset.UtcNow };
                db.Captures.Add(capture);
                db.CaptureRevisions.Add(new() { Id = Guid.NewGuid(), CaptureId = capture.Id, Version = 1, Actor = "owner", Created = DateTimeOffset.UtcNow, Action = "Offline extraction", Detail = """{"Fields":{"Kind":"Invoice","Reference":"INV-77","Party":"Global Supplies","Date":"1 Oct 2026","Currency":"","Total":"NLe 500","Items":"","Deadline":"","Requirements":""},"Text":"Invoice No: INV-77"}""" });
                db.CaptureRevisions.Add(new() { Id = Guid.NewGuid(), CaptureId = capture.Id, Version = 2, Actor = "clerk", Created = DateTimeOffset.UtcNow, Action = "Reviewed capture", Detail = """{"Fields":{"Kind":"Invoice","Reference":"INV-77","Party":"Global Supplies Ltd","Date":"1 Oct 2026","Currency":"","Total":"NLe 500","Items":"","Deadline":"","Requirements":""},"Text":"Invoice No: INV-77"}""" });
                await db.SaveChangesAsync();
            }
            await using (var db = new BusinessDb(options)) await HistoryProtection.MigrateAsync(db);
            var upgraded = new DocumentLibrary(new Factory(options), session, Options.Create(new TradingOptions()), new DatabaseFileStore());
            var item = (await upgraded.Search(new("INV-77"))).Single();
            Assert.Equal((LibraryDocumentType.SupplierInvoice, "inv-77.pdf", "INV-77", "Global Supplies Ltd"), (item.Details.Type, item.Details.Title, item.Details.Reference, item.Details.Party));
            Assert.Contains("Printed total: NLe 500", item.Details.Notes);
            Assert.Equal(original, (await upgraded.Download(item.LatestFileId)).Content);
            await using var check = new BusinessDb(options);
            Assert.True((await HistoryProtection.Verify(check)).Ok);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(upgradePath); }
    }

    [Fact] public void TextHintsRecogniseCommonBusinessPapers()
    {
        Assert.Equal(LibraryDocumentType.Cheque, DocumentHints.Parse("Pay Bangura Enterprises or bearer the sum of five million leones").Type);
        Assert.Equal(LibraryDocumentType.TenderDocument, DocumentHints.Parse("REQUEST FOR QUOTATION\nReference: NHSL/2026/9\nClosing date: 20 October 2026").Type);
        var invoice = DocumentHints.Parse("TAX INVOICE\nInvoice No: INV-1001\nSupplier: Global Supplies\nDate: 01/10/2026\nTotal: NLe 4,500.00");
        Assert.Equal((LibraryDocumentType.SupplierInvoice, "INV-1001", "Global Supplies", "01/10/2026", "NLe 4,500.00"), (invoice.Type, invoice.Reference, invoice.Party, invoice.Date, invoice.Total));
        Assert.Equal(LibraryDocumentType.Contract, DocumentHints.Parse("SERVICE AGREEMENT between the parties").Type);
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
