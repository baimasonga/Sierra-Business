namespace SierraBusiness.SharedUI;

public sealed record NavigationArea(string Name, string Symbol, string[] Pages);
public static class WorkspaceNavigation
{
    public static IReadOnlyList<NavigationArea> Areas { get; } = [
        new("Overview", "◈", ["Overview", "Dashboard", "Reminders", "Reports"]),
        new("Sales & purchases", "⇄", ["New sale", "Receive stock", "Orders & quotations", "Tenders", "Customers & suppliers", "Returns", "Sales controls", "Price rules", "Supplier invoices", "Imports & landed cost"]),
        new("Inventory", "▦", ["Stock summary", "Catalogue", "Variants & bundles", "Product settings", "Barcodes", "Warehouse controls", "Count sessions", "Import from Excel", "Movement register", "Tracking register"]),
        new("Finance", "=", ["Accounting", "Foreign exchange", "Deposits & advances", "Financial statements", "Banking", "Banking tools", "Tills", "Trial balance", "Periods & tax"]),
        new("After-sales", "◎", ["Equipment lookup", "Installations", "Service jobs", "Equipment setup"]),
        new("People", "♙", ["Payroll"]),
        new("Documents", "▤", ["Document library", "Documents"]),
        new("Administration", "⚙", ["Users & permissions", "Approvals", "Controls & exceptions", "Company & tax", "System health", "Branch sync", "Backups & integrity", "Audit & sync"])
    ];
    public static IReadOnlyList<(string Page, string Area)> Search(IEnumerable<string> allowedPages, string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return [];
        return Visible(allowedPages).SelectMany(area => area.Pages.Select(page => (Page: page, Area: area.Name)))
            .Where(item => terms.All(term => (item.Page + " " + item.Area).Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Page.Equals(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Page, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static NavigationArea AreaFor(string page) => Areas.FirstOrDefault(x => x.Pages.Contains(page)) ?? Areas[0];
    public static IReadOnlyList<NavigationArea> Visible(IEnumerable<string> pages)
    {
        var allowed = pages.ToHashSet(StringComparer.Ordinal);
        return Areas.Select(x => x with { Pages = x.Pages.Where(allowed.Contains).ToArray() }).Where(x => x.Pages.Length > 0).ToArray();
    }
}
