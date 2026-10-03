using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

// ---------------- append-only entities (protected by database guards and the integrity seal chain) ----------------
public sealed class StoredDocument { public Guid Id { get; set; } public string Branch { get; set; } = ""; public string Number { get; set; } = ""; public DateTimeOffset Created { get; set; } public string CreatedBy { get; set; } = ""; }
/// <summary>Metadata version. The latest version is current; a voided version hides the document from normal lists but keeps it for audit.</summary>
public sealed class StoredDocumentRevision { public Guid Id { get; set; } public Guid DocumentId { get; set; } public int Version { get; set; } public string Action { get; set; } = ""; public string DetailsJson { get; set; } = "{}"; public bool Voided { get; set; } public string Reason { get; set; } = ""; public string Actor { get; set; } = ""; public DateTimeOffset Time { get; set; } }
/// <summary>One file version. Payload holds on-device OCR text. StorageKey points at the bytes (blob:SHA or capture:ID).</summary>
public sealed class StoredFile
{
    public Guid Id { get; set; } public Guid DocumentId { get; set; } public int Version { get; set; } public string FileName { get; set; } = ""; public string ContentType { get; set; } = "";
    public long Size { get; set; } public string Sha256 { get; set; } = ""; public int Pages { get; set; } public float Confidence { get; set; } public string Payload { get; set; } = "";
    public string StorageKey { get; set; } = ""; public string Reason { get; set; } = ""; public string Actor { get; set; } = ""; public DateTimeOffset Created { get; set; }
}
/// <summary>Content-addressed file bytes, stored once however many documents use them.</summary>
public sealed class FileBlob { public Guid Id { get; set; } public string Sha256 { get; set; } = ""; public long Size { get; set; } public byte[] Content { get; set; } = []; }
public sealed class StoredDocumentLink { public Guid Id { get; set; } public Guid DocumentId { get; set; } public DocumentLinkTarget Target { get; set; } public string Key { get; set; } = ""; public string Label { get; set; } = ""; public string Actor { get; set; } = ""; public DateTimeOffset Created { get; set; } }
public sealed class StoredDocumentLinkRemoval { public Guid Id { get; set; } public Guid LinkId { get; set; } public string Reason { get; set; } = ""; public string Actor { get; set; } = ""; public DateTimeOffset Created { get; set; } }

/// <summary>Where file bytes live. The database store keeps originals inside the company database (and therefore its backups); a file-share or cloud store can replace it later.</summary>
public interface IFileStore
{
    Task<string> Put(BusinessDb db, string sha256, byte[] content);
    Task<byte[]> Get(BusinessDb db, string key);
}
public sealed class DatabaseFileStore : IFileStore
{
    public async Task<string> Put(BusinessDb db, string sha256, byte[] content)
    {
        if (!await db.FileBlobs.AnyAsync(x => x.Sha256 == sha256) && !db.FileBlobs.Local.Any(x => x.Sha256 == sha256))
            db.FileBlobs.Add(new() { Id = Guid.NewGuid(), Sha256 = sha256, Size = content.LongLength, Content = content });
        return "blob:" + sha256;
    }
    public async Task<byte[]> Get(BusinessDb db, string key)
    {
        if (key.StartsWith("blob:", StringComparison.Ordinal)) { var sha = key[5..]; return await db.FileBlobs.Where(x => x.Sha256 == sha).Select(x => x.Content).SingleOrDefaultAsync() ?? throw new BusinessException("The stored file is missing. Restore from a backup."); }
        if (key.StartsWith("capture:", StringComparison.Ordinal) && Guid.TryParse(key[8..], out var id)) return await db.Captures.Where(x => x.Id == id).Select(x => x.Original).SingleOrDefaultAsync() ?? throw new BusinessException("The stored file is missing. Restore from a backup.");
        throw new BusinessException("Unknown file location.");
    }
}

public sealed class DocumentLibrary(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options, IFileStore files) : IDocumentLibrary
{
    public const int MaxBytes = 20 * 1024 * 1024;
    public const int MaxText = 300_000;
    private static readonly string[] Writers = ["sales.create", "inventory.receive", "accounting.write", "catalogue.write", "users.manage"];
    private sealed record Fingerprint(object Request, string? Sha256);

