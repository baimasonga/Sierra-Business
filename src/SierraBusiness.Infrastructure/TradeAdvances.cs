using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed partial class BankingService
{
    public Task<Guid> PostAdvance(TradeAdvanceCommand input) => Execute(input.OperationId, "accounting.write", "trade.advance.post", input, async (db, actor) =>
    {
        actor.Demand("accounting.read");
        if (!Enum.IsDefined(input.Kind)) throw new BusinessException("Choose a customer deposit or supplier advance.");
        var date = DateOnly.FromDateTime(DateTime.UtcNow); await OpenDate(db, actor, date);
        Amount(input.Amount);
        var party = await db.Parties.SingleOrDefaultAsync(x => x.Id == input.PartyId && x.Branch == actor.Branch) ?? throw new BusinessException("Select a registered party in this branch.");
        var customer = input.Kind == TradeAdvanceKind.CustomerDeposit;
        if (input.Cheque is not null && customer) { if (input.FundsAccount != 1020 || input.Cheque.Date.Year < 2000) throw new BusinessException("Customer cheque deposits use cheques in hand and a valid cheque date."); }
        else await Funds(db,input.FundsAccount);
        if (customer ? party.Type == PartyType.Supplier : party.Type == PartyType.Customer) throw new BusinessException("The party type does not match this deposit or advance.");
        Rules.Required(input.Reference, "Payment reference", 160);
        var doc = Document(actor, input.OperationId, customer ? DocumentKind.MoneyReceipt : DocumentKind.Payment, date, party.Name, input.Amount, customer ? "Customer deposit" : "Supplier advance", input);
        if (customer)
        {
            Journal(db, doc, new((Account)input.FundsAccount, input.Amount, 0), new((Account)2400, 0, input.Amount));
            db.CustomerCredits.Add(new() { Id = Guid.NewGuid(), ReturnId = doc.Id, Amount = input.Amount });
            if (input.Cheque is not null) await BankingRules.NewCheque(db,doc,input.Cheque.Number,input.Cheque.Bank,input.Cheque.Date);
        }
        else
        {
            Journal(db, doc, new((Account)1300, input.Amount, 0), new((Account)input.FundsAccount, 0, input.Amount));
            db.SupplierCredits.Add(new() { Id = Guid.NewGuid(), ReturnId = doc.Id, Amount = input.Amount });
            if (input.Cheque is not null)
            {
                if (input.FundsAccount == 1000 || input.Cheque.Date != date) throw new BusinessException("Supplier cheque advances use a bank ledger and today's issue date.");
                var number = Rules.Required(input.Cheque.Number,"Cheque number",30).ToUpperInvariant(); if (await db.IssuedCheques.AnyAsync(c => c.Branch == actor.Branch && c.BankAccount == input.FundsAccount && c.Number == number)) throw new BusinessException("This cheque number was already issued.");
                var issue = new IssueChequeCommand(input.OperationId,input.FundsAccount,party.Name,number,date,input.Amount,[],Narration:input.Reference,BookId:input.Cheque.BookId); await BankingExtrasService.ValidateBook(db,actor,issue,number);
                db.IssuedCheques.Add(new() { Id = Guid.NewGuid(),Branch = actor.Branch,BankAccount = input.FundsAccount,Number = number,Payee = party.Name,Date = date,Amount = input.Amount,VoucherId = doc.Id,Actor = actor.Id });
            }
        }
        return doc.Id;
    });

    public async Task<IReadOnlyList<TradeAdvanceView>> Advances()
    {
        var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync();
        var docs = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && (d.Settlement == "Customer deposit" || d.Settlement == "Supplier advance") && !db.Documents.Any(r => r.Reverses == d.Id)).ToDictionaryAsync(d => d.Id);
        var ids = docs.Keys.ToArray();
        var customers = await db.CustomerCredits.AsNoTracking().Where(c => ids.Contains(c.ReturnId)).ToListAsync();
        var suppliers = await db.SupplierCredits.AsNoTracking().Where(c => ids.Contains(c.ReturnId)).ToListAsync();
        var customerIds = customers.Select(c => c.Id).ToArray(); var supplierIds = suppliers.Select(c => c.Id).ToArray();
        var customerUses = await db.CustomerCreditUses.AsNoTracking().Where(u => customerIds.Contains(u.CreditId) && !db.Documents.Any(r => r.Reverses == u.DocumentId)).ToListAsync();
        var supplierUses = await db.SupplierCreditUses.AsNoTracking().Where(u => supplierIds.Contains(u.CreditId) && !db.Documents.Any(r => r.Reverses == u.DocumentId)).ToListAsync();
        var chequeStates = await ChequeAdvances.States(db,actor.Branch);
        return customers.Select(c => { var d = docs[c.ReturnId]; var state = chequeStates.TryGetValue(d.Id,out var chequeState) ? (ChequeState?)chequeState : null; return new TradeAdvanceView(c.Id,d.Id,TradeAdvanceKind.CustomerDeposit,d.Number,d.Party,d.BusinessDate,c.Amount,state is not null && state != ChequeState.Cleared ? 0 : c.Amount-customerUses.Where(u => u.CreditId == c.Id).Sum(u => u.Amount),state); })
            .Concat(suppliers.Select(c => { var d = docs[c.ReturnId]; return new TradeAdvanceView(c.Id, d.Id, TradeAdvanceKind.SupplierAdvance, d.Number, d.Party, d.BusinessDate, c.Amount, c.Amount - supplierUses.Where(u => u.CreditId == c.Id).Sum(u => u.Amount)); }))
            .OrderByDescending(d => d.Date).ThenBy(d => d.Number).ToArray();
    }
}
