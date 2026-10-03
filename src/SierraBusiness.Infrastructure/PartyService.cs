using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed class BusinessParty
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public string Branch { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public PartyType Type { get; set; }
    public string Contact { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public int PaymentDays { get; set; }
    public decimal CreditLimit { get; set; }
    public bool CreditHold { get; set; }
}
public sealed class PartyService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IPartyService
{
    private async Task<Actor> Authorize(bool write = false)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read"); if (write) actor.Demand("catalogue.write");
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch."); return actor;
    }
    public async Task<IReadOnlyList<PartyView>> Read()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        return await db.Parties.AsNoTracking().Where(x => x.Branch == actor.Branch).OrderBy(x => x.Code).Select(x => new PartyView(x.Id, x.Version, x.Code, x.Name, x.Type, x.Contact, x.Phone, x.Email, x.Address, x.PaymentDays, x.CreditLimit, x.CreditHold)).ToListAsync();
    }
    public async Task<Guid> Save(PartyCommand input)
    {
        var actor = await Authorize(true);
        if (input.OperationId == Guid.Empty) throw new BusinessException("Operation ID is required.");
        var payload = JsonSerializer.Serialize(input); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + "party.save" + payload)));
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.Operations.SingleOrDefaultAsync(x => x.Id == input.OperationId); if (previous is not null) { if (hash != previous.Fingerprint) throw new BusinessException("Operation ID already used with different data or identity."); return previous.ResultId; }
        var code = Rules.Required(input.Code, "Party code", 40).ToUpperInvariant(); var name = Rules.Required(input.Name, "Party name"); var normalized = name.ToUpperInvariant();
        if (!Enum.IsDefined(input.Type) || input.PaymentDays is < 0 or > 3650 || input.CreditLimit < 0 || input.CreditLimit > 1_000_000_000 || AccountingEngine.Money(input.CreditLimit) != input.CreditLimit) throw new BusinessException("Check party type, payment days (0–3650) and credit limit (up to two decimal places).");
        if (input.Contact.Length > 160 || input.Phone.Length > 60 || input.Email.Length > 160 || input.Address.Length > 200) throw new BusinessException("Contact/address fields exceed their maximum length.");
        if (input.Email.Length > 0 && !System.Net.Mail.MailAddress.TryCreate(input.Email, out _)) throw new BusinessException("Enter a valid email address.");
        if (await db.Parties.AnyAsync(x => x.Branch == actor.Branch && x.Id != input.Id && (x.Code == code || x.NormalizedName == normalized))) throw new BusinessException("Party code or name already exists in this branch.");
        BusinessParty party;
        if (input.Id is null) { party = new() { Id = Guid.NewGuid(), Branch = actor.Branch }; db.Parties.Add(party); }
        else { party = await db.Parties.SingleOrDefaultAsync(x => x.Id == input.Id && x.Branch == actor.Branch) ?? throw new BusinessException("Party not found."); if (party.Version != input.ExpectedVersion) throw new BusinessException("Party changed. Refresh before saving."); if (party.Name != name && await db.Documents.AnyAsync(x => x.Branch == actor.Branch && x.Party.ToUpper() == party.NormalizedName)) throw new BusinessException("A party with posted history cannot be renamed. Retain its name to preserve bill matching."); }
        party.Version++; party.Code = code; party.Name = name; party.NormalizedName = normalized; party.Type = input.Type; party.Contact = input.Contact.Trim(); party.Phone = input.Phone.Trim(); party.Email = input.Email.Trim(); party.Address = input.Address.Trim(); party.PaymentDays = input.PaymentDays; party.CreditLimit = input.CreditLimit; party.CreditHold = input.CreditHold;
        db.Operations.Add(new() { Id = input.OperationId, ResultId = party.Id, Branch = actor.Branch, Actor = actor.Id, Action = "party.save", Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = hash }); await db.SaveChangesAsync(); await tx.CommitAsync(); return party.Id;
    }
}

