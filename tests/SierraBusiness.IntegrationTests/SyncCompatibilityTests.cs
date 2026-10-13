using System.Net;
using System.Net.Http.Json;
using SierraBusiness.Application;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class SyncCompatibilityTests
{
    [Theory]
    [InlineData(404, 1, 1)]
    [InlineData(200, 1, 1)]
    [InlineData(200, 2, 1)]
    [InlineData(200, 999, 1)]
    [InlineData(200, 1, 999)]
    public async Task IncompatibleServerNeverReceivesBusinessData(int status, int protocol, int schema)
    {
        var handler = new CompatibilityHandler(status, new(protocol, schema));
        var transport = new HttpSyncTransport(new HttpClient(handler) { BaseAddress = new("https://hq.example/") }, "FREETOWN", "test-branch-secret");
        var error = await Assert.ThrowsAsync<PermanentJobException>(() => transport.Push(new("FREETOWN", [])));
        Assert.Contains("Upgrade head office", error.Message);
        Assert.Equal(new[] { "/api/sync/compatibility" }, handler.Paths);
    }
    private sealed class CompatibilityHandler(int status, SyncCompatibility value) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Assert.True(request.Headers.Contains(SyncContract.ProtocolHeader));
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = JsonContent.Create(value) });
        }
    }
}