    private async Task<Actor> Authorize(bool write = false)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        if (write && !Writers.Any(actor.Permissions.Contains)) throw new UnauthorizedAccessException("You cannot add or change documents.");
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }
    private static bool SeesConfidential(Actor actor) => actor.Permissions.Contains("accounting.read") || actor.Permissions.Contains("users.manage");
    private static DocumentDetails Details(StoredDocumentRevision r) => JsonSerializer.Deserialize<DocumentDetails>(r.DetailsJson)!;
    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    // ---------------- validation ----------------

    private static DocumentDetails Clean(DocumentDetails d)
    {
        if (d is null) throw new BusinessException("Enter the document details.");
        if (!Enum.IsDefined(d.Type)) throw new BusinessException("Choose a document type.");
        string Text(string? value, string field, int max) { value = (value ?? "").Trim(); if (value.Length > max) throw new BusinessException($"{field} is limited to {max} characters."); return value; }
        if (d.Amount is < 0 or > 100_000_000_000m || d.Amount is decimal a && decimal.Round(a, 2) != a) throw new BusinessException("Enter the amount with at most two decimal places.");
        var currency = Text(d.Currency, "Currency", 3).ToUpperInvariant();
        if (currency.Length is not (0 or 3)) throw new BusinessException("Use a three-letter currency code such as SLE or USD.");
        if (d.DocumentDate is DateOnly date && (date.Year < 1950 || date > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))) throw new BusinessException("The document date must be a real date, not in the future.");
        if (d.DueDate?.Year is < 1950 or > 2200) throw new BusinessException("Check the deadline or expiry date.");
        return new(d.Type, Rules.Required(d.Title, "Title", 160), Text(d.Reference, "Reference", 120), Text(d.Party, "Party", 160), d.DocumentDate, d.Amount, currency, d.DueDate,
            Text(d.Tags, "Tags", 200), Text(d.Notes, "Notes", 200), d.Confidential);
    }
    private static (string FileName, string ContentType) CheckFile(DocumentFileInput file)
    {
        if (file?.Content is null || file.Content.Length == 0) throw new BusinessException("Choose a file.");
        if (file.Content.Length > MaxBytes) throw new BusinessException("Each file can be up to 20 MB. Reduce the scan resolution or split the document.");
        var name = Rules.Required(Path.GetFileName(file.FileName ?? ""), "File name", 200);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (!DocumentHints.Extensions.Contains(extension)) throw new BusinessException("Use PDF, an image (JPG, PNG, TIFF, BMP, GIF, WebP), Word, Excel, PowerPoint, text or CSV.");
        var b = file.Content;
        bool Starts(params byte[] magic) => b.Length >= magic.Length && b.AsSpan(0, magic.Length).SequenceEqual(magic);
        var ok = extension switch
        {
            ".pdf" => Starts(0x25, 0x50, 0x44, 0x46), ".png" => Starts(0x89, 0x50, 0x4E, 0x47), ".jpg" or ".jpeg" => Starts(0xFF, 0xD8, 0xFF), ".gif" => Starts(0x47, 0x49, 0x46, 0x38), ".bmp" => Starts(0x42, 0x4D),
            ".tif" or ".tiff" => Starts(0x49, 0x49, 0x2A, 0x00) || Starts(0x4D, 0x4D, 0x00, 0x2A), ".webp" => b.Length > 12 && Encoding.ASCII.GetString(b, 8, 4) == "WEBP",
            ".docx" or ".xlsx" or ".pptx" => Starts(0x50, 0x4B, 0x03, 0x04), ".doc" or ".xls" => Starts(0xD0, 0xCF, 0x11, 0xE0),
            _ => !b.AsSpan(0, Math.Min(b.Length, 8192)).Contains((byte)0)
        };
        if (!ok) throw new BusinessException($"The file content does not match its {extension} extension. Save it again from the original program.");
        if ((file.Text ?? "").Length > MaxText || file.Pages is < 0 or > 1000) throw new BusinessException("The extracted text is too long; split the document.");
        var type = extension switch
        {
            ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".bmp" => "image/bmp", ".tif" or ".tiff" => "image/tiff", ".webp" => "image/webp",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation", ".doc" => "application/msword", ".xls" => "application/vnd.ms-excel", ".csv" => "text/csv", _ => "text/plain"
        };
        return (name, type);
    }
    private async Task<StoredFile> NewFile(BusinessDb db, Actor actor, Guid documentId, int version, DocumentFileInput file, string reason)
    {
        var (name, type) = CheckFile(file);
        var sha = Hash(file.Content);
        var dup = await (from f in db.StoredFiles join d in db.StoredDocuments on f.DocumentId equals d.Id where d.Branch == actor.Branch && f.Sha256 == sha select new { d.Id, d.Number }).ToListAsync();
        foreach (var x in dup)
            if (!await Voided(db, x.Id)) throw new BusinessException($"This exact file is already stored as {x.Number}. Open that document instead.");
        var key = await files.Put(db, sha, file.Content);
        var row = new StoredFile { Id = Guid.NewGuid(), DocumentId = documentId, Version = version, FileName = name, ContentType = type, Size = file.Content.LongLength, Sha256 = sha, Pages = file.Pages, Confidence = file.Confidence,
            Payload = (file.Text ?? "").Trim(), StorageKey = key, Reason = reason, Actor = actor.Id, Created = DateTimeOffset.UtcNow };
        db.StoredFiles.Add(row); return row;
    }
    private static async Task<bool> Voided(BusinessDb db, Guid documentId) => await db.StoredDocumentRevisions.Where(x => x.DocumentId == documentId).OrderByDescending(x => x.Version).Select(x => x.Voided).FirstAsync();

    private async Task<(string Key, string Label)> Target(BusinessDb db, Actor actor, DocumentLinkTarget target, string key)
    {
        key = (key ?? "").Trim();
        switch (target)
        {
            case DocumentLinkTarget.Transaction when Guid.TryParse(key, out var id):
                var doc = await db.Documents.SingleOrDefaultAsync(x => x.Id == id && x.Branch == actor.Branch) ?? throw new BusinessException("Transaction not found.");
                return (id.ToString(), $"{doc.Kind} {Short(doc.Number)} · {doc.Party} · {doc.BusinessDate:dd MMM yyyy}");
            case DocumentLinkTarget.Party when Guid.TryParse(key, out var id):
                var party = await db.Parties.SingleOrDefaultAsync(x => x.Id == id && x.Branch == actor.Branch) ?? throw new BusinessException("Customer or supplier not found.");
                return (id.ToString(), $"{party.Name} ({party.Code})");
            case DocumentLinkTarget.Cheque when Guid.TryParse(key, out var id):
                var cheque = await db.ReceivedCheques.SingleOrDefaultAsync(x => x.Id == id && x.Branch == actor.Branch) ?? throw new BusinessException("Cheque not found.");
                return (id.ToString(), $"Cheque {cheque.Reference} · {cheque.Bank} · {cheque.Party} · NLe {cheque.Amount:N2}");
            case DocumentLinkTarget.SerialUnit:
                var parts = key.Split('|'); if (parts.Length != 2 || !Guid.TryParse(parts[0], out var product)) throw new BusinessException("Choose a serial unit.");
                var serial = parts[1].Trim().ToUpperInvariant();
                if (!await db.Movements.AnyAsync(x => x.Branch == actor.Branch && x.ProductId == product && x.Serial == serial)) throw new BusinessException("Serial unit not found in this branch.");
                var sku = await db.Products.Where(x => x.Id == product).Select(x => x.Sku).SingleAsync();
                return ($"{product}|{serial}", $"{sku} · serial {serial}");
            case DocumentLinkTarget.Installation when Guid.TryParse(key, out var id):
                var job = await db.Installations.SingleOrDefaultAsync(x => x.Id == id && x.Branch == actor.Branch) ?? throw new BusinessException("Installation not found.");
                return (id.ToString(), $"Installation {Short(job.Number)} · {job.Customer} · {job.Site}");
            default: throw new BusinessException("Choose what to link the document to.");
        }
    }
    /// <summary>Sign-in names for display; the stored history keeps the user ID.</summary>
    private static async Task<Dictionary<string, string>> Names(BusinessDb db) => await db.Users.AsNoTracking().Where(x => x.UserName != null).ToDictionaryAsync(x => x.Id, x => x.UserName!);
    private static string Name(Dictionary<string, string> names, string id) => names.GetValueOrDefault(id, id);
    private static string Short(string number) => number.Length > 8 ? number[^8..] : number;

    // ---------------- queries ----------------

    private sealed record Row(StoredDocument Doc, StoredDocumentRevision Current, DocumentDetails Details, List<StoredFile> Files, int Links);
    private async Task<List<Row>> Rows(BusinessDb db, Actor actor, IReadOnlyCollection<Guid>? ids = null)
    {
        var query = db.StoredDocuments.AsNoTracking().Where(x => x.Branch == actor.Branch);
        if (ids is not null) query = query.Where(x => ids.Contains(x.Id));
        var docs = await query.ToListAsync(); var docIds = docs.Select(x => x.Id).ToList();
        var revisions = (await db.StoredDocumentRevisions.AsNoTracking().Where(x => docIds.Contains(x.DocumentId)).ToListAsync()).GroupBy(x => x.DocumentId).ToDictionary(g => g.Key, g => g.MaxBy(x => x.Version)!);
        var fileRows = (await db.StoredFiles.AsNoTracking().Where(x => docIds.Contains(x.DocumentId)).ToListAsync()).GroupBy(x => x.DocumentId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Version).ToList());
        var links = (await db.StoredDocumentLinks.AsNoTracking().Where(x => docIds.Contains(x.DocumentId) && !db.StoredDocumentLinkRemovals.Any(r => r.LinkId == x.Id)).Select(x => x.DocumentId).ToListAsync()).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var confidential = SeesConfidential(actor);
        return docs.Select(d => { var r = revisions[d.Id]; return new Row(d, r, Details(r), fileRows.GetValueOrDefault(d.Id) ?? [], links.GetValueOrDefault(d.Id)); })
            .Where(x => confidential || !x.Details.Confidential).ToList();
    }
    private static async Task<LibraryItemView[]> Named(BusinessDb db, LibraryItemView[] items) { var names = await Names(db); return items.Select(x => x with { CreatedBy = Name(names, x.CreatedBy) }).ToArray(); }
    private static LibraryItemView Item(Row r)
    {
        var latest = r.Files.LastOrDefault();
        return new(r.Doc.Id, r.Doc.Number, r.Current.Version, r.Details, r.Current.Voided, r.Doc.Created, r.Doc.CreatedBy, r.Files.Count, r.Links, latest?.Id ?? Guid.Empty, latest?.FileName ?? "", latest?.ContentType ?? "");
    }

    public async Task<IReadOnlyList<LibraryItemView>> Search(DocumentQuery query)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        List<Guid>? ids = null;
        if (query.Target is not null)
        {
            var key = (query.Key ?? "").Trim(); if (query.Target == DocumentLinkTarget.SerialUnit && key.Split('|') is [var p, var s]) key = $"{p}|{s.Trim().ToUpperInvariant()}";
            ids = await db.StoredDocumentLinks.Where(x => x.Target == query.Target && x.Key == key && !db.StoredDocumentLinkRemovals.Any(r => r.LinkId == x.Id)).Select(x => x.DocumentId).Distinct().ToListAsync();
        }
        var rows = await Rows(db, actor, ids);
        var text = (query.Text ?? "").Trim();
        return rows.Where(r => (query.IncludeVoided || !r.Current.Voided) && (query.Type is null || r.Details.Type == query.Type)
                && (text.Length == 0 || new[] { r.Doc.Number, r.Details.Title, r.Details.Reference, r.Details.Party, r.Details.Tags, r.Details.Notes }.Concat(r.Files.Select(f => f.FileName)).Concat(r.Files.Select(f => f.Payload)).Any(x => x.Contains(text, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(r => r.Details.DocumentDate ?? DateOnly.FromDateTime(r.Doc.Created.UtcDateTime)).ThenByDescending(r => r.Doc.Created).Take(500).Select(Item).ToArray() is var items && items.Length > 0
            ? await Named(db, items) : [];
    }

    public async Task<LibraryDocumentView> Get(Guid id)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var row = (await Rows(db, actor, [id])).SingleOrDefault() ?? throw new BusinessException("Document not found.");
        var history = await db.StoredDocumentRevisions.AsNoTracking().Where(x => x.DocumentId == id).OrderByDescending(x => x.Version).ToListAsync();
        var links = await db.StoredDocumentLinks.AsNoTracking().Where(x => x.DocumentId == id && !db.StoredDocumentLinkRemovals.Any(r => r.LinkId == x.Id)).ToListAsync();
        links = links.OrderBy(x => x.Created).ToList();
        var names = await Names(db);
        return new(Item(row) with { CreatedBy = Name(names, row.Doc.CreatedBy) }, row.Files.OrderByDescending(x => x.Version).Select(f => new DocumentFileView(f.Id, f.Version, f.FileName, f.ContentType, f.Size, f.Sha256, f.Pages, f.Created, Name(names, f.Actor), f.Reason)).ToArray(),
            links.Select(l => new DocumentLinkView(l.Id, l.Target, l.Key, l.Label)).ToArray(),
            history.Select(h => new DocumentRevisionView(h.Version, h.Action, Name(names, h.Actor), h.Time, Details(h), h.Reason)).ToArray(),
            row.Files.LastOrDefault()?.Payload ?? "", row.Current.Voided ? row.Current.Reason : "");
    }

    public async Task<DocumentContent> Download(Guid fileId)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var file = await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fileId) ?? throw new BusinessException("File not found.");
        if ((await Rows(db, actor, [file.DocumentId])).Count == 0) throw new BusinessException("File not found.");
        var content = await files.Get(db, file.StorageKey);
        if (Hash(content) != file.Sha256) throw new BusinessException("This file does not match the fingerprint recorded when it was stored. It may have been altered; restore it from a backup and report it.");
        return new(file.FileName, file.ContentType, content);
    }

    public async Task<IReadOnlyList<DocumentDeadline>> Deadlines(int days = 60)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return (await Rows(db, actor)).Where(r => !r.Current.Voided && r.Details.DueDate is DateOnly due && due >= today.AddDays(-30) && due <= today.AddDays(Math.Clamp(days, 1, 366)))
            .Select(r => new DocumentDeadline(r.Doc.Id, r.Doc.Number, r.Details.Title, r.Details.Type, r.Details.Party, r.Details.DueDate!.Value, r.Details.DueDate.Value.DayNumber - today.DayNumber))
            .OrderBy(x => x.DueDate).ToArray();
    }

    public async Task<LibraryItemView?> FindDuplicate(byte[] content)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var sha = Hash(content);
        var ids = await (from f in db.StoredFiles join d in db.StoredDocuments on f.DocumentId equals d.Id where d.Branch == actor.Branch && f.Sha256 == sha select d.Id).Distinct().ToListAsync();
        return (await Rows(db, actor, ids)).Where(x => !x.Current.Voided).Select(Item).FirstOrDefault();
    }

    public async Task<IReadOnlyList<LinkTargetOption>> Targets(DocumentLinkTarget target, string text)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var t = (text ?? "").Trim(); var u = t.ToUpperInvariant();
        IEnumerable<LinkTargetOption> options = target switch
        {
            DocumentLinkTarget.Transaction => (await db.Documents.AsNoTracking().Where(x => x.Branch == actor.Branch && x.Kind != DocumentKind.Reversal).OrderByDescending(x => x.BusinessDate).Take(2000).ToListAsync())
                .Where(x => t.Length == 0 || (x.Number + " " + x.Party + " " + x.Kind).Contains(t, StringComparison.OrdinalIgnoreCase)).Select(x => new LinkTargetOption(x.Id.ToString(), $"{x.Kind} {Short(x.Number)} · {x.Party} · {x.BusinessDate:dd MMM yyyy} · {x.Total:N2}")),
            DocumentLinkTarget.Party => (await db.Parties.AsNoTracking().Where(x => x.Branch == actor.Branch).OrderBy(x => x.Name).ToListAsync())
                .Where(x => t.Length == 0 || (x.Name + " " + x.Code).Contains(t, StringComparison.OrdinalIgnoreCase)).Select(x => new LinkTargetOption(x.Id.ToString(), $"{x.Name} ({x.Code})")),
            DocumentLinkTarget.Cheque => (await db.ReceivedCheques.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync()).OrderByDescending(x => x.ChequeDate)
                .Where(x => t.Length == 0 || (x.Reference + " " + x.Bank + " " + x.Party).Contains(t, StringComparison.OrdinalIgnoreCase)).Select(x => new LinkTargetOption(x.Id.ToString(), $"Cheque {x.Reference} · {x.Bank} · {x.Party} · NLe {x.Amount:N2}")),
            DocumentLinkTarget.SerialUnit => (await (from m in db.Movements join p in db.Products on m.ProductId equals p.Id where m.Branch == actor.Branch && m.Serial != null select new { m.ProductId, p.Sku, m.Serial }).Distinct().ToListAsync())
                .Where(x => t.Length == 0 || (x.Sku + " " + x.Serial).Contains(u, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Serial).Select(x => new LinkTargetOption($"{x.ProductId}|{x.Serial}", $"{x.Sku} · serial {x.Serial}")),
            DocumentLinkTarget.Installation => (await db.Installations.AsNoTracking().Where(x => x.Branch == actor.Branch).OrderByDescending(x => x.Scheduled).ToListAsync())
                .Where(x => t.Length == 0 || (x.Number + " " + x.Customer + " " + x.Site).Contains(t, StringComparison.OrdinalIgnoreCase)).Select(x => new LinkTargetOption(x.Id.ToString(), $"Installation {Short(x.Number)} · {x.Customer} · {x.Site}")),
            _ => []
        };
        return options.Take(30).ToArray();
    }

    // ---------------- commands ----------------

    public async Task<Guid> Add(AddDocumentCommand input)
    {
        var actor = await Authorize(true);
        var sha = input.File?.Content is { Length: > 0 } c ? Hash(c) : null;
        return await Commands.Run(factory, actor, input.OperationId, "library.add", new Fingerprint(new { input.Details, File = input.File?.FileName, input.Links }, sha), async db =>
        {
            var details = Clean(input.Details);
            if (details.Confidential && !SeesConfidential(actor)) throw new BusinessException("Only accounting staff and administrators can mark a document confidential.");
            var doc = new StoredDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"DOC-{options.Value.Device}-{input.OperationId.ToString("N")[..10].ToUpperInvariant()}", Created = DateTimeOffset.UtcNow, CreatedBy = actor.Id };
            db.StoredDocuments.Add(doc);
            db.StoredDocumentRevisions.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Version = 1, Action = "Created", DetailsJson = JsonSerializer.Serialize(details), Actor = actor.Id, Time = doc.Created });
            await NewFile(db, actor, doc.Id, 1, input.File!, "Original");
            foreach (var link in (input.Links ?? []).DistinctBy(x => (x.Target, x.Key)))
            {
                var (key, label) = await Target(db, actor, link.Target, link.Key);
                db.StoredDocumentLinks.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Target = link.Target, Key = key, Label = label, Actor = actor.Id, Created = doc.Created });
            }
            return doc.Id;
        });
    }

    private async Task<(StoredDocument Doc, StoredDocumentRevision Current)> Open(BusinessDb db, Actor actor, Guid id, bool allowVoided = false)
    {
        var doc = await db.StoredDocuments.SingleOrDefaultAsync(x => x.Id == id && x.Branch == actor.Branch) ?? throw new BusinessException("Document not found.");
        var current = await db.StoredDocumentRevisions.Where(x => x.DocumentId == id).OrderByDescending(x => x.Version).FirstAsync();
        if (Details(current).Confidential && !SeesConfidential(actor)) throw new BusinessException("Document not found.");
        if (current.Voided && !allowVoided) throw new BusinessException("This document is voided.");
        return (doc, current);
    }

    public async Task<Guid> AddVersion(AddDocumentVersionCommand input)
    {
        var actor = await Authorize(true);
        var sha = input.File?.Content is { Length: > 0 } c ? Hash(c) : null;
        return await Commands.Run(factory, actor, input.OperationId, "library.version", new Fingerprint(new { input.DocumentId, File = input.File?.FileName, input.Reason }, sha), async db =>
        {
            var (doc, _) = await Open(db, actor, input.DocumentId);
            var version = await db.StoredFiles.Where(x => x.DocumentId == doc.Id).MaxAsync(x => x.Version) + 1;
            if (version > 50) throw new BusinessException("A document can hold up to 50 file versions.");
            return (await NewFile(db, actor, doc.Id, version, input.File!, Rules.Required(input.Reason, "Reason for the new version", 160))).Id;
        });
    }

    public async Task<Guid> Update(UpdateDocumentCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "library.update", input, async db =>
        {
            var (doc, current) = await Open(db, actor, input.DocumentId);
            if (current.Version != input.ExpectedVersion) throw new BusinessException("This document was changed by someone else. Reopen it before saving.");
            var details = Clean(input.Details);
            if (details.Confidential != Details(current).Confidential && !SeesConfidential(actor)) throw new BusinessException("Only accounting staff and administrators can change confidentiality.");
            if ((input.Reason ?? "").Length > 160) throw new BusinessException("The reason is limited to 160 characters.");
            var row = new StoredDocumentRevision { Id = Guid.NewGuid(), DocumentId = doc.Id, Version = current.Version + 1, Action = "Details updated", DetailsJson = JsonSerializer.Serialize(details), Reason = (input.Reason ?? "").Trim(), Actor = actor.Id, Time = DateTimeOffset.UtcNow };
            db.StoredDocumentRevisions.Add(row); return row.Id;
        });
    }

    public async Task<Guid> Void(VoidDocumentCommand input)
    {
        var actor = await Authorize(true); actor.Demand("documents.reverse");
        return await Commands.Run(factory, actor, input.OperationId, "library.void", input, async db =>
        {
            var (doc, current) = await Open(db, actor, input.DocumentId);
            if (current.Version != input.ExpectedVersion) throw new BusinessException("This document was changed by someone else. Reopen it before voiding.");
            var row = new StoredDocumentRevision { Id = Guid.NewGuid(), DocumentId = doc.Id, Version = current.Version + 1, Action = "Voided", DetailsJson = current.DetailsJson, Voided = true, Reason = Rules.Required(input.Reason, "Reason", 160), Actor = actor.Id, Time = DateTimeOffset.UtcNow };
            db.StoredDocumentRevisions.Add(row); return row.Id;
        });
    }

    public async Task<Guid> Link(LinkDocumentCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "library.link", input, async db =>
        {
            var (doc, _) = await Open(db, actor, input.DocumentId);
            var (key, label) = await Target(db, actor, input.Target, input.Key);
            if (await db.StoredDocumentLinks.AnyAsync(x => x.DocumentId == doc.Id && x.Target == input.Target && x.Key == key && !db.StoredDocumentLinkRemovals.Any(r => r.LinkId == x.Id))) throw new BusinessException("The document is already linked to this record.");
            var link = new StoredDocumentLink { Id = Guid.NewGuid(), DocumentId = doc.Id, Target = input.Target, Key = key, Label = label, Actor = actor.Id, Created = DateTimeOffset.UtcNow };
            db.StoredDocumentLinks.Add(link); return link.Id;
        });
    }

    public async Task<Guid> Unlink(UnlinkDocumentCommand input)
    {
        var actor = await Authorize(true);
        return await Commands.Run(factory, actor, input.OperationId, "library.unlink", input, async db =>
        {
            var link = await db.StoredDocumentLinks.SingleOrDefaultAsync(x => x.Id == input.LinkId) ?? throw new BusinessException("Link not found.");
            await Open(db, actor, link.DocumentId);
            if (await db.StoredDocumentLinkRemovals.AnyAsync(x => x.LinkId == link.Id)) throw new BusinessException("This link was already removed.");
            var row = new StoredDocumentLinkRemoval { Id = Guid.NewGuid(), LinkId = link.Id, Reason = Rules.Required(input.Reason, "Reason", 160), Actor = actor.Id, Created = DateTimeOffset.UtcNow };
            db.StoredDocumentLinkRemovals.Add(row); return row.Id;
        });
    }
}
