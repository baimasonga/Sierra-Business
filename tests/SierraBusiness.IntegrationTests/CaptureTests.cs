using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SierraBusiness.Desktop;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class CaptureTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sierra-capture-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider services = null!;
    private DocumentCaptureService Capture => services.GetRequiredService<DocumentCaptureService>();
    private IDbContextFactory<BusinessDb> Factory => services.GetRequiredService<IDbContextFactory<BusinessDb>>();
    public async Task InitializeAsync()
    {
        var collection = new ServiceCollection(); collection.AddLogging(); collection.AddSierraDesktop(directory);
        services = collection.BuildServiceProvider();
        await services.GetRequiredService<DesktopStore>().Initialize(Factory);
        var accounts = services.GetRequiredService<LocalAccountService>();
        await accounts.CreateOwner("capture@example.test", "Local-Capture-Test!42", "Local-Capture-Test!42");
        await accounts.SignIn("capture@example.test", "Local-Capture-Test!42");
    }
    public async Task DisposeAsync()
    {
        await services.DisposeAsync(); SqliteConnection.ClearAllPools(); Directory.Delete(directory, true);
    }
    [Fact] public async Task CaptureDeduplicatesPreservesOriginalAndNeverPostsTrading()
    {
        byte[] bytes = [1, 2, 3];
        var id = await Capture.Import("invoice.png", bytes, new("INVOICE\nInvoice No: A12\nSupplier: Demo\nTotal: NLe 125.00", 1, .9f));
        Assert.Equal(id, await Capture.Import("renamed.png", bytes, new("different", 1, .8f)));
        Assert.Equal(bytes, await Capture.Original(id));
        var result = await Capture.Read(id);
        Assert.Equal("Invoice", result.Draft.Fields.Kind); Assert.Equal("A12", result.Draft.Fields.Reference);
        result.Draft.Fields.Total = "NLe 120.00";
        await Capture.Save(id, 1, result.Draft, true);
        var reviewed = await Capture.Read(id); Assert.Equal("Reviewed capture", reviewed.Action); Assert.Equal(2, reviewed.History.Count);
        Assert.Contains("125.00", reviewed.History.Last().Detail);
        await using var db = await Factory.CreateDbContextAsync();
        Assert.Empty(await db.Documents.ToListAsync()); Assert.Empty(await db.Movements.ToListAsync()); Assert.Empty(await db.Journal.ToListAsync());
        var original = await db.Captures.SingleAsync(); original.Payload = "tamper";
        await Assert.ThrowsAsync<BusinessException>(() => db.SaveChangesAsync());
    }
    [Fact] public async Task StaleEditsAndSignedOutSessionAreRejected()
    {
        var id = await Capture.Import("test.png", [1], new("Receipt", 1, .5f));
        var draft = (await Capture.Read(id)).Draft;
        await Capture.Save(id, 1, draft, false);
        await Assert.ThrowsAsync<BusinessException>(() => Capture.Save(id, 1, draft, true));
        services.GetRequiredService<LocalAccountService>().SignOut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Capture.Original(id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Capture.Save(id, 2, draft, true));
    }
    [Fact] public async Task BackupContainsOriginalAndAllReviewRevisions()
    {
        var id = await Capture.Import("tender.pdf", [7, 8, 9], new("TENDER\nClosing date: 12 October 2026, 12:00 GMT", 1, .8f));
        var review = await Capture.Read(id); await Capture.Save(id, 1, review.Draft, true);
        var backup = await services.GetRequiredService<DesktopStore>().CreateBackup();
        await using var restored = new BusinessDb(new DbContextOptionsBuilder<BusinessDb>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = backup }.ToString()).Options);
        Assert.Equal(new byte[] { 7, 8, 9 }, (await restored.Captures.SingleAsync()).Original);
        Assert.Equal(2, await restored.CaptureRevisions.CountAsync());
        Assert.Empty(await restored.Documents.ToListAsync());
    }
    [Fact] public async Task EmptyOrOversizedCaptureIsNotPartiallySaved()
    {
        await Assert.ThrowsAsync<BusinessException>(() => Capture.Import("test.png", [1], new("", 1, 0)));
        await Assert.ThrowsAsync<BusinessException>(() => Capture.Import("test.pdf", [1], new("some text", 101, .5f)));
        Assert.Empty(await Capture.List());
    }
    [Fact] public void OfflineParserPreservesOldCurrencyAndAmbiguousDate()
    {
        var fields = OfflineCaptureParser.Parse("PROFORMA\nDate: 03/04/26\nTotal: SLL 1,000,000");
        Assert.Equal("Proforma", fields.Kind); Assert.Equal("03/04/26", fields.Date); Assert.Equal("SLL", fields.Currency); Assert.Equal("SLL 1,000,000", fields.Total); Assert.Empty(fields.Requirements);
    }
    [Fact] public async Task OnlineRequiresConsentAndSendsOnlyExplicitTextWithNoStorageOrTools()
    {
        var handler = new FixtureHandler(); using var http = new HttpClient(handler);
        var online = new OnlineCaptureAssistant(http);
        await Assert.ThrowsAsync<BusinessException>(() => online.Suggest("Receipt", "fixture-key", "fixture-model", false, default));
        Assert.Equal(0, handler.Calls);
        var fields = await online.Suggest("Receipt", "fixture-key", "fixture-model", true, default);
        Assert.Equal("Receipt", fields.Kind); Assert.Equal(1, handler.Calls);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.False(json.RootElement.GetProperty("store").GetBoolean()); Assert.Equal("Receipt", json.RootElement.GetProperty("input").GetString());
        Assert.False(json.RootElement.TryGetProperty("tools", out _)); Assert.Contains("json_schema", handler.Body);
    }
    [Theory]
    [InlineData("{\"status\":\"incomplete\"}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\"}]}]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"{}\"}]}]}")]
    public async Task IncompleteRefusedOrMalformedAiOutputIsRejected(string response)
    {
        using var http = new HttpClient(new FixtureHandler { Response = response });
        await Assert.ThrowsAsync<BusinessException>(() => new OnlineCaptureAssistant(http).Suggest("Receipt", "fixture-key", "fixture-model", true, default));
    }
    private sealed class FixtureHandler : HttpMessageHandler
    {
        public int Calls; public string? Body; public string? Response;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.ToString());
            Body = await request.Content!.ReadAsStringAsync(token);
            var json = Response ?? JsonSerializer.Serialize(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = JsonSerializer.Serialize(new CaptureFields { Kind = "Receipt" }) } } } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

