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

public sealed record ReportExportRequest(string Report, DateOnly From, DateOnly To, SalesGrouping By = SalesGrouping.Day, int? AccountCode = null, string Party = "", string Kind = "", string Search = "", Guid? ProductId = null, int SlowDays = 90);

public interface IStatementsService
{
    Task<CompanyReportSnapshot> CompanySnapshot(string company, DateOnly from, DateOnly to);
    Task<CompanySearchIndex> ExportSearchIndex(string company);
    Task<IReadOnlyList<CompanySearchHit>> SearchCompanies(CompanySearchRequest input);
    Task<IReadOnlyList<IntercompanyProposal>> ReviewIntercompany(IntercompanyReview input);
    Task ValidateCompanySnapshot(CompanyReportSnapshot input);
    Task<ConsolidatedStatement> Consolidate(ConsolidationRequest input);
    Task<IntercompanyLedger> ExportIntercompanyLedger(string company, string counterparty, DateOnly from, DateOnly to);
    Task<IntercompanyMatchReport> MatchIntercompanyTransactions(IntercompanyMatchRequest input);
    Task<byte[]> ExportConsolidation(ConsolidationRequest input);
    Task<ComparativeStatement> Compare(ComparisonRequest input);
    Task<byte[]> ExportComparison(ComparisonRequest input);
    Task<FinancialRatios> Ratios(DateOnly from, DateOnly to);
    Task<ProfitAndLoss> ProfitAndLoss(DateOnly from, DateOnly to);
    Task<BalanceSheet> BalanceSheet(DateOnly asAt);
    Task<CashFlowStatement> CashFlow(DateOnly from, DateOnly to);
    Task<FundsFlowStatement> FundsFlow(DateOnly from, DateOnly to);
    Task<IReadOnlyList<YearEndView>> YearEnds();
    Task<Guid> CloseYear(CloseYearCommand input);
    /// <summary>Excel workbook of a statement ("Profit and loss", "Balance sheet", "Cash flow", "Funds flow") or a report from the Reports screen.</summary>
    Task<byte[]> Export(ReportExportRequest request);
}

public sealed record FinancialRatio(string Name, string Formula, decimal Numerator, decimal Denominator, bool Percentage)
{
    public decimal? Value => Denominator > 0 ? Numerator / Denominator * (Percentage ? 100 : 1) : null;
}
public sealed record FinancialRatios(DateOnly From, DateOnly To, IReadOnlyList<FinancialRatio> Rows);

public sealed record ComparisonRequest(string Statement, DateOnly From, DateOnly To, DateOnly ComparisonFrom, DateOnly ComparisonTo);
public sealed record ComparativeRow(string Section, string Label, int? AccountCode, decimal Current, decimal Previous, bool Total)
{
    public decimal Change => Current - Previous;
    public decimal? ChangePercent => Previous == 0 ? null : Change / Math.Abs(Previous) * 100;
}
public sealed record ComparativeStatement(ComparisonRequest Periods, IReadOnlyList<ComparativeRow> Rows);
