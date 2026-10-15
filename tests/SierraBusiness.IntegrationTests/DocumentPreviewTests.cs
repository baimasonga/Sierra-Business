using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.Infrastructure.Imports;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;
namespace SierraBusiness.IntegrationTests;

/// <summary>0.16.0: formatted previews of Office and text files, and the thumbnail cache behind the library cards.</summary>
public sealed class DocumentPreviewTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-previews-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private DocumentLibrary library = null!;
    private readonly Session session = new();
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        library = new(factory, session, Options.Create(new TradingOptions()), new DatabaseFileStore());
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    private async Task<Guid> Store(string name, byte[] content, LibraryDocumentType type = LibraryDocumentType.TenderDocument, bool confidential = false)
    {
        var id = await library.Add(new(Guid.NewGuid(), new(type, name, Confidential: confidential), new(name, content)));
        return (await library.Get(id)).Item.LatestFileId;
    }

    private static byte[] Word()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            W.Paragraph Para(string text, string? style = null, bool bold = false) => new(
                style is null ? new W.ParagraphProperties() : new W.ParagraphProperties(new W.ParagraphStyleId { Val = style }),
                new W.Run(bold ? new W.RunProperties(new W.Bold()) : new W.RunProperties(), new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
            var table = new W.Table(
                new W.TableRow(new W.TableCell(Para("Item")), new W.TableCell(Para("Quantity"))),
                new W.TableRow(new W.TableCell(Para("Haematology analyser")), new W.TableCell(Para("2"))));
            main.Document = new W.Document(new W.Body(Para("Invitation to tender", "Title"), Para("Section 1: Instructions", "Heading1"), Para("Bids close on 15 October 2026.", bold: true),
                Para("<script>alert('x')</script> & more"), table));
        }
        return stream.ToArray();
    }
    private static byte[] Slides()
    {
        using var stream = new MemoryStream();
        using (var deck = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var part = deck.AddPresentationPart(); part.Presentation = new P.Presentation();
            var slide = part.AddNewPart<SlidePart>("rId2");
            slide.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(new P.Shape(new P.TextBody(new A.BodyProperties(),
                new A.Paragraph(new A.Run(new A.Text("Technical proposal"))), new A.Paragraph(new A.Run(new A.Text("Installation within 14 days"))))))));
            part.Presentation.SlideIdList = new P.SlideIdList(new P.SlideId { Id = 256U, RelationshipId = "rId2" });
        }
        return stream.ToArray();
    }

    [Fact] public async Task OfficeAndCsvFilesGetFormattedPreviewsWithEncodedText()
    {
        var word = await library.Preview(await Store("dossier.docx", Word()));
        Assert.Equal(PreviewKind.Html, word.Kind);
        Assert.Contains("<h1>Invitation to tender</h1>", word.Html);
        Assert.Contains("<h2>Section 1: Instructions</h2>", word.Html);
        Assert.Contains("<strong>Bids close on 15 October 2026.</strong>", word.Html);
        Assert.Contains("<th>Item</th>", word.Html);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; more", word.Html);
        Assert.DoesNotContain("<script", word.Html);

        var sheet = await library.Preview(await Store("prices.xlsx", WorkbookWriter.Write([("Price schedule", [["Item", "Qty", "Price"], ["Analyser", "2", "148000"], ["=cmd|' /C calc'!A0", "1", "1"]])])));
        Assert.Equal(PreviewKind.Html, sheet.Kind);
        Assert.Contains("<h3>Price schedule</h3>", sheet.Html);
        Assert.Contains("<td class=\"numeric\">148000</td>", sheet.Html);

        var deck = await library.Preview(await Store("proposal.pptx", Slides()));
        Assert.Contains("<h3>Technical proposal</h3>", deck.Html);
        Assert.Contains("<li>Installation within 14 days</li>", deck.Html);

        var csv = await library.Preview(await Store("bids.csv", Encoding.UTF8.GetBytes("Bidder,Amount\n\"Sierra, Ltd\",355000\n<b>x</b>,1\n")));
        Assert.Contains("<td>Sierra, Ltd</td>", csv.Html);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", csv.Html);

        var pdf = await library.Preview(await Store("scan.pdf", "%PDF-1.4\n%%EOF"u8.ToArray()));
        Assert.Equal((PreviewKind.Native, "application/pdf"), (pdf.Kind, pdf.ContentType)); // drawn by the viewer script

        var broken = DocumentPreviews.For("bad.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "PK\u0003\u0004not really"u8.ToArray());
        Assert.Equal(PreviewKind.None, broken.Kind);
    }

    [Fact] public async Task ThumbnailsAreCachedByFingerprintValidatedAndFollowVisibility()
    {
        var open = await Store("cheque.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3], LibraryDocumentType.Cheque);
        var secret = await Store("bid.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9], confidential: true);
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
        Assert.Empty(await library.Thumbnails([open, secret]));
        await Assert.ThrowsAsync<BusinessException>(() => library.SaveThumbnail(open, [0x89, 0x50, 0x4E, 0x47])); // not a JPEG
        await Assert.ThrowsAsync<BusinessException>(() => library.SaveThumbnail(open, [0xFF, 0xD8, 0xFF, .. new byte[260_000]])); // too large
        await library.SaveThumbnail(open, jpeg);
        await library.SaveThumbnail(open, [0xFF, 0xD8, 0xFF, 0xEE]); // first picture wins
        await library.SaveThumbnail(secret, jpeg);
        Assert.Equal(jpeg, (await library.Thumbnails([open, secret]))[open]);
        Assert.Equal(2, (await library.Thumbnails([open, secret])).Count);

        session.Actor = session.Actor with { Id = "clerk", Permissions = new HashSet<string> { "business.read", "sales.create" } };
        Assert.Equal([open], (await library.Thumbnails([open, secret])).Keys); // confidential picture hidden
        await Assert.ThrowsAsync<BusinessException>(() => library.Preview(secret));
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => library.SaveThumbnail(open, jpeg)); // viewers do not write pictures
        await using var db = factory.CreateDbContext();
        Assert.True((await HistoryProtection.Verify(db)).Ok); // pictures are derived data outside the sealed history
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
