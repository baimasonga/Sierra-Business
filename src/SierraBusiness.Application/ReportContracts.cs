using SierraBusiness.Domain;
namespace SierraBusiness.Application;

/// <summary>Specification Skill 21 (0.18.0): owner dashboard and drill-down reports. Every figure is calculated from the ledger or stock movements; nothing here is stored.</summary>
public sealed record TrendPoint(DateOnly Date, string Label, decimal NetSales, decimal? GrossProfit);
public sealed record RankedItem(string Key, string Name, decimal Amount, decimal Quantity);
public sealed record BranchFigure(string Branch, decimal NetSales, decimal? GrossProfit, decimal? CashAndBank, decimal? Receivables);
/// <summary>A report total checked against its source of truth.</summary>
public sealed record ReconciliationCheck(string Name, decimal Report, decimal Source, string SourceName)
{
    public decimal Difference => Report - Source;
    public bool Ok => Difference == 0;
}
public sealed record DashboardView(DateOnly From, DateOnly To, bool Financial,
    decimal NetSales, int SalesDocuments, decimal? CostOfSales, decimal? GrossProfit,
    decimal? Cash, decimal? Bank, decimal? Cheques, decimal? Receivables, decimal? OverdueReceivables, decimal? Payables, decimal? OverduePayables,
    decimal StockValue, int LowStock, int OpenServiceJobs, int AwaitingBilling, int OpenTenders, decimal? TenderPipeline, int TendersClosingSoon, int WarrantiesExpiring,
    IReadOnlyList<TrendPoint> Trend, bool MonthlyTrend, IReadOnlyList<RankedItem> TopCustomers, IReadOnlyList<RankedItem> TopProducts,
    IReadOnlyList<BranchFigure> Branches, IReadOnlyList<ReconciliationCheck> Checks, DateTimeOffset AsOf, int ApprovalsWaiting = 0)
{
    public decimal? Margin => GrossProfit is decimal g && NetSales != 0 ? Math.Round(g / NetSales * 100, 1) : null;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int Pages => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

/// <summary>Day book / transaction register. With an account, only documents posting to that ledger are listed with their debit and credit.</summary>
public sealed record RegisterQuery(DateOnly From, DateOnly To, int? AccountCode = null, string Party = "", DocumentKind? Kind = null, string Search = "", int Page = 1, int PageSize = 50);
public sealed record RegisterRow(Guid DocumentId, string Number, DateOnly Date, DocumentKind Kind, string Party, decimal Total, string Settlement, bool Reversed, decimal Debit, decimal Credit);
public sealed record RegisterReport(PagedResult<RegisterRow> Rows, int? AccountCode, string AccountName, decimal Opening, decimal Debit, decimal Credit, decimal Closing);

public enum SalesGrouping { Day, Month, Customer, Product }
public sealed record SalesRow(string Key, string Label, decimal NetSales, decimal? Cost, decimal? GrossProfit, decimal Quantity, int Documents);
public sealed record SalesAnalysis(DateOnly From, DateOnly To, SalesGrouping By, IReadOnlyList<SalesRow> Rows, decimal Total, decimal? Cost, decimal? GrossProfit, decimal Adjustments, ReconciliationCheck? Check);

public sealed record StockValueRow(Guid ProductId, string Sku, string Name, string Category, string Unit, decimal Quantity, decimal Value, decimal AverageCost, IReadOnlyList<StockLocationValue> Locations);
public sealed record StockLocationValue(string Location, decimal Quantity, decimal Value);
public sealed record StockValuation(DateOnly AsAt, IReadOnlyList<StockValueRow> Rows, decimal Total, ReconciliationCheck? Check);

public sealed record MovementQuery(Guid ProductId, DateOnly From, DateOnly To, string Location = "", int Page = 1, int PageSize = 50);
public sealed record MovementRow(Guid DocumentId, string Number, DateOnly Date, DocumentKind Kind, string Party, string Location, string? Serial, string? Batch, decimal QuantityIn, decimal QuantityOut, decimal Value, decimal Balance);
public sealed record MovementReport(Guid ProductId, string Sku, string Name, string Unit, decimal Opening, decimal OpeningValue, decimal In, decimal Out, decimal Closing, decimal ClosingValue, PagedResult<MovementRow> Rows);

public sealed record AgeingBill(Guid DocumentId, string Number, DateOnly Date, DateOnly Due, decimal Outstanding, int OverdueDays);
public sealed record AgeingRow(string Party, decimal Current, decimal Days30, decimal Days60, decimal Days90, decimal Over90, decimal Total, IReadOnlyList<AgeingBill> Bills);
public sealed record AgeingReport(DateOnly AsAt, bool Receivable, IReadOnlyList<AgeingRow> Rows, decimal Current, decimal Days30, decimal Days60, decimal Days90, decimal Over90, decimal Total, ReconciliationCheck Check);

public interface IReportService
{
    Task<StockAgeingReport> StockAgeing(DateOnly asAt, int slowDays = 90);
    Task<DashboardView> Dashboard(DateOnly from, DateOnly to);
    Task<RegisterReport> Register(RegisterQuery query);
    Task<SalesAnalysis> Sales(DateOnly from, DateOnly to, SalesGrouping by);
    Task<StockValuation> Stock(DateOnly asAt);
    Task<MovementReport> Movements(MovementQuery query);
    Task<AgeingReport> Ageing(DateOnly asAt, bool receivable);
    Task<IReadOnlyList<LedgerDefinition>> Accounts();
}

public sealed record StockAgeingRow(Guid ProductId, string Sku, string Name, string Unit, string Location, string? Serial, string? Batch, DateOnly? Expiry,
    decimal Quantity, decimal Days30, decimal Days60, decimal Days90, decimal Over90, decimal UnknownAge, decimal Value, DateOnly? LastDispatch, bool SlowMoving);
public sealed record StockAgeingReport(DateOnly AsAt, int SlowDays, IReadOnlyList<StockAgeingRow> Rows, decimal Value);

/// <summary>Raised after any save that adds posted history, so open dashboards can refresh. Screens must keep a refresh/poll fallback.</summary>
public interface IChangeFeed
{
    event Action Changed;
}
