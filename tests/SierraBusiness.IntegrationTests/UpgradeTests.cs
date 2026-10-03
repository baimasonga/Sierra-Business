using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class UpgradeTests
{
    [Fact] public async Task UpgradeFromCaptureReleasePreservesStockAndOldCommandReplay()
    {
        var path = Path.Combine(Path.GetTempPath(), "sierra-upgrade-" + Guid.NewGuid().ToString("N") + ".db");
        var factory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        try
        {
            var product = Guid.NewGuid(); var doc = Guid.NewGuid(); var op = Guid.NewGuid(); var date = DateOnly.FromDateTime(DateTime.UtcNow);
            var oldRequest = new { OperationId = op, Kind = DocumentKind.Receipt, Party = "Supplier", BusinessDate = date, Lines = new[] { new TradeLine(product, 10, 60) }, Payment = (PaymentMethod?)null, PaymentReference = (string?)null, Bank = (string?)null, PaymentDate = (DateOnly?)null };
            var payload = JsonSerializer.Serialize(oldRequest); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("FREETOWN" + "owner" + "Receipt.post" + payload)));
            var detail = JsonSerializer.Serialize(new { Request = oldRequest, TaxRate = 0m, Net = 600m, Tax = 0m, Cost = 600m });
            await using (var db = factory.CreateDbContext())
            {
                await db.GetService<IMigrator>().MigrateAsync("20260927142103_DocumentCapture");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Products (Id,Sku,Name,Category,Unit,Price,Tracking) VALUES ({product},'P','Product','General','piece',100,0)");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Documents (Id,Number,Branch,Party,BusinessDate,Kind,Total,Settlement,Detail) VALUES ({doc},'LEGACY','FREETOWN','Supplier',{date},0,600,'Supplier payable',{detail})");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Movements (Id,DocumentId,ProductId,Branch,Quantity,Value) VALUES ({Guid.NewGuid()},{doc},{product},'FREETOWN',10,600)");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Journal (Id,DocumentId,Branch,Account,Debit,Credit) VALUES ({Guid.NewGuid()},{doc},'FREETOWN',1200,600,0)");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Journal (Id,DocumentId,Branch,Account,Debit,Credit) VALUES ({Guid.NewGuid()},{doc},'FREETOWN',2000,0,600)");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Operations (Id,ResultId,Fingerprint,Branch,Actor,Action,Time,Payload,SchemaVersion,Acknowledged) VALUES ({op},{doc},{hash},'FREETOWN','owner','Receipt.post',{DateTimeOffset.UtcNow},{payload},1,0)");
                await db.Database.MigrateAsync();
            }
            var service = new BusinessService(factory, Options.Create(new TradingOptions())); var actor = new Actor("owner", "FREETOWN", new HashSet<string> { "business.read", "inventory.receive", "accounting.read" });
            Assert.Equal(doc, await service.Post(actor, new(op, DocumentKind.Receipt, "Supplier", date, [new(product, 10, 60)], null)));
            var state = await service.Read(actor); Assert.Single(state.Documents); Assert.Equal("MAIN", state.Stock.Single().Location); Assert.Equal(600, state.Stock.Single().Value); Assert.Equal(1, state.PendingSync);
        }
        finally { File.Delete(path); }
    }
    [Fact] public async Task CommerceUpgradePreservesExistingReturnCreditsAndGrantsOnlyOwnerPricingPermission()
    {
        var path = Path.Combine(Path.GetTempPath(), "sierra-commerce-upgrade-" + Guid.NewGuid().ToString("N") + ".db");
        var factory = new Factory(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        try
        {
            await using var db = factory.CreateDbContext(); await db.GetService<IMigrator>().MigrateAsync("20260928175757_BankMatchCorrections");
            var original = Guid.NewGuid(); var returned = Guid.NewGuid(); var product = Guid.NewGuid(); var allocation = Guid.NewGuid(); var date = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var user in new[] { "owner", "clerk" }) await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AspNetUsers (Id,UserName,EmailConfirmed,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount) VALUES ({user},{user},0,0,0,1,0)");
            foreach (var permission in new[] { "business.read", "catalogue.write", "sales.create", "inventory.receive", "documents.reverse", "accounting.read", "audit.read" }) await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AspNetUserClaims (UserId,ClaimType,ClaimValue) VALUES ('owner','permission',{permission})");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO AspNetUserClaims (UserId,ClaimType,ClaimValue) VALUES ('clerk','permission','sales.create')");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Documents (Id,Number,Branch,Party,BusinessDate,Kind,Total,Settlement,Detail) VALUES ({original},'ORIGINAL','FREETOWN','School',{date},1,'0.30','Credit','{{}}')");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Documents (Id,Number,Branch,Party,BusinessDate,Kind,Total,Settlement,Detail) VALUES ({returned},'RETURN','FREETOWN','School',{date},10,'-0.30','Credit','{{}}')");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ReturnAllocations (Id,OriginalDocumentId,ReturnDocumentId,ProductId,Quantity,Net,Tax,Cost) VALUES ({allocation},{original},{returned},{product},1,'0.10','0.20','0.05')");
            await db.Database.MigrateAsync();
            Assert.Equal(.30m, (await db.ReturnAllocations.SingleAsync()).BillCredit);
            Assert.True(await db.UserClaims.AnyAsync(x => x.UserId == "owner" && x.ClaimValue == "pricing.manage"));
            Assert.False(await db.UserClaims.AnyAsync(x => x.UserId == "clerk" && x.ClaimValue == "pricing.manage"));
            Assert.Empty(await db.PricePolicies.ToListAsync());
        }
        finally { File.Delete(path); }
    }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}

