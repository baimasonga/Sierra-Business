using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed class Employee
{
    public Guid Id { get; set; }
    public string Branch { get; set; } = "";
    public int Version { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
    public decimal BasicSalary { get; set; }
    public decimal Allowances { get; set; }
    public decimal Deductions { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
public sealed class PayrollItem
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid DocumentId { get; set; }
    public DateOnly Month { get; set; }
    public string Detail { get; set; } = "";
}
public sealed class PayrollService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IPayrollService
{
    private async Task<Actor> Authorize()
    {
        var actor = await session.CurrentActor(); actor.Demand("accounting.read"); actor.Demand("accounting.write");
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch."); return actor;
    }
    public async Task<PayrollSnapshot> Read()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var employees = await db.Employees.AsNoTracking().Where(x => x.Branch == actor.Branch).OrderBy(x => x.Code).ToListAsync();
        var docs = await db.Documents.AsNoTracking().Where(x => x.Branch == actor.Branch).ToDictionaryAsync(x => x.Id);
        var items = await db.PayrollItems.AsNoTracking().Where(x => db.Documents.Any(d => d.Id == x.DocumentId && d.Branch == actor.Branch)).ToListAsync();
        return new(employees.Select(View).ToArray(), items.Select(x => { var e = JsonSerializer.Deserialize<EmployeeView>(x.Detail)!; var d = docs[x.DocumentId]; return new PayslipView(d.Id, d.Number, x.Month, d.BusinessDate, e.Code, e.Name, e.Department, e.BasicSalary, e.Allowances, e.Deductions, e.BasicSalary + e.Allowances - e.Deductions, docs.Values.Any(r => r.Reverses == d.Id)); }).OrderByDescending(x => x.Month).ToArray());
    }
    private static EmployeeView View(Employee e) => new(e.Id, e.Version, e.Code, e.Name, e.Department, e.BasicSalary, e.Allowances, e.Deductions, e.StartDate, e.EndDate);
    public Task<Guid> SaveEmployee(EmployeeCommand input) => Execute(input.OperationId, "employee.save", input, async (db, actor) =>
    {
        foreach (var amount in new[] { input.BasicSalary, input.Allowances, input.Deductions }) if (amount < 0 || amount > 1_000_000_000 || AccountingEngine.Money(amount) != amount) throw new BusinessException("Salary amounts must be non-negative, at most one billion and have up to two decimals.");
        if (input.BasicSalary + input.Allowances <= 0 || input.Deductions > input.BasicSalary + input.Allowances) throw new BusinessException("Gross salary must be positive and deductions cannot exceed it.");
        if (input.EndDate < input.StartDate) throw new BusinessException("Employment end cannot precede its start.");
        var code = Rules.Required(input.Code, "Employee code", 40).ToUpperInvariant();
        if (await db.Employees.AnyAsync(x => x.Branch == actor.Branch && x.Code == code && x.Id != input.EmployeeId)) throw new BusinessException("Employee code already exists.");
        Employee employee;
        if (input.EmployeeId is null) { employee = new() { Id = Guid.NewGuid(), Branch = actor.Branch }; db.Employees.Add(employee); }
        else { employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == input.EmployeeId && x.Branch == actor.Branch) ?? throw new BusinessException("Employee not found."); if (employee.Version != input.ExpectedVersion) throw new BusinessException("Employee changed. Refresh before saving."); }
        employee.Version++; employee.Code = code; employee.Name = Rules.Required(input.Name, "Employee name"); employee.Department = Rules.Required(input.Department, "Department", 100); employee.BasicSalary = input.BasicSalary; employee.Allowances = input.Allowances; employee.Deductions = input.Deductions; employee.StartDate = input.StartDate; employee.EndDate = input.EndDate;
        return employee.Id;
    });
    public Task<Guid> Post(PayrollCommand input) => Execute(input.OperationId, "payroll.post", input, async (db, actor) =>
    {
        var first = new DateOnly(input.Month.Year, input.Month.Month, 1); var last = first.AddMonths(1).AddDays(-1);
        if (input.PostingDate <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough) || input.PostingDate > DateOnly.FromDateTime(DateTime.UtcNow) || input.PostingDate < last) throw new BusinessException("Post monthly payroll on or after month-end, within an open period and no later than today.");
        if (input.EmployeeIds is null || input.EmployeeIds.Count is < 1 or > 500 || input.EmployeeIds.Distinct().Count() != input.EmployeeIds.Count) throw new BusinessException("Select 1–500 different employees.");
        var doc = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-PAY-{input.OperationId:N}", Kind = DocumentKind.Payroll, Party = $"Payroll {first:yyyy-MM}", BusinessDate = input.PostingDate, Settlement = "Payroll payable", Detail = JsonSerializer.Serialize(input) };
        decimal gross = 0, deductions = 0;
        foreach (var id in input.EmployeeIds)
        {
            var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == id && x.Branch == actor.Branch) ?? throw new BusinessException("Employee not found in this branch.");
            if (employee.StartDate > first || employee.EndDate < last) throw new BusinessException("This fixed monthly payroll covers full-month employees only. Partial-month payroll requires a separate attendance/proration workflow.");
            if (await db.PayrollItems.AnyAsync(x => x.EmployeeId == id && x.Month == first && !db.Documents.Any(d => d.Reverses == x.DocumentId))) throw new BusinessException($"Payroll is already posted for {employee.Code} in this month.");
            var amount = employee.BasicSalary + employee.Allowances; gross += amount; deductions += employee.Deductions;
            db.PayrollItems.Add(new() { Id = Guid.NewGuid(), EmployeeId = id, DocumentId = doc.Id, Month = first, Detail = JsonSerializer.Serialize(View(employee)) });
            db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, Account = (Account)5200, Debit = amount, CostCentre = employee.Department });
        }
        doc.Total = gross; db.Documents.Add(doc);
        if (gross > deductions) db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, Account = (Account)2200, Credit = gross - deductions });
        if (deductions > 0) db.Journal.Add(new() { Id = Guid.NewGuid(), DocumentId = doc.Id, Branch = actor.Branch, Account = (Account)2300, Credit = deductions });
        return doc.Id;
    });
    private async Task<Guid> Execute<T>(Guid operation, string action, T input, Func<BusinessDb, Actor, Task<Guid>> work)
    {
        var actor = await Authorize(); if (operation == Guid.Empty) throw new BusinessException("Operation ID is required.");
        var payload = JsonSerializer.Serialize(input); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Branch + actor.Id + action + payload)));
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.Operations.SingleOrDefaultAsync(x => x.Id == operation); if (previous is not null) { if (hash != previous.Fingerprint) throw new BusinessException("Operation ID already used with different data or identity."); return previous.ResultId; }
        var id = await work(db, actor); db.Operations.Add(new() { Id = operation, ResultId = id, Branch = actor.Branch, Actor = actor.Id, Action = action, Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = hash }); await db.SaveChangesAsync(); await tx.CommitAsync(); return id;
    }
}
