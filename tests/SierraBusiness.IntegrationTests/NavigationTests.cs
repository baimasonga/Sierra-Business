using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class NavigationTests
{
    [Fact] public void EveryScreenHasOneAreaAndRestrictedAreasDisappear()
    {
        var all = WorkspaceNavigation.Areas.SelectMany(x => x.Pages).ToArray();
        Assert.Equal(all.Length, all.Distinct().Count());
        Assert.Equal("Inventory", WorkspaceNavigation.AreaFor("Warehouse controls").Name);
        Assert.Equal("Finance", WorkspaceNavigation.AreaFor("Banking").Name);
        var visible = WorkspaceNavigation.Visible(["Overview", "New sale", "Stock summary", "Documents"]);
        Assert.Equal(new[] { "Overview", "Sales & purchases", "Inventory", "Documents" }, visible.Select(x => x.Name));
        Assert.Equal(new[] { "New sale" }, visible.Single(x => x.Name == "Sales & purchases").Pages);
    }
    [Fact] public void ScreenSearchMatchesAreasAndNeverReturnsRestrictedScreens()
    {
        var allowed = new[] { "Overview", "New sale", "Stock summary", "Catalogue" };
        Assert.Equal(new[] { "Catalogue", "Stock summary" }, WorkspaceNavigation.Search(allowed, "inventory").Select(x => x.Page));
        Assert.Equal("Stock summary", Assert.Single(WorkspaceNavigation.Search(allowed, "INVENTORY stock")).Page);
        Assert.Empty(WorkspaceNavigation.Search(allowed, "Payroll"));
        Assert.Empty(WorkspaceNavigation.Search(allowed, "  "));
        Assert.Equal("New sale", Assert.Single(WorkspaceNavigation.Search(allowed, "sale")).Page);
    }
}
