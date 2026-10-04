using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
namespace SierraBusiness.Infrastructure;

/// <summary>
/// One link in the tamper-evidence chain. Every commit that adds posted history appends a seal listing the
/// fingerprint of each new row; each seal's hash covers the previous seal, so editing, deleting or inserting
/// history outside the application breaks verification.
/// </summary>
public sealed class IntegritySeal
{
    public long Sequence { get; set; }
    public string Kind { get; set; } = "";
    public DateTimeOffset Time { get; set; }
    public string PreviousHash { get; set; } = "";
    public string Rows { get; set; } = "";
    public string Hash { get; set; } = "";
}

public static class HistoryProtection
{
    public const string TriggerPrefix = "sb_protect_";
    public const string CommitSeal = "commit", BaselineSeal = "baseline", UpgradeSeal = "upgrade";

    /// <summary>Append-only business history. Products, parties, employees and Identity records remain editable masters.</summary>
    public static readonly IReadOnlySet<Type> ProtectedTypes = new HashSet<Type>
    {
        typeof(BusinessDocument), typeof(Movement), typeof(JournalLine), typeof(Operation),
        typeof(InventoryRevision), typeof(ProductBarcode), typeof(HeldSale), typeof(HeldSaleEnd),
        typeof(BankStatement), typeof(BankStatementRow), typeof(BankStatementVoid), typeof(ReceivedCheque), typeof(ChequeEvent),
        typeof(SupplierCredit), typeof(SupplierCreditUse), typeof(TillShift), typeof(TillClose), typeof(TillEntry),
        typeof(PricePolicy), typeof(StockReservation), typeof(ReservationRelease), typeof(DeliveryInvoice),
        typeof(CustomerCredit), typeof(CustomerCreditUse), typeof(CapturedDocument), typeof(CaptureRevision),
        typeof(LedgerAccount), typeof(BillAllocation), typeof(BankMatch), typeof(BankMatchCancellation), typeof(LedgerBudget),
        typeof(TradeOrder), typeof(TradeOrderLine), typeof(OrderFulfillment), typeof(OrderClosure), typeof(StockLocation),
        typeof(PeriodClosure), typeof(SalesTaxRate), typeof(ReturnAllocation), typeof(PayrollItem),
        typeof(ExchangeRate), typeof(ForeignBill), typeof(ForeignSettlement), typeof(LandedCostAllocation), typeof(InvoiceMatch), typeof(OrderCurrency),
        typeof(EquipmentTermsVersion), typeof(ProductLink), typeof(ProductLinkRemoval), typeof(UnitProfile), typeof(Installation), typeof(InstallationUnit), typeof(InstallationCompletion), typeof(InstallationCancellation), typeof(Warranty), typeof(Tender), typeof(TenderBid), typeof(TenderEvent), typeof(TenderAward), typeof(ServiceJob), typeof(ServiceJobEvent), typeof(MaintenancePlan), typeof(MaintenancePlanEnd), typeof(ControlPolicyVersion), typeof(ApprovalRequest), typeof(ApprovalDecision), typeof(ApprovalUse), typeof(ApprovalNote), typeof(CompanyProfileVersion), typeof(DocumentTemplateVersion), typeof(TaxAssignment), typeof(FiscalSubmission), typeof(PayrollRuleVersion), typeof(PayrollPaymentLine), typeof(StatutoryRemittance),
        typeof(StoredDocument), typeof(StoredDocumentRevision), typeof(StoredFile), typeof(FileBlob), typeof(StoredDocumentLink), typeof(StoredDocumentLinkRemoval)
    };

    private static bool IsSqlite(BusinessDb db) => db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";
    private static IEnumerable<IEntityType> ProtectedEntities(BusinessDb db) => db.Model.GetEntityTypes().Where(x => ProtectedTypes.Contains(x.ClrType)).OrderBy(x => x.GetTableName(), StringComparer.Ordinal);
    private static IEnumerable<string> GuardedTables(BusinessDb db) => ProtectedEntities(db).Select(x => x.GetTableName()!).Append(db.Model.FindEntityType(typeof(IntegritySeal))!.GetTableName()!).Distinct();

    /// <summary>Applies pending migrations with database guards lifted only for the duration of the upgrade, then reinstates them and seals any rows the upgrade introduced.</summary>
    public static async Task MigrateAsync(BusinessDb db, CancellationToken ct = default)
    {
        var upgrading = (await db.Database.GetPendingMigrationsAsync(ct)).Any();
        if (IsSqlite(db)) await SuspendGuards(db, ct);
        await db.Database.MigrateAsync(ct);
        await ApplyGuards(db, ct);
        // Full re-scan only when the schema changed or protection is new; ordinary startups stay fast.
        if (upgrading || !await db.Set<IntegritySeal>().AnyAsync(ct)) await SealUncovered(db, ct);
    }

