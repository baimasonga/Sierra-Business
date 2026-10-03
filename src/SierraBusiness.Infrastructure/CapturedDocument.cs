namespace SierraBusiness.Infrastructure;

// Small desktop deployment: originals are limited to 20 MiB and included in SQLite backups.
public sealed class CapturedDocument
{
    public Guid Id { get; set; }
    public string Branch { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public byte[] Original { get; set; } = [];
    public string Payload { get; set; } = ""; // Unmodified OCR text
    public int Pages { get; set; }
    public float Confidence { get; set; }
    public string Actor { get; set; } = "";
    public DateTimeOffset Created { get; set; }
}
public sealed class CaptureRevision
{
    public Guid Id { get; set; }
    public Guid CaptureId { get; set; }
    public int Version { get; set; }
    public string Actor { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public string Action { get; set; } = "";
    public string Detail { get; set; } = ""; // Fields and editable extracted text, immutable revision
}
