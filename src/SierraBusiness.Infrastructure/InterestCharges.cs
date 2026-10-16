using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed partial class FinanceService
{
    private const string InterestChargeAction = "accounts.interest.charge";

    private static async Task<InterestChargeView[]> InterestChargesFor(BusinessDb db, string branch)
    {
        var posted = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == InterestChargeAction).Select(o => new { o.ResultId, o.Payload }).ToListAsync();
        if (posted.Count == 0) return [];
        var ids = posted.Select(p => p.ResultId).ToArray();
        var commands = posted.Select(p => JsonSerializer.Deserialize<InterestChargeCommand>(p.Payload)!).ToArray();
        var billIds = commands.Select(c => c.BillId).ToArray();
        var documents = await db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id) || billIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id);
        var reversed = await db.Documents.AsNoTracking().Where(d => d.Reverses != null && ids.Contains(d.Reverses!.Value)).Select(d => d.Reverses!.Value).ToListAsync();
        return posted.Zip(commands).Where(x => documents.ContainsKey(x.First.ResultId)).Select(x =>
        {
            var doc = documents[x.First.ResultId]; using var detail = JsonDocument.Parse(doc.Detail);
            return new InterestChargeView(doc.Id, doc.Number, x.Second.BillId, documents.GetValueOrDefault(x.Second.BillId)?.Number ?? "", doc.Party, doc.BusinessDate, x.Second.Request.From, x.Second.Request.To, detail.RootElement.GetProperty("Calculated").GetDecimal(), doc.Total, reversed.Contains(doc.Id));
        }).OrderByDescending(v => v.Date).ThenBy(v => v.Number).ToArray();
    }

    public async Task<IReadOnlyList<InterestChargeView>> InterestCharges() { var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync(); return await InterestChargesFor(db, actor.Branch); }

    public async Task<Guid> PostInterestCharge(InterestChargeCommand input)
    {
        if (input?.Request is null) throw new BusinessException("Calculate the interest worksheet before charging it.");
        if (!input.Request.Receivable) throw new BusinessException("Only customer interest can be billed. Record supplier interest as a reviewed payment or journal.");
        // Recalculated before entering the transaction, so the charge reflects the current settlements and reversals.
        var worksheet = await Interest(input.Request);
        return await Execute(input.OperationId, InterestChargeAction, input, async (db, actor) =>
        {
            Rules.Required(input.Reason, "Review reason", 300);
            Money(input.Amount, "Interest charge");
            if (input.PaymentDays is < 0 or > 365) throw new BusinessException("Allow 0–365 days for payment of the interest charge.");
            await OpenDate(db, actor.Branch, input.Date);
            if (input.Date < input.Request.To) throw new BusinessException("Charge interest on or after the end of the period it covers.");
            if (Controls.Backdated(await Controls.Current(db, actor.Branch), input.Date) is not null) throw new BusinessException("This charge date requires backdating approval. Charge it on an allowed current date.");
            var row = worksheet.Rows.SingleOrDefault(r => r.DocumentId == input.BillId) ?? throw new BusinessException("This bill has no interest in the recalculated worksheet. Recalculate and review it again.");
            if (input.Amount > row.Interest) throw new BusinessException($"The charge cannot exceed the recalculated interest of {row.Interest:N2}. Recalculate if settlements changed.");
            var bill = await db.Documents.SingleAsync(d => d.Id == input.BillId);
            if (await db.Documents.AnyAsync(d => d.Reverses == bill.Id)) throw new BusinessException("The bill was reversed; it cannot carry an interest charge.");
            var earlier = await InterestChargesFor(db, actor.Branch);
            if (earlier.Any(c => !c.Reversed && c.BillId == bill.Id && c.From <= input.Request.To && input.Request.From <= c.To)) throw new BusinessException("Interest for part of this period has already been charged on this bill. Start the worksheet after the last charged period.");
            var income = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.IncomeAccount);
            if (income is null || income.Group != LedgerGroup.Income || income.Control) throw new BusinessException("Choose a custom income ledger for interest charged.");
            var doc = new BusinessDocument
            {
                Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-INT-{input.OperationId:N}", Kind = DocumentKind.InterestCharge, BusinessDate = input.Date, DueDate = input.Date.AddDays(input.PaymentDays),
                Party = bill.Party, Total = input.Amount, Settlement = "Credit",
                Detail = JsonSerializer.Serialize(new { Source = bill.Id, SourceNumber = bill.Number, input.Request.From, input.Request.To, input.Request.AnnualPercent, input.Request.GraceDays, Basis = input.Request.Basis.ToString(), row.ChargedDays, Calculated = row.Interest, input.Reason })
            };
            db.Documents.Add(doc);
            db.Journal.AddRange(new JournalLine { Id = Guid.NewGuid(), Branch = actor.Branch, DocumentId = doc.Id, Account = Account.Receivables, Debit = input.Amount }, new JournalLine { Id = Guid.NewGuid(), Branch = actor.Branch, DocumentId = doc.Id, Account = (Account)income.Id, Credit = input.Amount });
            return doc.Id;
        });
    }
}
