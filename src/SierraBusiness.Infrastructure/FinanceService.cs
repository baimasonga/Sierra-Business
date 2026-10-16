using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed partial class FinanceService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IFinanceService
{
    private async Task<Actor> Authorize(bool write = false)
    {
        var actor = await session.CurrentActor(); actor.Demand("accounting.read");
        if (write) actor.Demand("accounting.write");
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }
    private async Task OpenDate(BusinessDb db, string branch, DateOnly date)
    {
        if (date <= await PeriodControls.ClosedThrough(db, branch, options.Value.ClosedThrough) || date > DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("Use an open-period date no later than today.");
    }
    private static void Money(decimal value, string field, bool zero = false)
    {
        if (value < 0 || (!zero && value == 0) || value > 1_000_000_000 || AccountingEngine.Money(value) != value) throw new BusinessException($"{field} must be {(zero ? "non-negative" : "positive")}, at most one billion and have at most two decimal places.");
    }
    public Task<Guid> CreateLedger(LedgerInput input) => Execute(input.OperationId, "ledger.create", input, async (db, actor) =>
    {
        await CreateLedgerCore(db, input); return Guid.NewGuid();
    });
    internal static async Task<LedgerAccount> CreateLedgerCore(BusinessDb db, LedgerInput input, bool apply = true)
    {
        if (input.Code is < 6000 or > 999999 || !Enum.IsDefined(input.Group)) throw new BusinessException("Custom ledger codes must be 6000–999999 and use a valid group.");
        if (input.CashOrBank && input.Group != LedgerGroup.Asset) throw new BusinessException("Cash/bank accounts must be assets.");
        if (await db.LedgerAccounts.AnyAsync(x => x.Id == input.Code)) throw new BusinessException("Ledger code already exists.");
        var account = new LedgerAccount { Id = input.Code, Name = Rules.Required(input.Name, "Ledger name"), Group = input.Group, CashOrBank = input.CashOrBank };
        if (apply) db.LedgerAccounts.Add(account); return account;
    }
    public async Task<Guid> PostVoucher(VoucherCommand input) => await Controls.Guard(factory, await session.CurrentActor(), () => Execute(input.OperationId, "voucher.post", input, (db, actor) => PostVoucherCore(db, actor, input)));
    internal async Task<Guid> PostVoucherCore(BusinessDb db, Actor actor, VoucherCommand input)
    {
        await OpenDate(db, actor.Branch, input.Date);
        if (Controls.Backdated(await Controls.Current(db, actor.Branch), input.Date) is { } late)
            await Controls.Gate(db, actor, Controls.Voucher, input with { ApprovalId = null }, input.ApprovalId, input.OperationId, [late with { Detail = $"{input.Type} voucher {late.Detail}" }], $"{input.Type} voucher · {input.Date:dd MMM yyyy} · NLe {input.Lines.Sum(x => x.Debit):N2} · {input.Narration.Trim()}");
        if (!Enum.IsDefined(input.Type) || input.Lines is null || input.Lines.Count is < 2 or > 200 || input.Allocations is null || input.Allocations.Count > 200) throw new BusinessException("Enter a valid voucher with 2–200 lines.");
        if (input.AllocationSource?.Length > 20000) throw new BusinessException("Cost centre allocation details are too large.");
        if (input.AllocationSource is string classSource && classSource.TrimStart().StartsWith('{'))
        {
            try
            {
                using var sourceJson = JsonDocument.Parse(classSource);
                if (sourceJson.RootElement.TryGetProperty(nameof(UseAllocationClassCommand.ClassId), out _))
                {
                    var request = JsonSerializer.Deserialize<UseAllocationClassCommand>(classSource)!;
                    var savedClass = (await ProductivityFor(db, actor.Branch)).Classes.SingleOrDefault(c => c.Id == request.ClassId) ?? throw new BusinessException("Allocation class not found.");
                    ClassAllocation(request, savedClass); CheckShares(savedClass.Shares, await StructureFor(db, actor.Branch));
                }
            }
            catch (JsonException) { throw new BusinessException("Cost centre allocation details contain invalid JSON. Review the allocation again."); }
        }
        var narration = Rules.Required(input.Narration, "Narration", 200);
        if (input.ExternalReference is string reference)
        {
            if (reference != Rules.Required(reference,"External voucher reference",80).ToUpperInvariant()) throw new BusinessException("Use a trimmed uppercase external voucher reference.");
            var earlier = await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == "voucher.post" && o.Payload.Contains(nameof(VoucherCommand.ExternalReference))).ToListAsync();
            var ids = earlier.Where(o => JsonSerializer.Deserialize<VoucherCommand>(o.Payload)?.ExternalReference == reference).Select(o => o.ResultId).ToArray();
            if (await db.Documents.AnyAsync(d => ids.Contains(d.Id) && !db.Documents.Any(r => r.Reverses == d.Id))) throw new BusinessException("This external voucher reference was already posted. Reconcile the existing voucher before importing another.");
        }
        var accounts = await db.LedgerAccounts.ToDictionaryAsync(x => x.Id);
        foreach (var line in input.Lines)
        {
            if (!accounts.TryGetValue(line.AccountCode, out var account)) throw new BusinessException("Unknown ledger account.");
            Money(line.Debit, "Debit", true); Money(line.Credit, "Credit", true);
            if ((line.Debit == 0) == (line.Credit == 0)) throw new BusinessException("Each line must have exactly one positive debit or credit.");
            if (line.CostCentre.Trim().Length > 100) throw new BusinessException("Cost centre is limited to 100 characters.");
            if (account.Control && line.AccountCode is not 1100 and not 2000) throw new BusinessException("Inventory, sales and cost-of-sales control accounts are posted through trading workflows.");
            if (line.CostCentre.Length > 0 && account.Group is not LedgerGroup.Income and not LedgerGroup.Expense) throw new BusinessException("Cost centres apply to income and expense lines.");
        }
        var total = input.Lines.Sum(x => x.Debit); Money(total, "Voucher total");
        AccountingEngine.Balanced(input.Lines.Select(x => new Posting((Account)x.AccountCode, x.Debit, x.Credit)).ToList());
        var funds = input.Lines.Where(x => accounts[x.AccountCode].CashOrBank).ToArray();
        var other = input.Lines.Where(x => !accounts[x.AccountCode].CashOrBank).ToArray();
        if (input.ChequeIssue is { } cheque)
        {
            var number = Rules.Required(cheque.ChequeNumber, "Cheque number", 30).ToUpperInvariant(); var payee = Rules.Required(cheque.Payee, "Payee", 160);
            if (cheque.OperationId != input.OperationId || cheque.Date != input.Date || cheque.Amount != total || input.Type != VoucherType.Payment || funds.Length != 1 || funds[0].AccountCode != cheque.BankAccount || cheque.BankAccount == 1000 || input.StatementRowId is not null) throw new BusinessException("Cheque details must match this one-bank payment voucher.");
            if (await db.IssuedCheques.AnyAsync(c => c.Branch == actor.Branch && c.BankAccount == cheque.BankAccount && c.Number == number)) throw new BusinessException("That cheque number was already issued from this bank.");
            await BankingExtrasService.ValidateBook(db, actor, cheque, number);
            db.IssuedCheques.Add(new IssuedCheque { Id = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(input.OperationId.ToString("N") + "cheque"))), Branch = actor.Branch, BankAccount = cheque.BankAccount, Number = number, Payee = payee, Date = cheque.Date, Amount = cheque.Amount, Actor = actor.Id });
        }
        BankStatementRow? sourceRow = null;
        if (input.StatementRowId is Guid rowId)
        {
            if (input.Type is not (VoucherType.Payment or VoucherType.Receipt) || funds.Length != 1) throw new BusinessException("Statement rows create one-bank payments or receipts.");
            sourceRow = await db.BankStatementRows.SingleOrDefaultAsync(r => r.Id == rowId && db.BankStatements.Any(s => s.Id == r.StatementId && s.Branch == actor.Branch && s.AccountCode == funds[0].AccountCode) && !db.BankStatementVoids.Any(v => v.StatementId == r.StatementId)) ?? throw new BusinessException("Active statement row not found for this bank.");
            if (sourceRow.Date != input.Date || sourceRow.Amount != funds[0].Debit - funds[0].Credit) throw new BusinessException("The voucher date and signed bank amount must equal the reviewed statement row.");
            if (await db.BankMatches.AnyAsync(m => m.StatementRowId == rowId && !db.BankMatchCancellations.Any(c => c.MatchId == m.Id))) throw new BusinessException("This statement row already has a reviewed match.");
            var token = rowId.ToString();
            var previousSources = await db.Operations.Where(o => o.Branch == actor.Branch && o.Action == "voucher.post" && o.Payload.Contains(token)).ToListAsync();
            var previousIds = previousSources.Where(o => JsonSerializer.Deserialize<VoucherCommand>(o.Payload)?.StatementRowId == rowId).Select(o => o.ResultId).ToArray();
            if (await db.Documents.AnyAsync(d => previousIds.Contains(d.Id) && !db.Documents.Any(r => r.Reverses == d.Id))) throw new BusinessException("A voucher was already created from this row. Match that voucher or reverse it before creating another.");
        }
        switch (input.Type)
        {
            case VoucherType.Payment:
                if (funds.Length != 1 || funds[0].Credit != total || other.Any(x => x.Debit == 0)) throw new BusinessException("A payment must credit one cash/bank account and debit its expense or payable accounts.");
                break;
            case VoucherType.Receipt:
                if (funds.Length != 1 || funds[0].Debit != total || other.Any(x => x.Credit == 0)) throw new BusinessException("A receipt must debit one cash/bank account and credit its income or receivable accounts.");
                break;
            case VoucherType.Contra:
                if (other.Length != 0 || funds.Select(x => x.AccountCode).Distinct().Count() < 2) throw new BusinessException("Contra transfers require at least two different cash/bank accounts.");
                break;
        }
        var control = input.Lines.Where(x => x.AccountCode is 1100 or 2000).ToArray();
        if (control.Length > 0 && (input.Type is not VoucherType.Payment and not VoucherType.Receipt || control.Length != 1)) throw new BusinessException("Settle trade control balances through a receipt or payment with bill allocations.");
        if (input.Allocations.Select(x => x.DocumentId).Distinct().Count() != input.Allocations.Count) throw new BusinessException("Allocate to each bill only once per voucher.");
        var receivable = input.Type == VoucherType.Receipt;
        if (control.Length > 0 && (control[0].AccountCode != (receivable ? 1100 : 2000) || (receivable ? control[0].Credit : control[0].Debit) != input.Allocations.Sum(x => x.Amount))) throw new BusinessException("The bill allocations must exactly match the receivable/payable control line.");
        if (control.Length == 0 && input.Allocations.Count != 0) throw new BusinessException("Bill allocations need a receivable or payable line.");
        var doc = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-V-{input.OperationId:N}", BusinessDate = input.Date, Party = narration, Kind = input.Type switch { VoucherType.Payment => DocumentKind.Payment, VoucherType.Receipt => DocumentKind.MoneyReceipt, VoucherType.Contra => DocumentKind.Contra, _ => DocumentKind.Journal }, Total = total, Settlement = input.Type.ToString(), Detail = JsonSerializer.Serialize(input) };
        var allDocuments = await db.Documents.Where(x => x.Branch == actor.Branch).ToListAsync();
        var reversed = allDocuments.Where(x => x.Reverses != null).Select(x => x.Reverses!.Value).ToHashSet();
        var allocations = await db.BillAllocations.Where(x => db.Documents.Any(d => d.Id == x.VoucherId && d.Branch == actor.Branch)).ToListAsync();
        var returned = await db.ReturnAllocations.Where(x => db.Documents.Any(d => d.Id == x.OriginalDocumentId && d.Branch == actor.Branch) && !db.Documents.Any(d => d.Reverses == x.ReturnDocumentId)).ToListAsync();
        foreach (var allocation in input.Allocations)
        {
            Money(allocation.Amount, "Allocated amount");
            var invoice = allDocuments.SingleOrDefault(x => x.Id == allocation.DocumentId) ?? throw new BusinessException("Allocated bill not found in this branch.");
            if (reversed.Contains(invoice.Id) || invoice.BusinessDate > input.Date || (receivable ? !ServiceRules.IsReceivable(invoice) : !(invoice.Kind is DocumentKind.Receipt or DocumentKind.LandedCost or DocumentKind.SupplierInvoice or DocumentKind.OpeningBalance && invoice.Settlement.StartsWith("Supplier payable", StringComparison.Ordinal)))) throw new BusinessException("Allocate only to an unreversed credit bill dated on or before this voucher.");
            await Fx.RejectLocalSettlement(db, invoice.Id);
            var settled = allocations.Where(x => x.InvoiceId == invoice.Id && !reversed.Contains(x.VoucherId)).Sum(x => x.Amount);
            var credited = returned.Where(x => x.OriginalDocumentId == invoice.Id).Sum(x => x.BillCredit);
            if (allocation.Amount > invoice.Total - settled - credited) throw new BusinessException($"Allocation exceeds the outstanding amount for {invoice.Number}.");
            db.BillAllocations.Add(new() { Id = Guid.NewGuid(), VoucherId = doc.Id, InvoiceId = invoice.Id, Amount = allocation.Amount });
        }
        db.Documents.Add(doc);
        if (input.ChequeIssue is not null) db.IssuedCheques.Local.Single(c => c.Id == new Guid(MD5.HashData(Encoding.UTF8.GetBytes(input.OperationId.ToString("N") + "cheque")))).VoucherId = doc.Id;
        foreach (var line in input.Lines)
        {
            var entry = new JournalLine { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, Account = (Account)line.AccountCode, Debit = line.Debit, Credit = line.Credit, CostCentre = line.CostCentre.Trim() }; db.Journal.Add(entry);
            if (sourceRow is not null && accounts[line.AccountCode].CashOrBank) db.BankMatches.Add(new BankMatch { Id = Guid.NewGuid(), Branch = actor.Branch, JournalLineId = entry.Id, StatementRowId = sourceRow.Id, ClearedDate = sourceRow.Date, StatementReference = sourceRow.Reference });
        }
        return doc.Id;
    }
    public Task<Guid> Reconcile(BankMatchCommand input) => Execute(input.OperationId, "bank.reconcile", input, async (db, actor) =>
    {
        var line = await db.Journal.SingleOrDefaultAsync(x => x.Id == input.JournalLineId && x.Branch == actor.Branch) ?? throw new BusinessException("Bank entry not found.");
        var account = await db.LedgerAccounts.FindAsync((int)line.Account);
        if (account?.CashOrBank != true) throw new BusinessException("Only cash/bank ledger entries can be reconciled.");
        var doc = await db.Documents.SingleAsync(x => x.Id == line.DocumentId);
        if (input.ClearedDate < doc.BusinessDate || input.ClearedDate > DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("Clearing date must be between the entry date and today.");
        if (await db.BankMatches.AnyAsync(x => x.JournalLineId == line.Id && !db.BankMatchCancellations.Any(c => c.MatchId == x.Id))) throw new BusinessException("This entry has already been reconciled.");
        if (input.StatementRowId is not null)
        {
            var row = await db.BankStatementRows.SingleOrDefaultAsync(x => x.Id == input.StatementRowId && db.BankStatements.Any(s => s.Id == x.StatementId && s.Branch == actor.Branch && s.AccountCode == (int)line.Account) && !db.BankStatementVoids.Any(v => v.StatementId == x.StatementId)) ?? throw new BusinessException("Active statement row not found for this account.");
            if (row.Date != input.ClearedDate || row.Amount != line.Debit - line.Credit) throw new BusinessException("Statement date and signed amount must match the ledger entry exactly.");
            if (await db.BankMatches.AnyAsync(x => x.StatementRowId == row.Id && !db.BankMatchCancellations.Any(c => c.MatchId == x.Id))) throw new BusinessException("This statement row is already matched.");
        }
        var match = new BankMatch { Id = Guid.NewGuid(), JournalLineId = line.Id, Branch = actor.Branch, StatementRowId = input.StatementRowId, ClearedDate = input.ClearedDate, StatementReference = Rules.Required(input.StatementReference, "Statement reference") };
        db.BankMatches.Add(match); return match.Id;
    });
    public Task<Guid> CancelMatch(CancelBankMatchCommand input) => Execute(input.OperationId, "bank.match.cancel", input, async (db, actor) =>
    {
        var match = await db.BankMatches.SingleOrDefaultAsync(x => x.Id == input.MatchId && x.Branch == actor.Branch) ?? throw new BusinessException("Statement match not found in this branch.");
        if (await db.BankMatchCancellations.AnyAsync(x => x.MatchId == match.Id)) throw new BusinessException("This match was already cancelled. Refresh before correcting another match.");
        var cancellation = new BankMatchCancellation { Id = Guid.NewGuid(), MatchId = match.Id, Reason = Rules.Required(input.Reason, "Correction reason", 200) };
        db.BankMatchCancellations.Add(cancellation);
        return cancellation.Id;
    });
    public async Task<IReadOnlyList<BankMatchHistory>> MatchHistory()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        return await (from match in db.BankMatches.AsNoTracking()
            join line in db.Journal on match.JournalLineId equals line.Id
            join doc in db.Documents on line.DocumentId equals doc.Id
            join cancellation in db.BankMatchCancellations on match.Id equals cancellation.MatchId into cancellations
            from cancellation in cancellations.DefaultIfEmpty()
            where match.Branch == actor.Branch && line.Branch == actor.Branch
            orderby match.ClearedDate descending
            select new BankMatchHistory(match.Id, line.Id, (int)line.Account, doc.Number, match.ClearedDate, match.StatementReference, cancellation == null ? null : cancellation.Reason)).ToListAsync();
    }
    public async Task<CashFlowReport> CashFlow(DateOnly from, DateOnly to)
    {
        var report = await Read(from, to);
        var codes = report.Accounts.Where(x => x.CashOrBank).Select(x => x.Code).ToHashSet();
        var balances = report.Balances.Where(x => codes.Contains(x.Code)).ToArray();
        var entries = report.Entries.Where(x => codes.Contains(x.AccountCode)).GroupBy(x => x.DocumentId).Select(g =>
        {
            var first = g.First(); var net = g.Sum(x => x.Debit - x.Credit);
            return new CashFlowEntry(g.Key, first.Number, first.Date, first.Party, Math.Max(net, 0), Math.Max(-net, 0));
        }).Where(x => x.Received != 0 || x.Paid != 0).OrderBy(x => x.Date).ThenBy(x => x.Number).ToArray();
        return new(balances.Sum(x => x.Opening), entries.Sum(x => x.Received), entries.Sum(x => x.Paid), balances.Sum(x => x.Closing), entries);
    }
    public Task<Guid> SetBudget(BudgetCommand input) => Execute(input.OperationId, "budget.create", input, async (db, actor) =>
    {
        if (input.From > input.To) throw new BusinessException("Budget start must precede its end.");
        Money(input.Amount, "Budget"); var centre = input.CostCentre.Trim();
        if (centre.Length > 100) throw new BusinessException("Cost centre is limited to 100 characters.");
        var account = await db.LedgerAccounts.FindAsync(input.AccountCode);
        if (account?.Group is not LedgerGroup.Income and not LedgerGroup.Expense) throw new BusinessException("Budgets apply to income or expense accounts.");
        if (await db.LedgerBudgets.AnyAsync(x => x.Branch == actor.Branch && x.AccountCode == input.AccountCode && x.CostCentre == centre && x.From <= input.To && x.To >= input.From)) throw new BusinessException("An overlapping budget exists for this account and cost centre.");
        var budget = new LedgerBudget { Id = Guid.NewGuid(), Branch = actor.Branch, AccountCode = input.AccountCode, From = input.From, To = input.To, Amount = input.Amount, CostCentre = centre };
        db.LedgerBudgets.Add(budget); return budget.Id;
    });
    public async Task<FinanceSnapshot> Read(DateOnly from, DateOnly to)
    {
        var actor = await Authorize(); if (from > to) throw new BusinessException("Report start must precede its end.");
        await using var db = await factory.CreateDbContextAsync();
        var accounts = await db.LedgerAccounts.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var documents = await db.Documents.AsNoTracking().Where(x => x.Branch == actor.Branch && x.BusinessDate <= to).ToDictionaryAsync(x => x.Id);
        var lines = (await db.Journal.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync()).Where(x => documents.ContainsKey(x.DocumentId)).ToArray();
        var matches = await db.BankMatches.AsNoTracking().Where(x => x.Branch == actor.Branch && x.ClearedDate <= to && !db.BankMatchCancellations.Any(c => c.MatchId == x.Id)).ToDictionaryAsync(x => x.JournalLineId);
        var reversed = documents.Values.Where(x => x.Reverses != null).Select(x => x.Reverses!.Value).ToHashSet();
        var balances = accounts.Select(a =>
        {
            var selected = lines.Where(x => (int)x.Account == a.Id).ToArray();
            var opening = selected.Where(x => documents[x.DocumentId].BusinessDate < from).Sum(x => x.Debit - x.Credit);
            var activity = selected.Where(x => documents[x.DocumentId].BusinessDate >= from).ToArray();
            return new LedgerBalance(a.Id, a.Name, a.Group, opening, activity.Sum(x => x.Debit), activity.Sum(x => x.Credit), selected.Sum(x => x.Debit - x.Credit));
        }).ToArray();
        var entries = lines.Where(x => documents[x.DocumentId].BusinessDate >= from).Select(x =>
        {
            var doc = documents[x.DocumentId]; matches.TryGetValue(x.Id, out var match);
            return new FinanceEntry(x.Id, doc.Id, doc.Number, doc.BusinessDate, doc.Party, (int)x.Account, accounts.Single(a => a.Id == (int)x.Account).Name, x.Debit, x.Credit, x.CostCentre, reversed.Contains(doc.Id), match?.ClearedDate, match?.StatementReference ?? "");
        }).OrderByDescending(x => x.Date).ThenBy(x => x.Number).ToArray();
        var allocations = await db.BillAllocations.AsNoTracking().Where(x => db.Documents.Any(d => d.Id == x.VoucherId && d.Branch == actor.Branch)).ToListAsync();
        var returned = await db.ReturnAllocations.AsNoTracking().Where(x => db.Documents.Any(d => d.Id == x.OriginalDocumentId && d.Branch == actor.Branch)).ToListAsync();
        var bills = documents.Values.Where(x => !reversed.Contains(x.Id) && (x.Kind is DocumentKind.Receipt or DocumentKind.LandedCost or DocumentKind.SupplierInvoice or DocumentKind.OpeningBalance && x.Settlement.StartsWith("Supplier payable", StringComparison.Ordinal) || ServiceRules.IsReceivable(x))).Select(x =>
        {
            var settled = allocations.Where(a => a.InvoiceId == x.Id && documents.ContainsKey(a.VoucherId) && !reversed.Contains(a.VoucherId)).Sum(a => a.Amount);
            settled += returned.Where(a => a.OriginalDocumentId == x.Id && documents.ContainsKey(a.ReturnDocumentId) && !reversed.Contains(a.ReturnDocumentId)).Sum(a => a.BillCredit);
            var due = x.DueDate ?? x.BusinessDate;
            return new OutstandingBill(x.Id, x.Number, x.Party, ServiceRules.IsReceivable(x), x.BusinessDate, due, x.Total, settled, x.Total - settled, Math.Max(0, to.DayNumber - due.DayNumber));
        }).Where(x => x.Outstanding > 0).OrderBy(x => x.DueDate).ToArray();
        var budgets = await db.LedgerBudgets.AsNoTracking().Where(x => x.Branch == actor.Branch && x.From <= to && x.To >= from).ToListAsync();
        var budgetViews = budgets.Select(b =>
        {
            var a = accounts.Single(x => x.Id == b.AccountCode);
            var actual = lines.Where(x => (int)x.Account == b.AccountCode && documents[x.DocumentId].BusinessDate >= b.From && documents[x.DocumentId].BusinessDate <= b.To && (b.CostCentre == "" || x.CostCentre == b.CostCentre)).Sum(x => x.Debit - x.Credit) * (a.Group == LedgerGroup.Income ? -1 : 1);
            return new BudgetView(b.AccountCode, a.Name, b.From, b.To, b.CostCentre, b.Amount, actual);
        }).ToArray();
        var profit = balances.Where(x => x.Group is LedgerGroup.Income or LedgerGroup.Expense).Sum(x => x.Credit - x.Debit);
        return new(accounts.Select(x => new LedgerDefinition(x.Id, x.Name, x.Group, x.CashOrBank, x.Control)).ToArray(), balances, entries, bills, budgetViews, profit,
            balances.Where(x => x.Group == LedgerGroup.Asset).Sum(x => x.Closing), -balances.Where(x => x.Group == LedgerGroup.Liability).Sum(x => x.Closing),
            -balances.Where(x => x.Group == LedgerGroup.Equity).Sum(x => x.Closing), -balances.Where(x => x.Group is LedgerGroup.Income or LedgerGroup.Expense).Sum(x => x.Closing),
            accounts.Where(x => x.CashOrBank).Select(a => new BankBalance(a.Id, lines.Where(x => (int)x.Account == a.Id).Sum(x => x.Debit - x.Credit), lines.Where(x => (int)x.Account == a.Id && !matches.ContainsKey(x.Id)).Sum(x => x.Debit - x.Credit), lines.Where(x => (int)x.Account == a.Id && matches.ContainsKey(x.Id)).Sum(x => x.Debit - x.Credit))).ToArray());
    }
    private async Task<Guid> Execute<T>(Guid operationId, string action, T input, Func<BusinessDb, Actor, Task<Guid>> work)
    {
        var actor = await Authorize(true); if (operationId == Guid.Empty) throw new BusinessException("Operation ID is required.");
        var payload = JsonSerializer.Serialize(input); var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + action + payload)));
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.Operations.SingleOrDefaultAsync(x => x.Id == operationId);
        if (previous is not null) { if (previous.Fingerprint != fingerprint && (previous.Actor != actor.Id || previous.Branch != actor.Branch || previous.Action != action || JsonSerializer.Serialize(JsonSerializer.Deserialize<T>(previous.Payload)) != payload)) throw new BusinessException("Operation ID already used with different data or identity."); return previous.ResultId; }
        var result = await work(db, actor);
        db.Operations.Add(new() { Id = operationId, ResultId = result, Branch = actor.Branch, Actor = actor.Id, Action = action, Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = fingerprint });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return result;
    }
}



