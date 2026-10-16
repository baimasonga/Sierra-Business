using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed partial class StatementsService
{
    public async Task<IReadOnlyList<IntercompanyProposal>> ReviewIntercompany(IntercompanyReview input)
    {
        if (input is null || input.Pairs is null || input.Pairs.Count is < 1 or > 100 || input.Pairs.Any(p => p is null || string.IsNullOrWhiteSpace(p.Reference))) throw new BusinessException("Review between one and one hundred reciprocal account pairs with references.");
        await Consolidate(input.Report);
        // Compare in NLe after translation, so a foreign member's balance is matched at the rates used in the report.
        var members = Prepare(input.Report).Members;
        if (input.Pairs.Select(p => p.Reference.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Pairs.Count) throw new BusinessException("Each proposed elimination needs a unique reference.");
        var used = new HashSet<(Guid, int)>(); var proposals = new List<IntercompanyProposal>();
        foreach (var pair in input.Pairs)
        {
            Rules.Required(pair.Reference, "Elimination reference", 80); Rules.Required(pair.Reason, "Elimination reason", 200);
            if (pair.DebitSource == pair.CreditSource || !used.Add((pair.DebitSource, pair.DebitAccount)) || !used.Add((pair.CreditSource, pair.CreditAccount))) throw new BusinessException("Choose different companies and use each reciprocal account once in a review.");
            var debit = members.SingleOrDefault(m => m.SourceId == pair.DebitSource)?.Accounts.SingleOrDefault(a => a.Code == pair.DebitAccount) ?? throw new BusinessException("Debit member account not found.");
            var credit = members.SingleOrDefault(m => m.SourceId == pair.CreditSource)?.Accounts.SingleOrDefault(a => a.Code == pair.CreditAccount) ?? throw new BusinessException("Credit member account not found.");
            var pl = input.Report.Statement == "Profit and loss";
            if (pl ? debit.Nature != LedgerGroup.Expense || credit.Nature != LedgerGroup.Income : debit.Nature != LedgerGroup.Asset || credit.Nature != LedgerGroup.Liability) throw new BusinessException("Pair expense with income for profit and loss, or asset with liability for balance sheet.");
            var d = pl ? debit.PeriodBalance : debit.ClosingBalance; var c = pl ? credit.PeriodBalance : credit.ClosingBalance;
            var debitCode = input.Report.Mappings.SingleOrDefault(m => m.SourceId == pair.DebitSource && m.SourceCode == pair.DebitAccount)?.GroupCode ?? debit.Code;
            var creditCode = input.Report.Mappings.SingleOrDefault(m => m.SourceId == pair.CreditSource && m.SourceCode == pair.CreditAccount)?.GroupCode ?? credit.Code;
            var matched = d > 0 && c < 0 && d == -c && debitCode != creditCode;
            var adjustment = matched ? new ConsolidationAdjustment(pair.Reference.Trim(), input.Report.Members[0].To, pair.Reason.Trim(), [new(creditCode, d, 0), new(debitCode, 0, d)]) : null;
            proposals.Add(new(pair.Reference, matched, d, c, matched ? "Exact reciprocal balances matched. Review before adding this elimination." : "Balances do not match exactly; resolve differences or enter a reviewed manual adjustment.", adjustment));
        }
        return proposals;
    }

    public async Task<IntercompanyLedger> ExportIntercompanyLedger(string company, string counterparty, DateOnly from, DateOnly to)
    {
        var actor = await Authorize(); company = Rules.Required(company,"Company name",120); counterparty = Rules.Required(counterparty,"Counterparty name",160);
        if (from > to || from.Year < 2000 || to.DayNumber - from.DayNumber > 3660) throw new BusinessException("Choose a period of at most ten years, starting in 2000 or later.");
        await using var db = await factory.CreateDbContextAsync(); await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var origin = await db.IntegritySeals.AsNoTracking().OrderBy(s => s.Sequence).Select(s => s.Hash).FirstOrDefaultAsync() ?? throw new BusinessException("Initialize the company's protected history before exporting.");
        var source = new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(origin + "\n" + actor.Branch)).AsSpan(0, 16));
        var name = counterparty.ToUpperInvariant();
        var docs = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && d.Party.ToUpper() == name && d.BusinessDate >= from && d.BusinessDate <= to).ToListAsync();
        var ids = docs.Select(d => d.Id).ToArray();
        var effects = (await db.Journal.AsNoTracking().Where(j => ids.Contains(j.DocumentId) && (j.Account == Account.Receivables || j.Account == Account.Payables)).ToListAsync()).GroupBy(j => j.DocumentId).ToDictionary(g => g.Key,g => g.Sum(j => j.Debit-j.Credit));
        await transaction.CommitAsync();
        if (docs.Count > 100000) throw new BusinessException("Export at most 100,000 transactions; choose a shorter period.");
        var rows = docs.Where(d => effects.GetValueOrDefault(d.Id) != 0).OrderBy(d => d.BusinessDate).ThenBy(d => d.Number).Select(d => new IntercompanyTransaction(d.Number,d.BusinessDate,d.Kind.ToString(),effects[d.Id])).ToArray();
        return new(1,source,company,actor.Branch,counterparty,from,to,DateTimeOffset.UtcNow,rows);
    }

    public async Task<IntercompanyMatchReport> MatchIntercompanyTransactions(IntercompanyMatchRequest input)
    {
        await Authorize();
        if (input?.Left is null || input.Right is null || input.DateToleranceDays is < 0 or > 31) throw new BusinessException("Load two intercompany ledgers and allow 0–31 days between matching documents.");
        foreach (var ledger in new[] { input.Left,input.Right })
        {
            Rules.Required(ledger.Company,"Company name",120); Rules.Required(ledger.Counterparty,"Counterparty name",160);
            if (ledger.Version != 1 || ledger.SourceId == Guid.Empty || ledger.Transactions is null || ledger.Transactions.Count > 100000 || ledger.Transactions.Any(t => t is null || string.IsNullOrWhiteSpace(t.Number) || t.Amount == 0 || AccountingEngine.Money(t.Amount) != t.Amount || Math.Abs(t.Amount) > 1_000_000_000_000m || t.Date < ledger.From || t.Date > ledger.To))
                throw new BusinessException("Use valid version 1 intercompany ledgers exported from Rokela.");
        }
        if (input.Left.SourceId == input.Right.SourceId) throw new BusinessException("Load the ledgers of two different companies.");
        // Each export must name the other as its counterparty; otherwise unrelated ledgers could match by coincidence.
        static bool Same(string a, string b) => a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);
        if (!Same(input.Left.Counterparty, input.Right.Company) || !Same(input.Right.Counterparty, input.Left.Company)) throw new BusinessException($"These exports are not reciprocal: {input.Left.Company} is with {input.Left.Counterparty} and {input.Right.Company} is with {input.Right.Counterparty}. Export each company with the other's name as its company and counterparty.");
        if (input.Left.From != input.Right.From || input.Left.To != input.Right.To) throw new BusinessException("Export both ledgers for the same period.");
        var open = input.Right.Transactions.ToList(); var matched = new List<IntercompanyTransactionMatch>(); var leftOnly = new List<IntercompanyTransaction>();
        foreach (var left in input.Left.Transactions.OrderBy(t => t.Date).ThenBy(t => t.Number))
        {
            var candidate = open.Where(r => r.Amount == -left.Amount && Math.Abs(r.Date.DayNumber - left.Date.DayNumber) <= input.DateToleranceDays).OrderBy(r => Math.Abs(r.Date.DayNumber - left.Date.DayNumber)).ThenBy(r => r.Date).ThenBy(r => r.Number).FirstOrDefault();
            if (candidate is null) { leftOnly.Add(left); continue; }
            open.Remove(candidate); matched.Add(new(left,candidate,Math.Abs(candidate.Date.DayNumber - left.Date.DayNumber)));
        }
        return new(matched,leftOnly,open.OrderBy(t => t.Date).ThenBy(t => t.Number).ToArray(),input.Left.Transactions.Sum(t => t.Amount),input.Right.Transactions.Sum(t => t.Amount));
    }
}
