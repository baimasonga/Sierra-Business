using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public static partial class Fx
{
    internal const string BankConfigAction = "fx.bank.configure", BankTradeAction = "fx.bank.trade", CollectionAction = "fx.collection", RevaluationAction = "fx.revalue", RevaluationCancelAction = "fx.revaluation.cancel";
    internal sealed record BankTradeStored(ForeignBankTradeCommand FxBankTrade, ForeignBankTradePreview Calculation);
    internal sealed record ValuationStored(ForeignRevaluationPreview FxRevaluation, Guid AutomaticReversalId, DateOnly ReversalDate, string Reason);
    internal sealed record ValuationReversalStored(Guid FxAutomaticReversalOf);
    internal sealed record ValuationCancelStored(Guid FxRevaluationCancellationOf, Guid CancelAutomaticId, string Reason);
    internal sealed record BankConfig(int AccountCode, string Currency);
    internal static string Fingerprint<T>(T input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
    internal static async Task<IReadOnlyList<BankConfig>> BankConfigs(BusinessDb db, string branch)
    {
        var events = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == BankConfigAction).ToListAsync();
        var configs = events.Select(o => JsonSerializer.Deserialize<ForeignBankCommand>(o.Payload)!).Select(c => new BankConfig(c.AccountCode, Normalize(c.Currency)!)).ToArray();
        if (configs.Select(c => c.AccountCode).Distinct().Count() != configs.Length) throw new BusinessException("Conflicting currency-bank configurations require review."); return configs;
    }
    internal static async Task<(decimal Source, decimal Carrying)> OutstandingAt(BusinessDb db, BusinessDocument bill, ForeignBill foreign, DateOnly date)
    {
        var docs = await db.Documents.AsNoTracking().Where(d => d.Branch == bill.Branch && d.BusinessDate <= date).ToDictionaryAsync(d => d.Id);
        var reversed = docs.Values.Where(d => d.Reverses != null).Select(d => d.Reverses!.Value).ToHashSet();
        if (bill.BusinessDate > date || reversed.Contains(bill.Id)) return (0, 0);
        bool Active(Guid id) => docs.ContainsKey(id) && !reversed.Contains(id);
        var allocated = (await db.BillAllocations.AsNoTracking().Where(a => a.InvoiceId == bill.Id).ToListAsync()).Where(a => Active(a.VoucherId)).Sum(a => a.Amount);
        var returned = (await db.ReturnAllocations.AsNoTracking().Where(r => r.OriginalDocumentId == bill.Id).ToListAsync()).Where(r => Active(r.ReturnDocumentId)).Sum(r => r.BillCredit);
        var paid = (await db.ForeignSettlements.AsNoTracking().Where(s => s.BillId == bill.Id).ToListAsync()).Where(s => Active(s.VoucherId)).Sum(s => s.SourceAmount);
        var sourceReturn = ServiceRules.IsReceivable(bill) ? bill.Total == 0 ? 0 : AccountingEngine.Money(foreign.SourceAmount * returned / bill.Total) : AccountingEngine.Money(returned / foreign.Rate);
        var carrying = bill.Total - allocated - returned;
        return carrying <= 0 ? (0, 0) : (Math.Max(0, foreign.SourceAmount - paid - sourceReturn), carrying);
    }
    internal static async Task<IReadOnlyList<ForeignBankView>> BankPositions(BusinessDb db, string branch, DateOnly date)
    {
        var configs = await BankConfigs(db, branch); if (configs.Count == 0) return [];
        var docs = await db.Documents.AsNoTracking().Where(d => d.Branch == branch && d.BusinessDate <= date).ToDictionaryAsync(d => d.Id);
        var reversed = docs.Values.Where(d => d.Reverses != null).Select(d => d.Reverses!.Value).ToHashSet();
        bool Active(Guid id) => docs.ContainsKey(id) && !reversed.Contains(id);
        var accounts = await db.LedgerAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id);
        var lines = (await db.Journal.AsNoTracking().Where(j => j.Branch == branch).ToListAsync()).Where(j => docs.ContainsKey(j.DocumentId)).ToArray();
        var trades = (await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == BankTradeAction).ToListAsync()).Where(o => Active(o.ResultId)).Select(o => JsonSerializer.Deserialize<ForeignBankTradeCommand>(o.Payload)!).ToList();
        var settlements = (await db.ForeignSettlements.AsNoTracking().Where(s => db.Documents.Any(d => d.Id == s.VoucherId && d.Branch == branch)).ToListAsync()).Where(s => Active(s.VoucherId)).ToList();
        var advanceDocuments = docs.Values.Where(d => d.Reverses is null && Active(d.Id) && (d.Settlement == "Foreign customer deposit" || d.Settlement == "Foreign supplier advance")).Select(d => JsonSerializer.Deserialize<AdvanceStored>(d.Detail)!).ToArray();
        var advanceRefunds = docs.Values.Where(d => d.Reverses is null && Active(d.Id) && d.Settlement == "Foreign advance refund").Select(d => (Id: d.Id, Stored: JsonSerializer.Deserialize<AdvanceUseStored>(d.Detail)!)).ToArray();
        var result = new List<ForeignBankView>();
        foreach (var bank in configs)
        {
            var source = trades.Where(t => t.BankAccount == bank.AccountCode).Sum(t => t.Kind == ForeignBankTradeKind.Deposit ? t.SourceAmount : -t.SourceAmount);
            source += trades.Where(t => t.Kind == ForeignBankTradeKind.Transfer && t.CounterAccount == bank.AccountCode).Sum(t => t.TargetSourceAmount);
            foreach (var s in settlements) { var fund = lines.Where(l => l.DocumentId == s.VoucherId && (int)l.Account == bank.AccountCode).ToArray(); source += fund.Any(l => l.Debit > 0) ? s.SourceAmount : fund.Any(l => l.Credit > 0) ? -s.SourceAmount : 0; }
            source += advanceDocuments.Where(a => a.Request.FundsAccount == bank.AccountCode).Sum(a => a.Request.Kind == TradeAdvanceKind.CustomerDeposit ? a.Request.SourceAmount : -a.Request.SourceAmount);
            foreach (var refund in advanceRefunds.Where(a => a.Stored.Request.RefundAccount == bank.AccountCode)) { var fund = lines.Where(l => l.DocumentId == refund.Id && (int)l.Account == bank.AccountCode).ToArray(); source += fund.Any(l => l.Debit > 0) ? refund.Stored.Request.SourceAmount : -refund.Stored.Request.SourceAmount; }
            result.Add(new(bank.AccountCode, accounts[bank.AccountCode].Name, bank.Currency, source, lines.Where(l => (int)l.Account == bank.AccountCode).Sum(l => l.Debit - l.Credit)));
        }
        return result;
    }
    internal static async Task EnsureOpenCurrencyDate(BusinessDb db, string branch, string currency, DateOnly date)
    {
        var valuations = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == RevaluationAction).ToListAsync();
        var cancellations = (await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == RevaluationCancelAction).ToListAsync()).Select(o => JsonSerializer.Deserialize<CancelForeignRevaluationCommand>(o.Payload)!.DocumentId).ToHashSet();
        if (valuations.Any(o => !cancellations.Contains(o.ResultId) && JsonSerializer.Deserialize<ForeignRevaluationCommand>(o.Payload) is { } v && v.Request.Date >= date && (v.Request.Currencies.Count == 0 || v.Request.Currencies.Any(c => Normalize(c) == currency)))) throw new BusinessException("Closing FX valuation locks this currency date and earlier dates. Cancel that valuation before changing its underlying history.");
    }
    internal static async Task EnsureBillDate(BusinessDb db, BusinessDocument bill, ForeignBill foreign, DateOnly date)
    {
        await EnsureOpenCurrencyDate(db, bill.Branch, foreign.Currency, date);
        var related = await db.Documents.AsNoTracking().Where(d => d.Branch == bill.Branch && (db.BillAllocations.Any(a => a.InvoiceId == bill.Id && a.VoucherId == d.Id) || db.ReturnAllocations.Any(a => a.OriginalDocumentId == bill.Id && a.ReturnDocumentId == d.Id))).ToListAsync();
        var ids = related.Select(d => d.Id).ToArray();
        var reversals = await db.Documents.AsNoTracking().Where(d => d.Branch == bill.Branch && d.Reverses != null && ids.Contains(d.Reverses.Value)).ToListAsync();
        if (related.Concat(reversals).Any(d => d.BusinessDate > date)) throw new BusinessException("Use a date on or after this foreign bill's later settlements and returns.");
    }
    internal static async Task EnsureBankDate(BusinessDb db, string branch, int account, string currency, DateOnly date)
    {
        await EnsureOpenCurrencyDate(db, branch, currency, date);
        if (await db.Journal.AnyAsync(j => j.Branch == branch && (int)j.Account == account && db.Documents.Any(d => d.Id == j.DocumentId && d.BusinessDate > date && !d.Detail.Contains("FxAutomaticReversalOf") && !d.Detail.Contains("CancelAutomaticReversalOf")))) throw new BusinessException("Use a date on or after the currency bank's later transactions.");
    }
    internal static async Task<decimal?> BankWithdrawalCost(BusinessDb db, string branch, int account, string currency, DateOnly date, decimal sourceAmount)
    {
        var config = (await BankConfigs(db, branch)).SingleOrDefault(b => b.AccountCode == account); if (config is null) return null;
        if (config.Currency != currency) throw new BusinessException("This bank account uses another currency.");
        await EnsureBankDate(db, branch, account, currency, date); var bank = (await BankPositions(db, branch, date)).Single(b => b.AccountCode == account);
        if (sourceAmount > bank.SourceBalance || bank.SourceBalance <= 0) throw new BusinessException("Not enough foreign currency in this bank account.");
        var carrying = sourceAmount == bank.SourceBalance ? bank.LocalCarrying : AccountingEngine.Money(bank.LocalCarrying * sourceAmount / bank.SourceBalance);
        if (carrying <= 0) throw new BusinessException("The currency amount is too small for a positive carrying value."); return carrying;
    }
    internal static async Task GuardCurrencyBankJournal(BusinessDb db)
    {
        if (db.SyncApplyingOrigin is not null) return;
        var lines = db.ChangeTracker.Entries<JournalLine>().Where(e => e.State == EntityState.Added && (int)e.Entity.Account >= 6000).Select(e => e.Entity).ToList(); if (lines.Count == 0) return;
        var actions = db.ChangeTracker.Entries<Operation>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
        foreach (var branch in lines.GroupBy(l => l.Branch))
        {
            var codes = (await BankConfigs(db, branch.Key)).Select(b => b.AccountCode).ToHashSet();
            if (branch.Any(l => codes.Contains((int)l.Account)) && !actions.Any(o => o.Branch == branch.Key && o.Action is BankTradeAction or CollectionAction or "fx.payment" or AdvanceAction or AdvanceUseAction or RevaluationAction or RevaluationCancelAction or "document.reverse")) throw new BusinessException("Use Foreign exchange for a currency bank transaction so its currency quantity and local journal stay together.");
        }
    }
    internal static async Task CheckForeignReversal(BusinessDb db, BusinessDocument original, DateOnly date)
    {
        if (!string.IsNullOrWhiteSpace(original.Detail)) try
        {
            using var detail = JsonDocument.Parse(original.Detail);
            if (detail.RootElement.ValueKind == JsonValueKind.Object && (detail.RootElement.TryGetProperty("FxRevaluation", out _) || detail.RootElement.TryGetProperty("FxAutomaticReversalOf", out _) || detail.RootElement.TryGetProperty("FxRevaluationCancellationOf", out _) || detail.RootElement.TryGetProperty("CancelAutomaticReversalOf", out _))) throw new BusinessException("Cancel paired FX valuations from Foreign exchange; do not reverse a single valuation journal.");
            if (detail.RootElement.ValueKind == JsonValueKind.Object && detail.RootElement.TryGetProperty("Currency", out var currency) && currency.ValueKind == JsonValueKind.String && Normalize(currency.GetString()) is string code) await EnsureOpenCurrencyDate(db, original.Branch, code, date);
        } catch (JsonException) { /* Legacy document text has no FX pairing metadata. */ }
        var foreignBill = await db.ForeignBills.SingleOrDefaultAsync(f => f.DocumentId == original.Id);
        if (foreignBill is not null) await EnsureBillDate(db, original, foreignBill, date);
        foreach (var currency in await (from s in db.ForeignSettlements join f in db.ForeignBills on s.BillId equals f.DocumentId where s.VoucherId == original.Id select f.Currency).Distinct().ToListAsync()) await EnsureOpenCurrencyDate(db, original.Branch, currency, date);
        var configs = await BankConfigs(db, original.Branch); var lines = await db.Journal.AsNoTracking().Where(j => j.DocumentId == original.Id).ToListAsync();
        var banks = configs.Where(c => lines.Any(j => (int)j.Account == c.AccountCode)).ToArray();
        foreach (var bank in banks)
        {
            await EnsureOpenCurrencyDate(db, original.Branch, bank.Currency, date);
            var originalTime = await db.Operations.Where(o => o.ResultId == original.Id).Select(o => (DateTimeOffset?)o.Time).FirstOrDefaultAsync() ?? DateTimeOffset.MinValue;
            // SQLite cannot order/compare DateTimeOffset values in SQL. Keep the scope filter
            // server-side, then compare the normalized timestamps in memory for every provider.
            var activity = await db.Operations.AsNoTracking().Where(o => o.Branch == original.Branch && o.ResultId != original.Id).Select(o => new { o.ResultId, o.Time }).ToListAsync();
            var later = activity.Where(o => o.Time > originalTime).Select(o => o.ResultId).ToList();
            if (await db.Journal.AnyAsync(j => j.Branch == original.Branch && (int)j.Account == bank.AccountCode && later.Contains(j.DocumentId) && db.Documents.Any(d => d.Id == j.DocumentId && d.Reverses == null && !d.Detail.Contains("FxRevaluation") && !d.Detail.Contains("FxAutomaticReversalOf") && !d.Detail.Contains("CancelAutomaticReversalOf")) && !db.Documents.Any(r => r.Reverses == j.DocumentId))) throw new BusinessException("Reverse later currency-bank movements first, so its units and carrying amount remain consistent.");
        }
    }
}

