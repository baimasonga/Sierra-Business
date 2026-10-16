namespace SierraBusiness.Application;

/// <summary>Specification Skill 23 (0.20.0): Sierra Leone configuration and compliance. Rates, identifiers and formats are settings with history, never code.</summary>
public enum TaxCategory { Standard, ZeroRated, Exempt, Relieved }

/// <summary>A company identifier printed on documents, for example TIN, GST registration number or business registration number.</summary>
public sealed record CompanyIdentifier(string Label, string Value, bool OnInvoices = true);

/// <summary>Who the business is and how money is written. Versioned; every saved change is kept.</summary>
/// <param name="TaxRegistered">Registered for GST: sales invoices are titled with <see cref="TaxInvoiceTitle"/> and show the tax breakdown.</param>
/// <param name="FiscalFrom">Sales and service invoices dated on or after this day must be registered with the fiscal device/service. Null: fiscal registration is off.</param>
public sealed record CompanyProfile(int Version = 0, string LegalName = "", string TradingName = "", string Address = "", string Phone = "", string Email = "",
    IReadOnlyList<CompanyIdentifier>? Identifiers = null, bool TaxRegistered = false, string TaxName = "GST", string TaxInvoiceTitle = "Tax invoice",
    string CurrencyCode = "SLE", string CurrencySymbol = "NLe", int Decimals = 2, bool SymbolAfter = false, string CurrencyName = "Leones", string MinorUnitName = "cents", bool AmountInWords = true,
    DateOnly? FiscalFrom = null, string SavedBy = "", DateTimeOffset? Saved = null)
{
    public IReadOnlyList<CompanyIdentifier> Ids => Identifiers ?? [];
    public string Money(decimal amount)
    {
        var digits = Math.Clamp(Decimals, 0, 4);
        var text = Math.Abs(amount).ToString("N" + digits, System.Globalization.CultureInfo.InvariantCulture);
        var signed = amount < 0 ? "-" + text : text;
        return CurrencySymbol.Length == 0 ? signed : SymbolAfter ? $"{signed} {CurrencySymbol}" : $"{CurrencySymbol} {signed}";
    }
}
public sealed record SaveCompanyProfile(Guid OperationId, CompanyProfile Profile);

/// <summary>How printed documents look: letterhead, footer, payment details and the standard wording for each document type. Versioned.</summary>
/// <param name="Logo">A small PNG/JPEG/SVG image as a data URL (at most 200 KB).</param>
/// <param name="ReceiptWidth">"A4" for full-page documents or "80mm" for till receipts.</param>
public sealed record DocumentTemplate(int Version = 0, string Logo = "", string AccentColor = "#1f4e79", string Tagline = "", string FooterText = "", string BankDetails = "",
    string InvoiceTerms = "", string ReceiptNote = "Thank you for your business.", string QuotationTerms = "", string DeliveryNote = "", bool SignatureLines = true, string ReceiptWidth = "A4",
    string SavedBy = "", DateTimeOffset? Saved = null);
public sealed record SaveDocumentTemplate(Guid OperationId, DocumentTemplate Template);

/// <summary>Everything a printed document needs, readable by anyone who can print.</summary>
public sealed record PrintSetup(CompanyProfile Company, DocumentTemplate Template);

public sealed record TaxCategoryChange(DateOnly From, TaxCategory Category, string Reason, string By);
public sealed record ProductTaxView(Guid ProductId, string Sku, string Name, string Category, TaxCategory Current, TaxCategory? Next, DateOnly? NextFrom, IReadOnlyList<TaxCategoryChange> History);
public sealed record SetTaxCategory(Guid OperationId, Guid ProductId, TaxCategory Category, DateOnly From, string Reason);

/// <summary>Tax summary for a period, for preparing the GST return. Amounts are in the functional currency.</summary>
public sealed record TaxReturnLine(string Section, decimal Standard, decimal ZeroRated, decimal Exempt, decimal Tax, int Documents,decimal Relieved = 0);
public sealed record TaxReturnDocument(Guid DocumentId, string Number, DateOnly Date, string Kind, string Party, decimal Standard, decimal ZeroRated, decimal Exempt, decimal Tax, decimal Rate, string Fiscal,decimal Relieved = 0);
public sealed record TaxReturnReport(DateOnly From, DateOnly To, string TaxName, IReadOnlyList<TaxReturnLine> Lines, decimal OutputTax, IReadOnlyList<TaxReturnDocument> Documents, ReconciliationCheck Check);

// ---------------- fiscal device / service ----------------

