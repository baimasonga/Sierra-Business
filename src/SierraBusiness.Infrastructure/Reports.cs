using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

/// <summary>In-process notification that posted history changed. Raised after a save adds sealed records; listeners debounce and re-read.</summary>
public static class ChangeNotifier
{
    public static event Action? Changed;
    internal static void Raise() { try { Changed?.Invoke(); } catch { /* a listener must never break a save */ } }
}
public sealed class ChangeFeed : IChangeFeed
{
    public event Action Changed { add => ChangeNotifier.Changed += value; remove => ChangeNotifier.Changed -= value; }
}

/// <summary>Read-only reports. Financial figures come from the journal and stock figures from movements, so every total reconciles to its source.</summary>
public sealed partial class ReportService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IReportService
{
    private static readonly Account[] SalesAccounts = [Account.Sales, ServiceRules.Income];
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private async Task<Actor> Authorize(bool financial = false)
    {
        var actor = await session.CurrentActor(); actor.Demand("business.read");
        if (financial) actor.Demand("accounting.read");
        if (actor.Branch != options.Value.Branch || string.IsNullOrWhiteSpace(actor.Id)) throw new UnauthorizedAccessException("Sign in to the configured branch.");
        return actor;
    }
    private static void Period(DateOnly from, DateOnly to)
    {
        if (from > to) throw new BusinessException("The report start must be on or before its end.");
        if (to.DayNumber - from.DayNumber > 3660) throw new BusinessException("Choose a period of at most ten years.");
    }
    private static bool Financial(Actor a) => a.Permissions.Contains("accounting.read");

    private sealed record Line(Guid DocumentId, Account Account, decimal Debit, decimal Credit, DateOnly Date, string Party, DocumentKind Kind, string Branch);
    private static async Task<List<Line>> Lines(BusinessDb db, string? branch, DateOnly start, DateOnly end, params Account[] accounts)
    {
        var query = from j in db.Journal.AsNoTracking() join d in db.Documents.AsNoTracking() on j.DocumentId equals d.Id
                    where d.BusinessDate >= start && d.BusinessDate <= end && (branch == null || j.Branch == branch) && (accounts.Length == 0 || accounts.Contains(j.Account))
                    select new Line(d.Id, j.Account, j.Debit, j.Credit, d.BusinessDate, d.Party, d.Kind, j.Branch);
        return await query.ToListAsync();
    }
    private static decimal Net(IEnumerable<Line> lines) => lines.Where(x => SalesAccounts.Contains(x.Account)).Sum(x => x.Credit - x.Debit);
    private static decimal Cost(IEnumerable<Line> lines) => lines.Where(x => x.Account == Account.CostOfSales).Sum(x => x.Debit - x.Credit);

    // ---------------- dashboard ----------------

    public async Task<DashboardView> Dashboard(DateOnly from, DateOnly to)
    {
        var actor = await Authorize(); Period(from, to);
        var financial = Financial(actor);
        await using var db = await factory.CreateDbContextAsync();
        var lines = await Lines(db, actor.Branch, from, to, Account.Sales, ServiceRules.Income, Account.CostOfSales);
        var net = Net(lines); var cost = Cost(lines);
        var salesDocuments = lines.Where(x => SalesAccounts.Contains(x.Account) && x.Kind is DocumentKind.Sale or DocumentKind.ServiceInvoice).Select(x => x.DocumentId).Distinct().Count();

        var monthly = to.DayNumber - from.DayNumber > 62;
        var trend = new List<TrendPoint>();
        if (monthly)
            for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
            {
                var month = lines.Where(x => x.Date.Year == m.Year && x.Date.Month == m.Month).ToList();
                trend.Add(new(m, m.ToString("MMM yy"), Net(month), financial ? Net(month) - Cost(month) : null));
            }
        else
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                var day = lines.Where(x => x.Date == d).ToList();
                trend.Add(new(d, d.ToString("dd MMM"), Net(day), financial ? Net(day) - Cost(day) : null));
            }

