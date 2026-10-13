using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed partial class StatementsService
{
    public async Task<IReadOnlyList<IntercompanyProposal>> ReviewIntercompany(IntercompanyReview input)
    {
        if (input is null || input.Pairs is null || input.Pairs.Count is < 1 or > 100 || input.Pairs.Any(p => p is null || string.IsNullOrWhiteSpace(p.Reference))) throw new BusinessException("Review between one and one hundred reciprocal account pairs with references.");
        await Consolidate(input.Report);
        if (input.Pairs.Select(p => p.Reference.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Pairs.Count) throw new BusinessException("Each proposed elimination needs a unique reference.");
        var used = new HashSet<(Guid, int)>(); var proposals = new List<IntercompanyProposal>();
        foreach (var pair in input.Pairs)
        {
            Rules.Required(pair.Reference, "Elimination reference", 80); Rules.Required(pair.Reason, "Elimination reason", 200);
            if (pair.DebitSource == pair.CreditSource || !used.Add((pair.DebitSource, pair.DebitAccount)) || !used.Add((pair.CreditSource, pair.CreditAccount))) throw new BusinessException("Choose different companies and use each reciprocal account once in a review.");
            var debit = input.Report.Members.SingleOrDefault(m => m.SourceId == pair.DebitSource)?.Accounts.SingleOrDefault(a => a.Code == pair.DebitAccount) ?? throw new BusinessException("Debit member account not found.");
            var credit = input.Report.Members.SingleOrDefault(m => m.SourceId == pair.CreditSource)?.Accounts.SingleOrDefault(a => a.Code == pair.CreditAccount) ?? throw new BusinessException("Credit member account not found.");
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
}
