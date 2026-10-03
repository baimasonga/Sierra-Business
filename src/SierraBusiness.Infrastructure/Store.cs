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
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
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
        builder.Entity<StockLocation>().HasKey(x => x.Code);
        builder.Entity<StockLocation>().HasData(new StockLocation { Code = "MAIN", Name = "Main warehouse" });
        builder.Entity<Movement>().Property(x => x.Location).HasDefaultValue("MAIN");
        builder.Entity<Movement>().HasOne<StockLocation>().WithMany().HasForeignKey(x => x.Location).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TradeOrder>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<TradeOrder>().HasIndex(x => x.QuotationId).IsUnique().HasFilter("[QuotationId] IS NOT NULL");
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
        builder.Entity<BusinessDocument>().HasIndex(x => x.Reverses).IsUnique().HasFilter("[Reverses] IS NOT NULL");
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
        GuardHistory(); BankingRules.CaptureTillEntries(this).GetAwaiter().GetResult();
        var history = NewHistory(); if (history.Count > 0) HistoryProtection.AddCommitSeal(this, history);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardHistory(); await BankingRules.CaptureTillEntries(this, cancellationToken).ConfigureAwait(false);
        var history = NewHistory(); if (history.Count > 0) await HistoryProtection.AddCommitSealAsync(this, history, cancellationToken).ConfigureAwait(false);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
    }
}
public sealed class TradingOptions
{
    public string Branch { get; set; } = "FREETOWN";
    public string Device { get; set; } = "COUNTER01";
    public decimal TaxRate { get; set; }
    public DateOnly ClosedThrough { get; set; } = new(2000, 1, 1);
}