        var customers = lines.Where(x => SalesAccounts.Contains(x.Account)).GroupBy(x => x.Party.ToUpperInvariant())
            .Select(g => new RankedItem(g.First().Party, g.First().Party, g.Sum(x => x.Credit - x.Debit), g.Select(x => x.DocumentId).Distinct().Count()))
            .Where(x => x.Amount > 0).OrderByDescending(x => x.Amount).Take(8).ToArray();
        var products = (await ProductSales(db, actor.Branch, from, to, false)).Rows.OrderByDescending(x => x.NetSales).Take(8)
            .Select(x => new RankedItem(x.Key, x.Label, x.NetSales, x.Quantity)).ToArray();

        var stockValue = (await db.Movements.AsNoTracking().Where(x => x.Branch == actor.Branch && db.Documents.Any(d => d.Id == x.DocumentId && d.BusinessDate <= to)).Select(x => x.Value).ToListAsync()).Sum();
        var lowStock = (await new InventoryManagement(factory, session, options).Reorder()).Count(x => x.SuggestedQuantity > 0);

        var (openJobs, awaiting) = await ServiceCounts(db, actor.Branch);
        var tenders = (await new TenderService(factory, session, options).List()).Where(t => t.Status is not (TenderStatus.Won or TenderStatus.Lost or TenderStatus.Cancelled)).ToList();
        var warranties = (await new EquipmentService(factory, session, options).Warranties()).Count(w => w.Status == "Active" && w.End <= Today.AddDays(30));

