using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.IntegrationTests;

/// <summary>Registers customers/suppliers for tests: credit sales require a registered customer (0.13.0).</summary>
internal static class TestParties
{
    public static async Task Register(IDbContextFactory<BusinessDb> factory, string name, PartyType type = PartyType.Customer, decimal creditLimit = 100_000_000)
    {
        var service = new PartyService(factory, new Owner(), Options.Create(new TradingOptions()));
        var code = new string(name.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        await service.Save(new(Guid.NewGuid(), null, 0, code, name, type, "", "", "", "", 0, creditLimit, false));
    }
    private sealed class Owner : IActorSession { public Task<Actor> CurrentActor() => Task.FromResult(new Actor("test-setup", "FREETOWN", BusinessPermissions.All.ToHashSet())); }
}
