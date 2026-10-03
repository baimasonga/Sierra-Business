using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed class BankStatement { public Guid Id { get; set; } public string Branch { get; set; } = ""; public int AccountCode { get; set; } public string Reference { get; set; } = ""; public string ContentHash { get; set; } = ""; public decimal Opening { get; set; } public decimal Closing { get; set; } }
public sealed class BankStatementRow { public Guid Id { get; set; } public Guid StatementId { get; set; } public int Line { get; set; } public DateOnly Date { get; set; } public string Reference { get; set; } = ""; public decimal Amount { get; set; } }
public sealed class BankStatementVoid { public Guid Id { get; set; } public Guid StatementId { get; set; } public string Reason { get; set; } = ""; }
public sealed class ReceivedCheque { public Guid Id { get; set; } public Guid SourceDocumentId { get; set; } public string Branch { get; set; } = ""; public string Party { get; set; } = ""; public string Reference { get; set; } = ""; public string Bank { get; set; } = ""; public DateOnly ChequeDate { get; set; } public decimal Amount { get; set; } }
public sealed class ChequeEvent { public Guid Id { get; set; } public Guid ChequeId { get; set; } public Guid DocumentId { get; set; } public int Version { get; set; } public ChequeAction Action { get; set; } public ChequeState PreviousState { get; set; } public ChequeState State { get; set; } public int BankAccount { get; set; } public DateOnly Date { get; set; } public string Reason { get; set; } = ""; }
public sealed class SupplierCredit { public Guid Id { get; set; } public Guid ReturnId { get; set; } public decimal Amount { get; set; } }
public sealed class SupplierCreditUse { public Guid Id { get; set; } public Guid CreditId { get; set; } public Guid DocumentId { get; set; } public decimal Amount { get; set; } }
public sealed class TillShift { public Guid Id { get; set; } public string Branch { get; set; } = ""; public string Cashier { get; set; } = ""; public int AccountCode { get; set; } public DateTimeOffset Opened { get; set; } public decimal OpeningCount { get; set; } public decimal OpeningBook { get; set; } }
public sealed class TillClose { public Guid Id { get; set; } public Guid ShiftId { get; set; } public DateTimeOffset Closed { get; set; } public decimal Expected { get; set; } public decimal Counted { get; set; } public string Note { get; set; } = ""; }
public sealed class TillEntry { public Guid Id { get; set; } public Guid ShiftId { get; set; } public Guid JournalLineId { get; set; } public string Actor { get; set; } = ""; }
public static class BankingRules
{
    public static async Task<ReceivedCheque> NewCheque(BusinessDb db, BusinessDocument source, string reference, string bank, DateOnly date)
    {
        reference = Rules.Required(reference, "Cheque reference", 100).ToUpperInvariant(); bank = Rules.Required(bank, "Bank / issuer account", 100).ToUpperInvariant();
        if (await db.ReceivedCheques.AnyAsync(x => x.Branch == source.Branch && x.Party == source.Party && x.Reference == reference && x.Bank == bank)) throw new BusinessException("This customer's cheque reference and bank are already registered.");
        var cheque = new ReceivedCheque { Id = Guid.NewGuid(), SourceDocumentId = source.Id, Branch = source.Branch, Party = source.Party, Reference = reference, Bank = bank, ChequeDate = date, Amount = source.Total };
        db.ReceivedCheques.Add(cheque); return cheque;
    }
    public static async Task CaptureTillEntries(BusinessDb db, CancellationToken ct = default)
    {
        var added = db.ChangeTracker.Entries<JournalLine>().Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToArray(); if (added.Length == 0) return;
        var shifts = await db.TillShifts.Where(x => !db.TillCloses.Any(c => c.ShiftId == x.Id)).ToListAsync(ct).ConfigureAwait(false);
        foreach (var line in added)
        {
            var shift = shifts.SingleOrDefault(x => x.Branch == line.Branch && x.AccountCode == (int)line.Account); if (shift is null || db.TillEntries.Local.Any(x => x.JournalLineId == line.Id)) continue;
            var actor = db.Operations.Local.FirstOrDefault(x => x.ResultId == line.DocumentId)?.Actor ?? "Unattributed";
            db.TillEntries.Add(new() { Id = Guid.NewGuid(), ShiftId = shift.Id, JournalLineId = line.Id, Actor = actor });
        }
    }
}
