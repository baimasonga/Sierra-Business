using System.Reflection;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class InventoryWorkspaceTests
{
    [Fact] public void SelectingAndEditingBatchUsesExactIdentityAndClearsPreviousCost()
    {
        var view = new InventoryWorkspace(); var id = Guid.NewGuid(); var date = new DateOnly(2030, 1, 1);
        Set(view, "products", new List<ProductView> { new(id, "B", "Batch", "Goods", "unit", 10, TrackingMode.BatchExpiry) });
        Set(view, "allStock", new List<StockView> { new(id, "B", "Batch", "unit", null, "LOT", date, 4, 40), new(id, "B", "Batch", "unit", null, "LOT", date.AddDays(1), 8, 80), new(id, "B", "Batch", "unit", null, "LOT", date, 15, 150, "SECOND") });
        Set(view, "countProduct", id); Set(view, "countLotIndex", 0); Call(view, "SelectCountLot");
        Assert.Equal(4m, Get(view, "expectedQuantity")); Assert.Equal(date, Get(view, "countExpiry"));
        Set(view, "surplusCost", 15m); Set(view, "countBatch", " lot "); Set(view, "countExpiry", date.AddDays(1)); Call(view, "EditCountIdentity");
        Assert.Equal(8m, Get(view, "expectedQuantity")); Assert.Equal(0m, Get(view, "surplusCost")); Assert.Equal(-1, Get(view, "countLotIndex"));
        Set(view, "countLocation", "SECOND"); Call(view, "ResetCountIdentity");
        Assert.Equal("", Get(view, "countBatch")); Assert.Null(Get(view, "countExpiry")); Assert.Equal(0m, Get(view, "expectedQuantity"));
    }
    [Fact] public void SerialSelectionNormalizesIdentityAndResetsWhenWarehouseChanges()
    {
        var view = new InventoryWorkspace(); var id = Guid.NewGuid();
        Set(view, "products", new List<ProductView> { new(id, "S", "Serial", "Goods", "unit", 10, TrackingMode.Serial) });
        Set(view, "allStock", new List<StockView> { new(id, "S", "Serial", "unit", "ABC", null, null, 1, 60) });
        Set(view, "countProduct", id); Set(view, "countSerial", " abc "); Call(view, "PrepareCount");
        Assert.Equal(1m, Get(view, "expectedQuantity")); Assert.Equal(1m, Get(view, "countedQuantity"));
        Set(view, "countLocation", "SECOND"); Call(view, "ResetCountIdentity");
        Assert.Equal("", Get(view, "countSerial")); Assert.Equal(0m, Get(view, "expectedQuantity"));
    }
    private static void Set(object view, string name, object value) => view.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, value);
    private static object? Get(object view, string name) => view.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view);
    private static void Call(object view, string name) => view.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
}
