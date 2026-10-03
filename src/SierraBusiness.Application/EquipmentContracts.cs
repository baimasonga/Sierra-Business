namespace SierraBusiness.Application;

/// <summary>Specification Skills 17 and 20: serialized equipment lifecycle, installation/commissioning and warranty.</summary>
public enum WarrantyStart { Sale, Installation }
/// <summary>Per-product equipment terms. Each save adds a version; documents keep the terms in force when they were posted.</summary>
public sealed record EquipmentTerms(Guid ProductId, int Version = 0, int CustomerWarrantyMonths = 0, WarrantyStart StartsAt = WarrantyStart.Sale, int SupplierWarrantyMonths = 0,
    bool RequiresInstallation = false, IReadOnlyList<string>? Checklist = null, string Coverage = "");
public sealed record SaveEquipmentTerms(Guid OperationId, EquipmentTerms Terms);

public enum CompatibilityKind { Accessory, Consumable, SparePart }
public sealed record CompatibilityLink(Guid Id, Guid EquipmentProductId, Guid ItemProductId, CompatibilityKind Kind, string Note);
public sealed record LinkCompatibility(Guid OperationId, Guid EquipmentProductId, Guid ItemProductId, CompatibilityKind Kind, string Note = "");
public sealed record UnlinkCompatibility(Guid OperationId, Guid LinkId, string Reason);
public sealed record CompatibleItemView(Guid LinkId, Guid ProductId, string Sku, string Name, CompatibilityKind Kind, string Note, decimal OnHand, bool Reverse);

public sealed record UnitSpecification(string Name, string Value);
public sealed record SaveUnitProfile(Guid OperationId, Guid ProductId, string Serial, int ExpectedVersion, List<UnitSpecification> Specifications, string Accessories = "", string Notes = "");

public sealed record EquipmentSearchResult(Guid ProductId, string Sku, string Name, string Serial, string Status, string Customer);
public sealed record EquipmentEvent(DateOnly Date, string Kind, string Description, string Document, Guid? DocumentId);
public sealed record WarrantyView(Guid Id, Guid ProductId, string Sku, string Serial, string Customer, DateOnly Start, DateOnly End, string Status, string Source, string Coverage);
public sealed record WarrantyCoverage(bool Covered, string Message, DateOnly? Until);
public sealed record EquipmentUnitView(Guid ProductId, string Sku, string Name, string Brand, string Model, string Serial, string Status, string Location, string Customer,
    DateOnly? Received, string Supplier, decimal? Cost, DateOnly? Sold, string SaleNumber, DateOnly? SupplierWarrantyEnd,
    IReadOnlyList<UnitSpecification> Specifications, int ProfileVersion, string Accessories, string Notes,
    WarrantyCoverage Coverage, IReadOnlyList<WarrantyView> Warranties, IReadOnlyList<EquipmentEvent> Timeline, IReadOnlyList<CompatibleItemView> Compatible, bool CanStartWarranty);
public sealed record StartWarrantyCommand(Guid OperationId, Guid ProductId, string Serial);

public sealed record InstallationUnitInput(Guid ProductId, string Serial);
public sealed record InstallationCommand(Guid OperationId, Guid SaleId, string Site, DateOnly Scheduled, string Engineer, List<InstallationUnitInput> Units, string Notes = "");
public sealed record CompleteInstallationCommand(Guid OperationId, Guid InstallationId, DateOnly Completed, List<string> ChecklistDone, string AcceptedBy, string TrainingGivenTo = "", string Notes = "");
public sealed record CancelInstallationCommand(Guid OperationId, Guid InstallationId, string Reason);
public sealed record InstallationUnitView(Guid ProductId, string Sku, string Name, string Serial);
public sealed record InstallableSale(Guid SaleId, string Number, string Customer, DateOnly Date, IReadOnlyList<InstallationUnitView> Units);
public sealed record InstallationView(Guid Id, string Number, string Customer, string Site, Guid SaleId, string SaleNumber, DateOnly SaleDate, DateOnly Scheduled, string Engineer, string Notes, string Status,
    IReadOnlyList<InstallationUnitView> Units, IReadOnlyList<string> Checklist, DateOnly? Completed, string AcceptedBy, string TrainingGivenTo, string CompletionNotes, string CancelReason, IReadOnlyList<string> ChecklistDone);

public interface IEquipmentService
{
    Task<IReadOnlyList<EquipmentTerms>> Terms();
    Task<Guid> SaveTerms(SaveEquipmentTerms input);
    Task<IReadOnlyList<CompatibilityLink>> Compatibility();
    Task<Guid> Link(LinkCompatibility input);
    Task<Guid> Unlink(UnlinkCompatibility input);
    Task<IReadOnlyList<CompatibleItemView>> CompatibleWith(Guid productId);
    Task<IReadOnlyList<EquipmentSearchResult>> Search(string text);
    Task<EquipmentUnitView?> Unit(Guid productId, string serial);
    Task<Guid> SaveProfile(SaveUnitProfile input);
    Task<Guid> StartWarranty(StartWarrantyCommand input);
    Task<IReadOnlyList<WarrantyView>> Warranties();
    Task<IReadOnlyList<InstallableSale>> Installable();
    Task<IReadOnlyList<InstallationView>> Installations();
    Task<Guid> ScheduleInstallation(InstallationCommand input);
    Task<Guid> CompleteInstallation(CompleteInstallationCommand input);
    Task<Guid> CancelInstallation(CancelInstallationCommand input);
}
