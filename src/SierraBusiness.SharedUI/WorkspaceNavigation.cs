namespace SierraBusiness.SharedUI;

public sealed record NavigationArea(string Name, string Symbol, string[] Pages);
public static class WorkspaceNavigation
{
    public static IReadOnlyList<NavigationArea> Areas { get; } = [
        new("Overview", "◈", ["Overview"]),
        new("Sales & purchases", "⇄", ["New sale", "Receive stock", "Orders & quotations", "Customers & suppliers", "Returns", "Sales controls", "Imports & landed cost"]),
        new("Inventory", "▦", ["Stock summary", "Catalogue", "Product settings", "Barcodes", "Warehouse controls", "Count sessions", "Import from Excel", "Movement register", "Tracking register"]),
        new("Finance", "=", ["Accounting", "Banking", "Tills", "Trial balance", "Periods & tax"]),
        new("People", "♙", ["Payroll"]),
        new("Documents", "▤", ["Documents"]),
        new("Administration", "⚙", ["Users & permissions", "Backups & integrity", "Audit & sync"])
    ];
    public static NavigationArea AreaFor(string page) => Areas.FirstOrDefault(x => x.Pages.Contains(page)) ?? Areas[0];
    public static IReadOnlyList<NavigationArea> Visible(IEnumerable<string> pages)
    {
        var allowed = pages.ToHashSet(StringComparer.Ordinal);
        return Areas.Select(x => x with { Pages = x.Pages.Where(allowed.Contains).ToArray() }).Where(x => x.Pages.Length > 0).ToArray();
    }
}
