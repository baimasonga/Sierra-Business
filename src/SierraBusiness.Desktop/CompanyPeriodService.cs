using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

public sealed record CompanyPeriodMarker(bool Archive, DateOnly Cutoff, Guid OperationId, string SourceHead);
public sealed class CompanyPeriodService(DesktopStore store, CompanyDirectory companies, IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : ICompanyPeriodService
{
    public const string MarkerFile = "company-period.json";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private sealed record Receipt(SplitCompanyPeriodCommand Command, CompanyPeriodResult Result,string CreatedBy);
    public static bool IsArchive(string directory) => File.Exists(Path.Combine(directory, MarkerFile)) && (JsonSerializer.Deserialize<CompanyPeriodMarker>(File.ReadAllText(Path.Combine(directory, MarkerFile))) ?? throw new BusinessException("Company period metadata is unreadable.")).Archive;
    private async Task Authorize()
    {
        var actor = await session.CurrentActor(); foreach (var p in new[] { "business.read", "accounting.read", "accounting.write", "users.manage", "backups.create" }) actor.Demand(p);
        if (actor.Branch != options.Value.Branch || IsArchive(store.DirectoryPath)) throw new UnauthorizedAccessException("Split only the active company in its configured branch.");
        if (new DesktopSyncConfiguration(store.DirectoryPath).Load().Role != SyncRole.Off) throw new BusinessException("Company period splitting requires a standalone company. A synced branch needs a coordinated head-office migration.");
    }
    private static async Task<CompanyPeriodPreview> Inspect(BusinessDb db, string branch, DateOnly cutoff,DateOnly configuredLock)
    {
        if (cutoff.Year < 2000 || cutoff >= DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("Choose a cutoff before today, in 2000 or later.");
        if (cutoff < await PeriodControls.ClosedThrough(db,branch,configuredLock)) throw new BusinessException("Use a cutoff on or after the company's existing period lock.");
        if (await db.Documents.AnyAsync(d => d.Branch != branch || d.BusinessDate > cutoff)) throw new BusinessException("The cutoff must include every posted document, including future automatic reversals. Use the latest posted date or later.");
        if (await db.TradeOrders.AnyAsync(o => o.Branch == branch && o.Date > cutoff)) throw new BusinessException("Include all recorded order dates in the cutoff.");
        var datedOperations = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && (o.Action == "consignment.receive" || o.Action == "consignment.return" || o.Action == "payroll.attendance")).ToListAsync();
        if (datedOperations.Any(o => { using var json = JsonDocument.Parse(o.Payload); return json.RootElement.GetProperty("Date").Deserialize<DateOnly>() > cutoff; })) throw new BusinessException("Include custody and attendance activity dates in the cutoff.");
        var integrity = await HistoryProtection.Verify(db); if (!integrity.Ok) throw new BusinessException("Resolve the record integrity problems before splitting this company.");
        var documents = await db.Documents.CountAsync(); if (documents == 0) throw new BusinessException("There are no posted documents to split.");
        var lines = await db.Journal.AsNoTracking().Select(j => new { j.Debit, j.Credit }).ToListAsync(); var debit = lines.Sum(j => j.Debit); var credit = lines.Sum(j => j.Credit);
        if (debit != credit) throw new BusinessException("The company trial balance does not balance.");
        return new(cutoff, integrity.Head, documents, await db.Movements.CountAsync(), lines.Count, debit, credit);
    }
    public async Task<CompanyPeriodPreview> Preview(DateOnly cutoff) { await Authorize(); await using var db = await factory.CreateDbContextAsync(); return await Inspect(db, options.Value.Branch, cutoff,options.Value.ClosedThrough); }
    public async Task<CompanyPeriodResult> Split(SplitCompanyPeriodCommand input)
    {
        var actor = await session.CurrentActor(); foreach (var permission in new[] {"business.read","accounting.read","backups.create"}) actor.Demand(permission);
        if (actor.Branch != options.Value.Branch) throw new UnauthorizedAccessException("Use the configured branch.");
        if (input.OperationId == Guid.Empty) throw new BusinessException("Use a new split operation identifier.");
        var archiveName = Rules.Required(input.ArchiveName, "Archive company name", 120); var continuationName = Rules.Required(input.ContinuationName, "Continuation company name", 120);
        if (archiveName.Equals(continuationName, StringComparison.OrdinalIgnoreCase)) throw new BusinessException("Use different archive and continuation names.");
        await Gate.WaitAsync();
        try
        {
            var receipts = Path.Combine(store.DirectoryPath, "Splits"); Directory.CreateDirectory(receipts); var receiptPath = Path.Combine(receipts, input.OperationId.ToString("N") + ".json");
            if (File.Exists(receiptPath)) { var saved = JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(receiptPath))!; if (saved.CreatedBy != actor.Id || saved.Command != input) throw new BusinessException("That split identifier was already used for different details or identity."); await Complete(saved.Result,saved.Command.OperationId); return saved.Result; }
            await Authorize();
            var backup = await store.CreateBackupWithCopy("before-split");
            var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(backup.Path + ".manifest.json")) ?? throw new BusinessException("Split backup manifest is unreadable.");
            var snapshotOptions = new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=" + backup.Path + ";Pooling=False").Options;
            CompanyPeriodPreview preview; await using (var snapshot = new BusinessDb(snapshotOptions)) preview = await Inspect(snapshot, options.Value.Branch, input.Cutoff,options.Value.ClosedThrough);
            if (preview.HistoryHead != input.ExpectedHistoryHead) throw new BusinessException("The company changed after the preview. Review the split again.");
            var archive = companies.Create(archiveName, true); var continuation = companies.Create(continuationName, true);
            foreach (var target in new[] { archive, continuation })
            {
                var path = Path.Combine(target.DataDirectory, "sierra.db"); File.Copy(backup.Path, path);
                await using (var file = File.OpenRead(path)) if (Convert.ToHexString(await SHA256.HashDataAsync(file)) != manifest.Sha256) throw new BusinessException("A split company copy failed its checksum check. The original company is unchanged.");
                var settings = new TradingOptions { Branch = options.Value.Branch, Device = "PERIOD" + target.Id.ToString("N")[..12], TaxRate = options.Value.TaxRate, ClosedThrough = input.Cutoff };
                await File.WriteAllTextAsync(Path.Combine(target.DataDirectory, "trading-settings.json"), JsonSerializer.Serialize(settings));
                await File.WriteAllTextAsync(Path.Combine(target.DataDirectory, MarkerFile), JsonSerializer.Serialize(new CompanyPeriodMarker(target.Id == archive.Id, input.Cutoff, input.OperationId, preview.HistoryHead)));
            }
            var result = new CompanyPeriodResult(archive.Id, continuation.Id, archive.Name, continuation.Name, input.Cutoff, backup.Path, manifest.Sha256, preview);
            await File.WriteAllTextAsync(receiptPath + ".pending", JsonSerializer.Serialize(new Receipt(input, result,actor.Id)));
            File.Move(receiptPath + ".pending", receiptPath);
            await Complete(result,input.OperationId);
            return result;
        }
        finally { Gate.Release(); }
    }
    private async Task Complete(CompanyPeriodResult result,Guid operationId)
    {
        await session.CurrentActor();
        await using var db = await factory.CreateDbContextAsync(); await using var barrier = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        if (IsArchive(store.DirectoryPath))
        {
            var previous = JsonSerializer.Deserialize<CompanyPeriodMarker>(await File.ReadAllTextAsync(Path.Combine(store.DirectoryPath,MarkerFile)))!;
            if (previous.OperationId != operationId) throw new BusinessException("A different period split already archived this company. Use its continuation.");
        }
        else
        {
            var current = await HistoryProtection.Verify(db);
            if (!current.Ok || current.Head != result.Verification.HistoryHead) throw new BusinessException("The company changed while the copies were prepared. Review the split again; the original remains active.");
            var marker = new CompanyPeriodMarker(true,result.Cutoff,operationId,result.Verification.HistoryHead);
            var path = Path.Combine(store.DirectoryPath,MarkerFile); await File.WriteAllTextAsync(path+".pending",JsonSerializer.Serialize(marker)); File.Move(path+".pending",path,true);
        }
        companies.Publish(result.ArchiveId); companies.Publish(result.ContinuationId);
        await barrier.CommitAsync();
    }
}

public sealed class CompanyArchiveInterceptor(string directory) : SaveChangesInterceptor
{
    private void Check(DbContext? db)
    {
        if (db is null || !CompanyPeriodService.IsArchive(directory)) return;
        if (db.ChangeTracker.Entries().Any(e => (e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) && e.Entity is not Microsoft.AspNetCore.Identity.IdentityUser && e.Entity is not IntegritySeal))
            throw new BusinessException("This company is a period archive. Open the continuation company to change records.");
    }
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) { Check(eventData.Context); return result; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Check(eventData.Context); return ValueTask.FromResult(result); }
}
