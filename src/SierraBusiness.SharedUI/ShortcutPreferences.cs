namespace SierraBusiness.SharedUI;
public sealed record ShortcutPreferences(string NewSale = "F2", string ReceiveStock = "F3", string SearchScreens = "F4")
{
    public static readonly string[] Allowed = ["", "F2", "F3", "F4", "F6", "F7", "F8", "F9"];
    public bool Valid => new[] { NewSale, ReceiveStock, SearchScreens }.All(Allowed.Contains) && new[] { NewSale, ReceiveStock, SearchScreens }.Where(k => k.Length > 0).Distinct().Count() == new[] { NewSale, ReceiveStock, SearchScreens }.Count(k => k.Length > 0);
}
