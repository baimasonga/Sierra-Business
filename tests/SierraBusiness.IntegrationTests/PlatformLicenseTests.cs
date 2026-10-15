using System.Net;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using SierraBusiness.Application;
using SierraBusiness.Desktop;
using Xunit;

namespace SierraBusiness.IntegrationTests;

public sealed class PlatformLicenseTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(2000000000);
    private sealed class Clock : TimeProvider { public DateTimeOffset Time = Now; public override DateTimeOffset GetUtcNow() => Time; }
    private sealed class Store : ILicenseStore
    {
        public string? Value; public int Writes; public bool FailWrites;
        public Task<string?> Read(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task Write(string state, CancellationToken cancellationToken = default) { if (FailWrites) throw new IOException("Synthetic protected store failure."); Value = state; Writes++; return Task.CompletedTask; }
    }
    private sealed class Provider : HttpMessageHandler
    {
        public readonly Key Signing = Key.Create(SignatureAlgorithm.Ed25519);
        public string Activation = "act_test", Installation = "", Thumbprint = "";
        public int Calls; public bool Offline, Revoked, BadBinding;
        public string LastPurpose = "";
        public LicenseVerificationKey Public => new("test", LicenseProtocol.Encode(Signing.PublicKey.Export(KeyBlobFormat.RawPublicKey)));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Offline) throw new HttpRequestException("offline");
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); var p = doc.RootElement;
            object result;
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/v1/auth/login":
                    Assert.Equal("platform@example.test", p.GetProperty("email").GetString());
                    Assert.Equal("one-use-password", p.GetProperty("password").GetString());
                    Assert.Equal("123456", p.GetProperty("code").GetString());
                    Assert.False(p.TryGetProperty("mfa_code", out _)); result = new { token = "session-only" }; break;
                case "/v1/devices/challenges":
                    Installation = p.GetProperty("installation_id").GetString()!;
                    LastPurpose = p.GetProperty("purpose").GetString()!;
                    if (LastPurpose == "activate") Assert.False(p.TryGetProperty("subject_id", out _));
                    else Assert.Equal(Activation, p.GetProperty("subject_id").GetString());
                    result = new { nonce = "server-nonce" }; break;
                default:
                    Assert.Equal(Installation, p.GetProperty("installation_id").GetString());
                    if (LastPurpose == "activate")
                    {
                        Assert.Equal("session-only", request.Headers.Authorization?.Parameter);
                        var jwk = p.GetProperty("device_public_key"); Assert.False(jwk.TryGetProperty("d", out _));
                        DeviceX = jwk.GetProperty("x").GetString()!; Thumbprint = LicenseProtocol.Thumbprint(DeviceX);
                    }
                    else Assert.Null(request.Headers.Authorization);
                    var publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, LicenseProtocol.Decode(DeviceX), KeyBlobFormat.RawPublicKey);
                    Assert.True(SignatureAlgorithm.Ed25519.Verify(publicKey, Encoding.UTF8.GetBytes(LicenseProtocol.Proof(LastPurpose, "server-nonce", Installation, LastPurpose == "activate" ? "" : Activation, Thumbprint)), LicenseProtocol.Decode(p.GetProperty("signature").GetString()!)));
                    if (Revoked) return new(HttpStatusCode.Forbidden);
                    result = LastPurpose == "deactivate" ? new { status = "ok" } : (object)new { lease = Lease(Signing, installation: BadBinding ? "other" : Installation, thumbprint: Thumbprint), activation_id = Activation }; break;
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result), Encoding.UTF8, "application/json") };
        }
        public string DeviceX = "";
        protected override void Dispose(bool disposing) { if (disposing) Signing.Dispose(); base.Dispose(disposing); }
    }
    private static string Lease(Key key, string installation = "installation", string thumbprint = "device", string product = "sierra-business", string typ = "lease+jws", long expiry = 2000003600, bool feature = true)
    {
        var header = LicenseProtocol.Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "EdDSA", typ, kid = "test" }));
        var claims = LicenseProtocol.Encode(JsonSerializer.SerializeToUtf8Bytes(new { v = 1, iss = "trust-platform", aud = "product:" + product, inst = installation, act = "act_test", cnf = new { jkt = thumbprint }, iat = 1999999900L, nbf = 1999999900L, exp = expiry, refresh_after = 2000001800L, ent = new Dictionary<string, object> { ["business_write"] = feature, ["numeric_only"] = 5 } }));
        var message = header + "." + claims;
        return message + "." + LicenseProtocol.Encode(SignatureAlgorithm.Ed25519.Sign(key, Encoding.ASCII.GetBytes(message)));
    }
    [Fact] public void SignedLeaseGrantsOnlyBooleanFeatures()
    {
        using var p = new Provider(); var c = LicenseProtocol.Verify(Lease(p.Signing), [p.Public], "sierra-business", "installation", "device", Now);
        Assert.True(c.Allows("business_write")); Assert.False(c.Allows("numeric_only")); Assert.False(c.Allows("missing"));
    }
    [Theory] [InlineData("product")] [InlineData("installation")] [InlineData("device")] [InlineData("purpose")] [InlineData("activation")] [InlineData("unknown_key")]
    public void RejectsWrongBindings(string binding)
    {
        using var p = new Provider(); var check = LicenseProtocol.Verify(Lease(p.Signing, typ: binding == "purpose" ? "trust-update-manifest+jws" : "lease+jws"), binding == "unknown_key" ? [] : [p.Public], binding == "product" ? "other" : "sierra-business", binding == "installation" ? "other" : "installation", binding == "device" ? "other" : "device", Now, activation: binding == "activation" ? "act_other" : "act_test");
        Assert.Equal(LicenseState.Invalid, check.State);
    }
    [Fact] public void TamperingFailsWithoutGrantingFeatures()
    {
        using var p = new Provider(); var parts = Lease(p.Signing).Split('.'); var sig = LicenseProtocol.Decode(parts[2]); sig[0] ^= 1; parts[2] = LicenseProtocol.Encode(sig);
        var check = LicenseProtocol.Verify(string.Join('.', parts), [p.Public], "sierra-business", "installation", "device", Now);
        Assert.Equal(LicenseState.Invalid, check.State); Assert.False(check.Allows("business_write"));
    }
    [Fact] public void ExpiryIsHardAndClockRollbackRestricts()
    {
        using var p = new Provider(); var token = Lease(p.Signing);
        Assert.Equal(LicenseState.Expired, LicenseProtocol.Verify(token, [p.Public], "sierra-business", "installation", "device", Now.AddHours(1)).State);
        Assert.Equal(LicenseState.ClockAnomaly, LicenseProtocol.Verify(token, [p.Public], "sierra-business", "installation", "device", Now, Now.AddMinutes(2)).State);
        Assert.Equal(LicenseState.RefreshRecommended, LicenseProtocol.Verify(token, [p.Public], "sierra-business", "installation", "device", Now.AddMinutes(30)).State);
    }
    [Fact] public async Task ActivationRefreshRestartOfflineAndExpiryPreserveIdentity()
    {
        using var p = new Provider(); using var http = new HttpClient(p); var store = new Store(); var clock = new Clock();
        var options = new PlatformLicenseOptions(new Uri("https://platform.example.test"), "sierra-business", [p.Public]);
        var client = new PlatformLicenseClient(options, store, http, clock);
        Assert.Equal(LicenseState.NotActivated, (await client.Check()).State);
        var identity = JsonDocument.Parse(store.Value!).RootElement.GetProperty("Installation").GetString();
        Assert.True((await client.Activate("platform@example.test", "one-use-password", "customer-key", "123456")).Usable);
        Assert.DoesNotContain("one-use-password", store.Value); Assert.DoesNotContain("session-only", store.Value); Assert.DoesNotContain("customer-key", store.Value);
        client.Demand("sales.create");
        Assert.True((await client.Refresh()).Usable);
        var restart = new PlatformLicenseClient(options, store, http, clock); Assert.True((await restart.Check()).Usable);
        Assert.Equal(identity, JsonDocument.Parse(store.Value!).RootElement.GetProperty("Installation").GetString());
        p.Offline = true; await Assert.ThrowsAsync<HttpRequestException>(() => restart.Refresh()); Assert.True(restart.Current.Usable);
        clock.Time = Now.AddHours(1); Assert.False((await restart.Check()).Usable);
        Assert.Throws<UnauthorizedAccessException>(() => restart.Demand("sales.create")); restart.Demand("business.read"); restart.Demand("backups.create");
        clock.Time = Now; Assert.Equal(LicenseState.ClockAnomaly, (await new PlatformLicenseClient(options, store, http, clock).Check()).State);
    }
    [Fact] public async Task InvalidServerLeaseDoesNotReplaceCheckpoint()
    {
        using var p = new Provider(); using var http = new HttpClient(p); var store = new Store(); var client = new PlatformLicenseClient(new(new Uri("https://platform.example.test"), "sierra-business", [p.Public]), store, http, new Clock());
        await client.Check(); var before = store.Value; p.BadBinding = true;
        await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() => client.Activate("platform@example.test", "one-use-password", "key", "123456"));
        Assert.Equal(before, store.Value); Assert.False(client.Current.Usable);
    }
    [Fact] public async Task AuthoritativeRevocationRestrictsWhileTransientFailuresDoNot()
    {
        using var p = new Provider(); using var http = new HttpClient(p); var store = new Store(); var client = new PlatformLicenseClient(new(new Uri("https://platform.example.test"), "sierra-business", [p.Public]), store, http, new Clock());
        await client.Activate("platform@example.test", "one-use-password", "key", "123456"); p.Revoked = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => client.Refresh()); Assert.Equal(LicenseState.NotActivated, client.Current.State);
    }
    [Fact] public async Task DeactivationRequiresServerAcknowledgement()
    {
        using var p = new Provider(); using var http = new HttpClient(p); var store = new Store(); var client = new PlatformLicenseClient(new(new Uri("https://platform.example.test"), "sierra-business", [p.Public]), store, http, new Clock());
        await client.Activate("platform@example.test", "one-use-password", "key", "123456"); p.Offline = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => client.Deactivate()); Assert.True(client.Current.Usable);
        p.Offline = false; await client.Deactivate(); Assert.Equal(LicenseState.NotActivated, client.Current.State);
    }
    [Fact] public void CachedActorsStillEnforceTheCurrentLicenseAndLocalPermissions()
    {
        using var p = new Provider(); using var http = new HttpClient(p); var client = new PlatformLicenseClient(new(new Uri("https://platform.example.test"), "sierra-business", [p.Public]), new Store(), http, new Clock());
        var actor = new Actor("local-user", "branch", new HashSet<string> { "business.read", "sales.create", "backups.create" }) { LicenseDemand = client.Demand };
        actor.Demand("business.read"); actor.Demand("backups.create");
        Assert.Throws<UnauthorizedAccessException>(() => actor.Demand("sales.create")); Assert.Throws<UnauthorizedAccessException>(() => actor.Demand("users.manage"));
    }
    [Fact] public void RejectsUnsafeTransportAndDuplicatePins()
    {
        using var p = new Provider(); Assert.Throws<InvalidOperationException>(() => new PlatformLicenseOptions(new Uri("http://example.test"), "sierra-business", [p.Public]).Validate());
        Assert.Throws<InvalidOperationException>(() => new PlatformLicenseOptions(new Uri("https://example.test"), "sierra-business", [p.Public, p.Public]).Validate());
    }
    [Fact] public void VerifiesLeaseSignedByThePlatformsNodeCrypto()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "node-lease.json")));
        var root = fixture.RootElement;
        var check = LicenseProtocol.Verify(root.GetProperty("token").GetString()!, [new("node-vector", root.GetProperty("x").GetString()!)], "sierra-business", "test-installation", "test-device", Now, activation: "act_test");
        Assert.True(check.Allows("business_write"));
    }
    [Fact] public async Task CachedActorChecksDetectClockRollbackBeforeAnotherSessionLookup()
    {
        using var p = new Provider(); using var http = new HttpClient(p); var store = new Store(); var clock = new Clock();
        var client = new PlatformLicenseClient(new(new Uri("https://platform.example.test"), "sierra-business", [p.Public]), store, http, clock);
        await client.Activate("platform@example.test", "one-use-password", "key", "123456");
        clock.Time = Now.AddMinutes(3); client.Demand("sales.create");
        clock.Time = Now; Assert.Equal(LicenseState.ClockAnomaly, client.Current.State);
        await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() => client.Refresh());
        Assert.Throws<UnauthorizedAccessException>(() => client.Demand("sales.create"));
    }
    [Fact] public async Task CorruptCheckpointIsNotSilentlyReplacedWithANewIdentity()
    {
        using var p = new Provider(); using var http = new HttpClient(p);
        var store = new Store { Value = JsonSerializer.Serialize(new { Installation = Guid.NewGuid().ToString(), PrivateKey = (string?)null }) };
        var before = store.Value;
        var client = new PlatformLicenseClient(new(new Uri("https://platform.example.test"), "sierra-business", [p.Public]), store, http, new Clock());
        await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() => client.Check());
        Assert.Equal(before, store.Value); Assert.Equal(0, store.Writes);
        client.Demand("business.read"); client.Demand("backups.create"); Assert.Throws<UnauthorizedAccessException>(() => client.Demand("sales.create"));
    }
    [Fact] public async Task ALocalManagerRoleDoesNotGrantAnUnpurchasedMappedFeature()
    {
        using var p = new Provider(); using var http = new HttpClient(p);
        var options = new PlatformLicenseOptions(new Uri("https://platform.example.test"), "sierra-business", [p.Public], new() { ["pricing.manage"] = "pricing_enabled" });
        var client = new PlatformLicenseClient(options, new Store(), http, new Clock());
        await client.Activate("platform@example.test", "one-use-password", "key", "123456");
        var actor = new Actor("manager", "branch", BusinessPermissions.All.ToHashSet()) { LicenseDemand = client.Demand };
        actor.Demand("sales.create"); Assert.True(actor.CanDemand("sales.create"));
        Assert.False(actor.CanDemand("pricing.manage")); Assert.Throws<UnauthorizedAccessException>(() => actor.Demand("pricing.manage"));
    }
    [Fact] public async Task KnownRevocationCannotLeaveCachedWriteAccessWhenProtectedStorageFails()
    {
        using var p = new Provider(); using var http = new HttpClient(p); var store = new Store();
        var options = new PlatformLicenseOptions(new Uri("https://platform.example.test"), "sierra-business", [p.Public]);
        var client = new PlatformLicenseClient(options, store, http, new Clock());
        await client.Activate("platform@example.test", "one-use-password", "key", "123456");
        p.Revoked = true; store.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(() => client.Refresh());
        Assert.False(client.Current.Usable); Assert.Throws<UnauthorizedAccessException>(() => client.Demand("sales.create")); client.Demand("business.read");
        store.FailWrites = false;
        Assert.Equal(LicenseState.NotActivated, (await client.Check()).State);
        Assert.Equal(LicenseState.NotActivated, (await new PlatformLicenseClient(options, store, http, new Clock()).Check()).State);
    }
    [Fact] public async Task ClockCheckpointFailureRestrictsUntilTheWatermarkIsDurable()
    {
        using var p = new Provider(); using var http = new HttpClient(p); var store = new Store(); var clock = new Clock();
        var client = new PlatformLicenseClient(new(new Uri("https://platform.example.test"), "sierra-business", [p.Public]), store, http, clock);
        await client.Activate("platform@example.test", "one-use-password", "key", "123456");
        clock.Time = Now.AddMinutes(2); store.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(() => client.Check()); Assert.False(client.Current.Usable);
        store.FailWrites = false; Assert.True((await client.Check()).Usable);
        clock.Time = Now; Assert.Equal(LicenseState.ClockAnomaly, (await client.Check()).State);
    }
    [Fact] public async Task TheClockWatermarkIsPersistedOncePerToleranceNotOnEveryCheck()
    {
        using var p = new Provider(); using var http = new HttpClient(p); var store = new Store(); var clock = new Clock();
        var options = new PlatformLicenseOptions(new Uri("https://platform.example.test"), "sierra-business", [p.Public]);
        var client = new PlatformLicenseClient(options, store, http, clock);
        await client.Activate("platform@example.test", "one-use-password", "key", "123456"); await client.Check(); var writes = store.Writes;
        for (var seconds = 1; seconds <= 50; seconds += 7) { clock.Time = Now.AddSeconds(seconds); Assert.True((await client.Check()).Usable); }
        Assert.Equal(writes, store.Writes);
        clock.Time = Now.AddMinutes(2); await client.Check(); Assert.Equal(writes + 1, store.Writes);
        // After a restart the durable watermark still detects a rollback beyond the tolerance.
        clock.Time = Now.AddMinutes(2).AddSeconds(-61); Assert.Equal(LicenseState.ClockAnomaly, (await new PlatformLicenseClient(options, store, http, clock).Check()).State);
    }
}
