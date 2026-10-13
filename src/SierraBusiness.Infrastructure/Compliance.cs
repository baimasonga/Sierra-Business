using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

// Append-only records (database guards and integrity seals).
public sealed class CompanyProfileVersion { public Guid Id { get; set; } public string Branch { get; set; } = ""; public int Version { get; set; } public string ProfileJson { get; set; } = "{}"; public string CreatedBy { get; set; } = ""; public DateTimeOffset Time { get; set; } }
public sealed class DocumentTemplateVersion { public Guid Id { get; set; } public string Branch { get; set; } = ""; public int Version { get; set; } public string TemplateJson { get; set; } = "{}"; public string CreatedBy { get; set; } = ""; public DateTimeOffset Time { get; set; } }
/// <summary>The tax category of a product from a date. Only one change per product per day.</summary>
public sealed class TaxAssignment { public Guid Id { get; set; } public Guid ProductId { get; set; } public DateOnly From { get; set; } public TaxCategory Category { get; set; } public string Reason { get; set; } = ""; public string CreatedBy { get; set; } = ""; public DateTimeOffset Time { get; set; } }
/// <summary>One attempt to register an invoice with the fiscal device or service.</summary>
public sealed class FiscalSubmission { public Guid Id { get; set; } public Guid DocumentId { get; set; } public string Branch { get; set; } = ""; public FiscalState Outcome { get; set; } public string Code { get; set; } = ""; public string Message { get; set; } = ""; public string Connector { get; set; } = ""; public string Actor { get; set; } = ""; public DateTimeOffset Time { get; set; } }

/// <summary>Used when no fiscal device or service is configured: nothing is sent and invoices wait as pending.</summary>
public sealed class NoFiscalDevice : IFiscalIntegration
{
    public string Name => "None connected";
    public bool Available => false;
    public Task<FiscalResult> Register(FiscalInvoice invoice, CancellationToken cancellationToken = default) => Task.FromResult(new FiscalResult(false, Message: "No fiscal device or service is connected."));
}

public static partial class TaxRules
{
    /// <summary>The product's tax category on a date; products never assigned one are standard-rated.</summary>
    public static async Task<TaxCategory> Category(BusinessDb db, Guid productId, DateOnly date) =>
        await db.TaxAssignments.Where(x => x.ProductId == productId && x.From <= date).OrderByDescending(x => x.From).Select(x => (TaxCategory?)x.Category).FirstOrDefaultAsync() ?? TaxCategory.Standard;

