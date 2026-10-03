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
}
