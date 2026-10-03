namespace SierraBusiness.Application;

/// <summary>Specification Skill 16: quotations, tenders and institutional procurement sales.</summary>
public enum TenderStatus { Draft, BidPreparation, Approved, Submitted, Clarification, Won, Lost, Cancelled }

public sealed record CreateTenderCommand(Guid OperationId, string Reference, string Institution, string Title, DateOnly Closing, string ClosingTime = "", string Officer = "", string SubmissionMethod = "", string Notes = "");

/// <summary>One bid line. Costs are per unit in CostCurrency; ExchangeRate converts to NLe. AlternativeTo (1-based line number) marks an alternative offer, which is excluded from the bid total.</summary>
public sealed record TenderLineInput(Guid? ProductId, string Description, decimal Quantity, string Unit, decimal SupplierCost, string CostCurrency, decimal ExchangeRate,
    decimal LandedPercent, decimal MarginPercent, decimal UnitPrice, int? AlternativeTo = null, string Notes = "");
public sealed record SaveBidCommand(Guid OperationId, Guid TenderId, int ExpectedVersion, List<TenderLineInput> Lines, string Reason = "");
public sealed record TenderDecisionCommand(Guid OperationId, Guid TenderId, int Version, string Reason = "");
public sealed record SubmitTenderCommand(Guid OperationId, Guid TenderId, int Version, DateOnly SubmittedOn, string Receipt = "", string Reason = "");
public sealed record TenderNoteCommand(Guid OperationId, Guid TenderId, string Reason);
public sealed record ChangeClosingCommand(Guid OperationId, Guid TenderId, DateOnly Closing, string ClosingTime, string Reason);
/// <summary>Awarded quantity per bid line (1-based line number). Price is the submitted bid price.</summary>
public sealed record AwardLine(int Line, decimal Quantity);
public sealed record WinTenderCommand(Guid OperationId, Guid TenderId, string AwardReference, DateOnly OrderDate, DateOnly DueDate, List<AwardLine> Lines);
public sealed record LoseTenderCommand(Guid OperationId, Guid TenderId, string Reason, string Winner = "", decimal? WinningAmount = null);

public sealed record TenderLineView(int Line, Guid? ProductId, string Sku, string Description, decimal Quantity, string Unit, decimal SupplierCost, string CostCurrency, decimal ExchangeRate,
    decimal LandedPercent, decimal MarginPercent, decimal LocalUnitCost, decimal SuggestedPrice, decimal UnitPrice, decimal LineTotal, decimal ActualMarginPercent, int? AlternativeTo, string Notes, bool BelowCost);
public sealed record TenderBidView(int Version, IReadOnlyList<TenderLineView> Lines, decimal TaxRate, decimal Subtotal, decimal Tax, decimal Total, decimal CostTotal, decimal MarginPercent,
    string Reason, DateTimeOffset Created, string CreatedBy, string ApprovedBy, bool Submitted);
public sealed record TenderEventView(int Sequence, string Kind, TenderStatus Status, int? Version, string Reason, string Actor, DateTimeOffset Time);
public sealed record TenderView(Guid Id, string Number, string Reference, string Institution, string Title, string Officer, DateOnly Closing, string ClosingTime, int DaysLeft, string SubmissionMethod, string Notes,
    TenderStatus Status, IReadOnlyList<TenderBidView> Bids, IReadOnlyList<TenderEventView> Events, int? ApprovedVersion, int? SubmittedVersion, Guid? OrderId, string OrderNumber, string Outcome, bool CostsVisible);

public interface ITenderService
{
    Task<IReadOnlyList<TenderView>> List();
    Task<Guid> Create(CreateTenderCommand input);
    Task<Guid> SaveBid(SaveBidCommand input);
    Task<Guid> Approve(TenderDecisionCommand input);
    Task<Guid> Submit(SubmitTenderCommand input);
    Task<Guid> Clarify(TenderNoteCommand input);
    Task<Guid> ChangeClosing(ChangeClosingCommand input);
    Task<Guid> Win(WinTenderCommand input);
    Task<Guid> Lose(LoseTenderCommand input);
    Task<Guid> Cancel(TenderNoteCommand input);
}
