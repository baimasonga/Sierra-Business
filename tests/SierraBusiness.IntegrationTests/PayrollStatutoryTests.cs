using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>0.21.0: PAYE and NASSIT from dated rules, proration and one-off adjustments, per-employee salary payments, PAYE/NASSIT remittances, schedules and reversal guards.</summary>
public sealed class PayrollStatutoryTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-paye-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private PayrollService payroll = null!; private BusinessService business = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private DateOnly month;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await TestDatabase.MigrateAsync(db);
        payroll = new(factory, session, Options.Create(new TradingOptions())); business = new(factory, Options.Create(new TradingOptions()));
        month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private Task<Guid> Hire(string code, decimal basic = 3000, decimal allowances = 800, decimal deductions = 100, bool resident = true, DateOnly? born = null, DateOnly? start = null) =>
        payroll.SaveEmployee(new(Guid.NewGuid(), null, 0, code, $"Employee {code}", "Sales", basic, allowances, deductions, start ?? today.AddYears(-2), null, $"TIN-{code}", $"NAS-{code}", resident, null, born));
    private Task Recommended() => payroll.SaveRules(new(Guid.NewGuid(), PayrollRules.Recommended(month)));
    private async Task<Dictionary<int, (decimal Debit, decimal Credit)>> Ledger()
    {
        var snapshot = await business.Read(session.Actor);
        Assert.Equal(snapshot.Ledger.Sum(x => x.Debit), snapshot.Ledger.Sum(x => x.Credit));
        return snapshot.Ledger.GroupBy(x => (int)x.Account).ToDictionary(g => g.Key, g => (g.Sum(x => x.Debit), g.Sum(x => x.Credit)));
    }
    private async Task Verified() { await using var db = factory.CreateDbContext(); var report = await HistoryProtection.Verify(db); Assert.True(report.Ok, string.Join("; ", report.Problems)); }

    [Fact] public void PayeIsWorkedOutBandByBand()
    {
        var bands = PayrollRules.Recommended(default).PayeBands;
        Assert.Equal(0m, PayrollMath.Paye(600, bands));
        Assert.Equal(90m, PayrollMath.Paye(1200, bands));
        Assert.Equal(360m, PayrollMath.Paye(2400, bands));
        Assert.Equal(585m, PayrollMath.Paye(3150, bands)); // 90 + 120 + 150 + 750 × 30%
        Assert.Equal(15m, PayrollMath.Paye(700, bands));
    }

    [Fact] public async Task StatutoryDeductionsArePostedToTheirOwnAccounts()
    {
        var a = await Hire("E001");
        var off = await payroll.Preview(new(Guid.NewGuid(), month, today, [a]));
        Assert.False(off.Statutory); Assert.Equal(0, off.Paye); // no rules yet: as before 0.21.0
        await Recommended();
        var preview = await payroll.Preview(new(Guid.NewGuid(), month, today, [a]));
        var c = preview.Lines.Single().Calculation;
        Assert.True(preview.Statutory);
        Assert.Equal((3800m, 500m, 150m, 3150m, 585m, 2965m, 300m), (c.Gross, c.TaxFreeAllowances, c.NassitEmployee, c.Chargeable, c.Paye, c.Net, c.NassitEmployer));
        var run = await payroll.Post(new(Guid.NewGuid(), month, today, [a]));
        var l = await Ledger();
        Assert.Equal(3800m, l[5200].Debit); Assert.Equal(300m, l[5210].Debit);
        Assert.Equal(2965m, l[2200].Credit); Assert.Equal(585m, l[2310].Credit); Assert.Equal(450m, l[2320].Credit); Assert.Equal(100m, l[2300].Credit);
        var slip = (await payroll.Read()).Payslips.Single();
        Assert.Equal(835m, slip.Deductions); Assert.Equal("TIN-E001", slip.Tin);
        await payroll.SaveRules(new(Guid.NewGuid(), PayrollRules.Recommended(month.AddMonths(1)) with { Version = 1, NassitEmployee = .06m, Source = "Test change" }));
        Assert.Equal(2965m, (await payroll.Read()).Payslips.Single().Net); // posted payslips keep their calculation
        await Verified();
    }

    [Fact] public async Task RulesAreValidatedAndCannotRewritePostedMonths()
    {
        var a = await Hire("E001");
        await Recommended();
        await payroll.Post(new(Guid.NewGuid(), month, today, [a]));
        var rules = PayrollRules.Recommended(month) with { Version = 1 };
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SaveRules(new(Guid.NewGuid(), rules))); // the month is already posted
        var next = rules with { EffectiveFrom = month.AddMonths(1) };
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SaveRules(new(Guid.NewGuid(), next with { Version = 0 })));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SaveRules(new(Guid.NewGuid(), next with { Bands = [new(1000, .1m), new(800, .2m), new(null, .3m)] })));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SaveRules(new(Guid.NewGuid(), next with { Bands = [new(1000, .1m)] }))); // no open-ended top band
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SaveRules(new(Guid.NewGuid(), next with { NassitEmployer = 1.5m })));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.SaveRules(new(Guid.NewGuid(), next with { Source = "" })));
        await payroll.SaveRules(new(Guid.NewGuid(), next with { TaxFreeAllowances = 600 }));
        var view = await payroll.Rules();
        Assert.Equal(2, view.History.Count); Assert.Equal(month.AddMonths(1), view.History[0].EffectiveFrom);
    }

    [Fact] public async Task ProrationAdjustmentsNonResidentsAndTheNassitAgeLimit()
    {
        await Recommended();
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        var joiner = await Hire("J01", start: month.AddDays(10));
        var absent = await Hire("A01");
        var foreign = await Hire("N01", resident: false);
        var senior = await Hire("S01", born: month.AddYears(-61));
        var p = await payroll.Preview(new(Guid.NewGuid(), month, today, [joiner, absent, foreign, senior], [new(absent, UnpaidDays: 2, Overtime: 400, Bonus: 100, ExtraDeduction: 50, Note: "Advance")]));
        var j = p.Lines.Single(x => x.Code == "J01").Calculation;
        Assert.Equal(days - 10, j.DaysPaid); Assert.Equal(AccountingEngine.Money(3000m * (days - 10) / days), j.Basic);
        var a = p.Lines.Single(x => x.Code == "A01").Calculation;
        Assert.Equal(days - 2, a.DaysPaid); Assert.Equal(a.Basic + a.Allowances + 500, a.Gross); Assert.Equal(150m, a.OtherDeductions); Assert.Equal("Advance", a.Note);
        Assert.Equal(AccountingEngine.Money(a.Basic * .05m), a.NassitEmployee); // NASSIT on basic salary only
        var n = p.Lines.Single(x => x.Code == "N01").Calculation;
        Assert.Equal(787.5m, n.Paye); // flat 25% for non-residents
        var s = p.Lines.Single(x => x.Code == "S01").Calculation;
        Assert.Equal((0m, 0m), (s.NassitEmployee, s.NassitEmployer)); Assert.Equal(3300m, s.Chargeable);
        await Assert.ThrowsAsync<BusinessException>(() => payroll.Preview(new(Guid.NewGuid(), month, today, [absent], [new(absent, UnpaidDays: 1.3m)])));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.Preview(new(Guid.NewGuid(), month, today, [absent], [new(joiner, Bonus: 10)])));
        var later = await Hire("L01", start: today.AddDays(5));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.Preview(new(Guid.NewGuid(), month, today, [later])));
    }

    [Fact] public async Task SalariesArePaidPerEmployeeAndDeductionsRemittedOnce()
    {
        await Recommended();
        var a = await Hire("E001"); var b = await Hire("E002", basic: 1000, allowances: 0, deductions: 0);
        var run = await payroll.Post(new(Guid.NewGuid(), month, today, [a, b]));
        var slips = (await payroll.Read()).Payslips;
        var first = slips.Single(x => x.EmployeeCode == "E001");
        await Assert.ThrowsAsync<BusinessException>(() => payroll.PaySalaries(new(Guid.NewGuid(), today, PaymentMethod.BankTransfer, "", [first.ItemId]))); // a transfer needs its reference
        var pay = await payroll.PaySalaries(new(Guid.NewGuid(), today, PaymentMethod.BankTransfer, "RCB-TRF-001", [first.ItemId]));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.PaySalaries(new(Guid.NewGuid(), today, PaymentMethod.Cash, "", [first.ItemId])));
        var read = (await payroll.Read()).Payslips;
        Assert.True(read.Single(x => x.EmployeeCode == "E001").Paid); Assert.False(read.Single(x => x.EmployeeCode == "E002").Paid);
        var l = await Ledger();
        Assert.Equal(2965m, l[2200].Debit); Assert.Equal(2965m, l[1010].Credit);

        var schedule = await payroll.Schedule(month);
        Assert.Equal(2, schedule.Lines.Count);
        var bPaye = PayrollMath.Paye(950, PayrollRules.Recommended(month).PayeBands); // 1000 − 5% NASSIT
        Assert.Equal(585m + bPaye, schedule.Paye); Assert.Equal(150m + 50m, schedule.NassitEmployee); Assert.Equal(300m + 100m, schedule.NassitEmployer);
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), run, "Wrong month"))); // paid salaries block reversal
        await payroll.Remit(new(Guid.NewGuid(), month, StatutoryKind.Paye, today, PaymentMethod.BankTransfer, "NRA-RCPT-77"));
        await payroll.Remit(new(Guid.NewGuid(), month, StatutoryKind.Nassit, today, PaymentMethod.Cash, "NASSIT-RCPT-12"));
        await Assert.ThrowsAsync<BusinessException>(() => payroll.Remit(new(Guid.NewGuid(), month, StatutoryKind.Paye, today, PaymentMethod.Cash, "Again")));
        schedule = await payroll.Schedule(month);
        Assert.Equal(0m, schedule.PayeOutstanding); Assert.Equal(0m, schedule.NassitOutstanding); Assert.Equal(2, schedule.Remittances.Count);
        l = await Ledger();
        Assert.Equal(l[2310].Credit, l[2310].Debit); Assert.Equal(l[2320].Credit, l[2320].Debit);

        var workbook = await payroll.ExportSchedule(month);
        using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(new MemoryStream(workbook), false))
            Assert.Equal(3, doc.WorkbookPart!.Workbook!.Sheets!.Count());

        await business.Reverse(session.Actor, new(Guid.NewGuid(), pay, "Paid to the wrong account"));
        await Assert.ThrowsAsync<BusinessException>(() => business.Reverse(session.Actor, new(Guid.NewGuid(), run, "Wrong month"))); // still remitted
        foreach (var r in schedule.Remittances) await business.Reverse(session.Actor, new(Guid.NewGuid(), r.DocumentId, "Remitted in error"));
        await business.Reverse(session.Actor, new(Guid.NewGuid(), run, "Wrong month"));
        Assert.All((await payroll.Read()).Payslips, x => Assert.True(x.Reversed));
        await Verified();
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
