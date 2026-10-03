namespace SierraBusiness.Application;

/// <summary>Specification Skill 22 (0.19.0): configurable business controls, approval requests and exception reporting.</summary>
/// <param name="MaxDiscountPercent">Sales more than this percentage below the list price need approval. Null: no limit.</param>
/// <param name="ApproveBelowCost">Sales priced below their stock cost need approval.</param>
/// <param name="BackdateDays">Postings dated more than this many days before today need approval. Null: no limit.</param>
/// <param name="ReversalAbove">Reversals of documents of at least this value need approval (0 = every reversal). Null: none.</param>
/// <param name="StockAdjustmentAbove">Direct stock count adjustments of at least this value need approval (0 = every adjustment). Null: none.</param>
/// <param name="CreditOverride">A credit sale over the customer's limit can be approved instead of being refused.</param>
/// <param name="SelfApprovalWhenAlone">When nobody else holds the approving permission, the requester may approve their own request (recorded as self-approved).</param>
public sealed record ControlPolicy(int Version = 0, decimal? MaxDiscountPercent = null, bool ApproveBelowCost = false, int? BackdateDays = null, decimal? ReversalAbove = null,
    decimal? StockAdjustmentAbove = null, bool CreditOverride = false, bool SelfApprovalWhenAlone = true, string WorkStart = "07:00", string WorkEnd = "19:00")
{
    public bool Active => MaxDiscountPercent is not null || ApproveBelowCost || BackdateDays is not null || ReversalAbove is not null || StockAdjustmentAbove is not null || CreditOverride;
    public static ControlPolicy Recommended => new(0, 10, true, 3, 0, 500, true, true);
}
public sealed record SaveControlPolicy(Guid OperationId, ControlPolicy Policy);

public enum ApprovalState { Pending, Approved, Rejected, Withdrawn, Used }
public sealed record ApprovalTrigger(string Rule, string Detail, string Permission);
public sealed record ApprovalView(Guid Id, string Number, string Action, string Summary, IReadOnlyList<ApprovalTrigger> Triggers, string RequestedBy, DateTimeOffset RequestedAt, string Note,
    ApprovalState State, string DecidedBy, DateTimeOffset? DecidedAt, string DecisionNote, bool SelfApproved, string UsedDocument, bool Mine, bool CanDecide, bool CanPost);
public sealed record ApprovalDecisionCommand(Guid OperationId, Guid RequestId, bool Approve, string Note);
public sealed record ApprovalNoteCommand(Guid OperationId, Guid RequestId, string Note);
public sealed record PostApprovedCommand(Guid RequestId);

public sealed record ExceptionRow(DateTimeOffset Time, string Actor, string Kind, string Reference, Guid? DocumentId, string Detail, decimal Amount, string Approval);
public sealed record UserExceptionSummary(string Actor, int Reversals, int Backdated, int Discounts, int BelowCost, int StockAdjustments, int AfterHours, int SelfApprovals, bool Review);
public sealed record ExceptionReport(DateOnly From, DateOnly To, ControlPolicy Policy, IReadOnlyList<ExceptionRow> Rows, IReadOnlyList<UserExceptionSummary> Users);
public sealed record AuditEntry(DateTimeOffset Time, string Actor, string Action, string Reference, Guid ResultId);

public interface IControlService
{
    Task<ControlPolicy> Policy();
    Task<Guid> SavePolicy(SaveControlPolicy input);
    Task<IReadOnlyList<ApprovalView>> Approvals();
    Task<Guid> Decide(ApprovalDecisionCommand input);
    Task<Guid> Withdraw(ApprovalNoteCommand input);
    Task<Guid> AddNote(ApprovalNoteCommand input);
    /// <summary>Posts an approved request exactly as it was requested. Only the requester can post it, once.</summary>
    Task<Guid> Post(PostApprovedCommand input);
    Task<ExceptionReport> Exceptions(DateOnly from, DateOnly to);
    Task<IReadOnlyList<AuditEntry>> AuditTrail(DateOnly from, DateOnly to);
    Task<byte[]> Export(DateOnly from, DateOnly to);
}

/// <summary>A posting that a control stopped. The request is saved; an approver decides under Approvals.</summary>
public sealed class ApprovalRequiredException(Guid requestId, string number, string message) : SierraBusiness.Domain.BusinessException(message)
{
    public Guid RequestId { get; } = requestId;
    public string Number { get; } = number;
}
