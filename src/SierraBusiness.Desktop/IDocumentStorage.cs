using Microsoft.EntityFrameworkCore;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

/// <summary>Attachment storage boundary. This bounded desktop implementation keeps originals in its database backup.</summary>
public interface IDocumentStorage
{
    Task<byte[]> Read(Guid id, string branch);
    Task Create(CapturedDocument source, CaptureRevision initialRevision);
}
public sealed class SqliteDocumentStorage(IDbContextFactory<BusinessDb> factory) : IDocumentStorage
{
    public async Task<byte[]> Read(Guid id, string branch)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Captures.Where(x => x.Id == id && x.Branch == branch).Select(x => x.Original).SingleOrDefaultAsync()
            ?? throw new BusinessException("Captured document not found in this branch.");
    }
    public async Task Create(CapturedDocument source, CaptureRevision initialRevision)
    {
        if (source.Original.Length is 0 or > DocumentCaptureService.MaxBytes) throw new BusinessException("Choose a file up to 20 MiB.");
        await using var db = await factory.CreateDbContextAsync();
        db.Captures.Add(source); db.CaptureRevisions.Add(initialRevision);
        await db.SaveChangesAsync(); // Original, metadata and first review revision commit together.
    }
}
