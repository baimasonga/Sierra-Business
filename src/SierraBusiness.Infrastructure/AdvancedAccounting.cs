using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure.Imports;
namespace SierraBusiness.Infrastructure;

public sealed partial class FinanceService
{
    private const string GroupAction = "accounts.group", MappingAction = "accounts.group.assign", CentreAction = "accounts.centre", ScenarioAction = "accounts.scenario";
    private static async Task<AccountingStructure> StructureFor(BusinessDb db, string branch)
    {
        var events = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && (o.Action == GroupAction || o.Action == MappingAction || o.Action == CentreAction)).ToListAsync();
        var groups = events.Where(o => o.Action == GroupAction).Select(o => (o.ResultId, C: JsonSerializer.Deserialize<AccountGroupCommand>(o.Payload)!)).GroupBy(x => x.ResultId).Select(g => g.MaxBy(x => x.C.ExpectedVersion)).Select(x => new AccountGroupView(x.ResultId, x.C.ExpectedVersion + 1, x.C.Name.Trim(), x.C.Nature, x.C.ParentId)).OrderBy(g => g.Name).ToArray();
        var mappings = events.Where(o => o.Action == MappingAction).Select(o => JsonSerializer.Deserialize<LedgerGroupingCommand>(o.Payload)!).GroupBy(c => c.AccountCode).Select(g => g.MaxBy(c => c.ExpectedVersion)!).Select(c => new LedgerGroupingView(c.AccountCode, c.ExpectedVersion + 1, c.GroupId)).ToArray();
        var centres = events.Where(o => o.Action == CentreAction).Select(o => (o.ResultId, C: JsonSerializer.Deserialize<CostCentreCommand>(o.Payload)!)).GroupBy(x => x.ResultId).Select(g => g.MaxBy(x => x.C.ExpectedVersion)).Select(x => new CostCentreView(x.ResultId, x.C.ExpectedVersion + 1, x.C.Code.Trim(), x.C.Name.Trim(), x.C.ParentId, x.C.Active)).OrderBy(c => c.Code).ToArray();
        return new(groups, mappings, centres);
    }
    public async Task<AccountingStructure> Structure() { var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync(); return await StructureFor(db, actor.Branch); }
    private static void Hierarchy(Guid id, Guid? parent, IReadOnlyDictionary<Guid, Guid?> nodes)
    {
        var seen = new HashSet<Guid> { id }; var depth = 0;
        while (parent is Guid p)
        {
            if (!seen.Add(p) || ++depth > 16) throw new BusinessException("Groups and cost centres cannot contain cycles or more than 16 parent levels.");
            if (!nodes.TryGetValue(p, out parent)) throw new BusinessException("Choose a parent in this branch.");
        }
    }
    public Task<Guid> SaveAccountGroup(AccountGroupCommand input) => Execute(input.OperationId, GroupAction, input, async (db, actor) =>
    {
        var name = Rules.Required(input.Name, "Account group name", 100); if (!Enum.IsDefined(input.Nature)) throw new BusinessException("Choose an account nature.");
        var s = await StructureFor(db, actor.Branch); var old = input.Id is Guid id ? s.Groups.SingleOrDefault(g => g.Id == id) ?? throw new BusinessException("Group not found.") : null;
        if ((old?.Version ?? 0) != input.ExpectedVersion) throw new BusinessException("Account group changed. Refresh before saving.");
        if (s.Groups.Any(g => g.Id != input.Id && g.ParentId == input.ParentId && g.Nature == input.Nature && g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new BusinessException("A group with this name already exists under that parent.");
        var result = old?.Id ?? Guid.NewGuid(); Hierarchy(result, input.ParentId, s.Groups.ToDictionary(g => g.Id, g => g.ParentId));
        var proposed = s.Groups.ToDictionary(g => g.Id, g => g.ParentId); proposed[result] = input.ParentId; foreach (var node in proposed) Hierarchy(node.Key, node.Value, proposed);
        if (input.ParentId is Guid parent && s.Groups.Single(g => g.Id == parent).Nature != input.Nature || s.Groups.Any(g => g.ParentId == result && g.Nature != input.Nature)) throw new BusinessException("A nested account group must retain its parent and child account nature.");
        var accounts = await db.LedgerAccounts.ToDictionaryAsync(a => a.Id);
        if (s.Ledgers.Any(m => m.GroupId == result && accounts[m.AccountCode].Group != input.Nature)) throw new BusinessException("This group contains accounts with a different nature.");
        return result;
    });
    public Task<Guid> AssignLedgerGroup(LedgerGroupingCommand input) => Execute(input.OperationId, MappingAction, input, async (db, actor) =>
    {
        var s = await StructureFor(db, actor.Branch); var account = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.AccountCode) ?? throw new BusinessException("Choose a ledger account.");
        if ((s.Ledgers.SingleOrDefault(m => m.AccountCode == input.AccountCode)?.Version ?? 0) != input.ExpectedVersion) throw new BusinessException("Ledger grouping changed. Refresh before saving.");
        if (input.GroupId is Guid group && s.Groups.SingleOrDefault(g => g.Id == group)?.Nature != account.Group) throw new BusinessException("Assign the account to a group with the same account nature.");
        return Guid.NewGuid();
    });
    public Task<Guid> SaveCostCentre(CostCentreCommand input) => Execute(input.OperationId, CentreAction, input, async (db, actor) =>
    {
        var code = Rules.Required(input.Code, "Cost centre code", 100); Rules.Required(input.Name, "Cost centre name", 100);
        var s = await StructureFor(db, actor.Branch); var old = input.Id is Guid id ? s.Centres.SingleOrDefault(c => c.Id == id) ?? throw new BusinessException("Cost centre not found.") : null;
        if ((old?.Version ?? 0) != input.ExpectedVersion) throw new BusinessException("Cost centre changed. Refresh before saving.");
        if (old is not null && old.Code != code) throw new BusinessException("Keep the existing cost centre code; create another centre for a new code.");
        if (s.Centres.Any(c => c.Id != input.Id && c.Code.Equals(code, StringComparison.OrdinalIgnoreCase))) throw new BusinessException("Cost centre code already exists.");
        var result = old?.Id ?? Guid.NewGuid(); var proposed = s.Centres.ToDictionary(c => c.Id, c => c.ParentId); proposed[result] = input.ParentId; foreach (var node in proposed) Hierarchy(node.Key, node.Value, proposed); return result;
    });
    public async Task<StructuredReport> StructureReport(DateOnly from, DateOnly to)
    {
        var s = await Structure(); var report = await Read(from, to); var mapped = s.Ledgers.Where(m => m.GroupId != null).ToDictionary(m => m.AccountCode, m => m.GroupId!.Value);
        var parents = s.Groups.ToDictionary(g => g.Id, g => g.ParentId);
        bool Under(Guid id, Guid group) { var seen = new HashSet<Guid>(); while (true) { if (id == group) return true; if (!seen.Add(id)) throw new BusinessException("Account grouping contains a cycle."); if (!parents.TryGetValue(id, out var p) || p is null) return false; id = p.Value; } }
        int Depth(Guid id) { var d = 0; var seen = new HashSet<Guid>(); while (parents[id] is Guid p) { if (!seen.Add(id)) throw new BusinessException("Account grouping contains a cycle."); d++; id = p; } return d; }
        static decimal Amount(LedgerBalance b) => b.Group == LedgerGroup.Income ? b.Credit - b.Debit : b.Group == LedgerGroup.Expense ? b.Debit - b.Credit : b.Group == LedgerGroup.Asset ? b.Closing : -b.Closing;
        var groups = s.Groups.Select(g => new GroupBalance(g.Id, g.Name, g.Nature, Depth(g.Id), report.Balances.Where(b => mapped.TryGetValue(b.Code, out var id) && Under(id, g.Id)).Sum(Amount))).Concat(Enum.GetValues<LedgerGroup>().Select(n => new GroupBalance(null, n + " not grouped", n, 0, report.Balances.Where(b => b.Group == n && !mapped.ContainsKey(b.Code)).Sum(Amount)))).ToArray();
        var codes = s.Centres.Select(c => c.Code).Concat(report.Entries.Select(e => e.CostCentre)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();
        var centres = codes.Select(code =>
        {
            var rows = report.Entries.Where(e => e.CostCentre.Equals(code, StringComparison.OrdinalIgnoreCase)).ToList();
            return new CostCentreBalance(code, s.Centres.SingleOrDefault(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.Name ?? (code.Length == 0 ? "Unallocated" : code), rows.Where(e => report.Accounts.Single(a => a.Code == e.AccountCode).Group == LedgerGroup.Income).Sum(e => e.Credit - e.Debit), rows.Where(e => report.Accounts.Single(a => a.Code == e.AccountCode).Group == LedgerGroup.Expense).Sum(e => e.Debit - e.Credit));
        }).ToArray();
        return new(from, to, groups, centres);
    }
    public async Task<CentreAllocationPreview> PreviewCentreAllocation(AllocateCentreCommand input)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync(); var s = await StructureFor(db, actor.Branch);
        Money(input.Amount, "Allocation amount"); Rules.Required(input.Narration, "Narration", 200);
        if (input.Shares is null || input.Shares.Count is < 1 or > 100 || input.Shares.Select(x => x.CentreId).Distinct().Count() != input.Shares.Count || input.Shares.Sum(x => x.Percent) != 100 || input.Shares.Any(x => x.Percent <= 0 || x.Percent > 100 || AccountingEngine.Money(x.Percent) != x.Percent)) throw new BusinessException("Enter distinct centres with positive percentages totalling exactly 100.");
        var account = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.AccountCode); var funds = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.FundsAccount);
        if (account is null || account.Control || account.Group != (input.Income ? LedgerGroup.Income : LedgerGroup.Expense) || funds?.CashOrBank != true) throw new BusinessException("Choose an appropriate custom income/expense account and a cash/bank account.");
        var lines = new List<VoucherLine>(); decimal used = 0;
        for (var i = 0; i < input.Shares.Count; i++)
        {
            var centre = s.Centres.SingleOrDefault(c => c.Id == input.Shares[i].CentreId) ?? throw new BusinessException("Cost centre not found.");
            Hierarchy(centre.Id, centre.ParentId, s.Centres.ToDictionary(c => c.Id, c => c.ParentId));
            for (CostCentreView? c = centre; c is not null; c = c.ParentId is Guid p ? s.Centres.Single(x => x.Id == p) : null) if (!c.Active) throw new BusinessException("Choose centres with active parents.");
            var amount = i == input.Shares.Count - 1 ? input.Amount - used : AccountingEngine.Money(input.Amount * input.Shares[i].Percent / 100); if (amount <= 0) throw new BusinessException("The amount is too small for these percentage shares."); used += amount;
            lines.Add(new(input.AccountCode, input.Income ? 0 : amount, input.Income ? amount : 0, centre.Code));
        }
        lines.Add(new(input.FundsAccount, input.Income ? input.Amount : 0, input.Income ? 0 : input.Amount)); return new(lines, input.Amount);
    }
    public async Task<Guid> PostCentreAllocation(AllocateCentreCommand input)
    {
        var actor = await Authorize(true); var source = JsonSerializer.Serialize(input);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var previous = await db.Operations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == input.OperationId);
            if (previous is not null)
            {
                if (previous.Branch != actor.Branch || previous.Actor != actor.Id || previous.Action != "voucher.post" || JsonSerializer.Deserialize<VoucherCommand>(previous.Payload)?.AllocationSource != source) throw new BusinessException("Operation ID already used with different allocation data or identity."); return previous.ResultId;
            }
        }
        var preview = await PreviewCentreAllocation(input);
        return await PostVoucher(new(input.OperationId, input.Income ? VoucherType.Receipt : VoucherType.Payment, input.Date, input.Narration, preview.Lines.ToList(), [], input.ApprovalId, source));
    }
    private static async Task<IReadOnlyList<ScenarioView>> ScenariosFor(BusinessDb db, string branch)
    {
        var events = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == ScenarioAction).ToListAsync();
        return events.Select(o => (o.ResultId, C: JsonSerializer.Deserialize<ScenarioCommand>(o.Payload)!)).GroupBy(x => x.ResultId).Select(g => g.MaxBy(x => x.C.ExpectedVersion)).Select(x => new ScenarioView(x.ResultId, x.C.ExpectedVersion + 1, x.C.Name.Trim(), x.C.Kind, x.C.Date, x.C.Note, x.C.Lines, x.C.Active)).OrderBy(s => s.Date).ToArray();
    }
    public async Task<IReadOnlyList<ScenarioView>> Scenarios() { var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync(); return await ScenariosFor(db, actor.Branch); }
    public Task<Guid> SaveScenario(ScenarioCommand input) => Execute(input.OperationId, ScenarioAction, input, async (db, actor) =>
    {
        Rules.Required(input.Name, "Scenario name", 100); if (input.Note?.Length > 1000 || !Enum.IsDefined(input.Kind) || input.Date.Year < 2000 || input.Lines is null || input.Lines.Count > 200) throw new BusinessException("Check scenario date, type, note and lines.");
        var current = await ScenariosFor(db, actor.Branch); var old = input.Id is Guid id ? current.SingleOrDefault(s => s.Id == id) ?? throw new BusinessException("Scenario not found.") : null;
        if ((old?.Version ?? 0) != input.ExpectedVersion) throw new BusinessException("Scenario changed. Refresh before saving.");
        if (input.Kind == ScenarioKind.Memorandum && input.Lines.Count != 0) throw new BusinessException("A memorandum records notes only; use an adjustment for projected amounts.");
        if (input.Kind == ScenarioKind.Adjustment)
        {
            if (input.Lines.Count < 2) throw new BusinessException("An adjustment needs at least two balanced lines."); var accounts = await db.LedgerAccounts.ToDictionaryAsync(a => a.Id);
            foreach (var line in input.Lines) { if (!accounts.ContainsKey(line.AccountCode)) throw new BusinessException("Unknown scenario ledger account."); Money(line.Debit, "Scenario debit", true); Money(line.Credit, "Scenario credit", true); if ((line.Debit == 0) == (line.Credit == 0) || line.CostCentre.Length > 100) throw new BusinessException("Each scenario line needs one positive debit or credit."); }
            AccountingEngine.Balanced(input.Lines.Select(l => new Posting((Account)l.AccountCode, l.Debit, l.Credit)).ToList());
        }
        return old?.Id ?? Guid.NewGuid();
    });
    public async Task<ScenarioReport> ProjectScenarios(DateOnly from, DateOnly to, IReadOnlyList<Guid> ids)
    {
        if (ids is null || ids.Count > 50 || ids.Distinct().Count() != ids.Count) throw new BusinessException("Choose at most 50 distinct scenarios.");
        var scenarios = await Scenarios(); if (ids.Any(id => !scenarios.Any(s => s.Id == id && s.Active && s.Date >= from && s.Date <= to))) throw new BusinessException("Choose active scenarios within the report period.");
        var report = await Read(from, to); var projected = scenarios.Where(s => ids.Contains(s.Id) && s.Kind == ScenarioKind.Adjustment).SelectMany(s => s.Lines).ToList();
        var balances = report.Balances.Select(b => { var change = projected.Where(l => l.AccountCode == b.Code).Sum(l => l.Debit - l.Credit); var actual = b.Group is LedgerGroup.Income or LedgerGroup.Expense ? b.Debit - b.Credit : b.Closing; if (b.Group is LedgerGroup.Income or LedgerGroup.Liability or LedgerGroup.Equity) { actual = -actual; change = -change; } return new ScenarioBalance(b.Code, b.Name, b.Group, actual, change, actual + change); }).ToArray();
        return new(from, to, balances, report.Profit, report.Profit + projected.Where(l => report.Accounts.Single(a => a.Code == l.AccountCode).Group is LedgerGroup.Income or LedgerGroup.Expense).Sum(l => l.Credit - l.Debit));
    }
    public async Task<InterestReport> Interest(InterestRequest q)
    {
        var actor = await Authorize(); if (q.From > q.To || q.From.Year < 2000 || q.To > DateOnly.FromDateTime(DateTime.UtcNow) || q.To.DayNumber - q.From.DayNumber > 3660 || q.GraceDays is < 0 or > 3660 || q.AnnualPercent < 0 || q.AnnualPercent > 100 || AccountingEngine.Money(q.AnnualPercent) != q.AnnualPercent || !Enum.IsDefined(q.Basis)) throw new BusinessException("Choose a past/current period of at most ten years, a 0–100 percent annual rate and a valid day-count basis.");
        await using var db = await factory.CreateDbContextAsync(); var docs = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && d.BusinessDate <= q.To).ToDictionaryAsync(d => d.Id);
        var reverseDates = docs.Values.Where(d => d.Reverses != null).ToDictionary(d => d.Reverses!.Value, d => d.BusinessDate);
        var allocations = await db.BillAllocations.AsNoTracking().Where(a => db.Documents.Any(d => d.Id == a.VoucherId && d.Branch == actor.Branch)).ToListAsync();
        var returns = await db.ReturnAllocations.AsNoTracking().Where(a => db.Documents.Any(d => d.Id == a.OriginalDocumentId && d.Branch == actor.Branch)).ToListAsync();
        bool Active(Guid id, DateOnly day) => docs.TryGetValue(id, out var d) && d.BusinessDate <= day && (!reverseDates.TryGetValue(id, out var reversed) || reversed > day);
        var rows = new List<InterestRow>();
        var bills = docs.Values.Where(d => q.Receivable ? ServiceRules.IsReceivable(d) && d.Kind != DocumentKind.InterestCharge : d.Kind is DocumentKind.Receipt or DocumentKind.LandedCost or DocumentKind.SupplierInvoice && d.Settlement.StartsWith("Supplier payable", StringComparison.Ordinal)).Where(d => string.IsNullOrWhiteSpace(q.Party) || d.Party.Equals(q.Party.Trim(), StringComparison.OrdinalIgnoreCase));
        foreach (var bill in bills)
        {
            var due = bill.DueDate ?? bill.BusinessDate; var startNumber = Math.Max(q.From.DayNumber, Math.Max(bill.BusinessDate.DayNumber, due.DayNumber + q.GraceDays + 1)); if (startNumber > q.To.DayNumber) continue;
            var settled = allocations.Where(a => a.InvoiceId == bill.Id).Select(a => (Id: a.VoucherId, Amount: a.Amount)).Concat(returns.Where(a => a.OriginalDocumentId == bill.Id).Select(a => (Id: a.ReturnDocumentId, Amount: a.BillCredit))).ToArray();
            decimal Balance(DateOnly day) => !Active(bill.Id, day) ? 0 : Math.Max(0, bill.Total - settled.Where(a => Active(a.Id, day)).Sum(a => a.Amount));
            var points = new HashSet<DateOnly> { DateOnly.FromDayNumber(startNumber), q.To.AddDays(1) };
            foreach (var id in settled.Select(a => a.Id).Append(bill.Id)) { if (docs.TryGetValue(id, out var d)) points.Add(d.BusinessDate); if (reverseDates.TryGetValue(id, out var r)) points.Add(r); }
            for (var year = q.From.Year + 1; year <= q.To.Year; year++) points.Add(new(year, 1, 1));
            var ordered = points.Where(d => d.DayNumber >= startNumber && d <= q.To.AddDays(1)).Order().ToArray(); decimal interest = 0; var days = 0;
            for (var i = 0; i < ordered.Length - 1; i++) { var balance = Balance(ordered[i]); var count = ordered[i + 1].DayNumber - ordered[i].DayNumber; if (balance <= 0) continue; var divisor = q.Basis == InterestBasis.Actual360 ? 360 : q.Basis == InterestBasis.ActualYear && DateTime.IsLeapYear(ordered[i].Year) ? 366 : 365; interest += balance * q.AnnualPercent / 100 * count / divisor; days += count; }
            if (days > 0) rows.Add(new(bill.Id, bill.Number, bill.Party, due, days, Balance(q.From), Balance(q.To), AccountingEngine.Money(interest)));
        }
        return new(q, rows.OrderBy(r => r.Due).ToArray(), rows.Sum(r => r.Interest));
    }
    public async Task<byte[]> ExportAdvanced(DateOnly from, DateOnly to, IReadOnlyList<Guid> scenarioIds, InterestRequest? interest = null)
    {
        var structure = await StructureReport(from, to); var scenario = await ProjectScenarios(from, to, scenarioIds); var rows = new List<(string, IReadOnlyList<IReadOnlyList<string>>)>
        {
            ("Account groups", [["Group", "Nature", "Depth", "Amount"], .. structure.Groups.Select(g => (IReadOnlyList<string>)[g.Name, g.Nature.ToString(), g.Depth.ToString(), g.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)])]),
            ("Cost centres", [["Code", "Name", "Income", "Expenses", "Profit"], .. structure.Centres.Select(c => (IReadOnlyList<string>)[c.Code, c.Name, c.Income.ToString(), c.Expenses.ToString(), c.Profit.ToString()])]),
            ("Scenario projection", [["Account", "Actual", "Scenario change", "Projected"], .. scenario.Balances.Select(b => (IReadOnlyList<string>)[b.Code + " " + b.Name, b.Actual.ToString(), b.Change.ToString(), b.Projected.ToString()])])
        };
        if (interest is not null) { var report = await Interest(interest); rows.Add(("Interest worksheet", [["Document", "Party", "Due", "Charged days", "Start balance", "End balance", "Interest"], .. report.Rows.Select(r => (IReadOnlyList<string>)[r.Number, r.Party, r.Due.ToString("yyyy-MM-dd"), r.ChargedDays.ToString(), r.StartBalance.ToString(), r.EndBalance.ToString(), r.Interest.ToString()])])); }
        return WorkbookWriter.Write(rows);
    }
}
