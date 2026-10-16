namespace SierraBusiness.Application;

/// <summary>Portable, user-reviewed reporting data. Amounts use debit minus credit in NLe.</summary>
public sealed record CompanyReportAccount(int Code, string Name, LedgerGroup Nature, decimal PeriodBalance, decimal ClosingBalance);
public sealed record CompanyReportSnapshot(int Version, Guid SourceId, string Company, string Branch, string Currency, DateOnly From, DateOnly To, DateTimeOffset Exported, IReadOnlyList<CompanyReportAccount> Accounts);
public sealed record ConsolidationMapping(Guid SourceId, int SourceCode, int GroupCode, string GroupName);
public sealed record ConsolidationAdjustment(string Reference, DateOnly Date, string Reason, IReadOnlyList<VoucherLine> Lines);
public sealed record IntercompanyPair(string Reference, Guid DebitSource, int DebitAccount, Guid CreditSource, int CreditAccount, string Reason);
public sealed record IntercompanyReview(ConsolidationRequest Report, IReadOnlyList<IntercompanyPair> Pairs);
public sealed record IntercompanyProposal(string Reference, bool Matched, decimal DebitBalance, decimal CreditBalance, string Message, ConsolidationAdjustment? Adjustment);
/// <summary>Group terms for one member. Ownership is the parent's percentage; the rest is non-controlling interest.
/// Rates are NLe per one unit of the member's reporting currency: closing for assets/liabilities, average for income/expenses and historical for equity.</summary>
public sealed record ConsolidationMemberTerms(Guid SourceId, decimal OwnershipPercent, decimal ClosingRate = 1, decimal AverageRate = 1, decimal EquityRate = 1);
public sealed record ConsolidationRequest(string Statement, IReadOnlyList<CompanyReportSnapshot> Members, IReadOnlyList<ConsolidationMapping> Mappings,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ConsolidationAdjustment>? Adjustments = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ConsolidationMemberTerms>? Terms = null);
public sealed record ConsolidatedRow(string Section, string Label, int? AccountCode, IReadOnlyList<decimal> Contributions, bool Total = false)
{
    public decimal Amount => Contributions.Sum();
}
public sealed record ConsolidatedStatement(string Statement, DateOnly From, DateOnly To, IReadOnlyList<CompanyReportSnapshot> Members, IReadOnlyList<ConsolidatedRow> Rows, decimal BalanceDifference, IReadOnlyList<ConsolidationAdjustment>? Adjustments = null, IReadOnlyList<ConsolidationMemberTerms>? Terms = null)
{
    public bool HasNonControllingInterest => Terms?.Any(t => t.OwnershipPercent < 100) == true;
    public bool HasTranslation => Members.Any(m => m.Currency != "NLe");
    public bool HasAdjustments => Adjustments?.Count > 0;
}
/// <summary>One company's documents with a named counterparty. Amount is the document's effect on trade receivables and payables
/// (debit minus credit), so a sale on credit to the other company is positive and the other company's purchase of it is negative.</summary>
public sealed record IntercompanyTransaction(string Number, DateOnly Date, string Kind, decimal Amount);
public sealed record IntercompanyLedger(int Version, Guid SourceId, string Company, string Branch, string Counterparty, DateOnly From, DateOnly To, DateTimeOffset Exported, IReadOnlyList<IntercompanyTransaction> Transactions);
public sealed record IntercompanyMatchRequest(IntercompanyLedger Left, IntercompanyLedger Right, int DateToleranceDays = 3);
public sealed record IntercompanyTransactionMatch(IntercompanyTransaction Left, IntercompanyTransaction Right, int DaysApart);
public sealed record IntercompanyMatchReport(IReadOnlyList<IntercompanyTransactionMatch> Matched, IReadOnlyList<IntercompanyTransaction> LeftOnly, IReadOnlyList<IntercompanyTransaction> RightOnly, decimal LeftBalance, decimal RightBalance)
{
    /// <summary>Zero when the two companies' intercompany balances are reciprocal.</summary>
    public decimal Difference => LeftBalance + RightBalance;
}
