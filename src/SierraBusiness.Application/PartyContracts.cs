using System.Text.Json.Serialization;
namespace SierraBusiness.Application;
public enum PartyType { Customer, Supplier, Both }
/// <summary>Suggested organisation types (Skill 19). Any other type can be typed in.</summary>
public static class PartyCategories { public static readonly string[] Suggested = ["Individual", "Business", "Government", "NGO", "Hospital / clinic", "School", "Other"]; }
/// <summary>A named contact at an institution, for example procurement, finance, technical/biomedical or management.</summary>
public sealed record PartyContact(string Role, string Name, string Phone = "", string Email = "");
public sealed record PartyCommand(Guid OperationId, Guid? Id, int ExpectedVersion, string Code, string Name, PartyType Type, string Contact, string Phone, string Email, string Address, int PaymentDays, decimal CreditLimit, bool CreditHold,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tin = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<PartyContact>? Contacts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notes = null);
public sealed record PartyView(Guid Id, int Version, string Code, string Name, PartyType Type, string Contact, string Phone, string Email, string Address, int PaymentDays, decimal CreditLimit, bool CreditHold,
    string Category = "", string Tin = "", IReadOnlyList<PartyContact>? Contacts = null, string Notes = "")
{
    public IReadOnlyList<PartyContact> People => Contacts ?? [];
}

/// <summary>Statement of account from the ledger: receivables (1100) for customers, payables (2000) for suppliers.</summary>
public sealed record AccountStatementLine(DateOnly Date, Guid DocumentId, string Number, string Description, decimal Debit, decimal Credit, decimal Balance);
public sealed record AccountStatement(PartyView Party, bool Supplier, DateOnly From, DateOnly To, decimal Opening, IReadOnlyList<AccountStatementLine> Lines, decimal Closing, decimal UnappliedCredit,
    decimal Current, decimal Days30, decimal Days60, decimal Days90, decimal Over90);

public interface IPartyService
{
    Task<IReadOnlyList<PartyView>> Read();
    Task<Guid> Save(PartyCommand input);
    Task<AccountStatement> Statement(Guid partyId, DateOnly from, DateOnly to, bool supplier = false);
    Task<byte[]> ExportStatement(Guid partyId, DateOnly from, DateOnly to, bool supplier = false);
}
