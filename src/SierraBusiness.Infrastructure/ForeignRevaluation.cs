using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed partial class PurchasingService
{
    private async Task<ForeignRevaluationPreview> RevaluationPlan(BusinessDb db, Actor actor, ForeignRevaluationRequest input)
    {
        if (input.Date.Year < 2000 || input.Date > DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("Choose a past or current FX reporting date.");
        if (input.Currencies is null || input.Currencies.Count > 30) throw new BusinessException("Choose at most 30 currency codes, or leave the list empty for all open currencies.");
        var currencies = input.Currencies.Select(c => Fx.Normalize(c) ?? throw new BusinessException("Use foreign currency codes for valuation.")).ToArray();
        if (currencies.Distinct().Count() != currencies.Length) throw new BusinessException("Choose each currency only once.");
        var normalized = input with { Currencies = currencies.Order().ToList() }; var rows = new List<ForeignValuationRow>();
        var bills = await (from f in db.ForeignBills join d in db.Documents on f.DocumentId equals d.Id where d.Branch == actor.Branch && d.BusinessDate <= input.Date select new { f, d }).ToListAsync();
        var cancelled = (await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == Fx.RevaluationCancelAction).ToListAsync()).Select(o => JsonSerializer.Deserialize<CancelForeignRevaluationCommand>(o.Payload)!.DocumentId).ToHashSet();
        var previous = (await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && d.Kind == DocumentKind.Journal && d.BusinessDate == input.Date).ToListAsync()).Where(d => !cancelled.Contains(d.Id)).Select(d => { try { return JsonSerializer.Deserialize<Fx.ValuationStored>(d.Detail); } catch (JsonException) { return null; } }).Where(v => v?.FxRevaluation is not null).ToArray();
        foreach (var pair in bills)
        {
            if (currencies.Length > 0 && !currencies.Contains(pair.f.Currency)) continue;
            var balance = await Fx.OutstandingAt(db, pair.d, pair.f, input.Date); if (balance.Source <= 0 || balance.Carrying <= 0) continue;
            var customer = ServiceRules.IsReceivable(pair.d); var rate = await Fx.Rate(db, actor.Branch, pair.f.Currency, input.Date); var target = AccountingEngine.Money(balance.Source * rate);
            var key = "bill:" + pair.d.Id.ToString("N"); var priorChange = previous.SelectMany(v => v!.FxRevaluation.Rows).Where(r => r.Key == key).Sum(r => r.ClosingValue - r.Carrying);
            var carrying = balance.Carrying + priorChange;
            rows.Add(new(key, customer ? "Customer receivable" : "Supplier payable", pair.d.Id, customer ? 1100 : 2000, pair.d.Number, pair.d.Party, pair.f.Currency, balance.Source, carrying, rate, target, customer ? target - carrying : carrying - target));
        }
        var documents = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && d.BusinessDate <= input.Date).ToDictionaryAsync(d => d.Id);
        foreach (var advance in await Fx.AdvancesAt(db,actor.Branch,input.Date))
        {
            if (advance.RemainingSource <= 0 || currencies.Length > 0 && !currencies.Contains(advance.Currency)) continue;
            var rate = await Fx.Rate(db,actor.Branch,advance.Currency,input.Date); var target = AccountingEngine.Money(advance.RemainingSource*rate); var key = "advance:"+advance.DocumentId.ToString("N"); var carrying = advance.LocalCarrying; var customer = advance.Kind == TradeAdvanceKind.CustomerDeposit;
            rows.Add(new(key,customer ? "Refundable customer deposit" : "Refundable supplier advance",advance.DocumentId,customer ? 2400 : 1300,advance.Number,advance.Party,advance.Currency,advance.RemainingSource,carrying,rate,target,customer ? carrying-target : target-carrying));
        }
        var reversed = documents.Values.Where(d => d.Reverses != null).Select(d => d.Reverses!.Value).ToHashSet();
        bool Active(Guid id) => documents.ContainsKey(id) && !reversed.Contains(id);
        var matches = await db.InvoiceMatches.AsNoTracking().Where(m => db.Documents.Any(d => d.Id == m.ReceiptId && d.Branch == actor.Branch)).ToListAsync();
        var returns = await db.ReturnAllocations.AsNoTracking().Where(r => db.Documents.Any(d => d.Id == r.OriginalDocumentId && d.Branch == actor.Branch)).ToListAsync();
        var grni = (await db.Journal.AsNoTracking().Where(j => j.Branch == actor.Branch && j.Account == Grni.Account).ToListAsync()).Where(j => Active(j.DocumentId)).ToArray();
        foreach (var receipt in documents.Values.Where(d => Grni.IsAwaiting(d) && Active(d.Id)))
        {
            using var detail = JsonDocument.Parse(receipt.Detail); if (!detail.RootElement.TryGetProperty("Currency", out var c) || c.ValueKind != JsonValueKind.String || Fx.Normalize(c.GetString()) is not string currency || currencies.Length > 0 && !currencies.Contains(currency)) continue;
            var creditedReturnIds = returns.Where(r => r.OriginalDocumentId == receipt.Id && Active(r.ReturnDocumentId)).Select(r => r.ReturnDocumentId).Distinct().ToHashSet();
            var baseCarrying = receipt.Total - matches.Where(m => m.ReceiptId == receipt.Id && Active(m.InvoiceId)).Sum(m => m.GoodsCleared) - grni.Where(j => creditedReturnIds.Contains(j.DocumentId)).Sum(j => j.Debit - j.Credit); if (baseCarrying <= 0 || receipt.Total <= 0) continue;
            var sourceTotal = detail.RootElement.GetProperty("SourceTotal").GetDecimal(); var source = AccountingEngine.Money(sourceTotal * baseCarrying / receipt.Total); if (source <= 0) continue;
            var key = "grni:" + receipt.Id.ToString("N"); var carrying = baseCarrying + previous.SelectMany(v => v!.FxRevaluation.Rows).Where(r => r.Key == key).Sum(r => r.ClosingValue - r.Carrying); var rate = await Fx.Rate(db, actor.Branch, currency, input.Date); var target = AccountingEngine.Money(source * rate);
            rows.Add(new(key, "Goods received not invoiced", receipt.Id, (int)Grni.Account, receipt.Number, receipt.Party, currency, source, carrying, rate, target, carrying - target));
        }
        foreach (var bank in await Fx.BankPositions(db, actor.Branch, input.Date))
        {
            if (bank.SourceBalance <= 0 || currencies.Length > 0 && !currencies.Contains(bank.Currency)) continue;
            var rate = await Fx.Rate(db, actor.Branch, bank.Currency, input.Date); var target = AccountingEngine.Money(bank.SourceBalance * rate);
            rows.Add(new("bank:" + bank.AccountCode, "Currency bank", null, bank.AccountCode, bank.Name, "", bank.Currency, bank.SourceBalance, bank.LocalCarrying, rate, target, target - bank.LocalCarrying));
        }
        var ordered = rows.OrderBy(r => r.Key).ToArray();
        return new(normalized, ordered, ordered.Sum(r => r.Gain), Fx.Fingerprint(new { Request = normalized, Rows = ordered }));
    }
    public async Task<ForeignRevaluationPreview> PreviewForeignRevaluation(ForeignRevaluationRequest input) { var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync(); return await RevaluationPlan(db, actor, input); }
    public async Task<Guid> PostForeignRevaluation(ForeignRevaluationCommand input)
    {
        var actor = await Authorize("accounting.read", "accounting.write");
        return await Commands.Run(factory, actor, input.OperationId, Fx.RevaluationAction, input, async db =>
        {
            Rules.Required(input.Reason, "Valuation reason", 160); var preview = await RevaluationPlan(db, actor, input.Request);
            await OpenDate(db, actor, input.Request.Date);
            if (input.ExpectedCalculation != preview.CalculationFingerprint) throw new BusinessException("FX positions or rates changed. Preview the closing valuation again before posting.");
            if (!preview.Rows.Any(r => r.Gain != 0)) throw new BusinessException("No exchange valuation difference needs posting.");
            foreach (var currency in preview.Rows.Select(r => r.Currency).Distinct()) await Fx.EnsureOpenCurrencyDate(db, actor.Branch, currency, input.Request.Date);
            var id = Guid.NewGuid(); var automatic = Guid.NewGuid(); var reversalDate = input.Request.Date.AddDays(1);
            var doc = new BusinessDocument { Id = id, Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-FXV-{input.OperationId:N}", Kind = DocumentKind.Journal, Party = input.Reason, BusinessDate = input.Request.Date, Total = 0, Settlement = "Closing FX valuation", Detail = JsonSerializer.Serialize(new Fx.ValuationStored(preview, automatic, reversalDate, input.Reason)) };
            var reverse = new BusinessDocument { Id = automatic, Branch = actor.Branch, Number = doc.Number + "-AUTO", Kind = DocumentKind.Journal, Party = input.Reason, BusinessDate = reversalDate, Settlement = "Automatic FX valuation reversal", Detail = JsonSerializer.Serialize(new Fx.ValuationReversalStored(id)) };
            var postings = new List<Posting>();
            foreach (var row in preview.Rows.Where(r => r.Gain != 0))
            {
                var delta = row.ClosingValue - row.Carrying; var liability = row.AccountCode is 2000 or 2050 or 2400;
                var debit = liability ? Math.Max(0, -delta) : Math.Max(0, delta); var credit = liability ? Math.Max(0, delta) : Math.Max(0, -delta);
                postings.Add(new((Account)row.AccountCode, debit, credit)); if (row.Gain > 0) postings.Add(new(Fx.Gains, 0, row.Gain)); else postings.Add(new(Fx.Losses, -row.Gain, 0));
            }
            doc.Total = reverse.Total = postings.Sum(p => p.Debit); db.Documents.AddRange(doc, reverse); AddFxJournal(db, actor.Branch, doc.Id, postings); AddFxJournal(db, actor.Branch, reverse.Id, postings.Select(p => new Posting(p.Account, p.Credit, p.Debit)).ToList()); return id;
        });
    }
    public async Task<IReadOnlyList<ForeignRevaluationView>> ForeignRevaluations()
    {
        var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync();
        var ids = await db.Operations.Where(o => o.Branch == actor.Branch && o.Action == Fx.RevaluationAction).Select(o => o.ResultId).ToListAsync();
        var cancelled = (await db.Operations.Where(o => o.Branch == actor.Branch && o.Action == Fx.RevaluationCancelAction).ToListAsync()).Select(o => JsonSerializer.Deserialize<CancelForeignRevaluationCommand>(o.Payload)!.DocumentId).ToHashSet();
        return (await db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id) && d.Branch == actor.Branch).OrderByDescending(d => d.BusinessDate).ToListAsync()).Select(d => { var value = JsonSerializer.Deserialize<Fx.ValuationStored>(d.Detail)!; return new ForeignRevaluationView(d.Id, d.Number, d.BusinessDate, value.ReversalDate, value.FxRevaluation.NetGain, cancelled.Contains(d.Id), value.Reason); }).ToArray();
    }
    public async Task<Guid> CancelForeignRevaluation(CancelForeignRevaluationCommand input)
    {
        var actor = await Authorize("accounting.read", "accounting.write");
        return await Commands.Run(factory, actor, input.OperationId, Fx.RevaluationCancelAction, input, async db =>
        {
            Rules.Required(input.Reason, "Cancellation reason", 160);
            var original = await db.Documents.SingleOrDefaultAsync(d => d.Id == input.DocumentId && d.Branch == actor.Branch && d.Settlement == "Closing FX valuation") ?? throw new BusinessException("FX valuation not found."); await OpenDate(db, actor, original.BusinessDate);
            if ((await db.Operations.Where(o => o.Branch == actor.Branch && o.Action == Fx.RevaluationCancelAction).ToListAsync()).Any(o => JsonSerializer.Deserialize<CancelForeignRevaluationCommand>(o.Payload)!.DocumentId == original.Id)) throw new BusinessException("That valuation was already cancelled.");
            var value = JsonSerializer.Deserialize<Fx.ValuationStored>(original.Detail)!;
            var automatic = await db.Documents.SingleAsync(d => d.Id == value.AutomaticReversalId && d.Branch == actor.Branch);
            var cancel = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-FXC-{input.OperationId:N}", Kind = DocumentKind.Journal, Party = input.Reason, BusinessDate = original.BusinessDate, Total = original.Total, Settlement = "Cancelled FX closing valuation" };
            var cancelAutomatic = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = cancel.Number + "-AUTO", Kind = DocumentKind.Journal, Party = input.Reason, BusinessDate = automatic.BusinessDate, Total = automatic.Total, Settlement = "Cancelled automatic FX reversal", Detail = JsonSerializer.Serialize(new { CancelAutomaticReversalOf = automatic.Id }) };
            cancel.Detail = JsonSerializer.Serialize(new Fx.ValuationCancelStored(original.Id, cancelAutomatic.Id, input.Reason)); db.Documents.AddRange(cancel, cancelAutomatic);
            var originalLines = await db.Journal.Where(j => j.DocumentId == original.Id).ToListAsync(); AddFxJournal(db, actor.Branch, cancel.Id, originalLines.Select(j => new Posting(j.Account, j.Credit, j.Debit)).ToList());
            var automaticLines = await db.Journal.Where(j => j.DocumentId == automatic.Id).ToListAsync(); AddFxJournal(db, actor.Branch, cancelAutomatic.Id, automaticLines.Select(j => new Posting(j.Account, j.Credit, j.Debit)).ToList()); return cancel.Id;
        });
    }
}
