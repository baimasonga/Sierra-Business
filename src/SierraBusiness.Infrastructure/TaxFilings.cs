using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed partial class ComplianceService
{
    private sealed record TaxFilingStored(TaxFilingCommand Request,NetTaxReturnReport Report,decimal Paid,decimal NewCredit,CompanyProfile? Company = null);
    private static void TaxMonth(DateOnly from,DateOnly to)
    {
        if (from.Year < 2000 || from.Day != 1 || from >= new DateOnly(Today.Year,Today.Month,1) || to != from.AddMonths(1).AddDays(-1)) throw new BusinessException("Choose one complete calendar month ending before today.");
    }
    public async Task<TaxFilingPreview> PreviewTaxFiling(DateOnly from,DateOnly to)
    {
        TaxMonth(from,to); var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync(); await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable); var report = await NetTaxReturnFor(db,actor,from,to); var company = await TaxRules.Profile(db,actor.Branch); await transaction.CommitAsync(); return new(report,Fx.Fingerprint(new { Report = report,Company = company }),company);
    }
    public async Task<Guid> RecordTaxFiling(TaxFilingCommand input)
    {
        var actor = await Authorize("accounting.read","accounting.write","users.manage");
        return await Commands.Run(factory,actor,input.OperationId,"tax.return.record",input,async db =>
        {
            TaxMonth(input.From,input.To); Rules.Required(input.FilingReference,"Filing or submission reference",80); Rules.Required(input.Reason,"GST return review reason",200);
            if (!input.SourcesReconciled) throw new BusinessException("Reconcile all invoice, input-tax and branch sources before recording the return.");
            var company = await TaxRules.Profile(db,actor.Branch); if (!company.TaxRegistered || company.CurrencyCode != "SLE") throw new BusinessException("Configure the company's GST registration and SLE reporting currency before recording NRA returns.");
            if (Today <= await PeriodControls.ClosedThrough(db,actor.Branch,options.Value.ClosedThrough)) throw new BusinessException("The settlement date is locked.");
            var prior = await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == "tax.return.record").ToListAsync(); if (prior.Select(o => JsonSerializer.Deserialize<TaxFilingCommand>(o.Payload)!).Any(c => c.From == input.From && c.To == input.To)) throw new BusinessException("This month already has a recorded return. Use a later reviewed correction instead of recording it twice.");
            var report = await NetTaxReturnFor(db,actor,input.From,input.To); if (!report.Output.Check.Ok || Fx.Fingerprint(new { Report = report,Company = company }) != input.ExpectedCalculation) throw new BusinessException("The GST working paper or company registration changed, or does not reconcile. Review it again.");
            var declared = input.DeclaredNetTax ?? report.NetPayable; var rounding = declared-report.NetPayable; if (AccountingEngine.Money(declared) != declared || Math.Abs(rounding) > 1) throw new BusinessException("Declared tax may differ from the precise worksheet by at most one Leone for reviewed filing rounding; larger adjustments need a separate correction.");
            if (input.CarryCreditUsed < 0 || AccountingEngine.Money(input.CarryCreditUsed) != input.CarryCreditUsed || input.CarryCreditUsed > Math.Max(0,declared)) throw new BusinessException("Use a non-negative brought-forward credit no greater than this month's declared net tax.");
            var recovery = report.Inputs.GroupBy(i => i.Claim.RecoveryAccount).ToDictionary(g => g.Key,g => g.Sum(i => i.Sign*i.Claim.AllowableTax));
            var carry = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.CarryAccount); if (carry?.Group != LedgerGroup.Asset || carry.Control || carry.CashOrBank || recovery.ContainsKey(input.CarryAccount)) throw new BusinessException("Choose a separate non-bank custom GST carry-forward asset, distinct from the input recovery ledgers.");
            var available = (await db.Journal.AsNoTracking().Where(j => j.Branch == actor.Branch && (int)j.Account == input.CarryAccount && db.Documents.Any(d => d.Id == j.DocumentId && d.BusinessDate <= Today)).ToListAsync()).Sum(j => j.Debit-j.Credit); if (input.CarryCreditUsed > Math.Max(0,available)) throw new BusinessException("Brought-forward credit exceeds the selected asset ledger's available balance.");
            var paid = Math.Max(0,declared-input.CarryCreditUsed); var newCredit = Math.Max(0,-declared);
            if (Math.Max(paid,newCredit) > 1_000_000_000) throw new BusinessException("Split a return requiring more than one billion through an accountant-reviewed settlement plan.");
            if (paid > 0 && (!await db.LedgerAccounts.AnyAsync(a => a.Id == input.BankAccount && a.CashOrBank) || (await Fx.BankConfigs(db,actor.Branch)).Any(b => b.AccountCode == input.BankAccount))) throw new BusinessException("GST settlement uses a local cash/bank ledger.");
            if (rounding != 0 && !await db.LedgerAccounts.AnyAsync(a => a.Id == input.RoundingAccount && a.Group == LedgerGroup.Expense && !a.Control)) throw new BusinessException("Choose a non-control expense ledger for the reviewed rounding difference.");
            var doc = new BusinessDocument { Id = Guid.NewGuid(),Branch = actor.Branch,Number = $"{actor.Branch}-{options.Value.Device}-GST-{input.OperationId:N}",Kind = paid > 0 ? DocumentKind.Payment : DocumentKind.Journal,BusinessDate = Today,Party = "National Revenue Authority",Total = paid,Settlement = "Reviewed GST return settlement",Detail = JsonSerializer.Serialize(new TaxFilingStored(input,report,paid,newCredit,company)) };
            var entries = new List<Posting>(); entries.AddRange(Grni.Signed(Account.TaxPayable,report.Output.OutputTax)); foreach (var account in recovery) entries.AddRange(Grni.Signed((Account)account.Key,-account.Value)); entries.AddRange(Grni.Signed((Account)input.CarryAccount,newCredit-input.CarryCreditUsed)); if (paid > 0) entries.Add(new((Account)input.BankAccount,0,paid)); if (rounding != 0) entries.AddRange(Grni.Signed((Account)input.RoundingAccount,rounding));
            db.Documents.Add(doc); foreach (var entry in AccountingEngine.Balanced(entries)) db.Journal.Add(new() { Id = Guid.NewGuid(),Branch = actor.Branch,DocumentId = doc.Id,Account = entry.Account,Debit = entry.Debit,Credit = entry.Credit });
            var closed = await PeriodControls.ClosedThrough(db,actor.Branch,options.Value.ClosedThrough); if (input.To > closed) db.PeriodClosures.Add(new() { Id = Guid.NewGuid(),Branch = actor.Branch,Through = input.To,Reason = "Recorded GST return "+input.From.ToString("yyyy-MM")+" · "+input.FilingReference.Trim() });
            return doc.Id;
        });
    }
    public async Task<IReadOnlyList<TaxFilingView>> TaxFilings()
    {
        var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync(); var docs = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && d.Settlement == "Reviewed GST return settlement").ToListAsync(); var by = await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == "tax.return.record").ToDictionaryAsync(o => o.ResultId,o => o.Actor);
        return docs.Select(d => { var stored = JsonSerializer.Deserialize<TaxFilingStored>(d.Detail)!; return new TaxFilingView(d.Id,d.Number,stored.Request.From,stored.Request.To,d.BusinessDate,stored.Request.FilingReference,stored.Request.DeclaredNetTax ?? stored.Report.NetPayable,stored.Request.CarryCreditUsed,stored.Paid,stored.NewCredit,by[d.Id]); }).OrderByDescending(f => f.To).ToArray();
    }
    private sealed record TaxCorrectionStored(TaxCorrectionCommand Request,DateOnly From,DateOnly To,decimal NetChange,decimal Paid,decimal NewCredit);
    internal const string CorrectionSettlement = "Reviewed GST return correction";
    /// <summary>Records a reviewed correction to an already recorded (immutable) monthly return. It is dated today, so locked periods are untouched.</summary>
    public async Task<Guid> RecordTaxCorrection(TaxCorrectionCommand input)
    {
        var actor = await Authorize("accounting.read","accounting.write","users.manage");
        return await Commands.Run(factory,actor,input.OperationId,"tax.return.correct",input,async db =>
        {
            var reference = Rules.Required(input.CorrectionReference,"Correction or amended-return reference",80); Rules.Required(input.Reason,"Correction review reason",200);
            foreach (var amount in new[] { input.OutputTaxChange,input.InputTaxChange,input.CarryCreditUsed,input.Penalty }) if (Math.Abs(amount) > 1_000_000_000 || AccountingEngine.Money(amount) != amount) throw new BusinessException("Correction amounts must be at most one billion with at most two decimal places.");
            if (input.CarryCreditUsed < 0 || input.Penalty < 0) throw new BusinessException("Brought-forward credit and penalties cannot be negative.");
            var net = input.OutputTaxChange-input.InputTaxChange; if (net == 0 && input.Penalty == 0) throw new BusinessException("Enter a tax change or a penalty to record.");
            var filing = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == input.FilingId && d.Branch == actor.Branch && d.Settlement == "Reviewed GST return settlement") ?? throw new BusinessException("Choose a recorded GST return to correct.");
            var original = JsonSerializer.Deserialize<TaxFilingStored>(filing.Detail)!.Request;
            if (Today <= await PeriodControls.ClosedThrough(db,actor.Branch,options.Value.ClosedThrough)) throw new BusinessException("Today's date is locked; corrections are booked on the current date.");
            var earlier = (await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == "tax.return.correct").ToListAsync()).Select(o => JsonSerializer.Deserialize<TaxCorrectionCommand>(o.Payload)!);
            if (earlier.Any(c => c.FilingId == input.FilingId && c.CorrectionReference.Trim().Equals(reference,StringComparison.OrdinalIgnoreCase))) throw new BusinessException("This return already has a correction with that reference.");
            var accounts = await db.LedgerAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id);
            if (net != 0 && (!accounts.TryGetValue(input.OffsetAccount,out var offset) || offset.Control || offset.Group is not (LedgerGroup.Expense or LedgerGroup.Income))) throw new BusinessException("Choose a non-control income or expense ledger to absorb the corrected tax in the books.");
            if (input.Penalty > 0 && (!accounts.TryGetValue(input.PenaltyAccount,out var penalty) || penalty.Control || penalty.Group != LedgerGroup.Expense)) throw new BusinessException("Choose a non-control expense ledger for penalties and interest.");
            if (input.CarryCreditUsed > Math.Max(0,net)) throw new BusinessException("Brought-forward credit can only settle additional tax due, up to its amount.");
            var paid = Math.Max(0,net)-input.CarryCreditUsed; var newCredit = Math.Max(0,-net);
            if (input.CarryCreditUsed > 0 || newCredit > 0)
            {
                if (input.CarryAccount != original.CarryAccount) throw new BusinessException("Use the GST carry-forward asset recorded on the return being corrected.");
                var available = (await db.Journal.AsNoTracking().Where(j => j.Branch == actor.Branch && (int)j.Account == input.CarryAccount).ToListAsync()).Sum(j => j.Debit-j.Credit);
                if (input.CarryCreditUsed > Math.Max(0,available)) throw new BusinessException("Brought-forward credit exceeds the selected asset ledger's available balance.");
            }
            var outflow = paid+input.Penalty;
            if (outflow > 0 && (!accounts.TryGetValue(input.BankAccount,out var bank) || !bank.CashOrBank || (await Fx.BankConfigs(db,actor.Branch)).Any(b => b.AccountCode == input.BankAccount))) throw new BusinessException("GST settlement uses a local cash/bank ledger.");
            var doc = new BusinessDocument { Id = Guid.NewGuid(),Branch = actor.Branch,Number = $"{actor.Branch}-{options.Value.Device}-GSTC-{input.OperationId:N}",Kind = outflow > 0 ? DocumentKind.Payment : DocumentKind.Journal,BusinessDate = Today,Party = "National Revenue Authority",Total = outflow,Settlement = CorrectionSettlement,Detail = JsonSerializer.Serialize(new TaxCorrectionStored(input,original.From,original.To,net,paid,newCredit)) };
            var entries = new List<Posting>();
            if (net > 0) entries.Add(new((Account)input.OffsetAccount,net,0)); else if (net < 0) entries.Add(new((Account)input.OffsetAccount,0,-net));
            if (input.CarryCreditUsed > 0) entries.Add(new((Account)input.CarryAccount,0,input.CarryCreditUsed)); if (newCredit > 0) entries.Add(new((Account)input.CarryAccount,newCredit,0));
            if (input.Penalty > 0) entries.Add(new((Account)input.PenaltyAccount,input.Penalty,0)); if (outflow > 0) entries.Add(new((Account)input.BankAccount,0,outflow));
            db.Documents.Add(doc); foreach (var entry in AccountingEngine.Balanced(entries)) db.Journal.Add(new() { Id = Guid.NewGuid(),Branch = actor.Branch,DocumentId = doc.Id,Account = entry.Account,Debit = entry.Debit,Credit = entry.Credit });
            return doc.Id;
        });
    }
    public async Task<IReadOnlyList<TaxCorrectionView>> TaxCorrections()
    {
        var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync();
        var docs = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && d.Settlement == CorrectionSettlement).ToListAsync();
        var by = await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == "tax.return.correct").ToDictionaryAsync(o => o.ResultId,o => o.Actor);
        return docs.Select(d => { var s = JsonSerializer.Deserialize<TaxCorrectionStored>(d.Detail)!; return new TaxCorrectionView(d.Id,d.Number,s.Request.FilingId,s.From,s.To,d.BusinessDate,s.Request.CorrectionReference.Trim(),s.Request.Reason.Trim(),s.Request.OutputTaxChange,s.Request.InputTaxChange,s.NetChange,s.Request.CarryCreditUsed,s.Paid,s.NewCredit,s.Request.Penalty,by.GetValueOrDefault(d.Id,"")); }).OrderByDescending(c => c.Recorded).ThenBy(c => c.Number).ToArray();
    }
}
