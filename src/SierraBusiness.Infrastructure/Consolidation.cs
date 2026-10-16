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
        if (member is null || member.Version != 1 || member.SourceId == Guid.Empty || member.Currency is null || (member.Currency != "NLe" && !System.Text.RegularExpressions.Regex.IsMatch(member.Currency, "^[A-Z]{3}$"))) throw new BusinessException("Use a version 1 Rokela company snapshot in NLe or a three-letter reporting currency code.");
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

    private sealed record PreparedGroup(CompanyReportSnapshot[] Members, decimal[] Reserves, decimal[] Ownership, ConsolidationMemberTerms[]? Terms);

    /// <summary>Validates the member terms and translates foreign-currency members into NLe.
    /// Closing-rate method: assets/liabilities at the closing rate, income/expenses at the average rate and equity at the historical rate.
    /// The difference is the member's foreign currency translation reserve.</summary>
    private static PreparedGroup Prepare(ConsolidationRequest input)
    {
        var terms = input.Terms?.ToArray();
        if (terms is not null)
        {
            if (terms.Length > input.Members.Count || terms.Any(t => t is null) || terms.Select(t => t.SourceId).Distinct().Count() != terms.Length || terms.Any(t => input.Members.All(m => m.SourceId != t.SourceId)))
                throw new BusinessException("Give group terms at most once for each loaded member company.");
            foreach (var t in terms)
            {
                if (t.OwnershipPercent is <= 0 or > 100 || decimal.Round(t.OwnershipPercent, 4) != t.OwnershipPercent) throw new BusinessException("Ownership must be more than 0% and at most 100%, with at most four decimal places.");
                if (new[] { t.ClosingRate, t.AverageRate, t.EquityRate }.Any(r => r is <= 0 or > 1_000_000m || decimal.Round(r, 8) != r)) throw new BusinessException("Exchange rates must be positive NLe amounts per currency unit, at most 1,000,000 with at most eight decimal places.");
            }
        }
        var members = new CompanyReportSnapshot[input.Members.Count]; var reserves = new decimal[members.Length]; var ownership = new decimal[members.Length];
        for (var i = 0; i < members.Length; i++)
        {
            var member = input.Members[i]; var t = terms?.SingleOrDefault(x => x.SourceId == member.SourceId);
            ownership[i] = t?.OwnershipPercent ?? 100;
            if (member.Currency == "NLe")
            {
                if (t is not null && (t.ClosingRate != 1 || t.AverageRate != 1 || t.EquityRate != 1)) throw new BusinessException($"{member.Company} reports in NLe; leave its exchange rates at 1.");
                members[i] = member; continue;
            }
            if (t is null) throw new BusinessException($"{member.Company} reports in {member.Currency}. Enter its closing, average and historical equity rates before building the group report.");
            decimal Rate(LedgerGroup nature) => nature switch { LedgerGroup.Asset or LedgerGroup.Liability => t.ClosingRate, LedgerGroup.Equity => t.EquityRate, _ => t.AverageRate };
            decimal X(decimal amount, LedgerGroup nature) => decimal.Round(amount * Rate(nature), 2, MidpointRounding.AwayFromZero);
            var accounts = member.Accounts.Select(a => a with { PeriodBalance = X(a.PeriodBalance, a.Nature), ClosingBalance = X(a.ClosingBalance, a.Nature) }).ToArray();
            // Translated debits and credits no longer offset; the balancing amount is held in equity, not in profit.
            reserves[i] = -accounts.Sum(a => a.ClosingBalance);
            members[i] = member with { Currency = "NLe", Accounts = accounts };
        }
        return new(members, reserves, ownership, terms);
    }

    public async Task<ConsolidatedStatement> Consolidate(ConsolidationRequest input)
    {
        await Authorize();
        if (input is null || input.Statement is not ("Profit and loss" or "Balance sheet") || input.Members is null || input.Members.Count is < 2 or > 20 || input.Mappings is null)
            throw new BusinessException("Choose profit and loss or balance sheet and between two and twenty company snapshots.");
        foreach (var member in input.Members) ValidateMember(member);
        var first = input.Members[0];
        if (input.Members.Select(m => m.SourceId).Distinct().Count() != input.Members.Count) throw new BusinessException("Each company source may be included only once, including renamed copies.");
        if (input.Members.Any(m => m.From != first.From || m.To != first.To)) throw new BusinessException("Export every member for the same start and end dates.");
        var prepared = Prepare(input); var members = prepared.Members;
        var accountKeys = members.SelectMany(m => m.Accounts.Select(a => (m.SourceId, a.Code))).ToHashSet();
        if (input.Mappings.Count > accountKeys.Count || input.Mappings.Any(m => m is null) || input.Mappings.Select(m => (m.SourceId, m.SourceCode)).Distinct().Count() != input.Mappings.Count || input.Mappings.Any(m => !accountKeys.Contains((m.SourceId, m.SourceCode))))
            throw new BusinessException("Map each existing member account at most once.");
        foreach (var map in input.Mappings) { Rules.Required(map.GroupName, "Group account name", 120); if (map.GroupCode <= 0) throw new BusinessException("Group account codes must be positive."); }
        var mappings = input.Mappings.ToDictionary(m => (m.SourceId, m.SourceCode));
        var mapped = members.SelectMany((m, index) => m.Accounts.Select(a =>
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
        var columns = members.Length + (adjustments.Length > 0 ? 1 : 0);
        var pl = input.Statement == "Profit and loss";
        var rows = new List<ConsolidatedRow>();
        string Section(LedgerGroup nature) => nature switch { LedgerGroup.Asset => "Assets", LedgerGroup.Liability => "Liabilities", LedgerGroup.Equity => "Equity", LedgerGroup.Income => "Income", _ => "Expenses" };
        foreach (var nature in pl ? new[] { LedgerGroup.Income, LedgerGroup.Expense } : new[] { LedgerGroup.Asset, LedgerGroup.Liability, LedgerGroup.Equity })
        {
            var section = Section(nature);
            foreach (var group in mapped.Where(a => a.Account.Nature == nature).GroupBy(a => a.Code).OrderBy(g => g.Key))
            {
                var values = Enumerable.Range(0, members.Length).Select(i => group.Where(a => a.Index == i).Sum(a => (pl ? a.Account.PeriodBalance : a.Account.ClosingBalance) * (nature is LedgerGroup.Asset or LedgerGroup.Expense ? 1 : -1))).ToArray();
                if (adjustments.Length > 0) values = [..values,changes.GetValueOrDefault(group.Key) * (nature is LedgerGroup.Asset or LedgerGroup.Expense ? 1 : -1)];
                if (values.Any(v => v != 0)) rows.Add(new(section, group.First().Name, group.Key, values));
            }
            if (nature == LedgerGroup.Equity)
            {
                var profits = members.Select(m => -m.Accounts.Where(a => a.Nature is LedgerGroup.Income or LedgerGroup.Expense).Sum(a => a.ClosingBalance)).ToArray();
                if (adjustments.Length > 0) profits = [..profits,-changes.Where(c => natures[c.Key] is LedgerGroup.Income or LedgerGroup.Expense).Sum(c => c.Value)];
                rows.Add(new(section, "Accumulated profit / loss", null, profits));
                if (prepared.Reserves.Any(r => r != 0)) rows.Add(new(section, "Foreign currency translation reserve", null, [.. prepared.Reserves.Select(r => -r), .. (adjustments.Length > 0 ? new decimal[] { 0 } : [])]));
            }
            rows.Add(new(section, "Total " + section.ToLowerInvariant(), null, Enumerable.Range(0, columns).Select(i => rows.Where(r => r.Section == section && !r.Total).Sum(r => r.Contributions[i])).ToArray(), true));
        }
        decimal Total(string section, int i) => rows.Single(r => r.Section == section && r.Total).Contributions[i];
        if (pl) rows.Add(new("Result", "Net profit / loss", null, Enumerable.Range(0, columns).Select(i => Total("Income", i) - Total("Expenses", i)).ToArray(), true));
        if (prepared.Ownership.Any(o => o < 100))
        {
            // Non-controlling interest takes its share of each member's result or equity as reported; group adjustments belong to the parent.
            var basis = pl ? rows.Single(r => r.Section == "Result").Contributions : rows.Single(r => r.Section == "Equity" && r.Total).Contributions;
            var nci = Enumerable.Range(0, columns).Select(i => i < members.Length ? decimal.Round(basis[i] * (100 - prepared.Ownership[i]) / 100, 2, MidpointRounding.AwayFromZero) : 0).ToArray();
            var section = pl ? "Profit attributable to" : "Equity attributable to";
            rows.Add(new(section, "Owners of the parent", null, Enumerable.Range(0, columns).Select(i => basis[i] - nci[i]).ToArray()));
            rows.Add(new(section, "Non-controlling interests", null, nci));
            rows.Add(new(section, pl ? "Net profit / loss" : "Total equity", null, basis.ToArray(), true));
        }
        var difference = !pl ? rows.Single(r => r.Section == "Assets" && r.Total).Amount - rows.Single(r => r.Section == "Liabilities" && r.Total).Amount - rows.Single(r => r.Section == "Equity" && r.Total).Amount : 0;
        return new(input.Statement, first.From, first.To, input.Members.ToArray(), rows, difference, adjustments, prepared.Terms);
    }

    public async Task<byte[]> ExportConsolidation(ConsolidationRequest input)
    {
        var report = await Consolidate(input);
        return WorkbookWriter.Write([("Group report", [
            [report.Statement, $"{report.From:yyyy-MM-dd} to {report.To:yyyy-MM-dd}", (report.HasAdjustments ? "NLe; includes reviewed group adjustments" : "NLe; aggregate before intercompany eliminations") + (report.HasTranslation ? "; foreign members translated (closing-rate method)" : "")],
            ["Account", .. report.Members.Select(m => m.Company + " / " + m.Branch), .. (report.HasAdjustments ? new[] { "Eliminations / adjustments" } : []), "Group total"],
            .. report.Rows.Select(r => (IReadOnlyList<string>)[r.Section + ": " + r.Label, .. r.Contributions.Select(M), M(r.Amount)])]),
            ("Sources", [["Company", "Branch", "Source ID", "Exported UTC", "Reporting currency", "Ownership %", "Closing rate", "Average rate", "Equity rate"], .. report.Members.Select(m =>
            {
                var t = report.Terms?.SingleOrDefault(x => x.SourceId == m.SourceId);
                return (IReadOnlyList<string>)[m.Company, m.Branch, m.SourceId.ToString(), m.Exported.ToString("O"), m.Currency, (t?.OwnershipPercent ?? 100).ToString(System.Globalization.CultureInfo.InvariantCulture), (t?.ClosingRate ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture), (t?.AverageRate ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture), (t?.EquityRate ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture)];
            })]),
            ("Adjustments", [["Reference","Date","Reason","Group account","Debit","Credit"], .. (report.Adjustments ?? []).SelectMany(a => a.Lines.Select(l => (IReadOnlyList<string>)[a.Reference,a.Date.ToString("yyyy-MM-dd"),a.Reason,l.AccountCode.ToString(),M(l.Debit),M(l.Credit)]))])]);
    }
}
