using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed partial class BankingService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IBankingService
{
    private async Task<Actor> Authorize(string permission) { var a = await session.CurrentActor(); a.Demand(permission); if (a.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(a.Id)) throw new UnauthorizedAccessException(); return a; }
    private static void Amount(decimal value, bool zero = false) { if (value < 0 || !zero && value == 0 || value > 1_000_000_000 || AccountingEngine.Money(value) != value) throw new BusinessException("Use a positive amount up to one billion with at most two decimal places."); }
    private async Task OpenDate(BusinessDb db, Actor actor, DateOnly date) { if (date <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough) || date > DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("Use an open-period date no later than today."); }
    private static async Task Funds(BusinessDb db, int code) { if (!await db.LedgerAccounts.AnyAsync(x => x.Id == code && x.CashOrBank)) throw new BusinessException("Select a cash or bank ledger."); }
    private BusinessDocument Document<T>(Actor actor, Guid operation, DocumentKind kind, DateOnly date, string party, decimal total, string settlement, T input) => new() { Id = Guid.NewGuid(), Number = $"{actor.Branch}-{options.Value.Device}-B-{operation:N}", Branch = actor.Branch, Kind = kind, BusinessDate = date, DueDate = date, Party = party, Total = total, Settlement = settlement, Detail = JsonSerializer.Serialize(input) };
    private static void Journal(BusinessDb db, BusinessDocument doc, params Posting[] lines) { db.Documents.Add(doc); foreach (var p in AccountingEngine.Balanced(lines)) db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = doc.Branch, Account = p.Account, Debit = p.Debit, Credit = p.Credit }); }
    public async Task<BankingSnapshot> Read()
    {
        var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync();
        var statements = await db.BankStatements.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync(); var ids = statements.Select(x => x.Id).ToArray();
        var voids = (await db.BankStatementVoids.Where(x => ids.Contains(x.StatementId)).Select(x => x.StatementId).ToListAsync()).ToHashSet();
        var rows = await db.BankStatementRows.AsNoTracking().Where(x => ids.Contains(x.StatementId)).ToListAsync();
        var matches = await db.BankMatches.AsNoTracking().Where(x => x.Branch == actor.Branch && !db.BankMatchCancellations.Any(c => c.MatchId == x.Id)).ToListAsync();
        var matchedLines = matches.Select(x => x.JournalLineId).ToHashSet();
        var docs = await db.Documents.AsNoTracking().Where(x => x.Branch == actor.Branch).ToDictionaryAsync(x => x.Id);
        var journal = await db.Journal.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync();
        var rowViews = rows.Where(x => !voids.Contains(x.StatementId)).Select(row => {
            var account = statements.Single(x => x.Id == row.StatementId).AccountCode;
            var match = matches.SingleOrDefault(x => x.StatementRowId == row.Id);
            var candidates = match is not null ? [] : journal.Where(x => (int)x.Account == account && x.Debit - x.Credit == row.Amount && !matchedLines.Contains(x.Id) && docs[x.DocumentId].BusinessDate <= row.Date && row.Date.DayNumber - docs[x.DocumentId].BusinessDate.DayNumber <= 30).OrderByDescending(x => docs[x.DocumentId].Detail.Contains(row.Reference, StringComparison.OrdinalIgnoreCase)).ThenByDescending(x => docs[x.DocumentId].BusinessDate).Take(10).Select(x => new StatementCandidate(x.Id, docs[x.DocumentId].Number, docs[x.DocumentId].BusinessDate, docs[x.DocumentId].Party)).ToArray();
            return new StatementRowView(row.Id, row.StatementId, row.Line, row.Date, row.Reference, row.Amount, match?.Id, candidates);
        }).OrderBy(x => x.Date).ThenBy(x => x.Line).ToArray();
        var cheques = await db.ReceivedCheques.AsNoTracking().Where(x => x.Branch == actor.Branch && !db.Documents.Any(d => d.Reverses == x.SourceDocumentId)).ToListAsync(); var chequeIds = cheques.Select(x => x.Id).ToArray();
        var events = await db.ChequeEvents.AsNoTracking().Where(x => chequeIds.Contains(x.ChequeId)).ToListAsync();
        var chequeViews = cheques.Select(c => { var history = events.Where(x => x.ChequeId == c.Id).OrderBy(x => x.Version).ToArray(); var last = history.LastOrDefault(); return new ChequeView(c.Id, c.Party, c.Reference, c.Bank, c.ChequeDate, c.Amount, last?.State ?? ChequeState.Received, last?.Version ?? 0, history.Select(x => new ChequeEventView(x.Version, x.Action, x.State, x.Date, x.Reason, x.DocumentId)).ToArray(), c.SourceDocumentId); }).ToArray();
        var credits = await db.SupplierCredits.Where(x => db.Documents.Any(d => d.Id == x.ReturnId && d.Branch == actor.Branch) && !db.Documents.Any(d => d.Reverses == x.ReturnId)).ToListAsync(); var creditIds = credits.Select(x => x.Id).ToArray();
        var uses = await db.SupplierCreditUses.Where(x => creditIds.Contains(x.CreditId) && !db.Documents.Any(d => d.Reverses == x.DocumentId)).ToListAsync();
        return new(statements.Select(x => new StatementView(x.Id, x.Reference, x.AccountCode, x.Opening, x.Closing, voids.Contains(x.Id))).ToArray(), rowViews, chequeViews, credits.Select(c => new CreditView(c.Id, docs[c.ReturnId].Number, docs[c.ReturnId].Party, c.Amount, c.Amount - uses.Where(x => x.CreditId == c.Id).Sum(x => x.Amount))).ToArray());
    }
    public Task<Guid> Import(ImportStatementCommand input) => Execute(input.OperationId, "accounting.write", "bank.statement.import", input, async (db, actor) => {
        await Funds(db, input.AccountCode); var rows = input.Format is null ? BankStatementCsv.Parse(input.Csv) : BankStatementFormats.Parse(input.Csv, input.Format); var reference = Rules.Required(input.Reference, "Statement identifier", 100).ToUpperInvariant();
        Amount(Math.Abs(input.OpeningBalance), true); Amount(Math.Abs(input.ClosingBalance), true);
        if (rows.Any(x => x.Date > DateOnly.FromDateTime(DateTime.UtcNow)) || input.OpeningBalance + rows.Sum(x => x.Amount) != input.ClosingBalance) throw new BusinessException("Check the statement dates and opening + transactions = closing balance.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows))));
        if (await db.BankStatements.AnyAsync(x => x.Branch == actor.Branch && x.AccountCode == input.AccountCode && (x.Reference == reference || x.ContentHash == hash))) throw new BusinessException("This statement identifier or transaction content was already imported for this account.");
        var statement = new BankStatement { Id = Guid.NewGuid(), Branch = actor.Branch, AccountCode = input.AccountCode, Reference = reference, ContentHash = hash, Opening = input.OpeningBalance, Closing = input.ClosingBalance }; db.BankStatements.Add(statement);
        for (var i = 0; i < rows.Count; i++) db.BankStatementRows.Add(new() { Id = Guid.NewGuid(), StatementId = statement.Id, Line = i + 1, Date = rows[i].Date, Reference = rows[i].Reference, Amount = rows[i].Amount }); return statement.Id;
    });
    public Task<Guid> VoidStatement(VoidStatementCommand input) => Execute(input.OperationId, "accounting.write", "bank.statement.void", input, async (db, actor) => {
        if (!await db.BankStatements.AnyAsync(x => x.Id == input.StatementId && x.Branch == actor.Branch) || await db.BankStatementVoids.AnyAsync(x => x.StatementId == input.StatementId)) throw new BusinessException("Active statement not found.");
        if (await db.BankMatches.AnyAsync(x => db.BankStatementRows.Any(r => r.Id == x.StatementRowId && r.StatementId == input.StatementId) && !db.BankMatchCancellations.Any(c => c.MatchId == x.Id))) throw new BusinessException("Cancel this statement's active matches before voiding it.");
        var record = new BankStatementVoid { Id = Guid.NewGuid(), StatementId = input.StatementId, Reason = Rules.Required(input.Reason, "Reason") }; db.BankStatementVoids.Add(record); return record.Id;
    });
    public Task<Guid> RegisterCheque(RegisterChequeCommand input) => Execute(input.OperationId, "accounting.write", "cheque.register", input, async (db, actor) => {
        var source = await db.Documents.SingleOrDefaultAsync(x => x.Id == input.SourceDocumentId && x.Branch == actor.Branch && x.Kind == DocumentKind.Sale && x.Settlement == "Cheque" && !db.Documents.Any(d => d.Reverses == x.Id)) ?? throw new BusinessException("Select an unreversed cheque sale.");
        if (await db.ReceivedCheques.AnyAsync(x => x.SourceDocumentId == source.Id)) throw new BusinessException("This cheque is already registered.");
        if (await db.ReturnAllocations.AnyAsync(x => x.OriginalDocumentId == source.Id && !db.Documents.Any(d => d.Reverses == x.ReturnDocumentId) && db.Journal.Any(j => j.DocumentId == x.ReturnDocumentId && j.Account == Account.ChequesInHand && j.Credit > 0))) throw new BusinessException("This legacy cheque has a return against cheques in hand. Correct that return before registering the physical cheque.");
        using var json = JsonDocument.Parse(source.Detail); var request = json.RootElement.GetProperty("Request").Deserialize<TradeCommand>()!;
        return (await BankingRules.NewCheque(db, source, request.PaymentReference!, request.Bank!, request.PaymentDate ?? source.BusinessDate)).Id;
    });
    public Task<Guid> ReceiveCheque(ReceiveChequeCommand input) => Execute(input.OperationId, "accounting.write", "cheque.receive", input, async (db, actor) => {
        var date = DateOnly.FromDateTime(DateTime.UtcNow); await OpenDate(db, actor, date); Amount(input.Amount);
        var invoice = await db.Documents.SingleOrDefaultAsync(x => x.Id == input.InvoiceId && x.Branch == actor.Branch && ((x.Kind == DocumentKind.Sale || x.Kind == DocumentKind.ServiceInvoice) && x.Settlement == "Credit" || x.Kind == DocumentKind.ChequeBounce) && !db.Documents.Any(d => d.Reverses == x.Id)) ?? throw new BusinessException("Select an outstanding customer invoice.");
        if (input.Amount > await Outstanding(db, invoice)) throw new BusinessException("Cheque amount exceeds the outstanding invoice.");
        var doc = Document(actor, input.OperationId, DocumentKind.ChequeReceipt, date, invoice.Party, input.Amount, "Cheque received", input);
        Journal(db, doc, new(Account.ChequesInHand, input.Amount, 0), new(Account.Receivables, 0, input.Amount));
        db.BillAllocations.Add(new() { Id = Guid.NewGuid(), InvoiceId = invoice.Id, VoucherId = doc.Id, Amount = input.Amount });
        return (await BankingRules.NewCheque(db, doc, input.Reference, input.Bank, input.ChequeDate)).Id;
    });
    public Task<Guid> ActOnCheque(ChequeActionCommand input) => Execute(input.OperationId, "accounting.write", "cheque.action", input, async (db, actor) => {
        await OpenDate(db, actor, input.Date); if (!Enum.IsDefined(input.Action)) throw new BusinessException("Unknown cheque action.");
        var cheque = await db.ReceivedCheques.SingleOrDefaultAsync(x => x.Id == input.ChequeId && x.Branch == actor.Branch && !db.Documents.Any(d => d.Reverses == x.SourceDocumentId)) ?? throw new BusinessException("Active cheque not found.");
        var previous = await db.ChequeEvents.Where(x => x.ChequeId == cheque.Id).OrderByDescending(x => x.Version).FirstOrDefaultAsync(); var state = previous?.State ?? ChequeState.Received; var version = previous?.Version ?? 0;
        var source = await db.Documents.SingleAsync(x => x.Id == cheque.SourceDocumentId);
        var advanceCredit = source.Settlement == "Customer deposit" ? await db.CustomerCredits.SingleOrDefaultAsync(c => c.ReturnId == source.Id) : null;
        var advanceUsed = advanceCredit is null ? 0 : (await db.CustomerCreditUses.Where(u => u.CreditId == advanceCredit.Id && !db.Documents.Any(r => r.Reverses == u.DocumentId)).ToListAsync()).Sum(u => u.Amount);
        if (input.ExpectedVersion != version || input.Date < (previous?.Date ?? source.BusinessDate) || input.Date < cheque.ChequeDate) throw new BusinessException("Cheque changed or date precedes the cheque/latest event. Refresh and check the date.");
        var bank = previous?.BankAccount ?? input.BankAccount; var next = state; BusinessDocument doc;
        if (input.Action == ChequeAction.Undo)
        {
            actor.Demand("documents.reverse");
            var target = await db.ChequeEvents.Where(x => x.ChequeId == cheque.Id && x.Action != ChequeAction.Undo && !db.Documents.Any(d => d.Reverses == x.DocumentId)).OrderByDescending(x => x.Version).FirstOrDefaultAsync() ?? throw new BusinessException("There is no action left to undo.");
            if (advanceCredit is not null && advanceUsed > 0 && target.Action != ChequeAction.Bounce) throw new BusinessException("Reverse this cheque deposit's allocations and refunds before undoing its clearance or deposit.");
            if (await db.BillAllocations.AnyAsync(x => x.InvoiceId == target.DocumentId && !db.Documents.Any(d => d.Reverses == x.VoucherId))) throw new BusinessException("Reverse receipts or credit allocations against the bounced cheque bill before undoing its bounce.");
            var original = await db.Documents.SingleAsync(x => x.Id == target.DocumentId); var entries = await db.Journal.Where(x => x.DocumentId == original.Id).ToListAsync();
            doc = Document(actor, input.OperationId, DocumentKind.Reversal, input.Date, cheque.Party, -original.Total, "Cheque action correction", input); doc.Reverses = original.Id;
            Journal(db, doc, entries.Select(x => new Posting(x.Account, x.Credit, x.Debit)).ToArray()); next = target.PreviousState;
        }
        else
        {
            var posting = new List<Posting>();
            switch (input.Action)
            {
                case ChequeAction.Deposit:
                    if (state != ChequeState.Received) throw new BusinessException("Only a received cheque can be deposited.");
                    await Funds(db, input.BankAccount); bank = input.BankAccount; next = ChequeState.Deposited;
                    posting.AddRange([new((Account)1030, cheque.Amount, 0), new(Account.ChequesInHand, 0, cheque.Amount)]); break;
                case ChequeAction.Clear:
                    if (state != ChequeState.Deposited) throw new BusinessException("Only a deposited cheque can clear.");
                    next = ChequeState.Cleared; posting.AddRange([new((Account)bank, cheque.Amount, 0), new((Account)1030, 0, cheque.Amount)]); break;
                case ChequeAction.Bounce:
                    if (state == ChequeState.Bounced) throw new BusinessException("This cheque has already bounced.");
                    next = ChequeState.Bounced;
                    if (advanceCredit is not null) posting.AddRange([new(Account.Receivables,advanceUsed,0),new((Account)2400,cheque.Amount-advanceUsed,0),new(state == ChequeState.Received ? Account.ChequesInHand : state == ChequeState.Deposited ? (Account)1030 : (Account)bank,0,cheque.Amount)]);
                    else posting.AddRange([new(Account.Receivables,cheque.Amount,0),new(state == ChequeState.Received ? Account.ChequesInHand : state == ChequeState.Deposited ? (Account)1030 : (Account)bank,0,cheque.Amount)]); break;
            }
            doc = Document(actor, input.OperationId, next == ChequeState.Bounced ? DocumentKind.ChequeBounce : DocumentKind.ChequeMovement, input.Date, cheque.Party, cheque.Amount, next == ChequeState.Bounced ? "Credit" : next.ToString(), input);
            if (next == ChequeState.Bounced && advanceCredit is not null) doc.Total = advanceUsed;
            Journal(db, doc, posting.ToArray());
        }
        db.ChequeEvents.Add(new() { Id = Guid.NewGuid(), ChequeId = cheque.Id, DocumentId = doc.Id, Version = version + 1, Action = input.Action, PreviousState = state, State = next, BankAccount = bank, Date = input.Date, Reason = Rules.Required(input.Reason, "Reason / bank reference") }); return doc.Id;
    });
    public Task<Guid> UseSupplierCredit(UseCreditCommand input) => Execute(input.OperationId, "accounting.write", "supplier.credit.use", input, async (db, actor) => {
        var date = DateOnly.FromDateTime(DateTime.UtcNow); await OpenDate(db, actor, date); Amount(input.Amount); Rules.Required(input.Reason, "Reason");
        if ((input.InvoiceId is null) == (input.RefundAccount is null)) throw new BusinessException("Choose a cash/bank refund receipt or a purchase bill allocation.");
        var credit = await db.SupplierCredits.SingleOrDefaultAsync(x => x.Id == input.CreditId && db.Documents.Any(d => d.Id == x.ReturnId && d.Branch == actor.Branch) && !db.Documents.Any(d => d.Reverses == x.ReturnId)) ?? throw new BusinessException("Supplier credit not found.");
        var source = await db.Documents.SingleAsync(x => x.Id == credit.ReturnId);
        await Fx.RejectLocalAdvance(db,source.Id);
        var used = (await db.SupplierCreditUses.Where(x => x.CreditId == credit.Id && !db.Documents.Any(d => d.Reverses == x.DocumentId)).ToListAsync()).Sum(x => x.Amount);
        if (input.Amount > credit.Amount - used) throw new BusinessException("Amount exceeds the remaining supplier credit.");
        var doc = Document(actor, input.OperationId, DocumentKind.SupplierCreditUse, date, source.Party, input.Amount, input.InvoiceId is null ? "Supplier refund received" : "Supplier credit allocation", input);
        var destination = input.RefundAccount ?? 2000;
        if (input.RefundAccount is not null) await Funds(db, destination);
        else
        {
            var invoice = await db.Documents.SingleOrDefaultAsync(x => x.Id == input.InvoiceId && x.Branch == actor.Branch && (x.Kind == DocumentKind.Receipt || x.Kind == DocumentKind.SupplierInvoice) && x.Settlement.StartsWith("Supplier payable") && x.Party.ToUpper() == source.Party.ToUpper() && !db.Documents.Any(d => d.Reverses == x.Id)) ?? throw new BusinessException("Select an outstanding purchase bill for the same supplier."); await Fx.RejectLocalSettlement(db, invoice.Id);
            if (input.Amount > await Outstanding(db, invoice)) throw new BusinessException("Credit exceeds the outstanding purchase bill.");
            db.BillAllocations.Add(new() { Id = Guid.NewGuid(), InvoiceId = invoice.Id, VoucherId = doc.Id, Amount = input.Amount });
        }
        Journal(db, doc, new((Account)destination, input.Amount, 0), new((Account)1300, 0, input.Amount));
        db.SupplierCreditUses.Add(new() { Id = Guid.NewGuid(), CreditId = credit.Id, DocumentId = doc.Id, Amount = input.Amount }); return doc.Id;
    });
    private static async Task<decimal> Outstanding(BusinessDb db, BusinessDocument invoice)
    {
        var paid = (await db.BillAllocations.Where(x => x.InvoiceId == invoice.Id && !db.Documents.Any(d => d.Reverses == x.VoucherId)).ToListAsync()).Sum(x => x.Amount);
        var credited = (await db.ReturnAllocations.Where(x => x.OriginalDocumentId == invoice.Id && !db.Documents.Any(d => d.Reverses == x.ReturnDocumentId)).ToListAsync()).Sum(x => x.BillCredit); return invoice.Total - paid - credited;
    }
    public async Task<IReadOnlyList<TillView>> Tills()
    {
        var actor = await Authorize("business.read"); await using var db = await factory.CreateDbContextAsync(); var manage = actor.Permissions.Contains("tills.manage");
        var shifts = await db.TillShifts.AsNoTracking().Where(x => x.Branch == actor.Branch && (manage || x.Cashier == actor.Id)).ToListAsync(); var ids = shifts.Select(x => x.Id).ToArray();
        var closes = await db.TillCloses.AsNoTracking().Where(x => ids.Contains(x.ShiftId)).ToDictionaryAsync(x => x.ShiftId);
        var entries = await (from e in db.TillEntries join j in db.Journal on e.JournalLineId equals j.Id join d in db.Documents on j.DocumentId equals d.Id where ids.Contains(e.ShiftId) select new { e.ShiftId, e.Actor, d.Number, j.Debit, j.Credit }).ToListAsync();
        return shifts.OrderByDescending(x => x.Opened).Select(s => { var lines = entries.Where(x => x.ShiftId == s.Id).ToArray(); var movement = lines.Sum(x => x.Debit - x.Credit); closes.TryGetValue(s.Id, out var close); return new TillView(s.Id, s.Cashier, s.AccountCode, s.Opened, s.OpeningCount, s.OpeningBook, movement, close?.Expected ?? s.OpeningCount + movement, close?.Closed, close?.Counted, close is null ? null : close.Counted - close.Expected, close?.Note ?? "", lines.Select(x => new TillMovementView(x.Number, x.Actor, x.Debit, x.Credit)).ToArray()); }).ToArray();
    }
    public Task<Guid> OpenTill(OpenTillCommand input) => Execute(input.OperationId, "tills.use", "till.open", input, async (db, actor) => {
        Amount(input.OpeningCount, true); await Funds(db, input.AccountCode);
        var assigned = await db.TillAssignments.Where(x => x.Branch == actor.Branch && x.AccountCode == input.AccountCode).Select(x => x.Cashier).ToListAsync();
        if (assigned.Count > 0 && !assigned.Contains(actor.Id)) throw new BusinessException("This cash drawer is assigned to other cashiers. Ask a manager to assign it to you under Banking → Cash drawers.");
        if (await db.TillShifts.AnyAsync(x => x.Branch == actor.Branch && (x.AccountCode == input.AccountCode || x.Cashier == actor.Id) && !db.TillCloses.Any(c => c.ShiftId == x.Id))) throw new BusinessException("This cashier or cash ledger already has an open shift.");
        var balance = (await db.Journal.Where(x => x.Branch == actor.Branch && (int)x.Account == input.AccountCode).ToListAsync()).Sum(x => x.Debit - x.Credit);
        var shift = new TillShift { Id = Guid.NewGuid(), Branch = actor.Branch, Cashier = actor.Id, AccountCode = input.AccountCode, Opened = DateTimeOffset.UtcNow, OpeningCount = input.OpeningCount, OpeningBook = balance }; db.TillShifts.Add(shift); return shift.Id;
    });
    public Task<Guid> CloseTill(CloseTillCommand input) => Execute(input.OperationId, "tills.use", "till.close", input, async (db, actor) => {
        Amount(input.Counted, true); var shift = await db.TillShifts.SingleOrDefaultAsync(x => x.Id == input.ShiftId && x.Branch == actor.Branch) ?? throw new BusinessException("Shift not found.");
        if (shift.Cashier != actor.Id) actor.Demand("tills.manage");
        if (await db.TillCloses.AnyAsync(x => x.ShiftId == shift.Id)) throw new BusinessException("This shift is already closed.");
        var lines = await (from e in db.TillEntries join j in db.Journal on e.JournalLineId equals j.Id where e.ShiftId == shift.Id select new { j.Debit, j.Credit }).ToListAsync(); var movement = lines.Sum(x => x.Debit - x.Credit);
        if (movement != input.ExpectedMovement) throw new BusinessException("Cash activity changed while you counted. Refresh and recount before closing.");
        var close = new TillClose { Id = Guid.NewGuid(), ShiftId = shift.Id, Closed = DateTimeOffset.UtcNow, Expected = shift.OpeningCount + movement, Counted = input.Counted, Note = Rules.Required(input.Note, "Closing note / discrepancy explanation") }; db.TillCloses.Add(close); return close.Id;
    });
    private async Task<Guid> Execute<T>(Guid operation, string permission, string action, T input, Func<BusinessDb, Actor, Task<Guid>> work)
    {
        var actor = await Authorize(permission); if (operation == Guid.Empty) throw new BusinessException("Operation ID is required.");
        var payload = JsonSerializer.Serialize(input); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + action + payload)));
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var prior = await db.Operations.SingleOrDefaultAsync(x => x.Id == operation); if (prior != null) { if (prior.Fingerprint != hash) throw new BusinessException("Operation ID already used with different data or identity."); return prior.ResultId; }
        var id = await work(db, actor); db.Operations.Add(new() { Id = operation, ResultId = id, Branch = actor.Branch, Actor = actor.Id, Action = action, Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = hash }); await db.SaveChangesAsync(); await tx.CommitAsync(); return id;
    }
}



