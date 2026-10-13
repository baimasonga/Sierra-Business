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
}
