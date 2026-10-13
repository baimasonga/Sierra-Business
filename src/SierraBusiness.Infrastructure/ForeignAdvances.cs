using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public static partial class Fx
{
    internal const string AdvanceAction = "fx.advance.post", AdvanceUseAction = "fx.advance.use";
    internal sealed record AdvanceStored(ForeignAdvanceCommand Request,string Currency,decimal Rate,decimal LocalCarrying);
    internal sealed record AdvanceUseStored(ForeignAdvanceUseCommand Request,string Currency,ForeignAdvanceUsePreview Calculation);
    internal static async Task RejectLocalAdvance(BusinessDb db,Guid document)
    {
        if (await db.Operations.AnyAsync(o => o.ResultId == document && o.Action == AdvanceAction)) throw new BusinessException("Use Foreign currency advances to retain currency units and realized exchange differences.");
    }
    internal static async Task<IReadOnlyList<ForeignAdvanceView>> AdvancesAt(BusinessDb db,string branch,DateOnly date)
    {
        var docs = await db.Documents.AsNoTracking().Where(d => d.Branch == branch && d.BusinessDate <= date).ToDictionaryAsync(d => d.Id); var reversed = docs.Values.Where(d => d.Reverses != null).Select(d => d.Reverses!.Value).ToHashSet(); bool Active(Guid id) => docs.ContainsKey(id) && !reversed.Contains(id);
        var operations = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == AdvanceAction).ToListAsync();
        var uses = (await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == AdvanceUseAction).ToListAsync()).Where(o => Active(o.ResultId)).Select(o => JsonSerializer.Deserialize<ForeignAdvanceUseCommand>(o.Payload)!).ToArray();
        var customer = await db.CustomerCredits.AsNoTracking().Where(c => db.Documents.Any(d => d.Id == c.ReturnId && d.Branch == branch)).ToListAsync(); var supplier = await db.SupplierCredits.AsNoTracking().Where(c => db.Documents.Any(d => d.Id == c.ReturnId && d.Branch == branch)).ToListAsync();
        var result = new List<ForeignAdvanceView>();
        var cancelledValuations = (await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == RevaluationCancelAction).ToListAsync()).Where(o => Active(o.ResultId)).Select(o => JsonSerializer.Deserialize<CancelForeignRevaluationCommand>(o.Payload)!.DocumentId).ToHashSet();
        var closing = docs.Values.Where(d => d.Kind == DocumentKind.Journal && Active(d.Id) && !cancelledValuations.Contains(d.Id)).Select(d => { try { return JsonSerializer.Deserialize<ValuationStored>(d.Detail); } catch (JsonException) { return null; } }).Where(v => v?.FxRevaluation is not null && v.ReversalDate > date).ToArray();
        foreach (var operation in operations.Where(o => Active(o.ResultId)))
        {
            var doc = docs[operation.ResultId]; var stored = JsonSerializer.Deserialize<AdvanceStored>(doc.Detail)!; var credit = stored.Request.Kind == TradeAdvanceKind.CustomerDeposit ? customer.Single(c => c.ReturnId == doc.Id).Id : supplier.Single(c => c.ReturnId == doc.Id).Id;
            var spent = uses.Where(u => u.CreditId == credit).Sum(u => u.SourceAmount); var remaining = stored.Request.SourceAmount-spent;
            var useDocs = docs.Values.Where(d => d.Reverses is null && Active(d.Id) && (d.Settlement == "Foreign advance allocation" || d.Settlement == "Foreign advance refund")).Select(d => JsonSerializer.Deserialize<AdvanceUseStored>(d.Detail)!).Where(u => u.Request.CreditId == credit && uses.Any(c => c.OperationId == u.Request.OperationId)).ToArray();
            var carrying = stored.LocalCarrying-useDocs.Sum(u => u.Calculation.CreditCarryingUsed);
            carrying += closing.SelectMany(v => v!.FxRevaluation.Rows).Where(r => r.Key == "advance:"+doc.Id.ToString("N")).Sum(r => r.ClosingValue-r.Carrying);
            result.Add(new(credit,doc.Id,stored.Request.Kind,doc.Number,doc.Party,doc.BusinessDate,stored.Currency,stored.Request.SourceAmount,remaining,carrying));
        }
        return result;
    }
}

