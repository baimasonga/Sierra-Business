using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class PrintableDocumentTests
{
    [Fact]
    public async Task InvoiceUsesPostedAmountsEscapesPartyAndMarksReversal()
    {
        using var services = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime, NoJavascript>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var id = Guid.NewGuid();
        var request = new TradeCommand(Guid.NewGuid(), DocumentKind.Sale, "<script>bad()</script>", new(2026, 9, 1), [new(id, 2, 100)], null);
        var doc = new DocumentView(Guid.NewGuid(), "INVOICE-001", DocumentKind.Sale, request.Party, request.BusinessDate, 230, "Credit", null, JsonSerializer.Serialize(new { Request = request, Net = 200, Tax = 30, TaxRate = .15m }));
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<TradeDocumentPreview>(ParameterView.FromDictionary(new Dictionary<string, object?> {
            ["Document"] = doc, ["Products"] = new List<ProductView> { new(id, "SKU", "Product", "General", "piece", 999, TrackingMode.Quantity) }, ["Reversed"] = true
        }))).ToHtmlString());
        Assert.Contains("230.00", html);
        Assert.Contains("100.00", html);
        Assert.DoesNotContain("999.00", html);
        Assert.Contains("REVERSED", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("not proof of payment", html);
    }
    [Fact]
    public async Task DeliveryPrintOmitsCommercialPricesAndPreservesTrackingAndReversal()
    {
        using var services = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime, NoJavascript>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var id = Guid.NewGuid(); var request = new TradeCommand(Guid.NewGuid(), DocumentKind.Delivery, "School", new(2026, 9, 1), [new(id, 1, 123.45m, Serial: "SERIAL-ABC")], null, OrderId: Guid.NewGuid());
        var doc = new DocumentView(Guid.NewGuid(), "DELIVERY-001", DocumentKind.Delivery, request.Party, request.BusinessDate, 0, "Awaiting invoice", null, JsonSerializer.Serialize(new { Request = request, Net = 123.45m, Cost = 67.89m }));
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<BusinessDocumentOutput>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Document"] = doc, ["Products"] = new List<ProductView> { new(id, "SKU", "Printer", "Office", "unit", 999, TrackingMode.Serial) }, ["Reversed"] = true }))).ToHtmlString());
        Assert.Contains("Delivery note", html); Assert.Contains("SERIAL-ABC", html); Assert.Contains("REVERSED", html);
        Assert.DoesNotContain("123.45", html); Assert.DoesNotContain("67.89", html); Assert.DoesNotContain("999", html);
    }
    private sealed class NoJavascript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new NotSupportedException();
    }
}

