using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

public sealed class DesktopDocumentDraftRecovery(string directory, IActorSession session, IDbContextFactory<BusinessDb> factory) : IDocumentDraftRecovery
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private async Task<string> FileName(string scope)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        if (scope == "orders") { if (!actor.Permissions.Contains("sales.create") && !actor.Permissions.Contains("inventory.receive")) throw new UnauthorizedAccessException(); }
        else if (scope is "tenders" or "fxsales") actor.Demand("sales.create");
        else throw new BusinessException("Unsupported document recovery scope.");
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + "\n" + actor.Id + "\n" + scope)));
        return Path.Combine(directory, "drafts", name + ".document-draft");
    }
    public async Task<DocumentDraft?> Read(string scope)
    {
        await gate.WaitAsync();
        try
        {
            var file = await FileName(scope); if (!File.Exists(file)) return null;
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            try
            {
                if (new FileInfo(file).Length > 1_000_000) throw new JsonException();
                var bytes = ProtectedData.Unprotect(await File.ReadAllBytesAsync(file), null, DataProtectionScope.CurrentUser);
                var draft = JsonSerializer.Deserialize<DocumentDraft>(bytes) ?? throw new JsonException();
                if (draft.Scope != scope || draft.OperationId == Guid.Empty || draft.Json.Length > 500_000) throw new JsonException();
                using var parsed = JsonDocument.Parse(draft.Json);
                await using var db = await factory.CreateDbContextAsync();
                if (await db.Operations.AnyAsync(o => o.Id == draft.OperationId)) { File.Delete(file); return null; }
                return draft;
            }
            catch (Exception e) when (e is CryptographicException or JsonException) { throw new BusinessException("This recovery copy cannot be opened. Discard it to start a new draft; saved documents are unaffected."); }
        }
        finally { gate.Release(); }
    }
    public async Task Save(DocumentDraft draft)
    {
        await gate.WaitAsync();
        try
        {
            var file = await FileName(draft.Scope);
            if (draft.OperationId == Guid.Empty || string.IsNullOrEmpty(draft.Json) || draft.Json.Length > 500_000) throw new BusinessException("Invalid recovery draft.");
            using var parsed = JsonDocument.Parse(draft.Json);
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(draft);
            if (bytes.Length > 750_000) throw new BusinessException("The recovery draft is too large.");
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temporary = file + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { await stream.WriteAsync(encrypted); stream.Flush(flushToDisk: true); }
            File.Move(temporary, file, true);
        }
        finally { gate.Release(); }
    }
    public async Task Clear(string scope)
    {
        await gate.WaitAsync();
        try { var file = await FileName(scope); if (File.Exists(file)) File.Delete(file); }
        finally { gate.Release(); }
    }
}
