using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure.Imports;
namespace SierraBusiness.Infrastructure;

public sealed partial class StatementsService
{
    public async Task<ComparativeStatement> Compare(ComparisonRequest q)
    {
        await Authorize();
        if (q.From > q.To || q.ComparisonFrom > q.ComparisonTo) throw new BusinessException("Each comparison period must end on or after its start.");
        if (q.To.DayNumber - q.From.DayNumber > 3660 || q.ComparisonTo.DayNumber - q.ComparisonFrom.DayNumber > 3660)
            throw new BusinessException("Choose comparison periods of at most ten years.");
        async Task<IReadOnlyList<StatementRow>> Read(DateOnly from, DateOnly to) => q.Statement switch
        {
            "Profit and loss" => (await ProfitAndLoss(from, to)).Rows.Select(r => r.Section == "Result" ? r with { Label = "Net profit / loss" } : r).ToArray(),
            "Balance sheet" => (await BalanceSheet(to)).Rows.Select(r => r.Section == "Equity" && r.Label.StartsWith("Profit", StringComparison.Ordinal) && r.Label != "Profit of closed years" ? r with { Label = "Unclosed profit to date" } : r).ToArray(),
            "Cash flow" => CashRows(await CashFlow(from, to)),
            "Funds flow" => FundsRows(await FundsFlow(from, to)),
            _ => throw new BusinessException("Choose profit and loss, balance sheet, cash flow or funds flow.")
        };
        var current = await Read(q.From, q.To); var previous = await Read(q.ComparisonFrom, q.ComparisonTo);
        static (string, int?, string, bool) Key(StatementRow r) => (r.Section, r.AccountCode, r.Label, r.Total);
        var keys = current.Concat(previous).Select(Key).Distinct().ToArray();
        return new(q, keys.Select(k => new ComparativeRow(k.Item1, k.Item3, k.Item2,
            current.Where(r => Key(r) == k).Sum(r => r.Amount), previous.Where(r => Key(r) == k).Sum(r => r.Amount), k.Item4)).ToArray());
    }
    private static IReadOnlyList<StatementRow> CashRows(CashFlowStatement c) =>
        [new("Balances", "Opening cash and bank", null, c.OpeningCash), .. c.Rows, new("Balances", "Net change in cash", null, c.NetChange, true), new("Balances", "Closing cash and bank", null, c.ClosingCash, true)];
    private static IReadOnlyList<StatementRow> FundsRows(FundsFlowStatement f) =>
        [.. f.Sources, new("Sources", "Total sources", null, f.TotalSources, true), .. f.Uses, new("Uses", "Total uses", null, f.TotalUses, true)];

    public async Task<byte[]> ExportComparison(ComparisonRequest q)
    {
        var report = await Compare(q);
        return WorkbookWriter.Write([("Comparison", [[q.Statement, $"Current {q.From:yyyy-MM-dd} to {q.To:yyyy-MM-dd}", $"Comparison {q.ComparisonFrom:yyyy-MM-dd} to {q.ComparisonTo:yyyy-MM-dd}", "Change", "Change %"],
            .. report.Rows.Select(r => (IReadOnlyList<string>)[r.Section + ": " + r.Label, M(r.Current), M(r.Previous), M(r.Change), r.ChangePercent?.ToString("0.00") ?? "N/A"])])]);
    }
}
