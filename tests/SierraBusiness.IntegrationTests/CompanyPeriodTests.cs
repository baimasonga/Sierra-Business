using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SierraBusiness.Application;
using SierraBusiness.Desktop;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class CompanyPeriodTests
{
    [Fact] public async Task SplitPreservesStockBillsIdentityAndHistoryAndLocksArchiveAndOpeningPeriod()
    {
        var root = Path.Combine(Path.GetTempPath(), "rokela-period-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            using var provider = new ServiceCollection().AddLogging().AddSierraDesktop(root).BuildServiceProvider();
            var store = provider.GetRequiredService<DesktopStore>(); await store.Initialize(provider.GetRequiredService<IDbContextFactory<BusinessDb>>());
            var accounts = provider.GetRequiredService<LocalAccountService>(); await accounts.CreateOwner("owner@example.test","Period-Test-Password-2026!","Period-Test-Password-2026!"); var actor = await accounts.SignIn("owner@example.test","Period-Test-Password-2026!");
            var business = provider.GetRequiredService<IBusinessService>(); var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            var product = await business.AddProduct(actor,new(Guid.NewGuid(),"PERIOD","Period stock","General","unit",50,TrackingMode.Quantity,MinimumPrice:1));
            await business.Post(actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",cutoff,[new(product,10,20)],null));
            var service = provider.GetRequiredService<ICompanyPeriodService>(); var preview = await service.Preview(cutoff); var command = new SplitCompanyPeriodCommand(Guid.NewGuid(),cutoff,"Prior period","New period",preview.HistoryHead);
            var result = await service.Split(command); Assert.Equal(result,await service.Split(command)); Assert.Equal(preview.Debit,preview.Credit);
            var directory = provider.GetRequiredService<CompanyDirectory>(); Assert.Equal(3,directory.List().Count);
            foreach (var company in new[] { directory.Resolve(result.ArchiveId), directory.Resolve(result.ContinuationId) })
            {
                using var clone = new ServiceCollection().AddLogging().AddSierraDesktop(company.DataDirectory).BuildServiceProvider(); await clone.GetRequiredService<DesktopStore>().Initialize(clone.GetRequiredService<IDbContextFactory<BusinessDb>>());
                var session = clone.GetRequiredService<LocalAccountService>(); var cloneActor = await session.SignIn("owner@example.test","Period-Test-Password-2026!"); var cloneBusiness = clone.GetRequiredService<IBusinessService>();
                var state = await cloneBusiness.Read(cloneActor); Assert.Equal(10,Assert.Single(state.Stock).Quantity); Assert.Equal(200,Assert.Single(state.Stock).Value);
                var ledger = await clone.GetRequiredService<IFinanceService>().Read(DateOnly.MinValue,cutoff); Assert.Equal(200,Assert.Single(ledger.Bills).Outstanding);
                if (company.Id == result.ArchiveId) { Assert.DoesNotContain("inventory.receive",cloneActor.Permissions); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => cloneBusiness.Post(cloneActor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",cutoff.AddDays(1),[new(product,1,20)],null))); }
                else { await Assert.ThrowsAsync<BusinessException>(() => cloneBusiness.Post(cloneActor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",cutoff,[new(product,1,20)],null))); await cloneBusiness.Post(cloneActor,new(Guid.NewGuid(),DocumentKind.Sale,"Cash customer",cutoff.AddDays(1),[new(product,1,50)],PaymentMethod.Cash)); }
                Assert.True((await clone.GetRequiredService<IIntegrityService>().Verify()).Ok);
            }
            Assert.Equal(10,Assert.Single((await business.Read(actor)).Stock).Quantity);
            await Assert.ThrowsAsync<BusinessException>(() => service.Split(command with { ContinuationName = "Other" }));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.Post(actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",cutoff.AddDays(1),[new(product,1,20)],null))); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.Preview(cutoff));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); var full = Path.GetFullPath(root); Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),full,StringComparison.OrdinalIgnoreCase); Directory.Delete(full,true); }
    }
    private static async Task WithCompany(Func<ServiceProvider,Actor,string,Task> body,Action<string>? prepare = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "rokela-period-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            prepare?.Invoke(root);
            using var provider = new ServiceCollection().AddLogging().AddSierraDesktop(root).BuildServiceProvider();
            var store = provider.GetRequiredService<DesktopStore>(); await store.Initialize(provider.GetRequiredService<IDbContextFactory<BusinessDb>>());
            var accounts = provider.GetRequiredService<LocalAccountService>(); await accounts.CreateOwner("owner@example.test","Period-Test-Password-2026!","Period-Test-Password-2026!");
            await body(provider,await accounts.SignIn("owner@example.test","Period-Test-Password-2026!"),root);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); var full = Path.GetFullPath(root); Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),full,StringComparison.OrdinalIgnoreCase); Directory.Delete(full,true); }
    }
    [Fact] public Task RetrospectiveSplitKeepsLaterActivityOpenInTheContinuation() => WithCompany(async (provider,actor,root) =>
    {
        var business = provider.GetRequiredService<IBusinessService>(); var today = DateOnly.FromDateTime(DateTime.UtcNow); var cutoff = today.AddDays(-3);
        var product = await business.AddProduct(actor,new(Guid.NewGuid(),"RETRO","Retro stock","General","unit",50,TrackingMode.Quantity,MinimumPrice:1));
        await business.Post(actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",cutoff,[new(product,10,20)],null));
        var later = await business.Post(actor,new(Guid.NewGuid(),DocumentKind.Sale,"Cash customer",today,[new(product,2,50)],PaymentMethod.Cash));
        var service = provider.GetRequiredService<ICompanyPeriodService>();
        await Assert.ThrowsAsync<BusinessException>(() => service.Preview(cutoff));
        await Assert.ThrowsAsync<BusinessException>(() => service.Preview(today.AddDays(-1),true).ContinueWith(t => t.Result.LaterDocuments == 1 ? throw new BusinessException("ok") : t.Result));
        var preview = await service.Preview(cutoff,true); Assert.Equal(1,preview.LaterDocuments);
        await Assert.ThrowsAsync<BusinessException>(() => service.Split(new(Guid.NewGuid(),cutoff,"Prior","Current",preview.HistoryHead)));
        var result = await service.Split(new(Guid.NewGuid(),cutoff,"Prior","Current",preview.HistoryHead,true));
        var directory = provider.GetRequiredService<CompanyDirectory>(); Assert.Contains("1 later document",directory.Resolve(result.ArchiveId).Name);
        var continuation = directory.Resolve(result.ContinuationId);
        using var clone = new ServiceCollection().AddLogging().AddSierraDesktop(continuation.DataDirectory).BuildServiceProvider(); await clone.GetRequiredService<DesktopStore>().Initialize(clone.GetRequiredService<IDbContextFactory<BusinessDb>>());
        var cloneActor = await clone.GetRequiredService<LocalAccountService>().SignIn("owner@example.test","Period-Test-Password-2026!"); var cloneBusiness = clone.GetRequiredService<IBusinessService>();
        Assert.Equal(8,Assert.Single((await cloneBusiness.Read(cloneActor)).Stock).Quantity);
        await cloneBusiness.Reverse(cloneActor,new(Guid.NewGuid(),later,"Later activity stays open in the continuation"));
        await Assert.ThrowsAsync<BusinessException>(() => cloneBusiness.Post(cloneActor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",cutoff,[new(product,1,20)],null)));
        Assert.True((await clone.GetRequiredService<IIntegrityService>().Verify()).Ok);
    });
    [Fact] public Task SyncedBranchSplitsOnlyWhenCaughtUpAndOnlyTheContinuationKeepsSyncing() => WithCompany(async (provider,actor,root) =>
    {
        var business = provider.GetRequiredService<IBusinessService>(); var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var product = await business.AddProduct(actor,new(Guid.NewGuid(),"SYNCED","Synced stock","General","unit",50,TrackingMode.Quantity,MinimumPrice:1));
        await business.Post(actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",cutoff,[new(product,10,20)],null));
        var service = provider.GetRequiredService<ICompanyPeriodService>(); var factory = provider.GetRequiredService<IDbContextFactory<BusinessDb>>();
        await Assert.ThrowsAsync<BusinessException>(() => service.Preview(cutoff));
        await using (var db = await factory.CreateDbContextAsync()) { db.SyncPeers.Add(new() { Name = SyncEngine.CentralPeer,Pushed = 0,LastSuccess = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); }
        Assert.Contains("waiting for head office",(await Assert.ThrowsAsync<BusinessException>(() => service.Preview(cutoff))).Message);
        await using (var db = await factory.CreateDbContextAsync()) { var peer = await db.SyncPeers.SingleAsync(p => p.Name == SyncEngine.CentralPeer); peer.Pushed = await db.SyncChanges.MaxAsync(c => c.Seq); peer.LastSuccess = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); }
        var preview = await service.Preview(cutoff); Assert.True(preview.Synced);
        var result = await service.Split(new(Guid.NewGuid(),cutoff,"Branch prior","Branch current",preview.HistoryHead));
        var directory = provider.GetRequiredService<CompanyDirectory>(); var archive = directory.Resolve(result.ArchiveId).DataDirectory; var continuation = directory.Resolve(result.ContinuationId).DataDirectory;
        Assert.Equal(SyncRole.Branch,new DesktopSyncConfiguration(continuation).Load().Role); Assert.True(CompanyPeriodService.Marker(continuation)!.SyncContinuation);
        Assert.Equal(SyncRole.Off,new DesktopSyncConfiguration(archive).Load().Role); Assert.False(File.Exists(Path.Combine(archive,CompanyPeriodService.SyncFile)));
        Assert.Equal(SyncRole.Off,new DesktopSyncConfiguration(root).Load().Role); Assert.True(File.Exists(Path.Combine(root,CompanyPeriodService.SyncFile+".archived")));
        Assert.Throws<BusinessException>(() => new DesktopSyncConfiguration(archive).Save("https://hq.example.test","0123456789abcdef0123"));
    },root => File.WriteAllText(Path.Combine(root,"sync-settings.json"),"{\"Server\":\"https://hq.example.test\",\"Key\":\"\",\"ProtectedKey\":\"\"}"));
    [Fact] public Task CompactionKeepsRecordsAndHistoryAndWorksOnArchives() => WithCompany(async (provider,actor,root) =>
    {
        var business = provider.GetRequiredService<IBusinessService>(); var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var product = await business.AddProduct(actor,new(Guid.NewGuid(),"COMPACT","Compact stock","General","unit",50,TrackingMode.Quantity,MinimumPrice:1));
        for (var i = 0; i < 20; i++) await business.Post(actor,new(Guid.NewGuid(),DocumentKind.Receipt,"Supplier",cutoff,[new(product,1,20)],null));
        var service = provider.GetRequiredService<ICompanyPeriodService>(); var head = (await provider.GetRequiredService<IIntegrityService>().Verify()).Head;
        var result = await service.Compact(); Assert.Equal(head,result.HistoryHead); Assert.Equal(20,result.Documents); Assert.True(File.Exists(result.BackupPath)); Assert.True(result.BytesAfter > 0);
        Assert.True((await provider.GetRequiredService<IIntegrityService>().Verify()).Ok); Assert.Equal(20,Assert.Single((await business.Read(actor)).Stock).Quantity);
        var preview = await service.Preview(cutoff); await service.Split(new(Guid.NewGuid(),cutoff,"Prior","Current",preview.HistoryHead));
        Assert.Equal(head,(await service.Compact()).HistoryHead);
    });
}