public sealed record FiscalLine(string Sku, string Description, decimal Quantity, decimal UnitPrice, decimal Amount, TaxCategory Category);
public sealed record FiscalInvoice(Guid DocumentId, string Number, DateOnly Date, string Customer, decimal Net, decimal Tax, decimal Total, string Currency, IReadOnlyList<FiscalLine> Lines, IReadOnlyList<CompanyIdentifier> Seller);
/// <param name="Permanent">The device/service rejected the invoice itself (do not retry); otherwise it was unavailable and will be retried.</param>
public sealed record FiscalResult(bool Registered, string Code = "", string Message = "", bool Permanent = false);

/// <summary>Boundary for any NRA / electronic fiscal device or online invoicing integration. Sales never depend on it: an invoice that cannot be registered is kept as pending and retried.</summary>
public interface IFiscalIntegration
{
    string Name { get; }
    bool Available { get; }
    Task<FiscalResult> Register(FiscalInvoice invoice, CancellationToken cancellationToken = default);
}

public enum FiscalState { NotRequired, Pending, Registered, Rejected }
public sealed record FiscalDocumentView(Guid DocumentId, string Number, DateOnly Date, string Party, decimal Total, FiscalState State, string Code, string LastMessage, int Attempts, DateTimeOffset? LastAttempt);
public sealed record FiscalStatus(bool Enabled, DateOnly? From, string Connector, bool ConnectorAvailable, int Pending, int Registered, int Rejected, IReadOnlyList<FiscalDocumentView> Documents);
public sealed record FiscalRunResult(int Tried, int Registered, int StillPending, int Rejected, string Message);

public interface IComplianceService
{
    Task<Guid> RecordInputTax(InputTaxCommand input);
    Task<StockInputTaxPreview> PreviewStockInputTax(Guid receiptId, decimal allowableTax);
    Task<IReadOnlyList<InputTaxSource>> InputTaxSources();
    Task<TaxFilingPreview> PreviewTaxFiling(DateOnly from,DateOnly to);
    Task<Guid> RecordTaxFiling(TaxFilingCommand input);
    Task<IReadOnlyList<TaxFilingView>> TaxFilings();
    Task<Guid> RecordTaxFilingCorrection(TaxFilingCorrectionCommand input);
    Task<IReadOnlyList<TaxFilingCorrectionView>> TaxFilingCorrections();
    Task<Guid> RecordReliefGrant(TaxReliefGrantCommand input);
    Task<IReadOnlyList<TaxReliefGrantView>> ReliefGrants();
    Task<NetTaxReturnReport> NetTaxReturn(DateOnly from, DateOnly to);
    Task<byte[]> ExportNetTaxReturn(DateOnly from, DateOnly to);
    Task<PrintSetup> PrintSetup();
    Task<CompanyProfile> Profile();
    Task<Guid> SaveProfile(SaveCompanyProfile input);
    Task<DocumentTemplate> Template();
    Task<Guid> SaveTemplate(SaveDocumentTemplate input);
    Task<IReadOnlyList<ProductTaxView>> ProductTax();
    Task<Guid> SetTaxCategory(SetTaxCategory input);
    Task<TaxReturnReport> TaxReturn(DateOnly from, DateOnly to);
    Task<FiscalStatus> Fiscal();
    Task<FiscalDocumentView?> FiscalFor(Guid documentId);
    Task<FiscalRunResult> RegisterPending();
}

/// <summary>Writes an amount in English words for invoices, e.g. "One thousand two hundred Leones and fifty cents".</summary>
public static class AmountWords
{
    private static readonly string[] Ones = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
    private static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];
    private static readonly (long Value, string Name)[] Scales = [(1_000_000_000_000, "trillion"), (1_000_000_000, "billion"), (1_000_000, "million"), (1_000, "thousand")];

    public static string Write(decimal amount, string currency, string minor, int decimals = 2)
    {
        amount = Math.Abs(Math.Round(amount, Math.Clamp(decimals, 0, 4), MidpointRounding.AwayFromZero));
        if (amount >= 1_000_000_000_000_000m) throw new ArgumentOutOfRangeException(nameof(amount));
        var whole = (long)decimal.Truncate(amount);
        var fraction = decimals <= 0 ? 0 : (long)Math.Round((amount - whole) * (decimal)Math.Pow(10, decimals));
        var text = Words(whole) + " " + currency;
        if (fraction > 0) text += " and " + Words(fraction) + " " + minor;
        return char.ToUpperInvariant(text[0]) + text[1..] + " only";
    }
    private static string Words(long n)
    {
        if (n < 20) return Ones[n];
        if (n < 100) return Tens[n / 10] + (n % 10 > 0 ? "-" + Ones[n % 10] : "");
        if (n < 1000) return Ones[n / 100] + " hundred" + (n % 100 > 0 ? " and " + Words(n % 100) : "");
        foreach (var (value, name) in Scales)
            if (n >= value) { var rest = n % value; return Words(n / value) + " " + name + (rest == 0 ? "" : rest < 100 ? " and " + Words(rest) : " " + Words(rest)); }
        return "";
    }
}
