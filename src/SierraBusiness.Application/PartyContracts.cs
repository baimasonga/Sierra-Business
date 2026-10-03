namespace SierraBusiness.Application;
public enum PartyType { Customer, Supplier, Both }
public sealed record PartyCommand(Guid OperationId, Guid? Id, int ExpectedVersion, string Code, string Name, PartyType Type, string Contact, string Phone, string Email, string Address, int PaymentDays, decimal CreditLimit, bool CreditHold);
public sealed record PartyView(Guid Id, int Version, string Code, string Name, PartyType Type, string Contact, string Phone, string Email, string Address, int PaymentDays, decimal CreditLimit, bool CreditHold);
public interface IPartyService { Task<IReadOnlyList<PartyView>> Read(); Task<Guid> Save(PartyCommand input); }
