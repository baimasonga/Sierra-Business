using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed class Product
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Price { get; set; }
    public TrackingMode Tracking { get; set; }
}
public sealed class BusinessDocument
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Party { get; set; } = "";
    public DateOnly BusinessDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public DocumentKind Kind { get; set; }
    public decimal Total { get; set; }
    public string Settlement { get; set; } = "";
    public string Detail { get; set; } = "";
    public Guid? Reverses { get; set; }
}
public sealed class Movement
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid ProductId { get; set; }
    public string Branch { get; set; } = "";
    public string Location { get; set; } = "MAIN";
    public string? Serial { get; set; }
    public string? Batch { get; set; }
    public DateOnly? Expiry { get; set; }
    public decimal Quantity { get; set; }
    public decimal Value { get; set; }
}
public sealed class JournalLine
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string Branch { get; set; } = "";
    public Account Account { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string CostCentre { get; set; } = "";
}
public sealed class Operation
{
    public Guid Id { get; set; }
    public Guid ResultId { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTimeOffset Time { get; set; }
    public string Payload { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public bool Acknowledged { get; set; }
}
public sealed class BusinessDb(DbContextOptions<BusinessDb> options) : IdentityDbContext(options)
{
    public DbSet<InventoryRevision> InventoryRevisions => Set<InventoryRevision>();
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<HeldSale> HeldSales => Set<HeldSale>();
    public DbSet<HeldSaleEnd> HeldSaleEnds => Set<HeldSaleEnd>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<BusinessDocument> Documents => Set<BusinessDocument>();
    public DbSet<Movement> Movements => Set<Movement>();
    public DbSet<JournalLine> Journal => Set<JournalLine>();
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<SyncChange> SyncChanges => Set<SyncChange>();
    public DbSet<UpdateStatus> UpdateStatuses => Set<UpdateStatus>();
    public DbSet<SyncPeer> SyncPeers => Set<SyncPeer>();
    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();
    /// <summary>Set while applying changes received from another installation: those are not captured again at a branch, and derived rows are not re-created.</summary>
    internal string? SyncApplyingOrigin { get; set; }
    public DbSet<CapturedDocument> Captures => Set<CapturedDocument>();
    public DbSet<CaptureRevision> CaptureRevisions => Set<CaptureRevision>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<BillAllocation> BillAllocations => Set<BillAllocation>();
    public DbSet<BankMatch> BankMatches => Set<BankMatch>();
    public DbSet<BankMatchCancellation> BankMatchCancellations => Set<BankMatchCancellation>();
    public DbSet<LedgerBudget> LedgerBudgets => Set<LedgerBudget>();
    public DbSet<TradeOrder> TradeOrders => Set<TradeOrder>();
    public DbSet<TradeOrderLine> TradeOrderLines => Set<TradeOrderLine>();
    public DbSet<OrderFulfillment> OrderFulfillments => Set<OrderFulfillment>();
    public DbSet<OrderClosure> OrderClosures => Set<OrderClosure>();
    public DbSet<StockLocation> StockLocations => Set<StockLocation>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<PayrollItem> PayrollItems => Set<PayrollItem>();
    public DbSet<PayrollRuleVersion> PayrollRules => Set<PayrollRuleVersion>();
    public DbSet<VariantGroup> VariantGroups => Set<VariantGroup>();
    public DbSet<VariantMember> VariantMembers => Set<VariantMember>();
    public DbSet<Bundle> Bundles => Set<Bundle>();
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();
    public DbSet<BankFormat> BankFormats => Set<BankFormat>();
    public DbSet<IssuedCheque> IssuedCheques => Set<IssuedCheque>();
    public DbSet<IssuedChequeEvent> IssuedChequeEvents => Set<IssuedChequeEvent>();
    public DbSet<TillAssignment> TillAssignments => Set<TillAssignment>();
    public DbSet<YearEndClosure> YearEndClosures => Set<YearEndClosure>();
    public DbSet<EmployeeLoan> EmployeeLoans => Set<EmployeeLoan>();
    public DbSet<JobState> JobStates => Set<JobState>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<ReminderDismissal> ReminderDismissals => Set<ReminderDismissal>();
    public DbSet<PayrollPaymentLine> PayrollPaymentLines => Set<PayrollPaymentLine>();
    public DbSet<StatutoryRemittance> StatutoryRemittances => Set<StatutoryRemittance>();
    public DbSet<BusinessParty> Parties => Set<BusinessParty>();
    public DbSet<ReturnAllocation> ReturnAllocations => Set<ReturnAllocation>();
    public DbSet<PeriodClosure> PeriodClosures => Set<PeriodClosure>();
    public DbSet<SalesTaxRate> SalesTaxRates => Set<SalesTaxRate>();
    public DbSet<PricePolicy> PricePolicies => Set<PricePolicy>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<ReservationRelease> ReservationReleases => Set<ReservationRelease>();
    public DbSet<DeliveryInvoice> DeliveryInvoices => Set<DeliveryInvoice>();
    public DbSet<CustomerCredit> CustomerCredits => Set<CustomerCredit>();
    public DbSet<CustomerCreditUse> CustomerCreditUses => Set<CustomerCreditUse>();
    public DbSet<BankStatement> BankStatements => Set<BankStatement>();
    public DbSet<BankStatementRow> BankStatementRows => Set<BankStatementRow>();
    public DbSet<BankStatementVoid> BankStatementVoids => Set<BankStatementVoid>();
    public DbSet<ReceivedCheque> ReceivedCheques => Set<ReceivedCheque>();
    public DbSet<ChequeEvent> ChequeEvents => Set<ChequeEvent>();
    public DbSet<SupplierCredit> SupplierCredits => Set<SupplierCredit>();
    public DbSet<SupplierCreditUse> SupplierCreditUses => Set<SupplierCreditUse>();
    public DbSet<TillShift> TillShifts => Set<TillShift>();
    public DbSet<TillClose> TillCloses => Set<TillClose>();
    public DbSet<TillEntry> TillEntries => Set<TillEntry>();
    public DbSet<IntegritySeal> IntegritySeals => Set<IntegritySeal>();
    public DbSet<Imports.ImportBatch> ImportBatches => Set<Imports.ImportBatch>();
    public DbSet<Imports.ImportRow> ImportRows => Set<Imports.ImportRow>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<ForeignBill> ForeignBills => Set<ForeignBill>();
    public DbSet<ForeignSettlement> ForeignSettlements => Set<ForeignSettlement>();
    public DbSet<LandedCostAllocation> LandedCostAllocations => Set<LandedCostAllocation>();
    public DbSet<InvoiceMatch> InvoiceMatches => Set<InvoiceMatch>();
    public DbSet<OrderCurrency> OrderCurrencies => Set<OrderCurrency>();
    public DbSet<EquipmentTermsVersion> EquipmentTerms => Set<EquipmentTermsVersion>();
    public DbSet<ProductLink> ProductLinks => Set<ProductLink>();
    public DbSet<ProductLinkRemoval> ProductLinkRemovals => Set<ProductLinkRemoval>();
    public DbSet<UnitProfile> UnitProfiles => Set<UnitProfile>();
    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<InstallationUnit> InstallationUnits => Set<InstallationUnit>();
    public DbSet<InstallationCompletion> InstallationCompletions => Set<InstallationCompletion>();
    public DbSet<InstallationCancellation> InstallationCancellations => Set<InstallationCancellation>();
    public DbSet<Warranty> Warranties => Set<Warranty>();
    public DbSet<StoredDocument> StoredDocuments => Set<StoredDocument>();
    public DbSet<StoredDocumentRevision> StoredDocumentRevisions => Set<StoredDocumentRevision>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<FileBlob> FileBlobs => Set<FileBlob>();
    public DbSet<DocumentThumbnail> DocumentThumbnails => Set<DocumentThumbnail>();
    public DbSet<Tender> Tenders => Set<Tender>();
    public DbSet<TenderBid> TenderBids => Set<TenderBid>();
    public DbSet<TenderEvent> TenderEvents => Set<TenderEvent>();
    public DbSet<TenderAward> TenderAwards => Set<TenderAward>();
    public DbSet<ServiceJob> ServiceJobs => Set<ServiceJob>();
    public DbSet<ServiceJobEvent> ServiceJobEvents => Set<ServiceJobEvent>();
    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();
    public DbSet<MaintenancePlanEnd> MaintenancePlanEnds => Set<MaintenancePlanEnd>();
    public DbSet<ControlPolicyVersion> ControlPolicies => Set<ControlPolicyVersion>();
    public DbSet<CompanyProfileVersion> CompanyProfiles => Set<CompanyProfileVersion>();
    public DbSet<DocumentTemplateVersion> DocumentTemplates => Set<DocumentTemplateVersion>();
    public DbSet<TaxAssignment> TaxAssignments => Set<TaxAssignment>();
    public DbSet<FiscalSubmission> FiscalSubmissions => Set<FiscalSubmission>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();
    public DbSet<ApprovalUse> ApprovalUses => Set<ApprovalUse>();
    public DbSet<ApprovalNote> ApprovalNotes => Set<ApprovalNote>();
    public DbSet<StoredDocumentLink> StoredDocumentLinks => Set<StoredDocumentLink>();
    public DbSet<StoredDocumentLinkRemoval> StoredDocumentLinkRemovals => Set<StoredDocumentLinkRemoval>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // PostgreSQL (Supabase): keep every table out of the "public" schema, which Supabase exposes through its automatic web API.
        if (HistoryProtection.IsPostgres(this)) builder.HasDefaultSchema(HistoryProtection.PostgresSchema);
        string NotNull(string column) => HistoryProtection.IsPostgres(this) ? $"\"{column}\" IS NOT NULL" : $"[{column}] IS NOT NULL";
        builder.Entity<InventoryRevision>().HasIndex(x => new { x.Branch, x.Kind, x.Key, x.Version }).IsUnique();
        builder.Entity<ProductBarcode>().HasIndex(x => x.Code).IsUnique();
        builder.Entity<ProductBarcode>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<HeldSaleEnd>().HasIndex(x => x.HeldSaleId).IsUnique();
        builder.Entity<HeldSaleEnd>().HasOne<HeldSale>().WithMany().HasForeignKey(x => x.HeldSaleId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<HeldSaleEnd>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankStatement>().HasIndex(x => new { x.Branch, x.AccountCode, x.Reference }).IsUnique();
        builder.Entity<BankStatement>().HasIndex(x => new { x.Branch, x.AccountCode, x.ContentHash }).IsUnique();
        builder.Entity<BankStatement>().HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.AccountCode).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankStatementRow>().HasOne<BankStatement>().WithMany().HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankStatementRow>().HasIndex(x => new { x.StatementId, x.Line }).IsUnique();
        builder.Entity<BankStatementVoid>().HasOne<BankStatement>().WithMany().HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankStatementVoid>().HasIndex(x => x.StatementId).IsUnique();
        builder.Entity<BankMatch>().HasOne<BankStatementRow>().WithMany().HasForeignKey(x => x.StatementRowId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ReceivedCheque>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ReceivedCheque>().HasIndex(x => x.SourceDocumentId).IsUnique();
        builder.Entity<ReceivedCheque>().HasIndex(x => new { x.Branch, x.Party, x.Reference, x.Bank }).IsUnique();
        builder.Entity<ChequeEvent>().HasOne<ReceivedCheque>().WithMany().HasForeignKey(x => x.ChequeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ChequeEvent>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ChequeEvent>().HasIndex(x => new { x.ChequeId, x.Version }).IsUnique();
        builder.Entity<SupplierCredit>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierCredit>().HasIndex(x => x.ReturnId).IsUnique();
        builder.Entity<SupplierCreditUse>().HasOne<SupplierCredit>().WithMany().HasForeignKey(x => x.CreditId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierCreditUse>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TillShift>().HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.AccountCode).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TillClose>().HasOne<TillShift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TillClose>().HasIndex(x => x.ShiftId).IsUnique();
        builder.Entity<TillEntry>().HasOne<TillShift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TillEntry>().HasOne<JournalLine>().WithMany().HasForeignKey(x => x.JournalLineId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TillEntry>().HasIndex(x => x.JournalLineId).IsUnique();
        builder.Entity<PricePolicy>().HasIndex(x => new { x.ProductId, x.Version }).IsUnique();
        builder.Entity<PricePolicy>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<StockReservation>().HasOne<TradeOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<StockReservation>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<StockReservation>().HasOne<StockLocation>().WithMany().HasForeignKey(x => x.Location).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ReservationRelease>().HasOne<StockReservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<DeliveryInvoice>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<DeliveryInvoice>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<DeliveryInvoice>().HasIndex(x => x.InvoiceId).IsUnique();
        builder.Entity<CustomerCredit>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CustomerCredit>().HasIndex(x => x.ReturnId).IsUnique();
        builder.Entity<CustomerCreditUse>().HasOne<CustomerCredit>().WithMany().HasForeignKey(x => x.CreditId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CustomerCreditUse>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesTaxRate>().HasIndex(x => new { x.Branch, x.From }).IsUnique();
        builder.Entity<ReturnAllocation>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.OriginalDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ReturnAllocation>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.ReturnDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BusinessParty>().HasIndex(x => new { x.Branch, x.Code }).IsUnique();
        builder.Entity<BusinessParty>().HasIndex(x => new { x.Branch, x.NormalizedName }).IsUnique();
        builder.Entity<BusinessParty>().Property(x => x.Version).IsConcurrencyToken();
        builder.Entity<Employee>().HasIndex(x => new { x.Branch, x.Code }).IsUnique();
        builder.Entity<Employee>().Property(x => x.Version).IsConcurrencyToken();
        builder.Entity<PayrollItem>().HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PayrollItem>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Employee>().Property(x => x.Tin).HasMaxLength(30);
        builder.Entity<Employee>().Property(x => x.NassitNumber).HasMaxLength(30);
        builder.Entity<PayrollRuleVersion>().HasIndex(x => new { x.Branch, x.Version }).IsUnique();
        builder.Entity<VariantGroup>().HasIndex(x => x.Code).IsUnique();
        builder.Entity<VariantMember>().HasKey(x => x.ProductId);
        builder.Entity<VariantMember>().HasIndex(x => x.GroupId);
        builder.Entity<VariantMember>().HasOne<Product>().WithOne().HasForeignKey<VariantMember>(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<VariantMember>().HasOne<VariantGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Bundle>().HasIndex(x => x.Code).IsUnique();
        builder.Entity<PriceRule>().HasIndex(x => x.Code).IsUnique();
        builder.Entity<BankFormat>().HasIndex(x => x.Bank).IsUnique();
        builder.Entity<IssuedCheque>().HasIndex(x => new { x.Branch, x.BankAccount, x.Number }).IsUnique();
        builder.Entity<IssuedCheque>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<IssuedChequeEvent>().HasOne<IssuedCheque>().WithMany().HasForeignKey(x => x.ChequeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TillAssignment>().HasIndex(x => new { x.Branch, x.AccountCode, x.Cashier }).IsUnique();
        builder.Entity<YearEndClosure>().HasIndex(x => new { x.Branch, x.YearEnd }).IsUnique();
        builder.Entity<SyncChange>().HasKey(x => x.Seq);
        builder.Entity<SyncChange>().HasIndex(x => new { x.Origin, x.Seq });
        builder.Entity<SyncChange>().HasIndex(x => new { x.Scope, x.Seq });
        builder.Entity<SyncChange>().HasIndex(x => x.Commit);
        builder.Entity<SyncPeer>().HasKey(x => x.Name);
        builder.Entity<SyncPeer>().Property(x => x.Name).HasMaxLength(60);
        builder.Entity<SyncConflict>().HasIndex(x => x.Time);
        builder.Entity<UpdateStatus>().HasKey(x => x.Branch);
        builder.Entity<UpdateStatus>().Property(x => x.Branch).HasMaxLength(60);
        builder.Entity<UpdateStatus>().Property(x => x.Url).HasMaxLength(500);
        builder.Entity<EmployeeLoan>().HasIndex(x => x.EmployeeId);
        builder.Entity<EmployeeLoan>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PriceRule>().Property(x => x.Version).IsConcurrencyToken();
        builder.Entity<JobState>().HasKey(x => new { x.Branch, x.Job });
        builder.Entity<JobRun>().HasIndex(x => new { x.Branch, x.Job });
        builder.Entity<Reminder>().HasIndex(x => new { x.Branch, x.Key }).IsUnique();
        builder.Entity<ReminderDismissal>().HasIndex(x => new { x.ReminderId, x.Actor }).IsUnique();
        builder.Entity<ReminderDismissal>().HasOne<Reminder>().WithMany().HasForeignKey(x => x.ReminderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Bundle>().Property(x => x.Version).IsConcurrencyToken();
        builder.Entity<PayrollRuleVersion>().HasIndex(x => new { x.Branch, x.EffectiveFrom });
        builder.Entity<PayrollPaymentLine>().HasIndex(x => x.PayrollItemId);
        builder.Entity<PayrollPaymentLine>().HasOne<PayrollItem>().WithMany().HasForeignKey(x => x.PayrollItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PayrollPaymentLine>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<StatutoryRemittance>().HasIndex(x => new { x.Branch, x.Month, x.Kind });
        builder.Entity<StatutoryRemittance>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<StockLocation>().HasKey(x => x.Code);
        builder.Entity<StockLocation>().HasData(new StockLocation { Code = "MAIN", Name = "Main warehouse" });
        builder.Entity<Movement>().Property(x => x.Location).HasDefaultValue("MAIN");
        builder.Entity<Movement>().HasOne<StockLocation>().WithMany().HasForeignKey(x => x.Location).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TradeOrder>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<TradeOrder>().HasIndex(x => x.QuotationId).IsUnique().HasFilter(NotNull("QuotationId"));
        builder.Entity<TradeOrder>().HasOne<TradeOrder>().WithMany().HasForeignKey(x => x.QuotationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TradeOrderLine>().HasOne<TradeOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TradeOrderLine>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TradeOrderLine>().HasIndex(x => new { x.OrderId, x.ProductId }).IsUnique();
        builder.Entity<OrderFulfillment>().HasOne<TradeOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OrderFulfillment>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OrderClosure>().HasOne<TradeOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OrderClosure>().HasIndex(x => x.OrderId).IsUnique();
        builder.Entity<LedgerAccount>().HasData(LedgerAccount.Standard);
        builder.Entity<LedgerAccount>().Property(x => x.Id).ValueGeneratedNever();
        builder.Entity<BillAllocation>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BillAllocation>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BillAllocation>().HasIndex(x => new { x.VoucherId, x.InvoiceId }).IsUnique();
        builder.Entity<BankMatch>().HasOne<JournalLine>().WithMany().HasForeignKey(x => x.JournalLineId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankMatch>().HasIndex(x => x.JournalLineId);
        builder.Entity<BankMatchCancellation>().HasIndex(x => x.MatchId).IsUnique();
        builder.Entity<BankMatchCancellation>().HasOne<BankMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ExchangeRate>().HasIndex(x => new { x.Branch, x.Currency, x.EffectiveFrom }).IsUnique();
        builder.Entity<ForeignBill>().HasIndex(x => x.DocumentId).IsUnique();
        builder.Entity<ForeignBill>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ForeignSettlement>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ForeignSettlement>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<LandedCostAllocation>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<LandedCostAllocation>().HasOne<Movement>().WithMany().HasForeignKey(x => x.ReceiptMovementId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InvoiceMatch>().HasIndex(x => new { x.InvoiceId, x.ReceiptMovementId }).IsUnique();
        builder.Entity<InvoiceMatch>().HasIndex(x => x.ReceiptId);
        builder.Entity<InvoiceMatch>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InvoiceMatch>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InvoiceMatch>().HasOne<Movement>().WithMany().HasForeignKey(x => x.ReceiptMovementId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OrderCurrency>().HasIndex(x => x.OrderId).IsUnique();
        builder.Entity<OrderCurrency>().HasOne<TradeOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EquipmentTermsVersion>().HasIndex(x => new { x.ProductId, x.Version }).IsUnique();
        builder.Entity<EquipmentTermsVersion>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProductLink>().HasOne<Product>().WithMany().HasForeignKey(x => x.EquipmentProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProductLink>().HasOne<Product>().WithMany().HasForeignKey(x => x.ItemProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProductLinkRemoval>().HasIndex(x => x.LinkId).IsUnique();
        builder.Entity<ProductLinkRemoval>().HasOne<ProductLink>().WithMany().HasForeignKey(x => x.LinkId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<UnitProfile>().HasIndex(x => new { x.ProductId, x.Serial, x.Version }).IsUnique();
        builder.Entity<Installation>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<Installation>().HasIndex(x => x.SaleId);
        builder.Entity<Installation>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InstallationUnit>().HasIndex(x => new { x.InstallationId, x.ProductId, x.Serial }).IsUnique();
        builder.Entity<InstallationUnit>().HasIndex(x => x.Serial);
        builder.Entity<InstallationUnit>().HasOne<Installation>().WithMany().HasForeignKey(x => x.InstallationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InstallationCompletion>().HasIndex(x => x.InstallationId).IsUnique();
        builder.Entity<InstallationCompletion>().HasOne<Installation>().WithMany().HasForeignKey(x => x.InstallationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InstallationCancellation>().HasIndex(x => x.InstallationId).IsUnique();
        builder.Entity<InstallationCancellation>().HasOne<Installation>().WithMany().HasForeignKey(x => x.InstallationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<DocumentThumbnail>().HasIndex(x => x.Sha256).IsUnique();
        builder.Entity<Tender>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<TenderBid>().HasIndex(x => new { x.TenderId, x.Version }).IsUnique();
        builder.Entity<TenderBid>().HasOne<Tender>().WithMany().HasForeignKey(x => x.TenderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TenderEvent>().HasIndex(x => new { x.TenderId, x.Sequence }).IsUnique();
        builder.Entity<TenderEvent>().HasOne<Tender>().WithMany().HasForeignKey(x => x.TenderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TenderAward>().HasIndex(x => x.TenderId).IsUnique();
        builder.Entity<TenderAward>().HasOne<Tender>().WithMany().HasForeignKey(x => x.TenderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TenderAward>().HasOne<TradeOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ServiceJob>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<ServiceJob>().HasIndex(x => new { x.PlanId, x.PlanDue }).IsUnique();
        builder.Entity<ServiceJob>().HasIndex(x => new { x.ProductId, x.Serial });
        builder.Entity<ServiceJob>().HasOne<MaintenancePlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ServiceJobEvent>().HasIndex(x => new { x.JobId, x.Sequence }).IsUnique();
        builder.Entity<ServiceJobEvent>().HasIndex(x => x.DocumentId);
        builder.Entity<ServiceJobEvent>().HasOne<ServiceJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<MaintenancePlan>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<MaintenancePlanEnd>().HasIndex(x => x.PlanId).IsUnique();
        builder.Entity<MaintenancePlanEnd>().HasOne<MaintenancePlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ControlPolicyVersion>().HasIndex(x => new { x.Branch, x.Version }).IsUnique();
        builder.Entity<CompanyProfileVersion>().HasIndex(x => new { x.Branch, x.Version }).IsUnique();
        builder.Entity<DocumentTemplateVersion>().HasIndex(x => new { x.Branch, x.Version }).IsUnique();
        builder.Entity<TaxAssignment>().HasIndex(x => new { x.ProductId, x.From }).IsUnique();
        builder.Entity<FiscalSubmission>().HasIndex(x => x.DocumentId);
        builder.Entity<TaxAssignment>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FiscalSubmission>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FiscalSubmission>().HasIndex(x => new { x.Branch, x.Outcome });
        builder.Entity<ApprovalRequest>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<ApprovalRequest>().HasIndex(x => new { x.Branch, x.PayloadHash });
        builder.Entity<ApprovalDecision>().HasIndex(x => x.RequestId).IsUnique();
        builder.Entity<ApprovalDecision>().HasOne<ApprovalRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ApprovalUse>().HasIndex(x => x.RequestId).IsUnique();
        builder.Entity<ApprovalUse>().HasIndex(x => x.OperationId);
        builder.Entity<ApprovalUse>().HasOne<ApprovalRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ApprovalNote>().HasOne<ApprovalRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Warranty>().HasIndex(x => new { x.SaleId, x.ProductId, x.Serial }).IsUnique();
        builder.Entity<Warranty>().HasIndex(x => x.Serial);
        builder.Entity<Warranty>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<StoredDocument>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<StoredDocument>().HasIndex(x => x.Branch);
        builder.Entity<StoredDocumentRevision>().HasIndex(x => new { x.DocumentId, x.Version }).IsUnique();
        builder.Entity<StoredDocumentRevision>().HasOne<StoredDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<StoredFile>().HasIndex(x => new { x.DocumentId, x.Version }).IsUnique();
        builder.Entity<StoredFile>().HasIndex(x => x.Sha256);
        builder.Entity<StoredFile>().HasOne<StoredDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FileBlob>().HasIndex(x => x.Sha256).IsUnique();
        builder.Entity<StoredDocumentLink>().HasIndex(x => new { x.Target, x.Key });
        builder.Entity<StoredDocumentLink>().HasOne<StoredDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<StoredDocumentLinkRemoval>().HasIndex(x => x.LinkId).IsUnique();
        builder.Entity<StoredDocumentLinkRemoval>().HasOne<StoredDocumentLink>().WithMany().HasForeignKey(x => x.LinkId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Imports.ImportBatch>().HasIndex(x => new { x.Branch, x.Key, x.Status });
        builder.Entity<Imports.ImportRow>().HasOne<Imports.ImportBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Imports.ImportRow>().HasIndex(x => new { x.BatchId, x.RowNumber }).IsUnique();
        builder.Entity<IntegritySeal>().HasKey(x => x.Sequence);
        builder.Entity<IntegritySeal>().Property(x => x.Sequence).ValueGeneratedNever();
        builder.Entity<LedgerBudget>().HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.AccountCode).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CapturedDocument>().HasIndex(x => new { x.Branch, x.Sha256 }).IsUnique();
        builder.Entity<CaptureRevision>().HasIndex(x => new { x.CaptureId, x.Version }).IsUnique();
        builder.Entity<CaptureRevision>().HasOne<CapturedDocument>().WithMany().HasForeignKey(x => x.CaptureId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Product>().HasIndex(x => x.Sku).IsUnique();
        builder.Entity<BusinessDocument>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<BusinessDocument>().HasIndex(x => x.Reverses).IsUnique().HasFilter(NotNull("Reverses"));
        builder.Entity<BusinessDocument>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.Reverses).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Movement>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Movement>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<JournalLine>().HasOne<BusinessDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Movement>().HasIndex(x => new { x.Branch, x.ProductId });
        foreach (var entity in builder.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
        {
            if (property.ClrType == typeof(decimal)) { property.SetPrecision(19); property.SetScale(4); }
            if (property.ClrType == typeof(string) && property.GetMaxLength() is null && property.Name is not "Detail" and not "Payload" and not "Rows" && !property.Name.EndsWith("Json", StringComparison.Ordinal)) property.SetMaxLength(200);
        }
    }
    private void GuardHistory()
    {
        if (ChangeTracker.Entries().Any(x => (x.State is EntityState.Modified or EntityState.Deleted) && (x.Entity is PeriodClosure or SalesTaxRate))) throw new BusinessException("Period and tax history is immutable.");
        if (ChangeTracker.Entries<ReturnAllocation>().Any(x => x.State is EntityState.Modified or EntityState.Deleted)) throw new BusinessException("Return history is immutable. Use a reversal.");
        if (ChangeTracker.Entries<PayrollItem>().Any(x => x.State is EntityState.Modified or EntityState.Deleted)) throw new BusinessException("Payroll history is immutable. Reverse the payroll voucher.");
        if (ChangeTracker.Entries().Any(x => (x.State is EntityState.Deleted or EntityState.Modified) && (x.Entity is TradeOrder or TradeOrderLine or OrderFulfillment or OrderClosure or StockLocation))) throw new BusinessException("Master and order history is immutable. Create a replacement.");
        if (ChangeTracker.Entries().Any(x => (x.State is EntityState.Deleted or EntityState.Modified) && (x.Entity is InventoryRevision or ProductBarcode or HeldSale or HeldSaleEnd or BankStatement or BankStatementRow or BankStatementVoid or ReceivedCheque or ChequeEvent or SupplierCredit or SupplierCreditUse or TillShift or TillClose or TillEntry or PricePolicy or StockReservation or ReservationRelease or DeliveryInvoice or CustomerCredit or CustomerCreditUse or BusinessDocument or Movement or JournalLine or Operation or CapturedDocument or CaptureRevision or LedgerAccount or BillAllocation or BankMatch or BankMatchCancellation or LedgerBudget)))
            throw new BusinessException("Posted history is immutable. Create a reversal.");
        if (ChangeTracker.Entries().Any(x => (x.State is EntityState.Modified or EntityState.Deleted) && (x.Entity is IntegritySeal || HistoryProtection.ProtectedTypes.Contains(x.Entity.GetType())))) throw new BusinessException("Posted history is immutable. Create a reversal.");
    }
    private List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry> NewHistory() =>
        ChangeTracker.Entries().Where(x => x.State == EntityState.Added && HistoryProtection.ProtectedTypes.Contains(x.Entity.GetType())).ToList();
    // Every save that adds posted history also appends a seal to the tamper-evidence chain, atomically in the same save.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardHistory(); if (SyncApplyingOrigin is null) BankingRules.CaptureTillEntries(this).GetAwaiter().GetResult();
        HistoryProtection.NormalizeTimes(this);
        var history = NewHistory(); if (history.Count > 0) HistoryProtection.AddCommitSeal(this, history);
        var saved = base.SaveChanges(acceptAllChangesOnSuccess);
        if (history.Count > 0) ChangeNotifier.Raise();
        return saved;
    }
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardHistory(); if (SyncApplyingOrigin is null) await BankingRules.CaptureTillEntries(this, cancellationToken).ConfigureAwait(false);
        HistoryProtection.NormalizeTimes(this);
        var history = NewHistory(); if (history.Count > 0) await HistoryProtection.AddCommitSealAsync(this, history, cancellationToken).ConfigureAwait(false);
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        if (history.Count > 0) ChangeNotifier.Raise(); // listeners debounce, so a transaction committing just after this is picked up
        return saved;
    }
}
public sealed class TradingOptions
{
    public string Branch { get; set; } = "FREETOWN";
    public string Device { get; set; } = "COUNTER01";
    public decimal TaxRate { get; set; }
    public DateOnly ClosedThrough { get; set; } = new(2000, 1, 1);
}





