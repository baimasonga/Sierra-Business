using System.Reflection;
using Microsoft.JSInterop;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.SharedUI;
using Xunit;

namespace SierraBusiness.IntegrationTests;

public sealed class WorkspaceTests
{
    private static void Set(object component, string name, object value) => component.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(component, value);
    private static T Get<T>(object component, string name) => (T)component.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(component)!;
    private static object? Call(object component, string name, params object[] args) => component.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, args);

    [Fact] public async Task BarcodeScanAddsPackAndTrackedScanRequiresLineDetails()
    {
        var component = new Workspace(); var quantity = Guid.NewGuid(); var serial = Guid.NewGuid();
        Set(component, "data", new BusinessSnapshot([new(quantity, "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity), new(serial, "MACHINE", "Machine", "Equipment", "unit", 500, TrackingMode.Serial)], [], [], [], [], 0));
        var barcodes = new BarcodeView[] { new(Guid.NewGuid(), quantity, "000123", 6) };
        // Scan reloads current barcodes from the POS service so retired/changed codes are never used from a stale cache.
        typeof(Workspace).GetProperty("Pos", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(component, new StubPos(barcodes));
        Set(component, "page", "New sale"); Set(component, "scanCode", "000123");
        await (Task)Call(component, "Scan")!; var lines = Get<List<TradeLine>>(component, "lines"); Assert.Equal(6, lines.Single().Conversion); Assert.Equal(600, lines.Single().UnitPrice);
        Set(component, "scanCode", "000123"); await (Task)Call(component, "Scan")!; Call(component, "ChangeLinePrice", lines[1], 550m); Assert.Equal(600, lines[0].UnitPrice); Assert.Equal(550, lines[1].UnitPrice);
        Set(component, "scanCode", "MACHINE"); await (Task)Call(component, "Scan")!; Assert.Equal(serial, Get<Guid>(component, "selectedProduct")); Assert.Equal(2, lines.Count);
        Set(component, "scanCode", "PAPER"); await (Task)Call(component, "Scan")!; Assert.Contains("Finish the selected", Get<string>(component, "error")); Assert.Equal(serial, Get<Guid>(component, "selectedProduct"));
    }
    [Fact] public async Task RepeatedNavigationKeepsDraftAndCancelPreventsDiscard()
    {
        var component = new Workspace();
        var js = new Confirmation();
        typeof(Workspace).GetProperty("JS", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(component, js);
        Set(component, "page", "New sale");
        var lines = Get<List<TradeLine>>(component, "lines");
        lines.Add(new(Guid.NewGuid(), 1, 10));
        var operation = Get<Guid>(component, "operationId");
        await (Task)Call(component, "Navigate", "New sale")!;
        Assert.Single(lines); Assert.Equal(0, js.Calls);
        await (Task)Call(component, "Navigate", "Receive stock")!;
        Assert.Single(lines); Assert.Equal("New sale", Get<string>(component, "page"));
        Assert.Equal(operation, Get<Guid>(component, "operationId"));
        js.Accept = true;
        await (Task)Call(component, "Navigate", "Receive stock")!;
        Assert.Empty(lines); Assert.Equal("Receive stock", Get<string>(component, "page"));
        Assert.NotEqual(operation, Get<Guid>(component, "operationId"));
    }
    [Fact] public void AddedLineResetsPriceAndReceiptRequiresCost()
    {
        var component = new Workspace(); var id = Guid.NewGuid();
        Set(component, "data", new BusinessSnapshot([new(id, "SKU", "Product", "General", "piece", 10, TrackingMode.Quantity)], [], [], [], [], 0));
        Set(component, "page", "New sale"); Set(component, "selectedProduct", id);
        Set(component, "linePrice", 99m);
        Call(component, "AddLine");
        Assert.Equal(0, Get<decimal>(component, "linePrice"));
        Set(component, "conversion", 12m);
        Call(component, "AddLine");
        Assert.Equal(120, Get<List<TradeLine>>(component, "lines")[1].UnitPrice);
        Set(component, "page", "Receive stock");
        Call(component, "AddLine");
        Assert.Equal(2, Get<List<TradeLine>>(component, "lines").Count);
        Assert.Contains("positive receipt cost", Get<string>(component, "error"));
    }
    [Fact] public async Task EarliestBatchActionAddsReviewableLinesAndAccountsForExistingPackLines()
    {
        var component = new Workspace(); var id = Guid.NewGuid(); var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20);
        var inventory = new SuggestionInventory { Picks = [new("A", date, 2), new("B", date.AddDays(1), 1)] };
        typeof(Workspace).GetProperty("Inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(component, inventory);
        Set(component, "data", new BusinessSnapshot([new(id, "B", "Batch", "Goods", "piece", 100, TrackingMode.BatchExpiry)], [], [], [], [], 0));
        Set(component, "page", "New sale"); Set(component, "selectedProduct", id); Set(component, "quantity", 3m); Set(component, "linePrice", 90m);
        var lines = Get<List<TradeLine>>(component, "lines"); lines.Add(new(id, 2, 180, 2, Batch: "A", Expiry: date));
        await (Task)Call(component, "AddEarliestBatches")!;
        Assert.Equal(4, inventory.Request!.DraftAllocations!.Single().Quantity); Assert.Equal(3, inventory.Request.Quantity);
        Assert.Equal(3, lines.Count); Assert.Equal("A", lines[1].Batch); Assert.Equal(date.AddDays(1), lines[2].Expiry);
        Assert.All(lines.Skip(1), x => { Assert.Equal(90, x.UnitPrice); Assert.Equal(1, x.Conversion); });
        Assert.Equal(0, Get<decimal>(component, "linePrice")); Assert.Contains("Review", Get<string>(component, "success"));
    }
    [Fact] public async Task EarliestBatchFailureKeepsDraftAndManualTrackingIntact()
    {
        var component = new Workspace(); var id = Guid.NewGuid(); var inventory = new SuggestionInventory();
        typeof(Workspace).GetProperty("Inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(component, inventory);
        Set(component, "data", new BusinessSnapshot([new(id, "B", "Batch", "Goods", "piece", 100, TrackingMode.Batch)], [], [], [], [], 0));
        Set(component, "page", "New sale"); Set(component, "selectedProduct", id); Set(component, "conversion", 2m);
        await (Task)Call(component, "AddEarliestBatches")!; Assert.Null(inventory.Request);
        Set(component, "conversion", 1m); Set(component, "batch", "MANUAL");
        await (Task)Call(component, "AddEarliestBatches")!; Assert.Null(inventory.Request); Assert.Equal("MANUAL", Get<string>(component, "batch"));
        Set(component, "batch", ""); inventory.Fail = true;
        await (Task)Call(component, "AddEarliestBatches")!;
        Assert.Empty(Get<List<TradeLine>>(component, "lines")); Assert.Equal(id, Get<Guid>(component, "selectedProduct")); Assert.Contains("Unavailable", Get<string>(component, "error"));
    }
    private sealed class SuggestionInventory : IInventoryService
    {
        public Task<IReadOnlyList<InventoryCostPolicy>> CostPolicies() => throw new NotImplementedException();
        public Task<Guid> WriteDown(InventoryWriteDownCommand input) => throw new NotImplementedException();
        public Task<Guid> SetCostPolicy(InventoryCostCommand input) => throw new NotImplementedException();
        public Task<StandardCostMigrationPreview> PreviewStandardMigration(Guid productId, decimal standardUnitCost) => throw new NotImplementedException();
        public Task<Guid> MigrateToStandard(StandardCostMigrationCommand input) => throw new NotImplementedException();
        public Task<IReadOnlyList<StandardAllowanceView>> StandardAllowances() => throw new NotImplementedException();
        public Task<Guid> ReviewStandardAllowance(StandardAllowanceCommand input) => throw new NotImplementedException();
        public Task<InventoryValuationReport> Valuation(InventoryValuationRequest input) => throw new NotImplementedException();
        public Task<byte[]> ExportValuation(InventoryValuationRequest input) => throw new NotImplementedException();
        public Task<IReadOnlyList<ConsignmentSiteView>> ConsignmentSites() => throw new NotImplementedException();
        public Task<Guid> SaveConsignmentSite(ConsignmentSiteCommand input) => throw new NotImplementedException();
        public Task<Guid> ReceiveCustody(CustodyReceiveCommand input) => throw new NotImplementedException();
        public Task<Guid> ReturnCustody(CustodyReturnCommand input) => throw new NotImplementedException();
        public Task<IReadOnlyList<CustodyStockView>> CustodyStock() => throw new NotImplementedException();
        public Task<IReadOnlyList<PendingDeliveryView>> PendingDeliveries() => throw new NotImplementedException();
        public Task<Guid> RejectDelivery(DeliveryRejectionCommand input) => throw new NotImplementedException();
        public BatchSuggestionCommand? Request; public bool Fail; public IReadOnlyList<BatchPick> Picks = [];
        public Task<IReadOnlyList<BatchPick>> SuggestBatches(BatchSuggestionCommand input) { Request = input; if (Fail) throw new BusinessException("Unavailable batches"); return Task.FromResult(Picks); }
        public Task<IReadOnlyList<LocationView>> Locations() => throw new NotImplementedException();
        public Task<Guid> CreateLocation(LocationCommand input) => throw new NotImplementedException();
        public Task<Guid> Transfer(TransferCommand input) => throw new NotImplementedException();
        public Task<Guid> Count(StockCountCommand input) => throw new NotImplementedException();
    }
    private sealed class Confirmation : IJSRuntime
    {
        public bool Accept { get; set; }
        public int Calls { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) { Calls++; return ValueTask.FromResult((TValue)(object)Accept); }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
    private sealed class StubPos(IReadOnlyList<BarcodeView> barcodes) : IPosService
    {
        public Task<IReadOnlyList<BarcodeView>> Barcodes() => Task.FromResult(barcodes);
        public Task<Guid> AddBarcode(BarcodeCommand input) => throw new NotSupportedException();
        public Task<IReadOnlyList<HeldSaleView>> HeldSales() => throw new NotSupportedException();
        public Task<Guid> Hold(HoldSaleCommand input) => throw new NotSupportedException();
        public Task<Guid> Cancel(CancelHeldSaleCommand input) => throw new NotSupportedException();
    }
}


