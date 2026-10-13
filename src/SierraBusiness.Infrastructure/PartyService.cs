using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure.Imports;
namespace SierraBusiness.Infrastructure;
public sealed class BusinessParty
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public string Branch { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public PartyType Type { get; set; }
    public string Contact { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public int PaymentDays { get; set; }
    public decimal CreditLimit { get; set; }
    public bool CreditHold { get; set; }
    public string Category { get; set; } = "";
    public string Tin { get; set; } = "";
    public string ContactsJson { get; set; } = "[]";
    public string Notes { get; set; } = "";
}
public sealed class PartyService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IPartyService
{
    private async Task<Actor> Authorize(bool write = false)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read"); if (write) actor.Demand("catalogue.write");
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch."); return actor;
    }
    public async Task<IReadOnlyList<PartyView>> Read()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        return (await db.Parties.AsNoTracking().Where(x => x.Branch == actor.Branch).OrderBy(x => x.Code).ToListAsync()).Select(View).ToArray();
    }
    public async Task<Guid> Save(PartyCommand input)
    {
        var actor = await Authorize(true);
        if (input.OperationId == Guid.Empty) throw new BusinessException("Operation ID is required.");
        var payload = JsonSerializer.Serialize(input); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + "party.save" + payload)));
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.Operations.SingleOrDefaultAsync(x => x.Id == input.OperationId); if (previous is not null) { if (hash != previous.Fingerprint) throw new BusinessException("Operation ID already used with different data or identity."); return previous.ResultId; }
        var party = await SaveCore(db, actor, input);
        db.Operations.Add(new() { Id = input.OperationId, ResultId = party.Id, Branch = actor.Branch, Actor = actor.Id, Action = "party.save", Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = hash }); await db.SaveChangesAsync(); await tx.CommitAsync(); return party.Id;
    }
    internal static async Task<BusinessParty> SaveCore(BusinessDb db, Actor actor, PartyCommand input, bool apply = true)
    {
        actor.Demand("catalogue.write");
        var code = Rules.Required(input.Code, "Party code", 40).ToUpperInvariant(); var name = Rules.Required(input.Name, "Party name"); var normalized = name.ToUpperInvariant();
        if (!Enum.IsDefined(input.Type) || input.PaymentDays is < 0 or > 3650 || input.CreditLimit < 0 || input.CreditLimit > 1_000_000_000 || AccountingEngine.Money(input.CreditLimit) != input.CreditLimit) throw new BusinessException("Check party type, payment days (0–3650) and credit limit (up to two decimal places).");
        if (input.Contact.Length > 160 || input.Phone.Length > 60 || input.Email.Length > 160 || input.Address.Length > 200) throw new BusinessException("Contact/address fields exceed their maximum length.");
        if (input.Email.Length > 0 && !System.Net.Mail.MailAddress.TryCreate(input.Email, out _)) throw new BusinessException("Enter a valid email address.");
        if (await db.Parties.AnyAsync(x => x.Branch == actor.Branch && x.Id != input.Id && (x.Code == code || x.NormalizedName == normalized))) throw new BusinessException("Party code or name already exists in this branch.");
        BusinessParty party;
        if (input.Id is null) { party = new() { Id = Guid.NewGuid(), Branch = actor.Branch }; if (apply) db.Parties.Add(party); }
        else { party = await db.Parties.SingleOrDefaultAsync(x => x.Id == input.Id && x.Branch == actor.Branch) ?? throw new BusinessException("Party not found."); if (party.Version != input.ExpectedVersion) throw new BusinessException("Party changed. Refresh before saving."); if (party.Name != name && await db.Documents.AnyAsync(x => x.Branch == actor.Branch && x.Party.ToUpper() == party.NormalizedName)) throw new BusinessException("A party with posted history cannot be renamed. Retain its name to preserve bill matching."); }
        party.Version++; party.Code = code; party.Name = name; party.NormalizedName = normalized; party.Type = input.Type; party.Contact = input.Contact.Trim(); party.Phone = input.Phone.Trim(); party.Email = input.Email.Trim(); party.Address = input.Address.Trim(); party.PaymentDays = input.PaymentDays; party.CreditLimit = input.CreditLimit; party.CreditHold = input.CreditHold;
        if (input.Category is not null) { var category = input.Category.Trim(); if (category.Length > 40) throw new BusinessException("The organisation type is limited to 40 characters."); party.Category = category; }
        if (input.Tin is not null) { var tin = input.Tin.Trim().ToUpperInvariant(); if (tin.Length > 30) throw new BusinessException("The TIN is limited to 30 characters."); party.Tin = tin; }
        if (input.Notes is not null) { var notes = input.Notes.Trim(); if (notes.Length > 500) throw new BusinessException("Notes are limited to 500 characters."); party.Notes = notes; }
        if (input.Contacts is not null)
        {
            var contacts = input.Contacts.Where(c => !string.IsNullOrWhiteSpace(c.Name)).Select(c => new PartyContact(Rules.Required(c.Role, "Contact role", 60), Rules.Required(c.Name, "Contact name", 120), (c.Phone ?? "").Trim(), (c.Email ?? "").Trim())).ToList();
            if (contacts.Count > 12) throw new BusinessException("Up to 12 contacts can be recorded.");
            foreach (var c in contacts) { if (c.Phone.Length > 60 || c.Email.Length > 160) throw new BusinessException("A contact's phone or email is too long."); if (c.Email.Length > 0 && !System.Net.Mail.MailAddress.TryCreate(c.Email, out _)) throw new BusinessException($"Enter a valid email for {c.Name}."); }
            party.ContactsJson = JsonSerializer.Serialize(contacts);
        }
        return party;
    }
    private static PartyView View(BusinessParty x) => new(x.Id, x.Version, x.Code, x.Name, x.Type, x.Contact, x.Phone, x.Email, x.Address, x.PaymentDays, x.CreditLimit, x.CreditHold,
        x.Category, x.Tin, JsonSerializer.Deserialize<List<PartyContact>>(string.IsNullOrWhiteSpace(x.ContactsJson) ? "[]" : x.ContactsJson) ?? [], x.Notes);

    private static string Describe(BusinessDocument d) => d.Kind switch
    {
        DocumentKind.Sale => "Sales invoice", DocumentKind.ServiceInvoice => "Service invoice", DocumentKind.MoneyReceipt => "Payment received", DocumentKind.Payment => "Payment made",
        DocumentKind.SalesReturn => "Credit note (return)", DocumentKind.PurchaseReturn => "Debit note (return)", DocumentKind.Receipt => "Goods received", DocumentKind.SupplierInvoice => "Supplier invoice",
        DocumentKind.ChequeBounce => "Bounced cheque", DocumentKind.ChequeReceipt => "Cheque received", DocumentKind.CustomerCreditUse => "Credit applied", DocumentKind.SupplierCreditUse => "Supplier credit applied",
        DocumentKind.Reversal => "Reversal", DocumentKind.Journal => "Journal adjustment", DocumentKind.LandedCost => "Landed cost", _ => d.Kind.ToString()
    } + (d.Settlement.Length > 0 && d.Kind is DocumentKind.Sale or DocumentKind.ServiceInvoice ? $" ({d.Settlement})" : "");
    private static string Short(string number) { var parts = number.Split('-'); return parts.Length >= 3 && parts[^1].Length > 8 ? $"{parts[^2]}-{parts[^1][^8..].ToUpperInvariant()}" : number; }

    public async Task<AccountStatement> Statement(Guid partyId, DateOnly from, DateOnly to, bool supplier = false)
    {
        var actor = await Authorize(); actor.Demand("accounting.read");
        if (to < from) throw new BusinessException("The end date is before the start date.");
        await using var db = await factory.CreateDbContextAsync();
        var party = await db.Parties.AsNoTracking().SingleOrDefaultAsync(x => x.Id == partyId && x.Branch == actor.Branch) ?? throw new BusinessException("Customer or supplier not found.");
        var account = supplier ? Account.Payables : Account.Receivables;
        var docs = await db.Documents.AsNoTracking().Where(x => x.Branch == actor.Branch && x.Party.ToUpper() == party.NormalizedName && x.BusinessDate <= to).ToDictionaryAsync(x => x.Id);
        var ids = docs.Keys.ToList();
        var journal = await db.Journal.AsNoTracking().Where(x => ids.Contains(x.DocumentId) && (x.Account == account || x.Account == (Account)2400 || x.Account == (Account)1300)).Select(x => new { x.DocumentId, x.Account, x.Debit, x.Credit }).ToListAsync();
        // Customer: invoices are debits, receipts and credit notes are credits. Supplier: bills are credits, payments are debits; the balance is what we owe.
        decimal Signed(decimal debit, decimal credit) => supplier ? credit - debit : debit - credit;
        var main = journal.Where(x => x.Account == account).Select(x => (x.DocumentId, x.Debit, x.Credit)).ToList();
        // Payment and receipt vouchers are named by their narration: include them through their allocations to this party's bills (only the allocated amounts).
        var allocations = await db.BillAllocations.AsNoTracking().Where(x => ids.Contains(x.InvoiceId) && !ids.Contains(x.VoucherId) && !db.Documents.Any(r => r.Reverses == x.VoucherId)).ToListAsync();
        var voucherIds = allocations.Select(x => x.VoucherId).Distinct().ToList();
        foreach (var v in await db.Documents.AsNoTracking().Where(x => voucherIds.Contains(x.Id) && x.BusinessDate <= to).ToListAsync()) docs[v.Id] = v;
        foreach (var a in allocations.Where(x => docs.ContainsKey(x.VoucherId))) main.Add(supplier ? (a.VoucherId, a.Amount, 0m) : (a.VoucherId, 0m, a.Amount));
        var opening = main.Where(x => docs[x.DocumentId].BusinessDate < from).Sum(x => Signed(x.Debit, x.Credit));
        var balance = opening; var lines = new List<AccountStatementLine>();
        foreach (var g in main.Where(x => docs[x.DocumentId].BusinessDate >= from).GroupBy(x => x.DocumentId).OrderBy(g => docs[g.Key].BusinessDate).ThenBy(g => docs[g.Key].Number))
        {
            var d = docs[g.Key]; decimal debit = g.Sum(x => x.Debit), credit = g.Sum(x => x.Credit);
            if (debit == credit) continue;
            balance += Signed(debit, credit);
            lines.Add(new(d.BusinessDate, d.Id, Short(d.Number), Describe(d), debit, credit, balance));
        }
        var creditAccount = supplier ? (Account)1300 : (Account)2400;
        var unapplied = journal.Where(x => x.Account == creditAccount).Sum(x => supplier ? x.Debit - x.Credit : x.Credit - x.Debit);
        decimal cur = 0, d30 = 0, d60 = 0, d90 = 0, over = 0;
        if (actor.Permissions.Contains("accounting.read"))
        {
            var ageing = await new ReportService(factory, session, options).Ageing(to, !supplier);
            if (ageing.Rows.FirstOrDefault(x => x.Party.Equals(party.Name, StringComparison.OrdinalIgnoreCase)) is { } row) (cur, d30, d60, d90, over) = (row.Current, row.Days30, row.Days60, row.Days90, row.Over90);
        }
        return new(View(party), supplier, from, to, opening, lines, balance, unapplied, cur, d30, d60, d90, over);
    }

    public async Task<byte[]> ExportStatement(Guid partyId, DateOnly from, DateOnly to, bool supplier = false)
    {
        var s = await Statement(partyId, from, to, supplier);
        static string M(decimal v) => v.ToString("0.00");
        return WorkbookWriter.Write([
            ("Statement", [["Date", "Document", "Description", supplier ? "Paid / returned" : "Invoiced", supplier ? "Billed" : "Paid / credited", "Balance"],
                ["", "", "Opening balance", "", "", M(s.Opening)],
                .. s.Lines.Select(l => (IReadOnlyList<string>)[l.Date.ToString("yyyy-MM-dd"), l.Number, l.Description, M(l.Debit), M(l.Credit), M(l.Balance)]),
                ["", "", "Closing balance", "", "", M(s.Closing)]]),
            ("Ageing", [["Current", "1–30 days", "31–60 days", "61–90 days", "Over 90 days", "Unapplied credit"], [M(s.Current), M(s.Days30), M(s.Days60), M(s.Days90), M(s.Over90), M(s.UnappliedCredit)]])
        ]);
    }
}