    public static async Task SuspendGuards(BusinessDb db, CancellationToken ct = default)
    {
        if (!IsSqlite(db)) return;
        var names = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'sb_protect_%'").ToListAsync(ct);
        foreach (var name in names) { var sql = "DROP TRIGGER IF EXISTS \"" + Identifier(name) + "\""; await db.Database.ExecuteSqlRawAsync(sql, ct); }
    }

    /// <summary>Database-level guards: posted rows cannot be updated or deleted even by tools that bypass the application.</summary>
    public static async Task ApplyGuards(BusinessDb db, CancellationToken ct = default)
    {
        if (!IsSqlite(db)) return;
        foreach (var table in GuardedTables(db))
        foreach (var operation in new[] { "UPDATE", "DELETE" })
        {
            var sql = "CREATE TRIGGER IF NOT EXISTS \"" + Identifier(TriggerPrefix + table + "_" + operation.ToLowerInvariant()) + "\" BEFORE " + operation + " ON \"" + Identifier(table) + "\" BEGIN SELECT RAISE(ABORT, 'Posted history is immutable. Create a reversal.'); END;";
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
    }

    // Only model table names and our own trigger names reach SQL; reject anything that is not a plain identifier.
    private static string Identifier(string name) => System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]{1,128}$") ? name : throw new InvalidOperationException("Unexpected database identifier.");

