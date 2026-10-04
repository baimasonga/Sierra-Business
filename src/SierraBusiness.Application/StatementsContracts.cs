namespace SierraBusiness.Application;

/// <summary>0.24.0: financial statements, year-end close and report exports.</summary>
public sealed record StatementRow(string Section, string Label, int? AccountCode, decimal Amount, bool Total = false);
public sealed record ProfitAndLoss(DateOnly From, DateOnly To, IReadOnlyList<StatementRow> Rows, decimal Income, decimal Expenses, decimal Profit);
/// <param name="RetainedEarnings">Profit of years already closed (plus the retained earnings ledger).</param>
/// <param name="CurrentProfit">Profit since the last year-end close up to the date.</param>
public sealed record BalanceSheet(DateOnly AsAt, IReadOnlyList<StatementRow> Rows, decimal Assets, decimal Liabilities, decimal Equity, decimal RetainedEarnings, decimal CurrentProfit, DateOnly? LastYearEnd)
{
    public decimal Difference => Assets - Liabilities - Equity - RetainedEarnings - CurrentProfit;
}
/// <summary>Direct-method cash flow: every cash and bank movement classified by what it paid for or came from.</summary>
public sealed record CashFlowStatement(DateOnly From, DateOnly To, decimal OpeningCash, IReadOnlyList<StatementRow> Rows, decimal Operating, decimal Investing, decimal Financing, decimal ClosingCash)
{
    public decimal NetChange => Operating + Investing + Financing;
    public bool Reconciled => OpeningCash + NetChange == ClosingCash;
}
/// <summary>Sources and uses of funds: changes in balance-sheet accounts over the period, with the period's profit.</summary>
public sealed record FundsFlowStatement(DateOnly From, DateOnly To, IReadOnlyList<StatementRow> Sources, IReadOnlyList<StatementRow> Uses, decimal TotalSources, decimal TotalUses);
public sealed record YearEndView(DateOnly YearEnd, decimal Profit, string ClosedBy, DateTimeOffset Closed, string Reason);
public sealed record CloseYearCommand(Guid OperationId, DateOnly YearEnd, string Reason);

public sealed record ReportExportRequest(string Report, DateOnly From, DateOnly To, SalesGrouping By = SalesGrouping.Day, int? AccountCode = null, string Party = "", string Kind = "", string Search = "", Guid? ProductId = null);

public interface IStatementsService
{
    Task<ProfitAndLoss> ProfitAndLoss(DateOnly from, DateOnly to);
    Task<BalanceSheet> BalanceSheet(DateOnly asAt);
    Task<CashFlowStatement> CashFlow(DateOnly from, DateOnly to);
    Task<FundsFlowStatement> FundsFlow(DateOnly from, DateOnly to);
    Task<IReadOnlyList<YearEndView>> YearEnds();
    Task<Guid> CloseYear(CloseYearCommand input);
    /// <summary>Excel workbook of a statement ("Profit and loss", "Balance sheet", "Cash flow", "Funds flow") or a report from the Reports screen.</summary>
    Task<byte[]> Export(ReportExportRequest request);
}