public sealed partial class BankingService
{
    public Task<Guid> PostForeignAdvance(ForeignAdvanceCommand input) => Execute(input.OperationId,"accounting.write",Fx.AdvanceAction,input,async (db,actor) =>
    {
        actor.Demand("accounting.read"); if (!Enum.IsDefined(input.Kind) || input.Date != DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("Use today's date for a refundable currency advance."); await OpenDate(db,actor,input.Date); Amount(input.SourceAmount); Rules.Required(input.Reference,"Advance reference",160);
        var currency = Fx.Normalize(input.Currency) ?? throw new BusinessException("Choose a foreign currency."); await Fx.EnsureOpenCurrencyDate(db,actor.Branch,currency,input.Date); var rate = await Fx.Rate(db,actor.Branch,currency,input.Date); var carrying = AccountingEngine.Money(input.SourceAmount*rate); Amount(carrying);
        var party = await db.Parties.SingleOrDefaultAsync(p => p.Id == input.PartyId && p.Branch == actor.Branch) ?? throw new BusinessException("Choose a registered party."); var customer = input.Kind == TradeAdvanceKind.CustomerDeposit; if (customer ? party.Type == PartyType.Supplier : party.Type == PartyType.Customer) throw new BusinessException("The party type does not match the advance.");
        await Funds(db,input.FundsAccount); var config = (await Fx.BankConfigs(db,actor.Branch)).SingleOrDefault(b => b.AccountCode == input.FundsAccount); decimal funds;
        if (config is not null) { if (config.Currency != currency || input.LocalAmount != 0) throw new BusinessException("Use a bank in the advance currency, with no local amount override."); await Fx.EnsureBankDate(db,actor.Branch,input.FundsAccount,currency,input.Date); funds = customer ? carrying : (await Fx.BankWithdrawalCost(db,actor.Branch,input.FundsAccount,currency,input.Date,input.SourceAmount))!.Value; }
        else { Amount(input.LocalAmount); funds = input.LocalAmount; }
        var doc = Document(actor,input.OperationId,customer ? DocumentKind.MoneyReceipt : DocumentKind.Payment,input.Date,party.Name,carrying,customer ? "Foreign customer deposit" : "Foreign supplier advance",new Fx.AdvanceStored(input,currency,rate,carrying));
        var delta = customer ? funds-carrying : carrying-funds; var entries = new List<Posting> { new((Account)input.FundsAccount,customer ? funds : 0,customer ? 0 : funds),new((Account)(customer ? 2400 : 1300),customer ? 0 : carrying,customer ? carrying : 0) };
        if (delta > 0) entries.Add(new(Fx.Gains,0,delta)); if (delta < 0) entries.Add(new(Fx.Losses,-delta,0)); Journal(db,doc,entries.ToArray());
        if (customer) db.CustomerCredits.Add(new() { Id = Guid.NewGuid(),ReturnId = doc.Id,Amount = carrying }); else db.SupplierCredits.Add(new() { Id = Guid.NewGuid(),ReturnId = doc.Id,Amount = carrying }); return doc.Id;
    });
    public async Task<IReadOnlyList<ForeignAdvanceView>> ForeignAdvances() { var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync(); return await Fx.AdvancesAt(db,actor.Branch,DateOnly.FromDateTime(DateTime.UtcNow)); }
    private async Task<ForeignAdvanceUsePreview> AdvancePlan(BusinessDb db,Actor actor,ForeignAdvanceUseCommand input)
    {
        if (input.Date != DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("Use today's date for an advance allocation or refund."); await OpenDate(db,actor,input.Date); Amount(input.SourceAmount); Rules.Required(input.Reference,"Allocation or refund reference",160);
        if ((input.BillId is null) == (input.RefundAccount is null)) throw new BusinessException("Choose one bill allocation or cash/bank refund.");
        var advance = (await Fx.AdvancesAt(db,actor.Branch,input.Date)).SingleOrDefault(a => a.CreditId == input.CreditId) ?? throw new BusinessException("Active currency advance not found."); if (input.SourceAmount > advance.RemainingSource || advance.RemainingSource <= 0) throw new BusinessException("Currency amount exceeds the remaining advance.");
        await Fx.EnsureOpenCurrencyDate(db,actor.Branch,advance.Currency,input.Date); var used = input.SourceAmount == advance.RemainingSource ? advance.LocalCarrying : AccountingEngine.Money(advance.LocalCarrying*input.SourceAmount/advance.RemainingSource); Amount(used); var customer = advance.Kind == TradeAdvanceKind.CustomerDeposit; decimal destination; int account;
        if (input.BillId is Guid billId)
        {
            if (input.LocalRefundAmount != 0) throw new BusinessException("Bill allocation does not accept a refund override.");
            var bill = await db.Documents.SingleOrDefaultAsync(d => d.Id == billId && d.Branch == actor.Branch && !db.Documents.Any(r => r.Reverses == d.Id)) ?? throw new BusinessException("Active bill not found.");
            if (!bill.Party.Equals(advance.Party,StringComparison.OrdinalIgnoreCase) || bill.BusinessDate > input.Date || (customer ? !ServiceRules.IsReceivable(bill) : !(bill.Kind is DocumentKind.Receipt or DocumentKind.SupplierInvoice && bill.Settlement.StartsWith("Supplier payable",StringComparison.Ordinal)))) throw new BusinessException("Choose an outstanding bill for the same party and advance type.");
            var foreign = await db.ForeignBills.SingleOrDefaultAsync(f => f.DocumentId == bill.Id);
            if (foreign is not null) { if (foreign.Currency != advance.Currency) throw new BusinessException("The bill and advance must use the same foreign currency."); await Fx.EnsureBillDate(db,bill,foreign,input.Date); var balance = await Fx.OutstandingAt(db,bill,foreign,input.Date); if (input.SourceAmount > balance.Source || balance.Source <= 0) throw new BusinessException("Advance currency exceeds the foreign bill's outstanding units."); destination = input.SourceAmount == balance.Source ? balance.Carrying : AccountingEngine.Money(balance.Carrying*input.SourceAmount/balance.Source); }
            else { destination = AccountingEngine.Money(input.SourceAmount*await Fx.Rate(db,actor.Branch,advance.Currency,input.Date)); if (destination > await Outstanding(db,bill)) throw new BusinessException("Converted advance exceeds the local bill's outstanding amount."); }
            account = customer ? 1100 : 2000;
        }
        else
        {
            account = input.RefundAccount!.Value; await Funds(db,account); var config = (await Fx.BankConfigs(db,actor.Branch)).SingleOrDefault(b => b.AccountCode == account);
            if (config is not null) { if (config.Currency != advance.Currency || input.LocalRefundAmount != 0) throw new BusinessException("Refund through a bank in the advance currency, without a local override."); await Fx.EnsureBankDate(db,actor.Branch,account,config.Currency,input.Date); destination = customer ? (await Fx.BankWithdrawalCost(db,actor.Branch,account,advance.Currency,input.Date,input.SourceAmount))!.Value : AccountingEngine.Money(input.SourceAmount*await Fx.Rate(db,actor.Branch,advance.Currency,input.Date)); }
            else { Amount(input.LocalRefundAmount); destination = input.LocalRefundAmount; }
        }
        Amount(destination); var lines = new List<VoucherLine> { new(customer ? 2400 : 1300,customer ? used : 0,customer ? 0 : used),new(account,customer ? 0 : destination,customer ? destination : 0) }; var difference = customer ? used-destination : destination-used; if (difference > 0) lines.Add(new((int)Fx.Gains,0,difference)); if (difference < 0) lines.Add(new((int)Fx.Losses,-difference,0));
        return new(lines,advance.RemainingSource-input.SourceAmount,used,input.BillId is null ? 0 : destination,Fx.Fingerprint(new { Request = input with { ExpectedCalculation = null },Lines = lines,advance.RemainingSource,advance.LocalCarrying }));
    }
    public async Task<ForeignAdvanceUsePreview> PreviewForeignAdvanceUse(ForeignAdvanceUseCommand input) { var actor = await Authorize("accounting.write"); actor.Demand("accounting.read"); await using var db = await factory.CreateDbContextAsync(); return await AdvancePlan(db,actor,input); }
    public Task<Guid> UseForeignAdvance(ForeignAdvanceUseCommand input) => Execute(input.OperationId,"accounting.write",Fx.AdvanceUseAction,input,async (db,actor) =>
    {
        actor.Demand("accounting.read"); var preview = await AdvancePlan(db,actor,input); if (preview.CalculationFingerprint != input.ExpectedCalculation) throw new BusinessException("The advance, bill or rate changed. Review the calculation again."); var advance = (await Fx.AdvancesAt(db,actor.Branch,input.Date)).Single(a => a.CreditId == input.CreditId); var customer = advance.Kind == TradeAdvanceKind.CustomerDeposit;
        var doc = Document(actor,input.OperationId,customer ? DocumentKind.CustomerCreditUse : DocumentKind.SupplierCreditUse,input.Date,advance.Party,preview.CreditCarryingUsed,input.BillId is null ? "Foreign advance refund" : "Foreign advance allocation",new Fx.AdvanceUseStored(input,advance.Currency,preview));
        Journal(db,doc,preview.Lines.Select(l => new Posting((Account)l.AccountCode,l.Debit,l.Credit)).ToArray());
        if (customer) db.CustomerCreditUses.Add(new() { Id = Guid.NewGuid(),CreditId = input.CreditId,DocumentId = doc.Id,Amount = preview.CreditCarryingUsed }); else db.SupplierCreditUses.Add(new() { Id = Guid.NewGuid(),CreditId = input.CreditId,DocumentId = doc.Id,Amount = preview.CreditCarryingUsed });
        if (input.BillId is Guid bill) { db.BillAllocations.Add(new() { Id = Guid.NewGuid(),InvoiceId = bill,VoucherId = doc.Id,Amount = preview.BillCarryingUsed }); if (await db.ForeignBills.AnyAsync(f => f.DocumentId == bill)) db.ForeignSettlements.Add(new() { Id = Guid.NewGuid(),BillId = bill,VoucherId = doc.Id,SourceAmount = input.SourceAmount,CarryingAmount = preview.BillCarryingUsed,PaidAmount = preview.CreditCarryingUsed }); } return doc.Id;
    });
}
