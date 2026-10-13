namespace SierraBusiness.Application;

/// <summary>Portable, user-reviewed reporting data. Amounts use debit minus credit in NLe.</summary>
public sealed record CompanyReportAccount(int Code, string Name, LedgerGroup Nature, decimal PeriodBalance, decimal ClosingBalance);
public sealed record CompanyReportSnapshot(int Version, Guid SourceId, string Company, string Branch, string Currency, DateOnly From, DateOnly To, DateTimeOffset Exported, IReadOnlyList<CompanyReportAccount> Accounts);
public sealed record ConsolidationMapping(Guid SourceId, int SourceCode, int GroupCode, string GroupName);
public sealed record ConsolidationAdjustment(string Reference, DateOnly Date, string Reason, IReadOnlyList<VoucherLine> Lines);
public sealed record IntercompanyPair(string Reference, Guid DebitSource, int DebitAccount, Guid CreditSource, int CreditAccount, string Reason);
public sealed record IntercompanyReview(ConsolidationRequest Report, IReadOnlyList<IntercompanyPair> Pairs);
public sealed record IntercompanyProposal(string Reference, bool Matched, decimal DebitBalance, decimal CreditBalance, string Message, ConsolidationAdjustment? Adjustment);
public sealed record ConsolidationRequest(string Statement, IReadOnlyList<CompanyReportSnapshot> Members, IReadOnlyList<ConsolidationMapping> Mappings,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ConsolidationAdjustment>? Adjustments = null);
public sealed record ConsolidatedRow(string Section, string Label, int? AccountCode, IReadOnlyList<decimal> Contributions, bool Total = false)
{
    public decimal Amount => Contributions.Sum();
}
public sealed record ConsolidatedStatement(string Statement, DateOnly From, DateOnly To, IReadOnlyList<CompanyReportSnapshot> Members, IReadOnlyList<ConsolidatedRow> Rows, decimal BalanceDifference, IReadOnlyList<ConsolidationAdjustment>? Adjustments = null)
{
    public bool HasAdjustments => Adjustments?.Count > 0;
}