    /// <summary>Net amounts by tax category, as stored on a posted document. Documents from before 0.20.0 are wholly standard-rated.</summary>
    public static (decimal Standard, decimal Zero, decimal Exempt) Split(JsonElement detail, decimal net)
    {
        if (detail.TryGetProperty("Taxable", out var t) && t.TryGetDecimal(out var taxable))
            return (taxable, Value(detail, "ZeroRated"), Value(detail, "Exempt"));
        return (net, 0, 0);
    }
    public static decimal Value(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetDecimal(out var d) ? d : 0;

    public static async Task<CompanyProfile> Profile(BusinessDb db, string branch)
    {
        var row = await db.CompanyProfiles.AsNoTracking().Where(x => x.Branch == branch).OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        return row is null ? new() : JsonSerializer.Deserialize<CompanyProfile>(row.ProfileJson)! with { Version = row.Version };
    }
}

public sealed partial class ComplianceService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options, IFiscalIntegration fiscal) : IComplianceService
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DocumentKind[] Invoices = [DocumentKind.Sale, DocumentKind.ServiceInvoice];
    private async Task<Actor> Authorize(params string[] permissions)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        foreach (var p in permissions) actor.Demand(p);
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }
    private static string Short(string? number) { var parts = (number ?? "").Split('-'); return parts.Length >= 3 && parts[^1].Length > 8 ? $"{parts[^2]}-{parts[^1][^8..].ToUpperInvariant()}" : number ?? ""; }
    private static async Task<Dictionary<string, string>> Names(BusinessDb db) => await db.Users.AsNoTracking().Where(x => x.UserName != null).ToDictionaryAsync(x => x.Id, x => x.UserName!);

    // ---------------- company profile and templates ----------------

    public async Task<PrintSetup> PrintSetup()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        return new(await TaxRules.Profile(db, actor.Branch), await CurrentTemplate(db, actor.Branch));
    }
    public async Task<CompanyProfile> Profile() { var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync(); return await TaxRules.Profile(db, actor.Branch); }
    public async Task<DocumentTemplate> Template() { var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync(); return await CurrentTemplate(db, actor.Branch); }
    private static async Task<DocumentTemplate> CurrentTemplate(BusinessDb db, string branch)
    {
        var row = await db.DocumentTemplates.AsNoTracking().Where(x => x.Branch == branch).OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        return row is null ? new() : JsonSerializer.Deserialize<DocumentTemplate>(row.TemplateJson)! with { Version = row.Version };
    }

    private static string Text(string? value, string field, int max, bool required = false)
    {
        value = (value ?? "").Trim();
        if (required && value.Length == 0) throw new BusinessException($"Enter the {field}.");
        if (value.Length > max) throw new BusinessException($"The {field} is limited to {max} characters.");
        return value;
    }

    public async Task<Guid> SaveProfile(SaveCompanyProfile input)
    {
        var actor = await Authorize("users.manage");
        return await Commands.Run(factory, actor, input.OperationId, "company.profile", input, async db =>
        {
            var p = input.Profile ?? throw new BusinessException("Enter the company details.");
            var ids = (p.Identifiers ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => new CompanyIdentifier(Text(x.Label, "identifier name", 30, true), Text(x.Value, $"{x.Label} value", 60, true), x.OnInvoices)).ToList();
            if (ids.Count > 8) throw new BusinessException("Up to 8 identifiers can be recorded.");
            if (ids.GroupBy(x => x.Label, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new BusinessException("Each identifier name can be used once.");
            var code = Text(p.CurrencyCode, "currency code", 3, true).ToUpperInvariant();
            if (!CurrencyCode().IsMatch(code)) throw new BusinessException("The currency code is three letters, for example SLE.");
            if (p.Decimals is < 0 or > 4) throw new BusinessException("Show 0 to 4 decimal places.");
            if (p.TaxRegistered && !ids.Any(x => x.OnInvoices)) throw new BusinessException("A tax-registered business must print at least one identifier (such as its TIN or GST number) on invoices.");
            var clean = p with
            {
                LegalName = Text(p.LegalName, "legal name", 160, true), TradingName = Text(p.TradingName, "trading name", 160), Address = Text(p.Address, "address", 300), Phone = Text(p.Phone, "phone", 60), Email = Text(p.Email, "email", 120),
                Identifiers = ids, TaxName = Text(p.TaxName, "tax name", 20, true), TaxInvoiceTitle = Text(p.TaxInvoiceTitle, "tax invoice title", 40, true),
                CurrencyCode = code, CurrencySymbol = Text(p.CurrencySymbol, "currency symbol", 6), CurrencyName = Text(p.CurrencyName, "currency name", 30, true), MinorUnitName = Text(p.MinorUnitName, "minor unit name", 30, p.Decimals > 0),
                SavedBy = actor.Id, Saved = DateTimeOffset.UtcNow
            };
            var current = await db.CompanyProfiles.Where(x => x.Branch == actor.Branch).MaxAsync(x => (int?)x.Version) ?? 0;
            if (current != p.Version) throw new BusinessException("The company details were changed by someone else. Refresh before saving.");
            var row = new CompanyProfileVersion { Id = Guid.NewGuid(), Branch = actor.Branch, Version = current + 1, ProfileJson = JsonSerializer.Serialize(clean with { Version = current + 1 }), CreatedBy = actor.Id, Time = DateTimeOffset.UtcNow };
            db.CompanyProfiles.Add(row); return row.Id;
        });
    }

    public async Task<Guid> SaveTemplate(SaveDocumentTemplate input)
    {
        var actor = await Authorize("users.manage");
        return await Commands.Run(factory, actor, input.OperationId, "documents.template", input, async db =>
        {
            var t = input.Template ?? throw new BusinessException("Enter the document settings.");
            var logo = (t.Logo ?? "").Trim();
            if (logo.Length > 0)
            {
                if (!LogoData().IsMatch(logo)) throw new BusinessException("The logo must be a PNG, JPEG or SVG image.");
                if (logo.Length > 280_000) throw new BusinessException("The logo is too large. Use an image under 200 KB.");
                try { Convert.FromBase64String(logo[(logo.IndexOf(',') + 1)..]); } catch (FormatException) { throw new BusinessException("The logo image could not be read."); }
            }
            if (!Colour().IsMatch(t.AccentColor ?? "")) throw new BusinessException("The accent colour is a hex colour such as #1f4e79.");
            if (t.ReceiptWidth is not ("A4" or "80mm")) throw new BusinessException("Choose A4 or 80 mm receipts.");
            var clean = t with
            {
                Logo = logo, Tagline = Text(t.Tagline, "tagline", 120), FooterText = Text(t.FooterText, "footer", 300), BankDetails = Text(t.BankDetails, "payment details", 400),
                InvoiceTerms = Text(t.InvoiceTerms, "invoice terms", 1000), ReceiptNote = Text(t.ReceiptNote, "receipt note", 200), QuotationTerms = Text(t.QuotationTerms, "quotation terms", 1000), DeliveryNote = Text(t.DeliveryNote, "delivery note wording", 400),
                SavedBy = actor.Id, Saved = DateTimeOffset.UtcNow
            };
            var current = await db.DocumentTemplates.Where(x => x.Branch == actor.Branch).MaxAsync(x => (int?)x.Version) ?? 0;
            if (current != t.Version) throw new BusinessException("The document settings were changed by someone else. Refresh before saving.");
            var row = new DocumentTemplateVersion { Id = Guid.NewGuid(), Branch = actor.Branch, Version = current + 1, TemplateJson = JsonSerializer.Serialize(clean with { Version = current + 1 }), CreatedBy = actor.Id, Time = DateTimeOffset.UtcNow };
            db.DocumentTemplates.Add(row); return row.Id;
        });
    }

    // ---------------- tax categories ----------------

    public async Task<IReadOnlyList<ProductTaxView>> ProductTax()
    {
        await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var names = await Names(db);
        var all = (await db.TaxAssignments.AsNoTracking().ToListAsync()).ToLookup(x => x.ProductId);
        var products = await db.Products.AsNoTracking().OrderBy(x => x.Sku).ToListAsync();
        var today = Today;
        return products.Select(p =>
        {
            var rows = all[p.Id].OrderBy(x => x.From).ToList();
            var current = rows.LastOrDefault(x => x.From <= today)?.Category ?? TaxCategory.Standard;
            var next = rows.FirstOrDefault(x => x.From > today);
            return new ProductTaxView(p.Id, p.Sku, p.Name, p.Category, current, next?.Category, next?.From, rows.Select(x => new TaxCategoryChange(x.From, x.Category, x.Reason, names.GetValueOrDefault(x.CreatedBy, x.CreatedBy))).ToArray());
        }).ToArray();
    }

    public async Task<Guid> SetTaxCategory(SetTaxCategory input)
    {
        var actor = await Authorize("accounting.write");
        return await Commands.Run(factory, actor, input.OperationId, "tax.category", input, async db =>
        {
            if (!Enum.IsDefined(input.Category) || input.Category == TaxCategory.Relieved) throw new BusinessException("Choose standard-rated, zero-rated or exempt. Relief belongs to a reviewed recipient certificate and invoice, not every customer buying this product.");
            if (!await db.Products.AnyAsync(x => x.Id == input.ProductId)) throw new BusinessException("Product not found.");
            if (input.From < Today) throw new BusinessException("A tax change takes effect today or later. Posted invoices keep the category they were issued with.");
            var reason = Text(input.Reason, "reason (for example the law or notice)", 200, true);
            if (await db.TaxAssignments.AnyAsync(x => x.ProductId == input.ProductId && x.From == input.From)) throw new BusinessException("This product already has a tax change on that date.");
            if (await TaxRules.Category(db, input.ProductId, input.From) == input.Category && !await db.TaxAssignments.AnyAsync(x => x.ProductId == input.ProductId && x.From > input.From))
                throw new BusinessException($"The product is already {Label(input.Category)} from that date.");
            var row = new TaxAssignment { Id = Guid.NewGuid(), ProductId = input.ProductId, From = input.From, Category = input.Category, Reason = reason, CreatedBy = actor.Id, Time = DateTimeOffset.UtcNow };
            db.TaxAssignments.Add(row); return row.Id;
        });
    }
    public static string Label(TaxCategory c) => c switch { TaxCategory.ZeroRated => "zero-rated", TaxCategory.Exempt => "exempt",TaxCategory.Relieved => "relieved", _ => "standard-rated" };

    // ---------------- tax return ----------------

    public async Task<TaxReturnReport> TaxReturn(DateOnly from, DateOnly to)
    {
        var actor = await Authorize("accounting.read");
        await using var db = await factory.CreateDbContextAsync(); return await TaxReturnFor(db,actor,from,to);
    }
    private static async Task<TaxReturnReport> TaxReturnFor(BusinessDb db,Actor actor,DateOnly from,DateOnly to)
    {
        if (to < from) throw new BusinessException("The end date is before the start date.");
        if (to.DayNumber - from.DayNumber > 400) throw new BusinessException("Choose a period of at most 13 months.");
        var profile = await TaxRules.Profile(db, actor.Branch);
        var kinds = new[] { DocumentKind.Sale, DocumentKind.ServiceInvoice, DocumentKind.SalesReturn };
        var docs = await db.Documents.AsNoTracking().Where(x => x.Branch == actor.Branch && (kinds.Contains(x.Kind) || x.Kind == DocumentKind.Reversal && db.Documents.Any(p => p.Id == x.Reverses && kinds.Contains(p.Kind))) && x.BusinessDate >= from && x.BusinessDate <= to).ToListAsync();
        var parentIds = docs.Where(d => d.Reverses != null).Select(d => d.Reverses!.Value).ToArray(); var parents = await db.Documents.AsNoTracking().Where(d => parentIds.Contains(d.Id) && d.Branch == actor.Branch).ToDictionaryAsync(d => d.Id);
        var ids = docs.Select(x => x.Id).ToList();
        var fiscalRows = (await db.FiscalSubmissions.AsNoTracking().Where(x => ids.Contains(x.DocumentId)).ToListAsync()).ToLookup(x => x.DocumentId);
        var rows = new List<TaxReturnDocument>();
        foreach (var d in docs.OrderBy(x => x.BusinessDate).ThenBy(x => x.Number))
        {
            var source = d.Reverses is Guid parent ? parents[parent] : d;
            using var json = JsonDocument.Parse(source.Detail); var root = json.RootElement;
            var net = TaxRules.Value(root, "Net"); var tax = TaxRules.Value(root, "Tax"); var rate = TaxRules.Value(root, "TaxRate");
            var (s, z, e) = TaxRules.Split(root, net);
            var sign = (source.Kind == DocumentKind.SalesReturn ? -1 : 1)*(d.Reverses is null ? 1 : -1);
            var state = d.Reverses is not null ? "Cancellation" : d.Kind == DocumentKind.SalesReturn ? "" : FiscalOf(d,profile,fiscalRows[d.Id]).State switch { FiscalState.Registered => "Registered", FiscalState.Pending => "Pending", FiscalState.Rejected => "Rejected", _ => "" };
            rows.Add(new(d.Id,Short(d.Number),d.BusinessDate,d.Reverses is not null ? source.Kind == DocumentKind.SalesReturn ? "Sales return cancellation" : "Invoice cancellation" : d.Kind switch { DocumentKind.Sale => "Sale",DocumentKind.ServiceInvoice => "Service invoice",_ => "Sales return" },d.Party,sign*s,sign*z,sign*e,sign*tax,rate,state,sign*TaxRules.Value(root,"Relieved")));
        }
        TaxReturnLine Line(string section, IEnumerable<TaxReturnDocument> r) { var l = r.ToList(); return new(section,l.Sum(x => x.Standard),l.Sum(x => x.ZeroRated),l.Sum(x => x.Exempt),l.Sum(x => x.Tax),l.Count,l.Sum(x => x.Relieved)); }
        var lines = new[] { Line("Sales and service invoices", rows.Where(x => x.Kind != "Sales return")), Line("Less sales returns", rows.Where(x => x.Kind == "Sales return")), Line("Net for the period", rows) };
        var ledger = (await db.Journal.AsNoTracking().Where(x => ids.Contains(x.DocumentId) && x.Account == Account.TaxPayable).Select(x => new { x.Credit, x.Debit }).ToListAsync()).Sum(x => x.Credit - x.Debit);
        var output = lines[2].Tax;
        return new(from, to, profile.TaxName, lines, output, rows, new ReconciliationCheck($"Output {profile.TaxName}", output, ledger, "Tax payable (2100) on these documents"));
    }

    // ---------------- fiscal registration ----------------

    private static FiscalDocumentView FiscalOf(BusinessDocument d, CompanyProfile profile, IEnumerable<FiscalSubmission> attempts)
    {
        var list = attempts.OrderBy(x => x.Time).ToList();
        var registered = list.FirstOrDefault(x => x.Outcome == FiscalState.Registered);
        var last = list.LastOrDefault();
        var state = registered is not null ? FiscalState.Registered : last?.Outcome == FiscalState.Rejected ? FiscalState.Rejected
            : profile.FiscalFrom is DateOnly f && d.BusinessDate >= f && Invoices.Contains(d.Kind) ? FiscalState.Pending : FiscalState.NotRequired;
        return new(d.Id, Short(d.Number), d.BusinessDate, d.Party, d.Total, state, registered?.Code ?? "", last?.Message ?? "", list.Count, last?.Time);
    }

    private async Task<List<FiscalDocumentView>> FiscalDocuments(BusinessDb db, string branch, CompanyProfile profile)
    {
        var from = profile.FiscalFrom ?? DateOnly.MaxValue;
        var submitted = db.FiscalSubmissions.Where(x => x.Branch == branch).Select(x => x.DocumentId);
        var docs = await db.Documents.AsNoTracking().Where(x => x.Branch == branch && Invoices.Contains(x.Kind) && (x.BusinessDate >= from || submitted.Contains(x.Id)) && !db.Documents.Any(r => r.Reverses == x.Id)).ToListAsync();
        var ids = docs.Select(x => x.Id).ToList();
        var attempts = (await db.FiscalSubmissions.AsNoTracking().Where(x => ids.Contains(x.DocumentId)).ToListAsync()).ToLookup(x => x.DocumentId);
        return docs.Select(d => FiscalOf(d, profile, attempts[d.Id])).ToList();
    }

    public async Task<FiscalStatus> Fiscal()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var profile = await TaxRules.Profile(db, actor.Branch);
        var all = await FiscalDocuments(db, actor.Branch, profile);
        var shown = all.Where(x => x.State != FiscalState.NotRequired).OrderBy(x => x.State switch { FiscalState.Rejected => 0, FiscalState.Pending => 1, _ => 2 }).ThenByDescending(x => x.Date).Take(300).ToArray();
        return new(profile.FiscalFrom is not null, profile.FiscalFrom, fiscal.Name, fiscal.Available, all.Count(x => x.State == FiscalState.Pending), all.Count(x => x.State == FiscalState.Registered), all.Count(x => x.State == FiscalState.Rejected), shown);
    }

    public async Task<FiscalDocumentView?> FiscalFor(Guid documentId)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var d = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId && x.Branch == actor.Branch);
        if (d is null || !Invoices.Contains(d.Kind)) return null;
        var view = FiscalOf(d, await TaxRules.Profile(db, actor.Branch), await db.FiscalSubmissions.AsNoTracking().Where(x => x.DocumentId == documentId).ToListAsync());
        return view.State == FiscalState.NotRequired ? null : view;
    }

    public async Task<FiscalRunResult> RegisterPending()
    {
        var actor = await session.CurrentActor();
        actor.DemandAny("sales.create", "accounting.write");
        await Authorize();
        await using var db = await factory.CreateDbContextAsync();
        var profile = await TaxRules.Profile(db, actor.Branch);
        if (profile.FiscalFrom is null) return new(0, 0, 0, 0, "Fiscal registration is switched off in the company settings.");
        var pending = (await FiscalDocuments(db, actor.Branch, profile)).Where(x => x.State == FiscalState.Pending).OrderBy(x => x.Date).Take(200).ToList();
        if (pending.Count == 0) return new(0, 0, 0, 0, "No invoices are waiting for fiscal registration.");
        if (!fiscal.Available) return new(0, 0, pending.Count, 0, $"The fiscal device or service ({fiscal.Name}) is not available. {pending.Count} invoice{(pending.Count == 1 ? " is" : "s are")} kept as pending and nothing was lost; try again when it is connected.");
        int registered = 0, rejected = 0;
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        foreach (var p in pending)
        {
            if (await db.FiscalSubmissions.AnyAsync(x => x.DocumentId == p.DocumentId && x.Outcome == FiscalState.Registered)) continue; // registered meanwhile by another user
            var d = await db.Documents.AsNoTracking().SingleAsync(x => x.Id == p.DocumentId);
            var invoice = BuildInvoice(d, profile, products);
            FiscalResult result;
            try { result = await fiscal.Register(invoice); }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException or IOException or TaskCanceledException) { result = new(false, Message: "The fiscal device did not respond: " + ex.Message); }
            var outcome = result.Registered ? FiscalState.Registered : result.Permanent ? FiscalState.Rejected : FiscalState.Pending;
            if (result.Registered && string.IsNullOrWhiteSpace(result.Code)) outcome = FiscalState.Pending;
            db.FiscalSubmissions.Add(new() { Id = Guid.NewGuid(), DocumentId = d.Id, Branch = actor.Branch, Outcome = outcome, Code = (result.Code ?? "").Trim()[..Math.Min(100, (result.Code ?? "").Trim().Length)], Message = (result.Message ?? "")[..Math.Min(300, (result.Message ?? "").Length)], Connector = fiscal.Name, Actor = actor.Id, Time = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            if (outcome == FiscalState.Registered) registered++; else if (outcome == FiscalState.Rejected) rejected++;
        }
        var still = pending.Count - registered - rejected;
        return new(pending.Count, registered, still, rejected, $"{registered} registered, {still} still pending, {rejected} rejected.");
    }

    private static FiscalInvoice BuildInvoice(BusinessDocument d, CompanyProfile profile, IReadOnlyDictionary<Guid, Product> products)
    {
        using var json = JsonDocument.Parse(d.Detail); var root = json.RootElement;
        var net = TaxRules.Value(root, "Net"); var tax = TaxRules.Value(root, "Tax");
        var lines = new List<FiscalLine>();
        if (d.Kind == DocumentKind.Sale)
        {
            var request = root.GetProperty("Request").Deserialize<TradeCommand>()!;
            var categories = root.TryGetProperty("Categories", out var c) ? c.Deserialize<Dictionary<Guid, TaxCategory>>()! : [];
            foreach (var l in request.Lines)
            {
                var p = products.GetValueOrDefault(l.ProductId);
                lines.Add(new(p?.Sku ?? "", p?.Name ?? "", l.Quantity, l.UnitPrice, AccountingEngine.Money(l.Quantity * l.UnitPrice), categories.GetValueOrDefault(l.ProductId, TaxCategory.Standard)));
            }
        }
        else if (root.TryGetProperty("Lines", out var serviceLines))
            foreach (var l in serviceLines.EnumerateArray())
                lines.Add(new("", l.GetProperty("Description").GetString() ?? "", TaxRules.Value(l, "Quantity"), TaxRules.Value(l, "UnitPrice"), TaxRules.Value(l, "Amount"), l.TryGetProperty("Category", out var cat) && cat.TryGetInt32(out var ci) ? (TaxCategory)ci : TaxCategory.Standard));
        return new(d.Id, d.Number, d.BusinessDate, d.Party, net, tax, d.Total, profile.CurrencyCode, lines, profile.Ids.Where(x => x.OnInvoices).ToArray());
    }

    [GeneratedRegex("^[A-Z]{3}$")] private static partial Regex CurrencyCode();
    [GeneratedRegex("^data:image/(png|jpeg|svg\\+xml);base64,[A-Za-z0-9+/=]+$")] private static partial Regex LogoData();
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")] private static partial Regex Colour();
}
