using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure.Imports;
namespace SierraBusiness.Infrastructure;

public sealed partial class StatementsService
{
    public async Task<CompanyReportSnapshot> CompanySnapshot(string company, DateOnly from, DateOnly to)
    {
        var actor = await Authorize(); company = Rules.Required(company, "Company name", 120);
        if (from > to || from.Year < 2000 || to.DayNumber - from.DayNumber > 3660) throw new BusinessException("Choose a reporting period of at most ten years, starting in 2000 or later.");
        await using var db = await factory.CreateDbContextAsync();
        // One read transaction keeps the lineage and both balance sets from the same database snapshot.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var origin = await db.IntegritySeals.AsNoTracking().OrderBy(s => s.Sequence).Select(s => s.Hash).FirstOrDefaultAsync() ?? throw new BusinessException("Initialize the company's protected history before exporting a reporting snapshot.");
        // The immutable chain origin survives renaming, backup restoration and later imported operations.
        var source = new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(origin + "\n" + actor.Branch)).AsSpan(0, 16));
        var (accounts, lines) = await Load(db, actor.Branch, to);
        var balances = lines.GroupBy(l => l.Account).ToDictionary(g => g.Key, g => new { Period = g.Where(l => l.Date >= from).Sum(l => l.Debit - l.Credit), Closing = g.Sum(l => l.Debit - l.Credit) });
        var rows = accounts.Select(a => new CompanyReportAccount(a.Id, a.Name, a.Group,
            balances.GetValueOrDefault(a.Id)?.Period ?? 0,
            balances.GetValueOrDefault(a.Id)?.Closing ?? 0)).ToArray();
        await transaction.CommitAsync();
        var result = new CompanyReportSnapshot(1, source, company, actor.Branch, "NLe", from, to, DateTimeOffset.UtcNow, rows);
        ValidateMember(result); return result;
    }

    private static void ValidateMember(CompanyReportSnapshot member)
    {
        if (member is null || member.Version != 1 || member.SourceId == Guid.Empty || member.Currency != "NLe") throw new BusinessException("Use a version 1 Rokela company snapshot in NLe.");
        Rules.Required(member.Company, "Company name", 120); Rules.Required(member.Branch, "Branch", 120);
        if (member.From > member.To || member.From.Year < 2000 || member.To.DayNumber - member.From.DayNumber > 3660) throw new BusinessException("The snapshot reporting period is invalid.");
        if (member.Accounts is null || member.Accounts.Count == 0 || member.Accounts.Count > 10000 || member.Accounts.Any(a => a is null)) throw new BusinessException("The snapshot must contain between 1 and 10,000 accounts.");
        if (member.Accounts.Select(a => a.Code).Distinct().Count() != member.Accounts.Count) throw new BusinessException("A snapshot contains duplicate account codes.");
        foreach (var a in member.Accounts)
        {
            Rules.Required(a.Name, "Account name", 120);
            if (a.Code <= 0 || !Enum.IsDefined(a.Nature) || a.PeriodBalance is < -1_000_000_000_000m or > 1_000_000_000_000m || a.ClosingBalance is < -1_000_000_000_000m or > 1_000_000_000_000m || decimal.Round(a.PeriodBalance, 2) != a.PeriodBalance || decimal.Round(a.ClosingBalance, 2) != a.ClosingBalance)
                throw new BusinessException("Snapshot accounts must have valid codes, account types and amounts with at most two decimal places.");
        }
        if (member.Accounts.Sum(a => a.PeriodBalance) != 0 || member.Accounts.Sum(a => a.ClosingBalance) != 0) throw new BusinessException($"The trial balance for {member.Company} does not balance. Export a fresh snapshot after checking its accounts.");
    }

    public async Task ValidateCompanySnapshot(CompanyReportSnapshot input) { await Authorize(); ValidateMember(input); }

    public async Task<ConsolidatedStatement> Consolidate(ConsolidationRequest input)
    {
        await Authorize();
        if (input is null || input.Statement is not ("Profit and loss" or "Balance sheet") || input.Members is null || input.Members.Count is < 2 or > 20 || input.Mappings is null)
            throw new BusinessException("Choose profit and loss or balance sheet and between two and twenty company snapshots.");
        foreach (var member in input.Members) ValidateMember(member);
        var first = input.Members[0];
        if (input.Members.Select(m => m.SourceId).Distinct().Count() != input.Members.Count) throw new BusinessException("Each company source may be included only once, including renamed copies.");
        if (input.Members.Any(m => m.From != first.From || m.To != first.To)) throw new BusinessException("Export every member for the same start and end dates.");
        var accountKeys = input.Members.SelectMany(m => m.Accounts.Select(a => (m.SourceId, a.Code))).ToHashSet();
        if (input.Mappings.Count > accountKeys.Count || input.Mappings.Any(m => m is null) || input.Mappings.Select(m => (m.SourceId, m.SourceCode)).Distinct().Count() != input.Mappings.Count || input.Mappings.Any(m => !accountKeys.Contains((m.SourceId, m.SourceCode))))
            throw new BusinessException("Map each existing member account at most once.");
        foreach (var map in input.Mappings) { Rules.Required(map.GroupName, "Group account name", 120); if (map.GroupCode <= 0) throw new BusinessException("Group account codes must be positive."); }
        var mappings = input.Mappings.ToDictionary(m => (m.SourceId, m.SourceCode));
        var mapped = input.Members.SelectMany((m, index) => m.Accounts.Select(a =>
        {
            mappings.TryGetValue((m.SourceId, a.Code), out var map);
            return new { Index = index, Account = a, Code = map?.GroupCode ?? a.Code, Name = (map?.GroupName ?? a.Name).Trim() };
        })).ToArray();
        if (mapped.GroupBy(a => a.Code).Any(g => g.Select(a => a.Account.Nature).Distinct().Count() != 1 || g.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1))
            throw new BusinessException("Group codes must share the same account type and name. Map conflicting member accounts to distinct codes or a common name.");
        var adjustments = input.Adjustments?.ToArray() ?? [];
        if (adjustments.Length > 100 || adjustments.Any(a => a is null)) throw new BusinessException("Use at most 100 group adjustments.");
        var natures = mapped.GroupBy(a => a.Code).ToDictionary(g => g.Key,g => g.First().Account.Nature);
        foreach (var adjustment in adjustments)
        {
            Rules.Required(adjustment.Reference,"Adjustment reference",80); Rules.Required(adjustment.Reason,"Adjustment reason",200);
            if (adjustment.Date < first.From || adjustment.Date > first.To || adjustment.Lines is null || adjustment.Lines.Count is < 2 or > 200) throw new BusinessException("Each group adjustment needs 2–200 lines and a date within the reporting period.");
            foreach (var line in adjustment.Lines)
                if (line is null || !natures.ContainsKey(line.AccountCode) || line.Debit < 0 || line.Credit < 0 || line.Debit > 1_000_000_000_000m || line.Credit > 1_000_000_000_000m || (line.Debit == 0) == (line.Credit == 0) || decimal.Round(line.Debit,2) != line.Debit || decimal.Round(line.Credit,2) != line.Credit || !string.IsNullOrEmpty(line.CostCentre)) throw new BusinessException("Use mapped group accounts and one positive debit or credit per adjustment line, with at most two decimal places.");
            if (adjustment.Lines.Sum(l => l.Debit) != adjustment.Lines.Sum(l => l.Credit)) throw new BusinessException("Each group adjustment must balance.");
        }
        if (adjustments.Select(a => a.Reference.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != adjustments.Length) throw new BusinessException("Use a unique reference for each group adjustment.");
        var changes = adjustments.SelectMany(a => a.Lines).GroupBy(l => l.AccountCode).ToDictionary(g => g.Key,g => g.Sum(l => l.Debit-l.Credit));
        var columns = input.Members.Count + (adjustments.Length > 0 ? 1 : 0);
        var rows = new List<ConsolidatedRow>();
        string Section(LedgerGroup nature) => nature switch { LedgerGroup.Asset => "Assets", LedgerGroup.Liability => "Liabilities", LedgerGroup.Equity => "Equity", LedgerGroup.Income => "Income", _ => "Expenses" };
        foreach (var nature in input.Statement == "Profit and loss" ? new[] { LedgerGroup.Income, LedgerGroup.Expense } : new[] { LedgerGroup.Asset, LedgerGroup.Liability, LedgerGroup.Equity })
        {
            var section = Section(nature);
            foreach (var group in mapped.Where(a => a.Account.Nature == nature).GroupBy(a => a.Code).OrderBy(g => g.Key))
            {
                var values = Enumerable.Range(0, input.Members.Count).Select(i => group.Where(a => a.Index == i).Sum(a => (input.Statement == "Profit and loss" ? a.Account.PeriodBalance : a.Account.ClosingBalance) * (nature is LedgerGroup.Asset or LedgerGroup.Expense ? 1 : -1))).ToArray();
                if (adjustments.Length > 0) values = [..values,changes.GetValueOrDefault(group.Key) * (nature is LedgerGroup.Asset or LedgerGroup.Expense ? 1 : -1)];
                if (values.Any(v => v != 0)) rows.Add(new(section, group.First().Name, group.Key, values));
            }
            if (nature == LedgerGroup.Equity)
            {
                var profits = input.Members.Select(m => -m.Accounts.Where(a => a.Nature is LedgerGroup.Income or LedgerGroup.Expense).Sum(a => a.ClosingBalance)).ToArray();
                if (adjustments.Length > 0) profits = [..profits,-changes.Where(c => natures[c.Key] is LedgerGroup.Income or LedgerGroup.Expense).Sum(c => c.Value)];
                rows.Add(new(section, "Accumulated profit / loss", null, profits));
            }
            rows.Add(new(section, "Total " + section.ToLowerInvariant(), null, Enumerable.Range(0, columns).Select(i => rows.Where(r => r.Section == section && !r.Total).Sum(r => r.Contributions[i])).ToArray(), true));
        }
        decimal Total(string section, int i) => rows.Single(r => r.Section == section && r.Total).Contributions[i];
        if (input.Statement == "Profit and loss") rows.Add(new("Result", "Net profit / loss", null, Enumerable.Range(0, columns).Select(i => Total("Income", i) - Total("Expenses", i)).ToArray(), true));
        var difference = input.Statement == "Balance sheet" ? rows.Single(r => r.Section == "Assets" && r.Total).Amount - rows.Single(r => r.Section == "Liabilities" && r.Total).Amount - rows.Single(r => r.Section == "Equity" && r.Total).Amount : 0;
        return new(input.Statement, first.From, first.To, input.Members.ToArray(), rows, difference, adjustments);
    }

    public async Task<byte[]> ExportConsolidation(ConsolidationRequest input)
    {
        var report = await Consolidate(input);
        return WorkbookWriter.Write([("Group report", [
            [report.Statement, $"{report.From:yyyy-MM-dd} to {report.To:yyyy-MM-dd}", report.HasAdjustments ? "NLe; includes reviewed group adjustments" : "NLe; aggregate before intercompany eliminations"],
            ["Account", .. report.Members.Select(m => m.Company + " / " + m.Branch), .. (report.HasAdjustments ? new[] { "Eliminations / adjustments" } : []), "Group total"],
            .. report.Rows.Select(r => (IReadOnlyList<string>)[r.Section + ": " + r.Label, .. r.Contributions.Select(M), M(r.Amount)])]),
            ("Sources", [["Company", "Branch", "Source ID", "Exported UTC"], .. report.Members.Select(m => (IReadOnlyList<string>)[m.Company, m.Branch, m.SourceId.ToString(), m.Exported.ToString("O")])]),
            ("Adjustments", [["Reference","Date","Reason","Group account","Debit","Credit"], .. (report.Adjustments ?? []).SelectMany(a => a.Lines.Select(l => (IReadOnlyList<string>)[a.Reference,a.Date.ToString("yyyy-MM-dd"),a.Reason,l.AccountCode.ToString(),M(l.Debit),M(l.Credit)]))])]);
    }
}
