using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed class BankFormat { public Guid Id { get; set; } public string Bank { get; set; } = ""; public string FormatJson { get; set; } = "{}"; }
// Append-only records (database guards and integrity seals).
public sealed class IssuedCheque { public Guid Id { get; set; } public string Branch { get; set; } = ""; public int BankAccount { get; set; } public string Number { get; set; } = ""; public string Payee { get; set; } = ""; public DateOnly Date { get; set; } public decimal Amount { get; set; } public Guid VoucherId { get; set; } public string Actor { get; set; } = ""; }
public sealed class IssuedChequeEvent { public Guid Id { get; set; } public Guid ChequeId { get; set; } public IssuedChequeState State { get; set; } public DateOnly Date { get; set; } public string Reason { get; set; } = ""; public string Actor { get; set; } = ""; public DateTimeOffset Time { get; set; } }
public sealed class TillAssignment { public Guid Id { get; set; } public string Branch { get; set; } = ""; public int AccountCode { get; set; } public string Cashier { get; set; } = ""; }

public sealed class BankingExtrasService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IBankingExtras
{
    private async Task<Actor> Authorize(params string[] permissions)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        foreach (var p in permissions) actor.Demand(p);
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }
    private static Guid Derived(Guid operation, string purpose) => new(MD5.HashData(Encoding.UTF8.GetBytes(operation.ToString("N") + purpose)));
    private static string Short(string number) { var parts = number.Split('-'); return parts.Length >= 3 && parts[^1].Length > 8 ? $"{parts[^2]}-{parts[^1][^8..].ToUpperInvariant()}" : number; }

    // ---------------- statement formats ----------------

    public async Task<IReadOnlyList<BankFormatView>> Formats()
    {
        await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var saved = (await db.BankFormats.AsNoTracking().OrderBy(x => x.Bank).ToListAsync()).Select(x => new BankFormatView(x.Id, x.Bank, JsonSerializer.Deserialize<StatementFormat>(x.FormatJson)!));
        return StatementFormat.BuiltIn.Select(f => new BankFormatView(Guid.Empty, "", f)).Concat(saved).ToArray();
    }
    public async Task<Guid> SaveFormat(SaveStatementFormat input)
    {
        var actor = await Authorize("accounting.write");
        return await Commands.Run(factory, actor, input.OperationId, "bank.format", input, async db =>
        {
            var bank = Rules.Required(input.Bank, "Bank name", 80);
            var f = input.Format ?? throw new BusinessException("Describe the statement layout.");
            if (f.Delimiter.Length != 1 && f.Delimiter != "\\t") throw new BusinessException("The column separator is one character (for example , or ;), or \\t for tab.");
            if (f.HeaderRows is < 0 or > 20 || string.IsNullOrWhiteSpace(f.DateFormat)) throw new BusinessException("Check the header rows (0–20) and the date format.");
            if (f.DateColumn < 1 || f.ReferenceColumn < 1 || f.AmountColumn == 0 && (f.DebitColumn < 1 || f.CreditColumn < 1)) throw new BusinessException("Choose the date, details and amount (or debit and credit) columns.");
            var row = await db.BankFormats.SingleOrDefaultAsync(x => x.Bank == bank);
            if (row is null) { row = new() { Id = Guid.NewGuid(), Bank = bank }; db.BankFormats.Add(row); }
            row.FormatJson = JsonSerializer.Serialize(f with { Name = Rules.Required(f.Name, "Format name", 120) });
            return row.Id;
        });
    }
    public async Task<IReadOnlyList<StatementLineInput>> Preview(string csv, StatementFormat format) { await Authorize("accounting.read"); return BankStatementFormats.Parse(csv, format); }

    // ---------------- cheques we issue ----------------

    public async Task<IReadOnlyList<IssuedChequeView>> IssuedCheques()
    {
        var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync();
        var cheques = await db.IssuedCheques.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync();
        var ids = cheques.Select(x => x.Id).ToList(); var vouchers = cheques.Select(x => x.VoucherId).ToList();
        var events = (await db.IssuedChequeEvents.AsNoTracking().Where(x => ids.Contains(x.ChequeId)).ToListAsync()).ToLookup(x => x.ChequeId);
        var docs = await db.Documents.AsNoTracking().Where(x => vouchers.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var reversed = (await db.Documents.AsNoTracking().Where(x => x.Reverses != null && vouchers.Contains(x.Reverses.Value)).Select(x => x.Reverses!.Value).ToListAsync()).ToHashSet();
        return cheques.Select(c =>
        {
            var last = events[c.Id].OrderBy(x => x.Time).LastOrDefault();
            var state = reversed.Contains(c.VoucherId) ? IssuedChequeState.Cancelled : last?.State ?? IssuedChequeState.Issued;
            return new IssuedChequeView(c.Id, c.Number, c.BankAccount, c.Payee, c.Date, c.Amount, state, last?.Date, last?.Reason ?? "", c.VoucherId, Short(docs.GetValueOrDefault(c.VoucherId)?.Number ?? ""));
        }).OrderByDescending(x => x.Date).ThenByDescending(x => x.Number).ToArray();
    }

    public async Task<Guid> IssueCheque(IssueChequeCommand input)
    {
        var actor = await Authorize("accounting.write");
        var number = Rules.Required(input.ChequeNumber, "Cheque number", 30).ToUpperInvariant();
        var payee = Rules.Required(input.Payee, "Payee", 160);
        Rules.Positive(input.Amount, "Cheque amount");
        await using (var db = await factory.CreateDbContextAsync())
        {
            var account = await db.LedgerAccounts.SingleOrDefaultAsync(x => x.Id == input.BankAccount);
            if (account is null || !account.CashOrBank || input.BankAccount == (int)Account.Cash) throw new BusinessException("Choose the bank account the cheque is drawn on.");
            var existing = await db.IssuedCheques.SingleOrDefaultAsync(x => x.Branch == actor.Branch && x.BankAccount == input.BankAccount && x.Number == number);
            if (existing is not null) { if (existing.VoucherId == input.OperationId || existing.Id == Derived(input.OperationId, "cheque")) return existing.Id; throw new BusinessException($"Cheque {number} has already been issued from this account."); }
        }
        var allocations = input.Allocations ?? [];
        if (allocations.Count == 0 && input.ExpenseAccount is null) throw new BusinessException("Allocate the cheque to supplier bills or choose an expense account.");
        if (allocations.Count > 0 && allocations.Sum(x => x.Amount) != input.Amount) throw new BusinessException("The bill allocations must add up to the cheque amount.");
        var debit = allocations.Count > 0 ? (int)Account.Payables : input.ExpenseAccount!.Value;
        var narration = $"Cheque {number} to {payee}" + (string.IsNullOrWhiteSpace(input.Narration) ? "" : $" · {input.Narration.Trim()}");
        var voucher = await new FinanceService(factory, session, options).PostVoucher(new(input.OperationId, VoucherType.Payment, input.Date, narration[..Math.Min(160, narration.Length)],
            [new(debit, input.Amount, 0), new(input.BankAccount, 0, input.Amount)], allocations));
        return await Commands.Run(factory, actor, Derived(input.OperationId, "cheque"), "cheque.issue", input with { Allocations = allocations }, async db =>
        {
            var row = new IssuedCheque { Id = Derived(input.OperationId, "cheque"), Branch = actor.Branch, BankAccount = input.BankAccount, Number = number, Payee = payee, Date = input.Date, Amount = input.Amount, VoucherId = voucher, Actor = actor.Id };
            db.IssuedCheques.Add(row); return row.Id;
        });
    }

    public async Task<Guid> ActOnIssuedCheque(IssuedChequeActionCommand input)
    {
        var actor = await Authorize("accounting.write");
        var view = (await IssuedCheques()).SingleOrDefault(x => x.Id == input.ChequeId) ?? throw new BusinessException("Cheque not found.");
        var reason = (input.Reason ?? "").Trim();
        if (input.NewState == IssuedChequeState.Presented)
        {
            if (view.State != IssuedChequeState.Issued) throw new BusinessException($"Cheque {view.Number} is {view.State.ToString().ToLowerInvariant()}.");
            if (input.Date < view.Date || input.Date > DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("The presented date is between the cheque date and today.");
        }
        else if (input.NewState == IssuedChequeState.Cancelled)
        {
            if (view.State == IssuedChequeState.Presented) throw new BusinessException("A cheque already paid by the bank cannot be cancelled. Record a refund instead.");
            if (reason.Length == 0) throw new BusinessException("Give the reason for cancelling or stopping the cheque.");
            if (view.State == IssuedChequeState.Issued)
                await new BusinessService(factory, options).Reverse(actor, new(Derived(input.OperationId, "reverse"), view.VoucherId, $"Cheque {view.Number} cancelled: {reason}"));
        }
        else throw new BusinessException("Mark the cheque as presented or cancelled.");
        return await Commands.Run(factory, actor, input.OperationId, "cheque.state", input, async db =>
        {
            var e = new IssuedChequeEvent { Id = Guid.NewGuid(), ChequeId = view.Id, State = input.NewState, Date = input.Date, Reason = reason, Actor = actor.Id, Time = DateTimeOffset.UtcNow };
            db.IssuedChequeEvents.Add(e); return e.Id;
        });
    }

    // ---------------- cash drawers ----------------

    public async Task<IReadOnlyList<DrawerAssignment>> Drawers()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var names = await db.Users.AsNoTracking().Where(x => x.UserName != null).ToDictionaryAsync(x => x.Id, x => x.UserName!);
        var assignments = (await db.TillAssignments.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync()).ToLookup(x => x.AccountCode);
        return (await db.LedgerAccounts.AsNoTracking().Where(x => x.CashOrBank).OrderBy(x => x.Id).ToListAsync()).Where(x => x.Id != (int)Account.Bank)
            .Select(a => new DrawerAssignment(a.Id, a.Name, assignments[a.Id].Select(x => names.GetValueOrDefault(x.Cashier, x.Cashier)).OrderBy(x => x).ToArray())).ToArray();
    }

    public async Task<Guid> AssignDrawer(AssignDrawerCommand input)
    {
        var actor = await Authorize("tills.manage");
        return await Commands.Run(factory, actor, input.OperationId, "till.assign", input, async db =>
        {
            var account = await db.LedgerAccounts.SingleOrDefaultAsync(x => x.Id == input.AccountCode && x.CashOrBank) ?? throw new BusinessException("Choose a cash ledger.");
            var name = (input.CashierUserName ?? "").Trim().ToUpperInvariant();
            var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedUserName == name) ?? throw new BusinessException("User not found.");
            var row = await db.TillAssignments.SingleOrDefaultAsync(x => x.Branch == actor.Branch && x.AccountCode == account.Id && x.Cashier == user.Id);
            if (input.Assign && row is null) { row = new() { Id = Guid.NewGuid(), Branch = actor.Branch, AccountCode = account.Id, Cashier = user.Id }; db.TillAssignments.Add(row); }
            else if (!input.Assign && row is not null) db.TillAssignments.Remove(row);
            return row?.Id ?? Guid.Empty;
        });
    }
}