    public static async Task<int> GuardCount(BusinessDb db, CancellationToken ct = default) => !IsSqlite(db) ? 0 :
        (await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'sb_protect_%'").ToListAsync(ct)).Count;

    public static int ExpectedGuardCount(BusinessDb db) => GuardedTables(db).Count() * 2;

    /// <summary>Called inside SaveChanges after the business rows are written, in the same transaction.</summary>
    internal static void AddCommitSeal(BusinessDb db, IReadOnlyList<EntityEntry> written)
    {
        var rows = written.Select(x => RowFingerprint(x.Metadata, x.Entity)).ToList();
        var previous = db.Set<IntegritySeal>().AsNoTracking().OrderByDescending(x => x.Sequence).FirstOrDefault();
        db.Set<IntegritySeal>().Add(NewSeal(previous, CommitSeal, rows));
    }

    internal static async Task AddCommitSealAsync(BusinessDb db, IReadOnlyList<EntityEntry> written, CancellationToken ct)
    {
        var rows = written.Select(x => RowFingerprint(x.Metadata, x.Entity)).ToList();
        var previous = await db.Set<IntegritySeal>().AsNoTracking().OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
        db.Set<IntegritySeal>().Add(NewSeal(previous, CommitSeal, rows));
    }

    private static IntegritySeal NewSeal(IntegritySeal? previous, string kind, List<string> rows)
    {
        var seal = new IntegritySeal { Sequence = (previous?.Sequence ?? 0) + 1, Kind = kind, Time = DateTimeOffset.UtcNow, PreviousHash = previous?.Hash ?? "", Rows = JsonSerializer.Serialize(rows) };
        seal.Hash = SealHash(seal);
        return seal;
    }

    private static string SealHash(IntegritySeal seal) => Hex(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', seal.PreviousHash, seal.Sequence.ToString(CultureInfo.InvariantCulture), seal.Kind, seal.Time.UtcTicks.ToString(CultureInfo.InvariantCulture), seal.Rows))));

    /// <summary>Seals rows that no seal covers yet: the whole history on first upgrade (baseline) or rows added by a later migration.</summary>
    public static async Task<int> SealUncovered(BusinessDb db, CancellationToken ct = default)
    {
        var covered = (await ReadSeals(db, ct)).SelectMany(x => Parse(x.Rows)).Select(KeyOf).ToHashSet(StringComparer.Ordinal);
        var rows = (await CurrentRows(db, ct)).Where(x => !covered.Contains(KeyOf(x))).ToList();
        if (rows.Count == 0) return 0;
        var previous = await db.Set<IntegritySeal>().AsNoTracking().OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
        db.Set<IntegritySeal>().Add(NewSeal(previous, previous is null ? BaselineSeal : UpgradeSeal, rows));
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    public static async Task<IntegrityReport> Verify(BusinessDb db, CancellationToken ct = default)
    {
        var problems = new List<string>(); var notes = new List<string>();
        void Problem(string text) { if (problems.Count < 100) problems.Add(text); else if (problems.Count == 100) problems.Add("Further problems were found and are not listed."); }
        var seals = await ReadSeals(db, ct);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        string previousHash = "";
        for (var i = 0; i < seals.Count; i++)
        {
            var seal = seals[i];
            if (seal.Sequence != i + 1) Problem($"Seal sequence is broken at {seal.Sequence}: a seal was removed or inserted.");
            if (seal.PreviousHash != previousHash) Problem($"Seal {seal.Sequence} does not link to the seal before it.");
            if (SealHash(seal) != seal.Hash) Problem($"Seal {seal.Sequence} was altered.");
            if (seal.Kind is BaselineSeal or UpgradeSeal) notes.Add($"{(seal.Kind == BaselineSeal ? "Protection started" : "Upgrade accepted")} on {seal.Time:yyyy-MM-dd HH:mm} UTC covering {Parse(seal.Rows).Count} existing rows.");
            foreach (var row in Parse(seal.Rows)) if (!expected.TryAdd(KeyOf(row), row)) Problem($"Row {KeyOf(row)} is sealed twice.");
            previousHash = seal.Hash;
        }
        var current = await CurrentRows(db, ct);
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in current)
        {
            var key = KeyOf(row); present.Add(key);
            if (!expected.TryGetValue(key, out var sealedRow)) Problem($"{Describe(key)} was added outside the application.");
            else if (sealedRow != row) Problem($"{Describe(key)} was changed after it was posted.");
        }
        foreach (var key in expected.Keys.Where(x => !present.Contains(x))) Problem($"{Describe(key)} was deleted.");
        var guards = await GuardCount(db, ct); var guarded = !IsSqlite(db) || guards >= ExpectedGuardCount(db);
        if (!guarded) Problem($"Database guards are missing ({guards} of {ExpectedGuardCount(db)} present). Someone may have removed them; restart the application to reinstate them.");
        return new(problems.Count == 0, seals.Count, current.Count, previousHash.Length >= 16 ? previousHash[..16] : previousHash, guarded, problems, notes, DateTimeOffset.UtcNow);
    }

    private static async Task<List<IntegritySeal>> ReadSeals(BusinessDb db, CancellationToken ct) => await db.Set<IntegritySeal>().AsNoTracking().OrderBy(x => x.Sequence).ToListAsync(ct);
    private static List<string> Parse(string rows) => JsonSerializer.Deserialize<List<string>>(rows) ?? [];
    private static string KeyOf(string fingerprint) => fingerprint[..fingerprint.LastIndexOf('|')];
    private static string Describe(string key) { var parts = key.Split('|', 2); return $"A {parts[0]} record ({parts[1]})"; }

    private static async Task<List<string>> CurrentRows(BusinessDb db, CancellationToken ct)
    {
        var result = new List<string>();
        foreach (var entity in ProtectedEntities(db))
        {
            var set = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(entity.ClrType).Invoke(db, null)!;
            var noTracking = typeof(EntityFrameworkQueryableExtensions).GetMethod(nameof(EntityFrameworkQueryableExtensions.AsNoTracking))!.MakeGenericMethod(entity.ClrType).Invoke(null, [set])!;
            var task = (Task)typeof(EntityFrameworkQueryableExtensions).GetMethod(nameof(EntityFrameworkQueryableExtensions.ToListAsync))!.MakeGenericMethod(entity.ClrType).Invoke(null, [noTracking, ct])!;
            await task;
            var list = (System.Collections.IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!;
            foreach (var row in list) result.Add(RowFingerprint(entity, row));
        }
        return result;
    }

    /// <summary>"Table|key|sha256" over every mapped column, using a canonical form that survives the SQLite round trip.</summary>
    public static string RowFingerprint(IEntityType entity, object row)
    {
        var key = string.Join('/', entity.FindPrimaryKey()!.Properties.Select(p => Canonical(p.GetGetter().GetClrValue(row))));
        var content = new StringBuilder();
        foreach (var property in entity.GetProperties().Where(x => !x.IsShadowProperty()).OrderBy(x => x.Name, StringComparer.Ordinal))
            content.Append(property.Name).Append('=').Append(Canonical(property.GetGetter().GetClrValue(row))).Append('\u001f');
        return $"{entity.GetTableName()}|{key}|{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())))}";
    }

    private static string Canonical(object? value) => value switch
    {
        null => "␀",
        string s => s,
        decimal d => d.ToString("0.############################", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        DateTimeOffset t => t.UtcTicks.ToString(CultureInfo.InvariantCulture),
        DateTime t => t.Ticks.ToString(CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Guid g => g.ToString("N"),
        bool b => b ? "1" : "0",
        byte[] bytes => Hex(SHA256.HashData(bytes)),
        Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);
}

public sealed class IntegrityService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IIntegrityService
{
    public async Task<IntegrityReport> Verify()
    {
        var actor = await session.CurrentActor(); actor.Demand("audit.read");
        if (actor.Branch != options.Value.Branch) throw new UnauthorizedAccessException("Wrong branch.");
        await using var db = await factory.CreateDbContextAsync();
        return await HistoryProtection.Verify(db);
    }
}
