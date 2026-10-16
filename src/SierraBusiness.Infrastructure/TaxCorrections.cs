using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed partial class ComplianceService
{
    private const string CorrectionSettlement = "Reviewed GST return correction", CorrectionAction = "tax.return.correct";
    private sealed record TaxCorrectionStored(TaxFilingCorrectionCommand Request,DateOnly From,DateOnly To,decimal Paid,decimal NewCredit);

    public async Task<Guid> RecordTaxFilingCorrection(TaxFilingCorrectionCommand input)
    {
        var actor = await Authorize("accounting.read","accounting.write","users.manage");
        return await Commands.Run(factory,actor,input.OperationId,CorrectionAction,input,async db =>
        {
            Rules.Required(input.CorrectionReference,"Correction or amended-return reference",80); Rules.Required(input.Reason,"Correction review reason",300);
            var filing = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == input.FilingId && d.Branch == actor.Branch && d.Settlement == "Reviewed GST return settlement") ?? throw new BusinessException("Choose a recorded GST return to correct.");
            var stored = JsonSerializer.Deserialize<TaxFilingStored>(filing.Detail)!;
            if (Today <= await PeriodControls.ClosedThrough(db,actor.Branch,options.Value.ClosedThrough)) throw new BusinessException("The correction date is locked.");
            foreach (var amount in new[] { input.OutputTaxChange,input.InputTaxChange }) if (Math.Abs(amount) > 1_000_000_000 || AccountingEngine.Money(amount) != amount) throw new BusinessException("Tax changes must be at most one billion either way, with two decimal places.");
            if (input.Penalty < 0 || input.Penalty > 1_000_000_000 || AccountingEngine.Money(input.Penalty) != input.Penalty || input.CarryCreditUsed < 0 || AccountingEngine.Money(input.CarryCreditUsed) != input.CarryCreditUsed) throw new BusinessException("Penalty and credit used must be non-negative with two decimal places.");
            var net = input.OutputTaxChange-input.InputTaxChange;
            if (net == 0 && input.Penalty == 0) throw new BusinessException("Enter the reviewed tax change or penalty for this correction.");
            var earlier = (await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == CorrectionAction).ToListAsync()).Select(o => JsonSerializer.Deserialize<TaxFilingCorrectionCommand>(o.Payload)!);
            if (earlier.Any(c => c.FilingId == input.FilingId && c.CorrectionReference.Trim().Equals(input.CorrectionReference.Trim(),StringComparison.OrdinalIgnoreCase))) throw new BusinessException("This correction reference is already recorded for the return.");
            var entries = new List<Posting>(); decimal paid = 0,newCredit = 0;
            if (net != 0)
            {
                var adjustment = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.AdjustmentAccount);
                if (adjustment is null || adjustment.Control || adjustment.CashOrBank || adjustment.Id == (int)Account.TaxPayable || adjustment.Id == input.CarryAccount) throw new BusinessException("Choose a custom ledger, other than cash/bank, tax payable or the carry-forward asset, for the corrected amount.");
                var carry = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.CarryAccount);
                if (carry?.Group != LedgerGroup.Asset || carry.Control || carry.CashOrBank) throw new BusinessException("Choose the separate non-bank custom GST carry-forward asset.");
                if (net > 0)
                {
                    var available = (await db.Journal.AsNoTracking().Where(j => j.Branch == actor.Branch && (int)j.Account == input.CarryAccount).ToListAsync()).Sum(j => j.Debit-j.Credit);
                    if (input.CarryCreditUsed > Math.Min(net,Math.Max(0,available))) throw new BusinessException("Credit used cannot exceed the additional tax or the carry-forward balance.");
                    paid = net-input.CarryCreditUsed;
                    entries.Add(new((Account)adjustment.Id,net,0)); if (input.CarryCreditUsed > 0) entries.Add(new((Account)carry.Id,0,input.CarryCreditUsed));
                }
                else
                {
                    if (input.CarryCreditUsed != 0) throw new BusinessException("A correction that reduces tax creates credit; it cannot also use credit.");
                    newCredit = -net; entries.Add(new((Account)carry.Id,newCredit,0)); entries.Add(new((Account)adjustment.Id,0,newCredit));
                }
            }
            else if (input.CarryCreditUsed != 0) throw new BusinessException("Credit can only meet additional tax.");
            if (input.Penalty > 0)
            {
                var penalty = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.PenaltyAccount);
                if (penalty?.Group != LedgerGroup.Expense || penalty.Control) throw new BusinessException("Choose a custom expense ledger for penalties and interest.");
                entries.Add(new((Account)penalty.Id,input.Penalty,0));
            }
            var cash = paid+input.Penalty;
            if (cash > 0)
            {
                if (!await db.LedgerAccounts.AnyAsync(a => a.Id == input.BankAccount && a.CashOrBank) || (await Fx.BankConfigs(db,actor.Branch)).Any(b => b.AccountCode == input.BankAccount)) throw new BusinessException("Pay corrections from a local cash/bank ledger.");
                entries.Add(new((Account)input.BankAccount,0,cash));
            }
            var doc = new BusinessDocument { Id = Guid.NewGuid(),Branch = actor.Branch,Number = $"{actor.Branch}-{options.Value.Device}-GSTC-{input.OperationId:N}",Kind = cash > 0 ? DocumentKind.Payment : DocumentKind.Journal,BusinessDate = Today,Party = "National Revenue Authority",Total = cash,Settlement = CorrectionSettlement,
                Detail = JsonSerializer.Serialize(new TaxCorrectionStored(input,stored.Request.From,stored.Request.To,paid,newCredit)) };
            db.Documents.Add(doc); foreach (var entry in AccountingEngine.Balanced(entries)) db.Journal.Add(new() { Id = Guid.NewGuid(),Branch = actor.Branch,DocumentId = doc.Id,Account = entry.Account,Debit = entry.Debit,Credit = entry.Credit });
            return doc.Id;
        });
    }

    public async Task<IReadOnlyList<TaxFilingCorrectionView>> TaxFilingCorrections()
    {
        var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync();
        var docs = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && d.Settlement == CorrectionSettlement).ToListAsync();
        var by = await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == CorrectionAction).ToDictionaryAsync(o => o.ResultId,o => o.Actor);
        return docs.Select(d => { var s = JsonSerializer.Deserialize<TaxCorrectionStored>(d.Detail)!; return new TaxFilingCorrectionView(d.Id,d.Number,s.Request.FilingId,s.From,s.To,d.BusinessDate,s.Request.CorrectionReference,s.Request.OutputTaxChange,s.Request.InputTaxChange,s.Paid,s.Request.CarryCreditUsed,s.NewCredit,s.Request.Penalty,by.GetValueOrDefault(d.Id,"")); }).OrderByDescending(c => c.Recorded).ThenBy(c => c.Number).ToArray();
    }
}
