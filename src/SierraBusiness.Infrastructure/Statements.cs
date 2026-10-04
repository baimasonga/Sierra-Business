using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure.Imports;
namespace SierraBusiness.Infrastructure;

/// <summary>A closed financial year: its profit moves to retained earnings in the balance sheet and the period is locked. Append-only.</summary>
public sealed class YearEndClosure { public Guid Id { get; set; } public string Branch { get; set; } = ""; public DateOnly YearEnd { get; set; } public decimal Profit { get; set; } public string Reason { get; set; } = ""; public string Actor { get; set; } = ""; public DateTimeOffset Time { get; set; } }

public sealed class StatementsService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IStatementsService
{
    private async Task<Actor> Authorize(string permission = "accounting.read")
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read"); actor.Demand(permission);
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }
    private sealed record Line(Guid DocumentId, DateOnly Date, int Account, decimal Debit, decimal Credit);
    private static async Task<(List<LedgerAccount> Accounts, List<Line> Lines)> Load(BusinessDb db, string branch, DateOnly to)
    {
        var accounts = await db.LedgerAccounts.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var dates = await db.Documents.AsNoTracking().Where(x => x.Branch == branch && x.BusinessDate <= to).Select(x => new { x.Id, x.BusinessDate }).ToDictionaryAsync(x => x.Id, x => x.BusinessDate);
        var lines = (await db.Journal.AsNoTracking().Where(x => x.Branch == branch).Select(x => new { x.DocumentId, x.Account, x.Debit, x.Credit }).ToListAsync())
            .Where(x => dates.ContainsKey(x.DocumentId)).Select(x => new Line(x.DocumentId, dates[x.DocumentId], (int)x.Account, x.Debit, x.Credit)).ToList();
        return (accounts, lines);
    }
    private static decimal Profit(IEnumerable<Line> lines, IReadOnlyDictionary<int, LedgerAccount> accounts) =>
        lines.Where(l => accounts[l.Account].Group is LedgerGroup.Income or LedgerGroup.Expense).Sum(l => l.Credit - l.Debit);

    public async Task<ProfitAndLoss> ProfitAndLoss(DateOnly from, DateOnly to)
    {
        var actor = await Authorize(); if (to < from) throw new BusinessException("The end date is before the start date.");
        await using var db = await factory.CreateDbContextAsync();
        var (accounts, lines) = await Load(db, actor.Branch, to);
        var period = lines.Where(l => l.Date >= from).ToList();
        var rows = new List<StatementRow>();
        decimal income = 0, expenses = 0;
        foreach (var a in accounts.Where(a => a.Group == LedgerGroup.Income)) { var v = period.Where(l => l.Account == a.Id).Sum(l => l.Credit - l.Debit); if (v != 0) { rows.Add(new("Income", a.Name, a.Id, v)); income += v; } }
        rows.Add(new("Income", "Total income", null, income, true));
        foreach (var a in accounts.Where(a => a.Group == LedgerGroup.Expense)) { var v = period.Where(l => l.Account == a.Id).Sum(l => l.Debit - l.Credit); if (v != 0) { rows.Add(new("Expenses", a.Name, a.Id, v)); expenses += v; } }
        rows.Add(new("Expenses", "Total expenses", null, expenses, true));
        rows.Add(new("Result", income - expenses >= 0 ? "Net profit" : "Net loss", null, income - expenses, true));
        return new(from, to, rows, income, expenses, income - expenses);
    }

    public async Task<BalanceSheet> BalanceSheet(DateOnly asAt)
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var (accounts, lines) = await Load(db, actor.Branch, asAt);
        var byId = accounts.ToDictionary(x => x.Id);
        var lastYearEnd = (await db.YearEndClosures.AsNoTracking().Where(x => x.Branch == actor.Branch && x.YearEnd <= asAt).Select(x => x.YearEnd).ToListAsync()).DefaultIfEmpty().Max();
        DateOnly? closed = lastYearEnd == default ? null : lastYearEnd;
        var rows = new List<StatementRow>(); decimal assets = 0, liabilities = 0, equity = 0;
        foreach (var a in accounts.Where(a => a.Group == LedgerGroup.Asset)) { var v = lines.Where(l => l.Account == a.Id).Sum(l => l.Debit - l.Credit); if (v != 0) { rows.Add(new("Assets", a.Name, a.Id, v)); assets += v; } }
        rows.Add(new("Assets", "Total assets", null, assets, true));
        foreach (var a in accounts.Where(a => a.Group == LedgerGroup.Liability)) { var v = lines.Where(l => l.Account == a.Id).Sum(l => l.Credit - l.Debit); if (v != 0) { rows.Add(new("Liabilities", a.Name, a.Id, v)); liabilities += v; } }
        rows.Add(new("Liabilities", "Total liabilities", null, liabilities, true));
        foreach (var a in accounts.Where(a => a.Group == LedgerGroup.Equity)) { var v = lines.Where(l => l.Account == a.Id).Sum(l => l.Credit - l.Debit); if (v != 0) { rows.Add(new("Equity", a.Name, a.Id, v)); equity += v; } }
        var retained = closed is DateOnly y ? Profit(lines.Where(l => l.Date <= y), byId) : 0;
        var current = Profit(lines.Where(l => closed is not DateOnly c || l.Date > c), byId);
        rows.Add(new("Equity", "Profit of closed years", null, retained)); rows.Add(new("Equity", closed is null ? "Profit to date" : $"Profit since {closed:dd MMM yyyy}", null, current));
        rows.Add(new("Equity", "Total equity", null, equity + retained + current, true));
        return new(asAt, rows, assets, liabilities, equity, retained, current, closed);
    }

    private static (string Section, string Label) Classify(LedgerAccount a) => a.Id switch
    {
        1100 or 1020 or 1030 or 2400 or 4000 or 4100 or 4300 => ("Operating", "Receipts from customers"),
        2000 or 2050 or 1200 or 1250 or 1260 or 1300 or 5000 => ("Operating", "Payments to suppliers"),
        2200 or 2300 or 2310 or 2320 or 5200 or 5210 or 1310 => ("Operating", "Payments to and for employees"),
        2100 => ("Operating", "Tax (GST) paid"),
        _ => a.Group switch
        {
            LedgerGroup.Income or LedgerGroup.Expense => ("Operating", a.Group == LedgerGroup.Income ? "Other income received" : "Operating expenses paid"),
            LedgerGroup.Equity => ("Financing", "Owner capital and drawings"),
            LedgerGroup.Liability => ("Financing", $"{a.Name}"),
            _ => ("Investing", $"{a.Name}")
        }
    };

    public async Task<CashFlowStatement> CashFlow(DateOnly from, DateOnly to)
    {
        var actor = await Authorize(); if (to < from) throw new BusinessException("The end date is before the start date.");
        await using var db = await factory.CreateDbContextAsync();
        var (accounts, lines) = await Load(db, actor.Branch, to);
        var byId = accounts.ToDictionary(x => x.Id);
        bool Cash(int code) => byId[code].CashOrBank;
        var opening = lines.Where(l => l.Date < from && Cash(l.Account)).Sum(l => l.Debit - l.Credit);
        var closing = lines.Where(l => Cash(l.Account)).Sum(l => l.Debit - l.Credit);
        var totals = new Dictionary<(string Section, string Label), decimal>();
        foreach (var doc in lines.Where(l => l.Date >= from).GroupBy(l => l.DocumentId))
        {
            var net = doc.Where(l => Cash(l.Account)).Sum(l => l.Debit - l.Credit);
            if (net == 0) continue; // no cash moved, or transfers between cash and bank
            var others = doc.Where(l => !Cash(l.Account)).GroupBy(l => Classify(byId[l.Account])).Select(g => (Key: g.Key, Weight: g.Sum(l => l.Credit - l.Debit))).Where(x => x.Weight != 0).ToList();
            if (others.Count == 0) continue;
            var weight = others.Sum(x => x.Weight); decimal allocated = 0;
            for (var i = 0; i < others.Count; i++)
            {
                var share = i == others.Count - 1 ? net - allocated : AccountingEngine.Money(net * others[i].Weight / weight);
                allocated += share; totals[others[i].Key] = totals.GetValueOrDefault(others[i].Key) + share;
            }
        }
        var rows = new List<StatementRow>(); var sums = new Dictionary<string, decimal>();
        foreach (var section in new[] { "Operating", "Investing", "Financing" })
        {
            var items = totals.Where(x => x.Key.Section == section && x.Value != 0).OrderByDescending(x => x.Value).ToList();
            foreach (var i in items) rows.Add(new(section, i.Key.Label, null, i.Value));
            sums[section] = items.Sum(x => x.Value);
            rows.Add(new(section, $"Net cash from {section.ToLowerInvariant()} activities", null, sums[section], true));
        }
        return new(from, to, opening, rows, sums["Operating"], sums["Investing"], sums["Financing"], closing);
    }

    public async Task<FundsFlowStatement> FundsFlow(DateOnly from, DateOnly to)
    {
        var actor = await Authorize(); if (to < from) throw new BusinessException("The end date is before the start date.");
        await using var db = await factory.CreateDbContextAsync();
        var (accounts, lines) = await Load(db, actor.Branch, to);
        var byId = accounts.ToDictionary(x => x.Id);
        var sources = new List<StatementRow>(); var uses = new List<StatementRow>();
        var profit = Profit(lines.Where(l => l.Date >= from), byId);
        if (profit > 0) sources.Add(new("Sources", "Profit for the period", null, profit)); else if (profit < 0) uses.Add(new("Uses", "Loss for the period", null, -profit));
        foreach (var a in accounts.Where(a => a.Group is LedgerGroup.Asset or LedgerGroup.Liability or LedgerGroup.Equity))
        {
            var change = lines.Where(l => l.Account == a.Id && l.Date >= from).Sum(l => l.Debit - l.Credit); // + = more asset / less liability
            if (change == 0) continue;
            if (a.Group == LedgerGroup.Asset)
            { if (change > 0) uses.Add(new("Uses", $"Increase in {a.Name}", a.Id, change)); else sources.Add(new("Sources", $"Decrease in {a.Name}", a.Id, -change)); }
            else
            { if (change < 0) sources.Add(new("Sources", $"Increase in {a.Name}", a.Id, -change)); else uses.Add(new("Uses", $"Decrease in {a.Name}", a.Id, change)); }
        }
        return new(from, to, sources.OrderByDescending(x => x.Amount).ToArray(), uses.OrderByDescending(x => x.Amount).ToArray(), sources.Sum(x => x.Amount), uses.Sum(x => x.Amount));
    }

    public async Task<IReadOnlyList<YearEndView>> YearEnds()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var names = await db.Users.AsNoTracking().Where(x => x.UserName != null).ToDictionaryAsync(x => x.Id, x => x.UserName!);
        return (await db.YearEndClosures.AsNoTracking().Where(x => x.Branch == actor.Branch).ToListAsync()).OrderByDescending(x => x.YearEnd)
            .Select(x => new YearEndView(x.YearEnd, x.Profit, names.GetValueOrDefault(x.Actor, x.Actor), x.Time, x.Reason)).ToArray();
    }

    public async Task<Guid> CloseYear(CloseYearCommand input)
    {
        var actor = await Authorize("accounting.write");
        return await Commands.Run(factory, actor, input.OperationId, "year.close", input, async db =>
        {
            var reason = Rules.Required(input.Reason, "Reason (for example: accounts approved by the auditor)", 200);
            if (input.YearEnd >= DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("A year can be closed only after its last day.");
            var last = (await db.YearEndClosures.Where(x => x.Branch == actor.Branch).Select(x => x.YearEnd).ToListAsync()).DefaultIfEmpty().Max();
            if (last != default && input.YearEnd <= last) throw new BusinessException($"The year to {last:dd MMM yyyy} is already closed. Close a later year end.");
            if (last != default && input.YearEnd > last.AddYears(1).AddDays(7)) throw new BusinessException($"Close the year to {last.AddYears(1):dd MMM yyyy} first.");
            var (accounts, lines) = await Load(db, actor.Branch, input.YearEnd);
            var byId = accounts.ToDictionary(x => x.Id);
            var profit = Profit(lines.Where(l => last == default || l.Date > last), byId);
            var closedThrough = await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough);
            if (input.YearEnd > closedThrough) db.PeriodClosures.Add(new() { Id = Guid.NewGuid(), Branch = actor.Branch, Through = input.YearEnd, Reason = $"Year-end close: {reason}" });
            var row = new YearEndClosure { Id = Guid.NewGuid(), Branch = actor.Branch, YearEnd = input.YearEnd, Profit = profit, Reason = reason, Actor = actor.Id, Time = DateTimeOffset.UtcNow };
            db.YearEndClosures.Add(row); return row.Id;
        });
    }

    // ---------------- exports ----------------

    private static string M(decimal v) => v.ToString("0.00");
    private static IReadOnlyList<IReadOnlyList<string>> Rows(string heading, IEnumerable<StatementRow> rows) =>
        [[heading, "Account", "Amount"], .. rows.Select(r => (IReadOnlyList<string>)[r.Total ? r.Label.ToUpperInvariant() : r.Label, r.AccountCode?.ToString() ?? "", M(r.Amount)])];

    public async Task<byte[]> Export(ReportExportRequest q)
    {
        var reports = new ReportService(factory, session, options);
        switch (q.Report)
        {
            case "Profit and loss": { var p = await ProfitAndLoss(q.From, q.To); return WorkbookWriter.Write([("Profit and loss", Rows($"Profit and loss {q.From:dd MMM yyyy} – {q.To:dd MMM yyyy}", p.Rows))]); }
            case "Balance sheet": { var b = await BalanceSheet(q.To); return WorkbookWriter.Write([("Balance sheet", Rows($"Balance sheet at {q.To:dd MMM yyyy}", b.Rows))]); }
            case "Cash flow":
            {
                var c = await CashFlow(q.From, q.To);
                return WorkbookWriter.Write([("Cash flow", [.. Rows($"Cash flow {q.From:dd MMM yyyy} – {q.To:dd MMM yyyy}", [new("", "Opening cash and bank", null, c.OpeningCash), .. c.Rows, new("", "Net change in cash", null, c.NetChange, true), new("", "Closing cash and bank", null, c.ClosingCash, true)])])]);
            }
            case "Funds flow": { var f = await FundsFlow(q.From, q.To); return WorkbookWriter.Write([("Sources", Rows("Sources of funds", [.. f.Sources, new("", "Total sources", null, f.TotalSources, true)])), ("Uses", Rows("Uses of funds", [.. f.Uses, new("", "Total uses", null, f.TotalUses, true)]))]); }
            case "Sales analysis":
            {
                var s = await reports.Sales(q.From, q.To, q.By);
                return WorkbookWriter.Write([("Sales", [[q.By.ToString(), "Net sales", "Cost", "Gross profit", "Quantity", "Documents"], .. s.Rows.Select(r => (IReadOnlyList<string>)[r.Label, M(r.NetSales), r.Cost is decimal c ? M(c) : "", r.GrossProfit is decimal g ? M(g) : "", r.Quantity.ToString("0.####"), $"{r.Documents}"]), ["Total", M(s.Total), s.Cost is decimal tc ? M(tc) : "", s.GrossProfit is decimal tg ? M(tg) : "", "", ""]])]);
            }
            case "Day book & ledgers":
            {
                var r = await reports.Register(new(q.From, q.To, q.AccountCode, q.Party, q.Kind.Length == 0 ? null : Enum.Parse<DocumentKind>(q.Kind), q.Search, 1, 100_000));
                return WorkbookWriter.Write([("Register", [["Date", "Document", "Type", "Party", "Total", "Settlement", "Debit", "Credit", "Reversed"], .. r.Rows.Items.Select(x => (IReadOnlyList<string>)[x.Date.ToString("yyyy-MM-dd"), x.Number, x.Kind.ToString(), x.Party, M(x.Total), x.Settlement, M(x.Debit), M(x.Credit), x.Reversed ? "Yes" : ""])])]);
            }
            case "Stock valuation":
            {
                var s = await reports.Stock(q.To);
                return WorkbookWriter.Write([("Stock", [["SKU", "Product", "Category", "Unit", "Quantity", "Average cost", "Value"], .. s.Rows.Select(x => (IReadOnlyList<string>)[x.Sku, x.Name, x.Category, x.Unit, x.Quantity.ToString("0.####"), M(x.AverageCost), M(x.Value)]), ["", "Total", "", "", "", "", M(s.Total)]])]);
            }
            case "Stock movements":
            {
                var m = await reports.Movements(new(q.ProductId ?? Guid.Empty, q.From, q.To, "", 1, 100_000));
                return WorkbookWriter.Write([("Movements", [["Date", "Document", "Type", "Party", "Warehouse", "Serial", "Batch", "In", "Out", "Value", "Balance"], .. m.Rows.Items.Select(x => (IReadOnlyList<string>)[x.Date.ToString("yyyy-MM-dd"), x.Number, x.Kind.ToString(), x.Party, x.Location, x.Serial ?? "", x.Batch ?? "", x.QuantityIn.ToString("0.####"), x.QuantityOut.ToString("0.####"), M(x.Value), x.Balance.ToString("0.####")])])]);
            }
            case "Receivables ageing" or "Payables ageing":
            {
                var a = await reports.Ageing(q.To, q.Report == "Receivables ageing");
                return WorkbookWriter.Write([("Ageing", [["Party", "Not yet due", "1–30", "31–60", "61–90", "Over 90", "Total"], .. a.Rows.Select(x => (IReadOnlyList<string>)[x.Party, M(x.Current), M(x.Days30), M(x.Days60), M(x.Days90), M(x.Over90), M(x.Total)]), ["Total", M(a.Current), M(a.Days30), M(a.Days60), M(a.Days90), M(a.Over90), M(a.Total)]])]);
            }
            default: throw new BusinessException("Choose a report to export.");
        }
    }
}
