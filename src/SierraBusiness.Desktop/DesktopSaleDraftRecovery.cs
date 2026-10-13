using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

public sealed class DesktopSaleDraftRecovery(string directory, IActorSession session, IDbContextFactory<BusinessDb> factory) : ISaleDraftRecovery
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private async Task<string> FileName()
    {
        var actor = await session.CurrentActor(); actor.Demand("sales.create");
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + "\n" + actor.Id)));
        return Path.Combine(directory, "drafts", name + ".draft");
    }
    public async Task<RecoverableSaleDraft?> Read()
    {
        await gate.WaitAsync();
        try { return await ReadCore(); } finally { gate.Release(); }
    }
    private async Task<RecoverableSaleDraft?> ReadCore()
    {
        var file = await FileName(); if (!File.Exists(file)) return null;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        try
        {
            if (new FileInfo(file).Length > 1_000_000) throw new JsonException();
            var text = ProtectedData.Unprotect(await File.ReadAllBytesAsync(file), null, DataProtectionScope.CurrentUser);
            var draft = JsonSerializer.Deserialize<RecoverableSaleDraft>(text) ?? throw new JsonException();
            if (draft.Document is null || draft.Entry is null || draft.Document.Lines is null || draft.Document.Kind != DocumentKind.Sale || draft.Document.Lines.Count > 200) throw new JsonException();
            await using var db = await factory.CreateDbContextAsync();
            if (await db.Operations.AnyAsync(x => x.Id == draft.Document.OperationId || x.Id == draft.Entry.HoldOperationId)) { File.Delete(file); return null; }
            return draft;
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        { throw new BusinessException("The saved sale draft cannot be recovered. Posted records are unaffected. Discard the recovery copy to start a new sale."); }
    }
    public async Task Save(RecoverableSaleDraft draft)
    {
        await gate.WaitAsync();
        try { await SaveCore(draft); } finally { gate.Release(); }
    }
    private async Task SaveCore(RecoverableSaleDraft draft)
    {
        var file = await FileName();
        if (draft.Document.Kind != DocumentKind.Sale || draft.Document.Lines.Count > 200) throw new BusinessException("Invalid sale recovery draft.");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var json = JsonSerializer.SerializeToUtf8Bytes(draft);
        if (json.Length > 500_000) throw new BusinessException("The sale draft is too large to recover automatically.");
        var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { await stream.WriteAsync(encrypted); stream.Flush(flushToDisk: true); }
        File.Move(temp, file, true);
    }
    public async Task Clear()
    {
        await gate.WaitAsync();
        try { var file = await FileName(); if (File.Exists(file)) File.Delete(file); } finally { gate.Release(); }
    }
}
