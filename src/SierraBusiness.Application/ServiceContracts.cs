using SierraBusiness.Domain;
namespace SierraBusiness.Application;

/// <summary>Specification Skill 20 (0.17.0): service jobs, parts used, warranty claims and preventive maintenance.</summary>
public enum ServiceStatus { Logged, Assigned, InProgress, Completed, Closed, Cancelled }
public enum ServicePriority { Low, Normal, High, Urgent }
/// <summary>Who pays. Warranty and contract decisions are explicit; goodwill and warranty exceptions need pricing authority.</summary>
public enum ServiceBilling { Chargeable, Warranty, Contract, Goodwill }

public sealed record LogServiceJobCommand(Guid OperationId, string Customer, string Site, string Contact, Guid? ProductId, string Serial, string Equipment, string Problem,
    ServicePriority Priority, DateOnly ReportedOn, string Engineer = "");
public sealed record ServiceTextCommand(Guid OperationId, Guid JobId, string Text);
public sealed record AssignServiceCommand(Guid OperationId, Guid JobId, string Engineer);
public sealed record AddLabourCommand(Guid OperationId, Guid JobId, DateOnly Date, string Engineer, decimal Hours, decimal Rate, string Description);
public sealed record IssuePartCommand(Guid OperationId, Guid JobId, DateOnly Date, Guid ProductId, decimal Quantity, string Location = "MAIN", string? Serial = null, string? Batch = null, DateOnly? Expiry = null, decimal UnitPrice = 0);
public sealed record ReturnPartCommand(Guid OperationId, Guid JobId, Guid IssueDocumentId, decimal Quantity, string Reason);
public sealed record CoverageCommand(Guid OperationId, Guid JobId, ServiceBilling Billing, bool CoversParts = true, bool CoversLabour = true, string Reason = "");
public sealed record CompleteServiceCommand(Guid OperationId, Guid JobId, DateOnly Completed, string Resolution, string AcceptedBy, List<string> ChecklistDone);
public sealed record CloseServiceCommand(Guid OperationId, Guid JobId, DateOnly Date, PaymentMethod? Payment = null, string? PaymentReference = null, string? Bank = null, DateOnly? PaymentDate = null, DateOnly? DueDate = null);
public sealed record SupplierClaimCommand(Guid OperationId, Guid JobId, string Supplier, string Reference, string Note = "");
public sealed record SupplierClaimOutcomeCommand(Guid OperationId, Guid JobId, string Outcome, string Note = "");

public sealed record MaintenancePlanCommand(Guid OperationId, Guid ProductId, string Serial, string Site, int IntervalMonths, DateOnly FirstDue, List<string> Checklist, string Engineer = "", string Notes = "");
public sealed record EndMaintenancePlanCommand(Guid OperationId, Guid PlanId, string Reason);
public sealed record GenerateMaintenanceCommand(Guid OperationId, DateOnly Through);

public sealed record ServicePartView(Guid IssueDocumentId, string Number, DateOnly Date, Guid ProductId, string Sku, string Name, string Unit, decimal Quantity, decimal Returned,
    string Location, string? Serial, string? Batch, decimal UnitPrice, decimal? Cost, bool Chargeable);
public sealed record ServiceLabourView(DateOnly Date, string Engineer, decimal Hours, decimal Rate, string Description, bool Chargeable);
public sealed record ServiceCoverageView(ServiceBilling Billing, bool CoversParts, bool CoversLabour, string Reason, bool Exception, string DecidedBy);
public sealed record ServiceEventView(int Sequence, string Kind, ServiceStatus Status, string Text, string Actor, DateTimeOffset Time);
public sealed record ServiceJobView(Guid Id, string Number, string Customer, string Site, string Contact, Guid? ProductId, string Sku, string Equipment, string Serial, string Problem,
    ServicePriority Priority, string Source, DateOnly ReportedOn, string PlanNumber, DateOnly? PlanDue, ServiceStatus Status, string Engineer, string Diagnosis,
    string Resolution, string AcceptedBy, DateOnly? CompletedOn, IReadOnlyList<string> Checklist, IReadOnlyList<string> ChecklistDone,
    ServiceCoverageView? Coverage, string WarrantyHint, bool WarrantyActive, DateOnly? SupplierWarrantyEnd,
    IReadOnlyList<ServicePartView> Parts, IReadOnlyList<ServiceLabourView> Labour, decimal PartsCharge, decimal LabourCharge, decimal? PartsCost,
    Guid? CloseDocumentId, string CloseNumber, string Settlement, decimal Invoiced, string SupplierClaim, IReadOnlyList<ServiceEventView> Events, bool CostsVisible);
public sealed record MaintenancePlanView(Guid Id, string Number, string Customer, string Site, Guid ProductId, string Sku, string Name, string Serial, int IntervalMonths,
    DateOnly FirstDue, DateOnly? NextDue, IReadOnlyList<string> Checklist, string Engineer, string Notes, bool Active, string EndReason, int Visits, DateOnly? LastVisit);
/// <summary>A sold serial unit that can be serviced, with its current warranty position.</summary>
public sealed record ServiceableUnit(Guid ProductId, string Sku, string Name, string Serial, string Customer, string Coverage, bool WarrantyActive);
public sealed record ServicePartOption(Guid ProductId, string Sku, string Name, string Unit, TrackingMode Tracking, decimal Price, decimal OnHand);
public sealed record ServiceDeskView(IReadOnlyList<ServiceJobView> Jobs, IReadOnlyList<MaintenancePlanView> Plans, IReadOnlyList<ServiceableUnit> Units,
    IReadOnlyList<ServicePartOption> Parts, IReadOnlyList<string> Locations, decimal TaxRate);

public interface IServiceDesk
{
    Task<ServiceDeskView> Read();
    Task<Guid> Log(LogServiceJobCommand input);
    Task<Guid> Assign(AssignServiceCommand input);
    Task<Guid> Diagnose(ServiceTextCommand input);
    Task<Guid> Note(ServiceTextCommand input);
    Task<Guid> AddLabour(AddLabourCommand input);
    Task<Guid> IssuePart(IssuePartCommand input);
    Task<Guid> ReturnPart(ReturnPartCommand input);
    Task<Guid> DecideCoverage(CoverageCommand input);
    Task<Guid> Complete(CompleteServiceCommand input);
    Task<Guid> Reopen(ServiceTextCommand input);
    Task<Guid> Close(CloseServiceCommand input);
    Task<Guid> Cancel(ServiceTextCommand input);
    Task<Guid> SupplierClaim(SupplierClaimCommand input);
    Task<Guid> SupplierClaimOutcome(SupplierClaimOutcomeCommand input);
    Task<Guid> CreatePlan(MaintenancePlanCommand input);
    Task<Guid> EndPlan(EndMaintenancePlanCommand input);
    Task<Guid> GenerateDue(GenerateMaintenanceCommand input);
}