public sealed partial class PurchasingService
{
    public async Task<IReadOnlyList<ForeignReceivableView>> ForeignReceivables()
    {
        var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync(); var rows = await (from f in db.ForeignBills join d in db.Documents on f.DocumentId equals d.Id where d.Branch == actor.Branch && d.Kind == DocumentKind.Sale && d.Settlement == "Credit" && !db.Documents.Any(r => r.Reverses == d.Id) select new { f, d }).ToListAsync();
        var result = new List<ForeignReceivableView>(); foreach (var row in rows) { var balance = await Fx.OutstandingAt(db, row.d, row.f, DateOnly.FromDateTime(DateTime.UtcNow)); if (balance.Source > 0) result.Add(new(row.d.Id, row.d.Number, row.d.Party, row.d.BusinessDate, row.f.Currency, row.f.Rate, row.f.SourceAmount, balance.Source, balance.Carrying)); } return result;
    }
    public async Task<Guid> CollectForeign(ForeignCollectionCommand input)
    {
        var actor = await Authorize("accounting.read", "accounting.write");
        return await Commands.Run(factory, actor, input.OperationId, Fx.CollectionAction, input, async db =>
        {
            await OpenDate(db, actor, input.Date); Money2(input.SourceAmount, "Currency received"); Money2(input.LocalReceived, "Local carrying value received");
            var bill = await db.Documents.SingleOrDefaultAsync(d => d.Id == input.BillId && d.Branch == actor.Branch && d.Kind == DocumentKind.Sale && d.Settlement == "Credit" && !db.Documents.Any(r => r.Reverses == d.Id)) ?? throw new BusinessException("Choose an outstanding foreign customer invoice.");
            var foreign = await db.ForeignBills.SingleOrDefaultAsync(f => f.DocumentId == bill.Id) ?? throw new BusinessException("This is a local-currency bill."); if (input.Date < bill.BusinessDate) throw new BusinessException("Receipt cannot precede its invoice."); await Fx.EnsureBillDate(db, bill, foreign, input.Date);
            if (!await db.LedgerAccounts.AnyAsync(a => a.Id == input.FundsAccount && a.CashOrBank)) throw new BusinessException("Choose a cash or bank account.");
            var config = (await Fx.BankConfigs(db, actor.Branch)).SingleOrDefault(b => b.AccountCode == input.FundsAccount); if (config is not null) { if (config.Currency != foreign.Currency) throw new BusinessException("Choose a bank with the invoice currency, or local funds for converted receipts."); await Fx.EnsureBankDate(db, actor.Branch, config.AccountCode, config.Currency, input.Date); }
            var balance = await Fx.OutstandingAt(db, bill, foreign, input.Date); if (input.SourceAmount > balance.Source || balance.Source <= 0) throw new BusinessException("Receipt exceeds the invoice's outstanding currency amount.");
            var carrying = input.SourceAmount == balance.Source ? balance.Carrying : AccountingEngine.Money(balance.Carrying * input.SourceAmount / balance.Source); if (carrying <= 0) throw new BusinessException("Receipt is too small for a positive invoice allocation.");
            var difference = input.LocalReceived - carrying; var document = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-FXR-{input.OperationId:N}", Kind = DocumentKind.MoneyReceipt, Party = bill.Party, BusinessDate = input.Date, Total = input.LocalReceived, Settlement = $"Foreign invoice receipt ({foreign.Currency})", Detail = JsonSerializer.Serialize(new { Bill = bill.Id, foreign.Currency, input.SourceAmount, input.LocalReceived, Carrying = carrying, ExchangeGain = difference, Reference = Rules.Required(input.Reference, "Receipt reference", 100) }) };
            db.Documents.Add(document); db.BillAllocations.Add(new() { Id = Guid.NewGuid(), InvoiceId = bill.Id, VoucherId = document.Id, Amount = carrying }); db.ForeignSettlements.Add(new() { Id = Guid.NewGuid(), BillId = bill.Id, VoucherId = document.Id, SourceAmount = input.SourceAmount, CarryingAmount = carrying, PaidAmount = input.LocalReceived });
            var postings = new List<Posting> { new((Account)input.FundsAccount, input.LocalReceived, 0), new(Account.Receivables, 0, carrying) }; if (difference > 0) postings.Add(new(Fx.Gains, 0, difference)); else if (difference < 0) postings.Add(new(Fx.Losses, -difference, 0)); AddFxJournal(db, actor.Branch, document.Id, postings); return document.Id;
        });
    }
    public async Task<Guid> ConfigureForeignBank(ForeignBankCommand input)
    {
        var actor = await Authorize("accounting.read", "accounting.write"); return await Commands.Run(factory, actor, input.OperationId, Fx.BankConfigAction, input, async db =>
        {
            var currency = Fx.Normalize(input.Currency) ?? throw new BusinessException("Choose a foreign currency.");
            if (input.AccountCode < 6000 || !await db.LedgerAccounts.AnyAsync(a => a.Id == input.AccountCode && a.CashOrBank && a.Group == LedgerGroup.Asset)) throw new BusinessException("Create a custom cash/bank asset ledger first, with a code of 6000 or above.");
            if ((await Fx.BankConfigs(db, actor.Branch)).Any(b => b.AccountCode == input.AccountCode)) throw new BusinessException("This bank already has a fixed currency. Create another ledger for a different currency.");
            if (await db.Journal.AnyAsync(j => j.Branch == actor.Branch && (int)j.Account == input.AccountCode)) throw new BusinessException("Currency assignment requires an unused bank ledger, so earlier local money is not reinterpreted.");
            await Fx.Rate(db, actor.Branch, currency, DateOnly.FromDateTime(DateTime.UtcNow)); return Guid.NewGuid();
        });
    }
    public async Task<IReadOnlyList<ForeignBankView>> ForeignBanks(DateOnly date) { var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync(); return await Fx.BankPositions(db, actor.Branch, date); }
    private static void AddFxJournal(BusinessDb db, string branch, Guid document, List<Posting> postings) { foreach (var p in AccountingEngine.Balanced(postings)) db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = document, Branch = branch, Account = p.Account, Debit = p.Debit, Credit = p.Credit }); }
    private async Task<ForeignBankTradePreview> BankTradePlan(BusinessDb db, Actor actor, ForeignBankTradeCommand input)
    {
        await OpenDate(db, actor, input.Date); Money2(input.SourceAmount, "Currency amount"); Rules.Required(input.Reference, "Bank transaction reference", 160);
        if (!Enum.IsDefined(input.Kind) || input.BankAccount == input.CounterAccount) throw new BusinessException("Choose a bank transaction and a different counter account.");
        var positions = await Fx.BankPositions(db, actor.Branch, input.Date); var bank = positions.SingleOrDefault(b => b.AccountCode == input.BankAccount) ?? throw new BusinessException("Choose a configured currency bank."); await Fx.EnsureBankDate(db, actor.Branch, bank.AccountCode, bank.Currency, input.Date);
        var counter = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.CounterAccount) ?? throw new BusinessException("Counter account not found."); var target = positions.SingleOrDefault(b => b.AccountCode == input.CounterAccount); var lines = new List<VoucherLine>(); decimal used = 0, targetAfter = 0, delta;
        if (input.Kind == ForeignBankTradeKind.Deposit)
        {
            Money2(input.LocalAmount, "NLe deposit value"); if (target is not null || (!counter.CashOrBank && (counter.Control || counter.Group is not LedgerGroup.Income and not LedgerGroup.Equity))) throw new BusinessException("Fund from local cash/bank, income or equity; use transfer between currency banks.");
            lines.Add(new(bank.AccountCode, input.LocalAmount, 0)); lines.Add(new(counter.Id, 0, input.LocalAmount)); delta = input.SourceAmount;
        }
        else
        {
            used = (await Fx.BankWithdrawalCost(db, actor.Branch, bank.AccountCode, bank.Currency, input.Date, input.SourceAmount))!.Value; delta = -input.SourceAmount;
            decimal destination;
            if (input.Kind == ForeignBankTradeKind.Transfer)
            {
                if (target is null) throw new BusinessException("Choose another configured currency bank."); Money2(input.TargetSourceAmount, "Target currency amount"); await Fx.EnsureBankDate(db, actor.Branch, target.AccountCode, target.Currency, input.Date);
                if (target.Currency == bank.Currency && input.TargetSourceAmount != input.SourceAmount) throw new BusinessException("A same-currency transfer preserves its currency quantity; record any fee separately.");
                destination = target.Currency == bank.Currency ? used : AccountingEngine.Money(input.TargetSourceAmount * await Fx.Rate(db, actor.Branch, target.Currency, input.Date)); Money2(destination, "Target carrying value"); targetAfter = target.SourceBalance + input.TargetSourceAmount;
            }
            else { Money2(input.LocalAmount, "NLe received or expense value"); if (target is not null || (!counter.CashOrBank && (counter.Control || counter.Group != LedgerGroup.Expense))) throw new BusinessException("Withdraw to local cash/bank or a permitted expense account."); destination = input.LocalAmount; }
            lines.Add(new(bank.AccountCode, 0, used)); lines.Add(new(counter.Id, destination, 0)); var difference = destination - used;
            if (difference > 0) lines.Add(new((int)Fx.Gains, 0, difference)); else if (difference < 0) lines.Add(new((int)Fx.Losses, -difference, 0));
        }
        return new(lines, bank.SourceBalance + delta, used, targetAfter, Fx.Fingerprint(new { Request = input with { ExpectedCalculation = null }, Lines = lines, Source = bank, Target = target }));
    }
    public async Task<ForeignBankTradePreview> PreviewForeignBankTrade(ForeignBankTradeCommand input) { var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync(); return await BankTradePlan(db, actor, input); }
    public async Task<Guid> PostForeignBankTrade(ForeignBankTradeCommand input)
    {
        var actor = await Authorize("accounting.read", "accounting.write"); return await Commands.Run(factory, actor, input.OperationId, Fx.BankTradeAction, input, async db =>
        {
            var plan = await BankTradePlan(db, actor, input); if (string.IsNullOrEmpty(input.ExpectedCalculation) || input.ExpectedCalculation != plan.CalculationFingerprint) throw new BusinessException("Review the current currency-bank calculation before posting.");
            var d = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-FXB-{input.OperationId:N}", Kind = DocumentKind.Journal, Party = input.Reference, BusinessDate = input.Date, Total = plan.Lines.Sum(l => l.Debit), Settlement = "Currency bank " + input.Kind, Detail = JsonSerializer.Serialize(new Fx.BankTradeStored(input, plan)) }; db.Documents.Add(d); AddFxJournal(db, actor.Branch, d.Id, plan.Lines.Select(l => new Posting((Account)l.AccountCode, l.Debit, l.Credit)).ToList()); return d.Id;
        });
    }
}
