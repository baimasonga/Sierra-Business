using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

public sealed record CaptureDraft(CaptureFields Fields, string Text);
public sealed record CaptureSummary(Guid Id, string FileName, DateTimeOffset Created);
public sealed record CaptureReview(CaptureSummary Source, int Version, string Action, CaptureDraft Draft, int Pages, float Confidence, IReadOnlyList<CaptureRevision> History);

public sealed class DocumentCaptureService(LocalAccountService accounts, IDbContextFactory<BusinessDb> factory, TimeProvider clock, IDocumentStorage storage)
{
    public const int MaxBytes = 20 * 1024 * 1024;
    public const int MaxText = 300_000;
    public async Task<Actor> Authorize(bool write = false)
    {
        var actor = await accounts.CurrentActor();
        actor.Demand("business.read");
        if (write) actor.Demand("catalogue.write");
        return actor;
    }
    public async Task<IReadOnlyList<CaptureSummary>> List()
    {
        var actor = await Authorize();
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Captures.AsNoTracking().Where(x => x.Branch == actor.Branch)
            .Select(x => new CaptureSummary(x.Id, x.FileName, x.Created)).ToListAsync();
        return rows.OrderByDescending(x => x.Created).ToArray();
    }
    public async Task<CaptureReview> Read(Guid id)
    {
        var actor = await Authorize();
        await using var db = await factory.CreateDbContextAsync();
        var source = await db.Captures.Where(x => x.Id == id && x.Branch == actor.Branch)
            .Select(x => new { x.Id, x.FileName, x.Created, x.Pages, x.Confidence }).SingleOrDefaultAsync()
            ?? throw new BusinessException("Captured document not found in this branch.");
        var history = await db.CaptureRevisions.AsNoTracking().Where(x => x.CaptureId == id).OrderByDescending(x => x.Version).ToListAsync();
        var latest = history.First();
        return new(new(source.Id, source.FileName, source.Created), latest.Version, latest.Action,
            JsonSerializer.Deserialize<CaptureDraft>(latest.Detail)!, source.Pages, source.Confidence, history);
    }
    public async Task<byte[]> Original(Guid id)
    {
        var actor = await Authorize();
        return await storage.Read(id, actor.Branch);
    }
    public async Task<Guid?> FindDuplicate(byte[] bytes)
    {
        var actor = await Authorize();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await using var db = await factory.CreateDbContextAsync();
        return await db.Captures.Where(x => x.Branch == actor.Branch && x.Sha256 == hash).Select(x => (Guid?)x.Id).SingleOrDefaultAsync();
    }
    public async Task<Guid> Import(string fileName, byte[] bytes, OcrResult result)
    {
        var actor = await Authorize(true);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!new[] { ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }.Contains(extension)) throw new BusinessException("Choose a PDF or supported image.");
        if (bytes.Length is 0 or > MaxBytes) throw new BusinessException("Each document must be between 1 byte and 20 MiB.");
        if (result.Pages is < 1 or > 100 || result.Text.Length > MaxText) throw new BusinessException("Split this document into smaller files (100 pages or 300,000 text characters maximum).");
        if (string.IsNullOrWhiteSpace(result.Text)) throw new BusinessException("No readable text was found. Try a clearer scan.");
        var existing = await FindDuplicate(bytes);
        if (existing.HasValue) return existing.Value;
        var source = new CapturedDocument { Id = Guid.NewGuid(), Branch = actor.Branch, FileName = Rules.Required(Path.GetFileName(fileName), "File name", 200), Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), Original = bytes, Payload = result.Text, Pages = result.Pages, Confidence = result.Confidence, Actor = actor.Id, Created = clock.GetUtcNow() };
        await storage.Create(source, new() { Id = Guid.NewGuid(), CaptureId = source.Id, Version = 1, Actor = actor.Id, Created = clock.GetUtcNow(), Action = "Offline extraction", Detail = JsonSerializer.Serialize(new CaptureDraft(OfflineCaptureParser.Parse(result.Text), result.Text)) });
        return source.Id;
    }
    public async Task Save(Guid id, int expectedVersion, CaptureDraft draft, bool reviewed, string action = "Draft saved")
    {
        var actor = await Authorize(true);
        Validate(draft);
        if (action != "Draft saved" && action != "Online text assistance requested" && action != "Online suggestions saved") throw new BusinessException("Invalid review action.");
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Captures.AnyAsync(x => x.Id == id && x.Branch == actor.Branch)) throw new BusinessException("Captured document not found in this branch.");
        var latest = await db.CaptureRevisions.Where(x => x.CaptureId == id).MaxAsync(x => x.Version);
        if (latest != expectedVersion) throw new BusinessException("This capture changed. Reopen it before saving another revision.");
        db.CaptureRevisions.Add(new() { Id = Guid.NewGuid(), CaptureId = id, Version = latest + 1, Actor = actor.Id, Created = clock.GetUtcNow(), Action = reviewed ? "Reviewed capture" : action, Detail = JsonSerializer.Serialize(draft) });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { throw new BusinessException("The revision could not be saved. Reopen the document and check its history before retrying."); }
    }
    public static void Validate(CaptureDraft draft)
    {
        if (draft.Text.Length > MaxText || JsonSerializer.Serialize(draft.Fields).Length > MaxText) throw new BusinessException("Captured text is too long; split the document.");
        if (!CaptureFields.Kinds.Contains(draft.Fields.Kind)) throw new BusinessException("Select a supported document type.");
    }
}