        decimal? cash = null, bank = null, cheques = null, receivables = null, overdueReceivables = null, payables = null, overduePayables = null;
        var branches = new List<BranchFigure>(); var checks = new List<ReconciliationCheck>();
        if (financial)
        {
            var snapshot = await new FinanceService(factory, session, options).Read(from, to);
            decimal Closing(int code) => snapshot.Balances.SingleOrDefault(x => x.Code == code)?.Closing ?? 0;
            var cashOrBank = snapshot.Accounts.Where(x => x.CashOrBank).Select(x => x.Code).ToList();
            cash = Closing(1000); bank = cashOrBank.Where(x => x != 1000).Sum(Closing); cheques = Closing(1020) + Closing(1030);
            receivables = Closing(1100); payables = -Closing(2000);
            overdueReceivables = snapshot.Bills.Where(x => x.Receivable && x.OverdueDays > 0).Sum(x => x.Outstanding);
            overduePayables = snapshot.Bills.Where(x => !x.Receivable && x.OverdueDays > 0).Sum(x => x.Outstanding);
            checks.Add(new("Stock value (movements) against the Inventory ledger", stockValue, Closing(1200), "Inventory (1200)"));
            checks.Add(new("Total debits against total credits", snapshot.Balances.Sum(x => x.Debit), snapshot.Balances.Sum(x => x.Credit), "Total credits"));

            var all = await Lines(db, null, DateOnly.MinValue, to);
            foreach (var g in all.GroupBy(x => x.Branch).OrderBy(x => x.Key))
            {
                var period = g.Where(x => x.Date >= from).ToList();
                decimal Balance(params int[] codes) => g.Where(x => codes.Contains((int)x.Account)).Sum(x => x.Debit - x.Credit);
                branches.Add(new(g.Key, Net(period), Net(period) - Cost(period), Balance([.. cashOrBank]), Balance(1100)));
            }
        }
        return new(from, to, financial, net, salesDocuments, financial ? cost : null, financial ? net - cost : null, cash, bank, cheques, receivables, overdueReceivables, payables, overduePayables,
            stockValue, lowStock, openJobs, awaiting, tenders.Count, tenders.Sum(t => t.Bids.Count > 0 ? t.Bids[0].Total : 0), tenders.Count(t => t.DaysLeft is >= 0 and <= 7), warranties,
            trend, monthly, customers, products, branches, checks, DateTimeOffset.UtcNow, (await new ControlService(factory, session, options).Approvals()).Count(x => x.CanDecide));
    }

    private static async Task<(int Open, int Awaiting)> ServiceCounts(BusinessDb db, string branch)
    {
        var jobs = await db.ServiceJobs.AsNoTracking().Where(x => x.Branch == branch).Select(x => x.Id).ToListAsync();
        var events = (await db.ServiceJobEvents.AsNoTracking().Where(x => jobs.Contains(x.JobId)).Select(x => new { x.JobId, x.Sequence, x.Kind, x.Status, x.DocumentId }).ToListAsync()).GroupBy(x => x.JobId).Select(g => g.MaxBy(x => x.Sequence)!).ToList();
        var closeDocs = events.Where(x => x.Kind == "Closed" && x.DocumentId != null).Select(x => x.DocumentId!.Value).ToList();
        var reversed = (await db.Documents.AsNoTracking().Where(x => x.Reverses != null && closeDocs.Contains(x.Reverses!.Value)).Select(x => x.Reverses!.Value).ToListAsync()).ToHashSet();
        var statuses = events.Select(x => x.Kind == "Closed" && x.DocumentId is Guid d && reversed.Contains(d) ? ServiceStatus.Completed : x.Status).ToList();
        return (statuses.Count(x => x is ServiceStatus.Logged or ServiceStatus.Assigned or ServiceStatus.InProgress), statuses.Count(x => x == ServiceStatus.Completed));
    }

    // ---------------- registers ----------------

    public async Task<IReadOnlyList<LedgerDefinition>> Accounts()
    {
        await Authorize(true); await using var db = await factory.CreateDbContextAsync();
        return await db.LedgerAccounts.AsNoTracking().OrderBy(x => x.Id).Select(x => new LedgerDefinition(x.Id, x.Name, x.Group, x.CashOrBank, x.Control)).ToArrayAsync();
    }

    public async Task<RegisterReport> Register(RegisterQuery q)
    {
        var actor = await Authorize(q.AccountCode is not null); Period(q.From, q.To);
        var size = Math.Clamp(q.PageSize, 10, 200); var page = Math.Max(1, q.Page);
        await using var db = await factory.CreateDbContextAsync();
        var account = q.AccountCode is int code ? await db.LedgerAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == code) ?? throw new BusinessException("Ledger account not found.") : null;
        var documents = db.Documents.AsNoTracking().Where(x => x.Branch == actor.Branch && x.BusinessDate >= q.From && x.BusinessDate <= q.To);
        if (q.Kind is DocumentKind kind) documents = documents.Where(x => x.Kind == kind);
        var party = (q.Party ?? "").Trim().ToUpper();
        if (party.Length > 0) documents = documents.Where(x => x.Party.ToUpper() == party);
        var search = (q.Search ?? "").Trim().ToUpper();
        if (search.Length > 0) documents = documents.Where(x => x.Number.ToUpper().Contains(search) || x.Party.ToUpper().Contains(search));
        if (account is not null) documents = documents.Where(d => db.Journal.Any(j => j.DocumentId == d.Id && (int)j.Account == account.Id));
        var total = await documents.CountAsync();
        var rows = await documents.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Number).Skip((page - 1) * size).Take(size).ToListAsync();
        var ids = rows.Select(x => x.Id).ToList();
        var reversed = (await db.Documents.AsNoTracking().Where(x => x.Reverses != null && ids.Contains(x.Reverses!.Value)).Select(x => x.Reverses!.Value).ToListAsync()).ToHashSet();
        decimal opening = 0, debit = 0, credit = 0;
        var amounts = new Dictionary<Guid, (decimal Debit, decimal Credit)>();
        if (account is not null)
        {
            var all = (await Lines(db, actor.Branch, DateOnly.MinValue, q.To, (Account)account.Id));
            opening = all.Where(x => x.Date < q.From).Sum(x => x.Debit - x.Credit);
            var period = all.Where(x => x.Date >= q.From && (q.Kind == null || x.Kind == q.Kind) && (party.Length == 0 || x.Party.ToUpperInvariant() == party)).ToList();
            if (search.Length > 0) { var matching = (await documents.Select(x => x.Id).ToListAsync()).ToHashSet(); period = period.Where(x => matching.Contains(x.DocumentId)).ToList(); }
            debit = period.Sum(x => x.Debit); credit = period.Sum(x => x.Credit);
            amounts = period.Where(x => ids.Contains(x.DocumentId)).GroupBy(x => x.DocumentId).ToDictionary(g => g.Key, g => (g.Sum(x => x.Debit), g.Sum(x => x.Credit)));
        }
        var items = rows.Select(d => new RegisterRow(d.Id, d.Number, d.BusinessDate, d.Kind, d.Party, d.Total, d.Settlement, reversed.Contains(d.Id), amounts.GetValueOrDefault(d.Id).Debit, amounts.GetValueOrDefault(d.Id).Credit)).ToArray();
        return new(new(items, total, page, size), account?.Id, account?.Name ?? "", opening, debit, credit, opening + debit - credit);
    }

    // ---------------- sales analysis ----------------

    public async Task<SalesAnalysis> Sales(DateOnly from, DateOnly to, SalesGrouping by)
    {
        var actor = await Authorize(); Period(from, to);
        if (!Enum.IsDefined(by)) throw new BusinessException("Choose how to group the sales.");
        var financial = Financial(actor);
        await using var db = await factory.CreateDbContextAsync();
        var lines = await Lines(db, actor.Branch, from, to, Account.Sales, ServiceRules.Income, Account.CostOfSales);
        var ledgerNet = Net(lines); var ledgerCost = Cost(lines);
        if (by == SalesGrouping.Product)
        {
            var products = await ProductSales(db, actor.Branch, from, to, financial);
            var sum = products.Rows.Sum(x => x.NetSales);
            return new(from, to, by, products.Rows.OrderByDescending(x => x.NetSales).ToArray(), ledgerNet, financial ? ledgerCost : null, financial ? ledgerNet - ledgerCost : null, ledgerNet - sum,
                new("Invoice lines against their Sales ledger postings", sum, products.LedgerForInvoices, "Sales (4000) on the same invoices"));
        }
        var groups = by switch
        {
            SalesGrouping.Day => lines.GroupBy(x => x.Date.ToString("yyyy-MM-dd")),
            SalesGrouping.Month => lines.GroupBy(x => x.Date.ToString("yyyy-MM")),
            _ => lines.GroupBy(x => x.Party.ToUpperInvariant())
        };
        var rows = groups.Select(g =>
        {
            var label = by switch { SalesGrouping.Day => g.First().Date.ToString("ddd dd MMM yyyy"), SalesGrouping.Month => g.First().Date.ToString("MMMM yyyy"), _ => g.First().Party };
            var key = by == SalesGrouping.Customer ? g.First().Party : g.Key;
            var n = Net(g); var c = Cost(g);
            return new SalesRow(key, label, n, financial ? c : null, financial ? n - c : null, 0, g.Where(x => SalesAccounts.Contains(x.Account)).Select(x => x.DocumentId).Distinct().Count());
        }).Where(x => x.NetSales != 0 || x.Cost is not null and not 0).ToList();
        rows = by == SalesGrouping.Customer ? rows.OrderByDescending(x => x.NetSales).ToList() : rows.OrderBy(x => x.Key).ToList();
        return new(from, to, by, rows, ledgerNet, financial ? ledgerCost : null, financial ? ledgerNet - ledgerCost : null, 0,
            new("Report total against the Sales and Service income ledgers", rows.Sum(x => x.NetSales), ledgerNet, "Sales (4000) + Service income (4300)"));
    }

    private sealed record ProductResult(IReadOnlyList<SalesRow> Rows, decimal LedgerForInvoices);
    /// <summary>Revenue by product from the lines of unreversed sales invoices; cost from the stock that left on the invoice or its delivery note.</summary>
    private static async Task<ProductResult> ProductSales(BusinessDb db, string branch, DateOnly from, DateOnly to, bool financial)
    {
        var invoices = await db.Documents.AsNoTracking().Where(x => x.Branch == branch && x.Kind == DocumentKind.Sale && x.BusinessDate >= from && x.BusinessDate <= to && !db.Documents.Any(r => r.Reverses == x.Id)).ToListAsync();
        var ids = invoices.Select(x => x.Id).ToList();
        var deliveries = await db.DeliveryInvoices.AsNoTracking().Where(x => ids.Contains(x.InvoiceId)).ToDictionaryAsync(x => x.InvoiceId, x => x.DeliveryId);
        var stockDocs = ids.Concat(deliveries.Values).ToList();
        var movements = (await db.Movements.AsNoTracking().Where(x => stockDocs.Contains(x.DocumentId)).ToListAsync()).ToLookup(x => x.DocumentId);
        var ledger = (await db.Journal.AsNoTracking().Where(x => ids.Contains(x.DocumentId) && x.Account == Account.Sales).ToListAsync()).Sum(x => x.Credit - x.Debit);
        var names = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var rows = new Dictionary<Guid, (decimal Net, decimal Quantity, decimal Cost, HashSet<Guid> Docs)>();
        foreach (var invoice in invoices)
        {
            using var json = JsonDocument.Parse(invoice.Detail);
            if (!json.RootElement.TryGetProperty("Request", out var request)) continue;
            var command = request.Deserialize<TradeCommand>(); if (command?.Lines is null) continue;
            var stock = movements[deliveries.TryGetValue(invoice.Id, out var delivery) ? delivery : invoice.Id].ToList();
            foreach (var group in command.Lines.GroupBy(x => x.ProductId))
            {
                var current = rows.GetValueOrDefault(group.Key, (0, 0, 0, new HashSet<Guid>()));
                current.Net += group.Sum(x => AccountingEngine.Money(x.Quantity * x.UnitPrice));
                current.Quantity += group.Sum(x => x.Quantity * x.Conversion);
                current.Cost += -stock.Where(m => m.ProductId == group.Key).Sum(m => m.Value);
                current.Docs.Add(invoice.Id);
                rows[group.Key] = current;
            }
        }
        return new(rows.Select(r => new SalesRow(r.Key.ToString(), names.TryGetValue(r.Key, out var p) ? $"{p.Sku} · {p.Name}" : r.Key.ToString(), r.Value.Net, financial ? r.Value.Cost : null, financial ? r.Value.Net - r.Value.Cost : null, r.Value.Quantity, r.Value.Docs.Count)).ToArray(), ledger);
    }

    // ---------------- stock ----------------

    public async Task<StockValuation> Stock(DateOnly asAt)
    {
        var actor = await Authorize();
        await using var db = await factory.CreateDbContextAsync();
        var movements = await (from m in db.Movements.AsNoTracking() join d in db.Documents.AsNoTracking() on m.DocumentId equals d.Id
                               where m.Branch == actor.Branch && d.BusinessDate <= asAt select new { m.ProductId, m.Location, m.Quantity, m.Value }).ToListAsync();
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var rows = movements.GroupBy(x => x.ProductId).Select(g =>
        {
            var p = products[g.Key]; var quantity = g.Sum(x => x.Quantity); var value = g.Sum(x => x.Value);
            var locations = g.GroupBy(x => x.Location).Select(l => new StockLocationValue(l.Key, l.Sum(x => x.Quantity), l.Sum(x => x.Value))).Where(x => x.Quantity != 0 || x.Value != 0).OrderBy(x => x.Location).ToArray();
            return new StockValueRow(p.Id, p.Sku, p.Name, p.Category, p.Unit, quantity, value, quantity > 0 ? Math.Round(value / quantity, 4) : 0, locations);
        }).Where(x => x.Quantity != 0 || x.Value != 0).OrderByDescending(x => x.Value).ThenBy(x => x.Sku).ToArray();
        var total = rows.Sum(x => x.Value);
        ReconciliationCheck? check = null;
        if (Financial(actor)) check = new("Stock value (movements) against the Inventory ledger", total, (await Lines(db, actor.Branch, DateOnly.MinValue, asAt, Account.Inventory)).Sum(x => x.Debit - x.Credit), "Inventory (1200)");
        return new(asAt, rows, total, check);
    }

    public async Task<MovementReport> Movements(MovementQuery q)
    {
        var actor = await Authorize(); Period(q.From, q.To);
        var size = Math.Clamp(q.PageSize, 10, 200); var page = Math.Max(1, q.Page);
        await using var db = await factory.CreateDbContextAsync();
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == q.ProductId) ?? throw new BusinessException("Product not found.");
        var location = (q.Location ?? "").Trim();
        var all = await (from m in db.Movements.AsNoTracking() join d in db.Documents.AsNoTracking() on m.DocumentId equals d.Id
                         where m.Branch == actor.Branch && m.ProductId == q.ProductId && d.BusinessDate <= q.To && (location == "" || m.Location == location)
                         select new { Movement = m, Document = d }).ToListAsync();
        var docIds = all.Select(x => x.Document.Id).Distinct().ToList();
        var times = (await db.Operations.AsNoTracking().Where(x => docIds.Contains(x.ResultId)).Select(x => new { x.ResultId, x.Time }).ToListAsync()).GroupBy(x => x.ResultId).ToDictionary(g => g.Key, g => g.Min(x => x.Time));
        var ordered = all.OrderBy(x => x.Document.BusinessDate).ThenBy(x => times.GetValueOrDefault(x.Document.Id)).ThenBy(x => x.Document.Number).ToList();
        var before = ordered.Where(x => x.Document.BusinessDate < q.From).ToList();
        decimal balance = before.Sum(x => x.Movement.Quantity), value = before.Sum(x => x.Movement.Value);
        var opening = balance; var openingValue = value;
        var rows = new List<MovementRow>();
        foreach (var x in ordered.Where(x => x.Document.BusinessDate >= q.From))
        {
            balance += x.Movement.Quantity; value += x.Movement.Value;
            rows.Add(new(x.Document.Id, x.Document.Number, x.Document.BusinessDate, x.Document.Kind, x.Document.Party, x.Movement.Location, x.Movement.Serial, x.Movement.Batch,
                Math.Max(0, x.Movement.Quantity), Math.Max(0, -x.Movement.Quantity), x.Movement.Value, balance));
        }
        var shown = rows.AsEnumerable().Reverse().Skip((page - 1) * size).Take(size).ToArray(); // newest first
        return new(product.Id, product.Sku, product.Name, product.Unit, opening, openingValue, rows.Sum(x => x.QuantityIn), rows.Sum(x => x.QuantityOut), balance, value, new(shown, rows.Count, page, size));
    }

    // ---------------- ageing ----------------

    public async Task<AgeingReport> Ageing(DateOnly asAt, bool receivable)
    {
        await Authorize(true);
        var snapshot = await new FinanceService(factory, session, options).Read(asAt, asAt);
        var bills = snapshot.Bills.Where(x => x.Receivable == receivable).ToList();
        static int Bucket(int days) => days <= 0 ? 0 : days <= 30 ? 1 : days <= 60 ? 2 : days <= 90 ? 3 : 4;
        var rows = bills.GroupBy(x => x.Party.ToUpperInvariant()).Select(g =>
        {
            decimal In(int b) => g.Where(x => Bucket(x.OverdueDays) == b).Sum(x => x.Outstanding);
            return new AgeingRow(g.First().Party, In(0), In(1), In(2), In(3), In(4), g.Sum(x => x.Outstanding),
                g.OrderBy(x => x.DueDate).Select(x => new AgeingBill(x.Id, x.Number, x.Date, x.DueDate, x.Outstanding, x.OverdueDays)).ToArray());
        }).OrderByDescending(x => x.Over90 + x.Days90 + x.Days60 + x.Days30).ThenByDescending(x => x.Total).ToArray();
        var ledgerCode = receivable ? 1100 : 2000;
        var ledger = snapshot.Balances.SingleOrDefault(x => x.Code == ledgerCode)?.Closing ?? 0;
        return new(asAt, receivable, rows, rows.Sum(x => x.Current), rows.Sum(x => x.Days30), rows.Sum(x => x.Days60), rows.Sum(x => x.Days90), rows.Sum(x => x.Over90), rows.Sum(x => x.Total),
            new(receivable ? "Outstanding customer bills against Trade receivables" : "Outstanding supplier bills against Trade payables", rows.Sum(x => x.Total), receivable ? ledger : -ledger, receivable ? "Trade receivables (1100)" : "Trade payables (2000)"));
    }
}
