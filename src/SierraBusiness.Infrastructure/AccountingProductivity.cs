using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed partial class FinanceService
{
    private const string ClassAction = "accounts.allocation-class", ProjectAction = "accounts.project", ScheduleAction = "accounts.schedule", SchedulePostAction = "accounts.schedule.post";
    private static async Task<AccountingProductivitySnapshot> ProductivityFor(BusinessDb db, string branch)
    {
        var events = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && (o.Action == ClassAction || o.Action == ProjectAction || o.Action == ScheduleAction || o.Action == SchedulePostAction)).ToListAsync();
        var classes = events.Where(o => o.Action == ClassAction).Select(o => (o.ResultId, C: JsonSerializer.Deserialize<AllocationClassCommand>(o.Payload)!)).GroupBy(x => x.ResultId).Select(g => g.MaxBy(x => x.C.ExpectedVersion)).Select(x => new AllocationClassView(x.ResultId, x.C.ExpectedVersion + 1, x.C.Name.Trim(), x.C.Shares, x.C.Active)).ToArray();
        var projects = events.Where(o => o.Action == ProjectAction).Select(o => (o.ResultId, C: JsonSerializer.Deserialize<ProjectCommand>(o.Payload)!)).GroupBy(x => x.ResultId).Select(g => g.MaxBy(x => x.C.ExpectedVersion)).Select(x => new ProjectView(x.ResultId, x.C.ExpectedVersion + 1, x.C.Code.Trim().ToUpperInvariant(), x.C.Name.Trim(), x.C.CentreId, x.C.Start, x.C.End, x.C.RevenueBudget, x.C.ExpenseBudget, x.C.Active)).ToArray();
        var posted = events.Where(o => o.Action == SchedulePostAction).Select(o => (o.ResultId, C: JsonSerializer.Deserialize<PostScheduledJournalCommand>(o.Payload)!)).ToArray();
        var journals = events.Where(o => o.Action == ScheduleAction).Select(o => (o.ResultId, C: JsonSerializer.Deserialize<ScheduledJournalCommand>(o.Payload)!)).GroupBy(x => x.ResultId).Select(g => g.MaxBy(x => x.C.ExpectedVersion)).Select(x => new ScheduledJournalView(x.ResultId, x.C.ExpectedVersion + 1, x.C.Name.Trim(), x.C.Date, x.C.ReverseOn, x.C.Lines, x.C.Active, posted.Where(p => p.C.ScheduleId == x.ResultId && !p.C.Reversal).Select(p => (Guid?)p.ResultId).SingleOrDefault(), posted.Where(p => p.C.ScheduleId == x.ResultId && p.C.Reversal).Select(p => (Guid?)p.ResultId).SingleOrDefault())).ToArray();
        return new(classes, projects, journals);
    }
    public async Task<AccountingProductivitySnapshot> Productivity() { var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync(); return await ProductivityFor(db, actor.Branch); }
    private static void CheckShares(IReadOnlyList<CentreShare> shares, AccountingStructure structure)
    {
        if (shares is null || shares.Count is < 1 or > 100 || shares.Select(s => s.CentreId).Distinct().Count() != shares.Count || shares.Any(s => s.Percent <= 0 || s.Percent > 100 || AccountingEngine.Money(s.Percent) != s.Percent) || shares.Sum(s => s.Percent) != 100) throw new BusinessException("Choose distinct centres with positive shares totalling 100 percent.");
        foreach (var share in shares)
        {
            var centre = structure.Centres.SingleOrDefault(c => c.Id == share.CentreId) ?? throw new BusinessException("Cost centre not found in this branch.");
            Hierarchy(centre.Id, centre.ParentId, structure.Centres.ToDictionary(c => c.Id, c => c.ParentId));
            for (CostCentreView? c = centre; c is not null; c = c.ParentId is Guid p ? structure.Centres.Single(x => x.Id == p) : null) if (!c.Active) throw new BusinessException("Choose centres with active parents.");
        }
    }
    public Task<Guid> SaveAllocationClass(AllocationClassCommand input) => Execute(input.OperationId, ClassAction, input, async (db, actor) =>
    {
        var name = Rules.Required(input.Name, "Allocation class name", 100); var current = (await ProductivityFor(db, actor.Branch)).Classes;
        var old = input.Id is Guid id ? current.SingleOrDefault(c => c.Id == id) ?? throw new BusinessException("Allocation class not found.") : null;
        if ((old?.Version ?? 0) != input.ExpectedVersion) throw new BusinessException("Allocation class changed. Refresh before saving.");
        if (current.Any(c => c.Id != input.Id && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new BusinessException("Allocation class name already exists.");
        CheckShares(input.Shares, await StructureFor(db, actor.Branch)); return old?.Id ?? Guid.NewGuid();
    });
    private static AllocateCentreCommand ClassAllocation(UseAllocationClassCommand input, AllocationClassView saved)
    {
        if (!saved.Active || saved.Version != input.ExpectedVersion) throw new BusinessException("Allocation class changed or is inactive. Review it again.");
        return new(input.OperationId, input.Date, input.Narration, input.AccountCode, input.FundsAccount, input.Income, input.Amount, saved.Shares.ToList(), input.ApprovalId);
    }
    public async Task<CentreAllocationPreview> PreviewAllocationClass(UseAllocationClassCommand input)
    {
        var saved = (await Productivity()).Classes.SingleOrDefault(c => c.Id == input.ClassId) ?? throw new BusinessException("Allocation class not found."); return await PreviewCentreAllocation(ClassAllocation(input, saved));
    }
    public async Task<Guid> PostAllocationClass(UseAllocationClassCommand input)
    {
        // Store the reviewed class identity as well as its shares; retries survive later edits.
        var actor = await Authorize(true); var source = JsonSerializer.Serialize(input);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == input.OperationId);
            if (prior is not null) { if (prior.Branch != actor.Branch || prior.Actor != actor.Id || prior.Action != "voucher.post" || JsonSerializer.Deserialize<VoucherCommand>(prior.Payload)?.AllocationSource != source) throw new BusinessException("Operation ID already used with different class data or identity."); return prior.ResultId; }
        }
        var preview = await PreviewAllocationClass(input);
        return await PostVoucher(new(input.OperationId, input.Income ? VoucherType.Receipt : VoucherType.Payment, input.Date, input.Narration, preview.Lines.ToList(), [], input.ApprovalId, source));
    }
    public Task<Guid> SaveProject(ProjectCommand input) => Execute(input.OperationId, ProjectAction, input, async (db, actor) =>
    {
        var code = Rules.Required(input.Code, "Project code", 40).ToUpperInvariant(); Rules.Required(input.Name, "Project name", 100); Money(input.RevenueBudget, "Revenue budget", true); Money(input.ExpenseBudget, "Expense budget", true);
        if (input.Start.Year < 2000 || input.End < input.Start) throw new BusinessException("Project end must be on or after its start.");
        var structure = await StructureFor(db, actor.Branch); if (!structure.Centres.Any(c => c.Id == input.CentreId)) throw new BusinessException("Choose a cost centre for this project.");
        if (input.Active) CheckShares([new(input.CentreId,100)],structure);
        var current = (await ProductivityFor(db, actor.Branch)).Projects; var old = input.Id is Guid id ? current.SingleOrDefault(p => p.Id == id) ?? throw new BusinessException("Project not found.") : null;
        if ((old?.Version ?? 0) != input.ExpectedVersion) throw new BusinessException("Project changed. Refresh before saving.");
        if (old is not null && (old.Code != code || old.CentreId != input.CentreId)) throw new BusinessException("Keep the project's original code and centre to preserve its reporting history.");
        if (current.Any(p => p.Id != input.Id && (p.Code == code || p.CentreId == input.CentreId))) throw new BusinessException("Project code or centre already belongs to another project.");
        return old?.Id ?? Guid.NewGuid();
    });
    public Task<Guid> AssignProjectDocument(ProjectDocumentCommand input) => Execute(input.OperationId,"accounts.project.document",input,async (db,actor) =>
    {
        Rules.Required(input.Reason,"Project assignment reason",200);
        var project = (await ProductivityFor(db,actor.Branch)).Projects.SingleOrDefault(p => p.Id == input.ProjectId) ?? throw new BusinessException("Project not found.");
        if (!project.Active || project.Version != input.ExpectedProjectVersion) throw new BusinessException("Project changed or is inactive. Refresh before assigning.");
        var doc = await db.Documents.SingleOrDefaultAsync(d => d.Id == input.DocumentId && d.Branch == actor.Branch && d.Reverses == null && !db.Documents.Any(r => r.Reverses == d.Id)) ?? throw new BusinessException("Choose an active document in this company.");
        if (doc.BusinessDate < project.Start || project.End is DateOnly end && doc.BusinessDate > end) throw new BusinessException("The source document must fall within the project dates.");
        await OpenDate(db,actor.Branch,doc.BusinessDate);
        var assignments = await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == "accounts.project.document").ToListAsync();
        if (assignments.Any(o => JsonSerializer.Deserialize<ProjectDocumentCommand>(o.Payload)!.DocumentId == doc.Id)) throw new BusinessException("This document already belongs to a project. Assignments are immutable; correct the original document if needed.");
        return Guid.NewGuid();
    });
    public async Task<ProjectReport> ProjectProfitability(DateOnly from, DateOnly to)
    {
        var saved = await Productivity(); var structure = await Structure(); var report = await Read(from, to);
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var assignments = (await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == "accounts.project.document").ToListAsync()).Select(o => JsonSerializer.Deserialize<ProjectDocumentCommand>(o.Payload)!).ToDictionary(c => c.DocumentId,c => c.ProjectId);
        var reversals = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && d.Reverses != null && d.BusinessDate >= from && d.BusinessDate <= to).ToDictionaryAsync(d => d.Id,d => d.Reverses!.Value);
        return new(from, to, saved.Projects.Select(p =>
        {
            var code = structure.Centres.Single(c => c.Id == p.CentreId).Code;
            var lines = report.Entries.Where(e => (assignments.TryGetValue(reversals.GetValueOrDefault(e.DocumentId,e.DocumentId),out var taggedProject) ? taggedProject == p.Id : e.CostCentre.Equals(code,StringComparison.OrdinalIgnoreCase)) && e.Date >= p.Start && (p.End is null || e.Date <= p.End || reversals.ContainsKey(e.DocumentId))).ToArray();
            var revenue = lines.Where(e => report.Accounts.Single(a => a.Code == e.AccountCode).Group == LedgerGroup.Income).Sum(e => e.Credit - e.Debit);
            var expense = lines.Where(e => report.Accounts.Single(a => a.Code == e.AccountCode).Group == LedgerGroup.Expense).Sum(e => e.Debit - e.Credit);
            return new ProjectBalance(p, revenue, expense, revenue - p.RevenueBudget, p.ExpenseBudget - expense);
        }).ToArray());
    }
    public Task<Guid> ScheduleJournal(ScheduledJournalCommand input) => Execute(input.OperationId, ScheduleAction, input, async (db, actor) =>
    {
        Rules.Required(input.Name, "Journal name", 100);
        if (input.Date.Year < 2000 || input.ReverseOn <= input.Date || input.Lines is null || input.Lines.Count is < 2 or > 200) throw new BusinessException("Enter 2–200 balanced lines and a reversal date after the journal date.");
        var accounts = await db.LedgerAccounts.ToDictionaryAsync(a => a.Id);
        foreach (var line in input.Lines)
        {
            Money(line.Debit, "Debit", true); Money(line.Credit, "Credit", true);
            if ((line.Debit == 0) == (line.Credit == 0) || !accounts.TryGetValue(line.AccountCode, out var account) || account.Control || line.CostCentre is null || line.CostCentre.Length > 100 || line.CostCentre.Length > 0 && account.Group is not (LedgerGroup.Income or LedgerGroup.Expense)) throw new BusinessException("Use custom or non-control accounts with valid debit/credit lines and income/expense centres.");
        }
        Money(input.Lines.Sum(l => l.Debit), "Journal total"); AccountingEngine.Balanced(input.Lines.Select(l => new Posting((Account)l.AccountCode, l.Debit, l.Credit)).ToList());
        var current = (await ProductivityFor(db, actor.Branch)).Journals; var old = input.Id is Guid id ? current.SingleOrDefault(j => j.Id == id) ?? throw new BusinessException("Schedule not found.") : null;
        if ((old?.Version ?? 0) != input.ExpectedVersion) throw new BusinessException("Journal schedule changed. Refresh before saving.");
        if (old?.PostedDocument is not null) throw new BusinessException("A posted schedule is immutable; create a new schedule for another journal.");
        return old?.Id ?? Guid.NewGuid();
    });
    public async Task<Guid> PostScheduledJournal(PostScheduledJournalCommand input) => await Controls.Guard(factory, await session.CurrentActor(), () => Execute(input.OperationId, SchedulePostAction, input, async (db, actor) =>
    {
        var saved = (await ProductivityFor(db, actor.Branch)).Journals.SingleOrDefault(j => j.Id == input.ScheduleId) ?? throw new BusinessException("Journal schedule not found.");
        if (!saved.Active || saved.Version != input.ExpectedVersion) throw new BusinessException("Schedule changed or is inactive. Review again.");
        if (input.Reversal ? saved.PostedDocument is null || saved.ReverseOn is null || saved.ReversalDocument is not null : saved.PostedDocument is not null) throw new BusinessException("This scheduled journal stage is unavailable or already posted.");
        if (input.Reversal && await db.Documents.AnyAsync(d => d.Reverses == saved.PostedDocument)) throw new BusinessException("The original scheduled journal was already cancelled. Do not post its scheduled reversal.");
        var lines = input.Reversal ? saved.Lines.Select(l => new VoucherLine(l.AccountCode, l.Credit, l.Debit, l.CostCentre)).ToList() : saved.Lines.ToList();
        return await PostVoucherCore(db, actor, new(input.OperationId, VoucherType.Journal, input.Reversal ? saved.ReverseOn!.Value : saved.Date, (input.Reversal ? "Scheduled reversal: " : "Scheduled journal: ") + saved.Name, lines, [], input.ApprovalId));
    }));
}
