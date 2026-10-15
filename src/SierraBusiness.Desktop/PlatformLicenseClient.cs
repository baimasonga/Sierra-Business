using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using SierraBusiness.Application;

namespace SierraBusiness.Desktop;

public sealed record PlatformLicenseOptions(Uri ApiOrigin, string ProductCode, LicenseVerificationKey[] Keys, Dictionary<string, string>? PermissionFeatures = null)
{
    public void Validate()
    {
        if (ApiOrigin is null || ApiOrigin.Scheme != "https" || ApiOrigin.AbsolutePath != "/" || ApiOrigin.UserInfo.Length > 0 || ApiOrigin.Query.Length > 0 || ApiOrigin.Fragment.Length > 0 ||
            string.IsNullOrWhiteSpace(ProductCode) || Keys is null || Keys.Length == 0 ||
            Keys.Any(k => k is null || string.IsNullOrWhiteSpace(k.Kid) || string.IsNullOrWhiteSpace(k.X) || LicenseProtocol.Decode(k.X).Length != 32) ||
            Keys.Select(k => k.Kid).Distinct().Count() != Keys.Length) throw new InvalidOperationException("Invalid licensing configuration.");
        if (PermissionFeatures?.Any(p => !BusinessPermissions.All.Contains(p.Key) || p.Key.EndsWith(".read", StringComparison.Ordinal) || p.Key is "backups.create" or "users.manage" ||
            string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(p.Value, "^[a-z][a-z0-9_]{0,63}$")) == true) throw new InvalidOperationException("Invalid license feature mapping.");
    }
}

/// <summary>One instance per company process. All identity, lease and clock updates share one durable checkpoint.</summary>
public sealed class PlatformLicenseClient(PlatformLicenseOptions options, ILicenseStore store, HttpClient http, TimeProvider clock) : ILicenseService
{
    /// <param name="RefreshPending">Written before the licensing service is asked to refresh and cleared with its answer. If a revocation
    /// arrives but cannot be saved, the marker survives a restart and the stored lease is not trusted until a refresh succeeds.</param>
    private sealed record Checkpoint(string Installation, string PrivateKey, string? Activation = null, string? Lease = null, DateTimeOffset? LastSeen = null, bool RefreshPending = false);
    private readonly SemaphoreSlim gate = new(1, 1);
    private Checkpoint? state;
    private DateTimeOffset? observed;
    private bool storageFault, refreshing;
    private readonly object stateLock = new();
    public PlatformLicenseOptions Options => options;
    public LicenseCheck Current
    {
        get
        {
            lock (stateLock)
            {
                if (storageFault) return new(LicenseState.Invalid);
                if (state?.Lease is null) return new(LicenseState.NotActivated);
                if (state.RefreshPending && !refreshing) return new(LicenseState.RefreshRequired);
                using var key = Import(state);
                var now = clock.GetUtcNow();
                var highWater = observed is { } seen && (state.LastSeen is null || seen > state.LastSeen) ? observed : state.LastSeen;
                var check = LicenseProtocol.Verify(state.Lease, options.Keys, options.ProductCode, state.Installation, Thumbprint(key), now, highWater, state.Activation);
                if (observed is null || now > observed) observed = now;
                return check;
            }
        }
    }
    public void Demand(string permission)
    {
        // Licensing never takes away reads, exports through read services, account recovery or backups.
        if (permission.EndsWith(".read", StringComparison.Ordinal) || permission is "backups.create" or "users.manage") return;
        var feature = options.PermissionFeatures?.GetValueOrDefault(permission) ?? "business_write";
        if (!Current.Allows(feature)) throw new UnauthorizedAccessException("This action requires an active license with the purchased feature. Saved records and backups remain available.");
    }
    private static Key Import(Checkpoint checkpoint)
    {
        var bytes = Convert.FromBase64String(checkpoint.PrivateKey);
        try { return Key.Import(SignatureAlgorithm.Ed25519, bytes, KeyBlobFormat.RawPrivateKey); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static string Thumbprint(Key key) => LicenseProtocol.Thumbprint(LicenseProtocol.Encode(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)));
    private async Task Load(CancellationToken cancellationToken)
    {
        options.Validate();
        if (state is not null) return;
        var raw = await store.Read(cancellationToken);
        Checkpoint checkpoint;
        if (raw is null)
        {
            using var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            var privateBytes = key.Export(KeyBlobFormat.RawPrivateKey);
            try { checkpoint = new(Guid.NewGuid().ToString(), Convert.ToBase64String(privateBytes)); }
            finally { CryptographicOperations.ZeroMemory(privateBytes); }
            await store.Write(JsonSerializer.Serialize(checkpoint), cancellationToken);
        }
        else
        {
            checkpoint = JsonSerializer.Deserialize<Checkpoint>(raw) ?? throw new CryptographicException("Protected license state is invalid.");
            if (!Guid.TryParse(checkpoint.Installation, out _) || string.IsNullOrWhiteSpace(checkpoint.PrivateKey) || checkpoint.PrivateKey.Length > 256 ||
                (checkpoint.Lease is null) != (checkpoint.Activation is null) || checkpoint.Lease?.Length > 16384 || checkpoint.Activation?.Length > 64)
                throw new CryptographicException("Protected license state is invalid.");
            using var key = Import(checkpoint);
        }
        lock (stateLock) { state = checkpoint; storageFault = false; }
    }
    private async Task Save(Checkpoint checkpoint, CancellationToken cancellationToken)
    {
        try { await store.Write(JsonSerializer.Serialize(checkpoint), cancellationToken); }
        catch
        {
            // Keep an authoritative revocation and clock watermark in memory even when disk persistence fails.
            lock (stateLock) { state = checkpoint; storageFault = true; }
            throw;
        }
        lock (stateLock) { state = checkpoint; storageFault = false; }
    }
    private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(60);
    private async Task ObserveClock(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        lock (stateLock)
        {
            if (observed > now) now = observed.Value;
            if (state!.LastSeen > now) now = state.LastSeen.Value;
        }
        // The in-memory watermark covers this session; persisting it once per rollback tolerance (60 s) avoids a forced disk flush on every call.
        if (storageFault || state!.LastSeen is null || now > state.LastSeen.Value.Add(PersistInterval)) await Save(state! with { LastSeen = now }, cancellationToken);
    }
    public async Task<LicenseCheck> Check(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { await Load(cancellationToken); _ = Current; await ObserveClock(cancellationToken); return Current; }
        finally { gate.Release(); }
    }
    private async Task<JsonDocument> Post(string path, object body, string? token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.ApiOrigin, path)) { Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }) };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Licensing service rejected the request ({(int)response.StatusCode}).", null, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream(); var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        { if (buffer.Length + read > 65536) throw new HttpRequestException("Licensing response exceeds the supported size."); buffer.Write(chunk, 0, read); }
        return JsonDocument.Parse(buffer.ToArray());
    }
    private async Task<(string Nonce, string Signature)> Proof(string purpose, string activation, Key key, CancellationToken cancellationToken)
    {
        using var challenge = await Post("/v1/devices/challenges", new { purpose, installation_id = state!.Installation, subject_id = activation.Length == 0 ? null : activation }, null, cancellationToken);
        var nonce = challenge.RootElement.GetProperty("nonce").GetString() ?? throw new HttpRequestException("Invalid device challenge.");
        return (nonce, LicenseProtocol.Encode(SignatureAlgorithm.Ed25519.Sign(key, Encoding.UTF8.GetBytes(LicenseProtocol.Proof(purpose, nonce, state.Installation, activation, Thumbprint(key))))));
    }
    private async Task<LicenseCheck> Accept(JsonElement grant, Key key, CancellationToken cancellationToken)
    {
        var lease = grant.GetProperty("lease").GetString() ?? "";
        var activation = grant.GetProperty("activation_id").GetString() ?? "";
        DateTimeOffset? highWater;
        lock (stateLock) highWater = observed is { } seen && (state!.LastSeen is null || seen > state.LastSeen) ? observed : state!.LastSeen;
        var check = LicenseProtocol.Verify(lease, options.Keys, options.ProductCode, state!.Installation, Thumbprint(key), clock.GetUtcNow(), highWater, activation);
        if (!check.Usable || string.IsNullOrWhiteSpace(activation)) throw new CryptographicException("The licensing service returned an unusable signed lease.");
        await Save(state with { Lease = lease, Activation = activation, RefreshPending = false, LastSeen = clock.GetUtcNow() > state.LastSeen.GetValueOrDefault() ? clock.GetUtcNow() : state.LastSeen }, cancellationToken);
        return check;
    }
    public async Task<LicenseCheck> Activate(string email, string password, string licenseKey, string? mfaCode = null, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await Load(cancellationToken);
            if (state!.Activation is not null) throw new InvalidOperationException("Deactivate this installation before replacing its license.");
            using var login = await Post("/v1/auth/login", new { email, password, code = string.IsNullOrWhiteSpace(mfaCode) ? null : mfaCode }, null, cancellationToken);
            var token = login.RootElement.GetProperty("token").GetString() ?? throw new HttpRequestException("Sign-in failed.");
            using var key = Import(state);
            var proof = await Proof("activate", "", key, cancellationToken);
            using var grant = await Post("/v1/licenses/activate", new { license_key = licenseKey, installation_id = state.Installation, device_public_key = new { kty = "OKP", crv = "Ed25519", x = LicenseProtocol.Encode(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)) }, nonce = proof.Nonce, signature = proof.Signature }, token, cancellationToken);
            return await Accept(grant.RootElement, key, cancellationToken);
        }
        finally { gate.Release(); }
    }
    public async Task<LicenseCheck> Refresh(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await Load(cancellationToken);
            if (state!.Activation is null) return Current;
            // Durable before the service is asked, so an answer that cannot be saved is never forgotten across a restart.
            // A marker left by an earlier run stays until the service answers: it may stand for a revocation that could not be saved.
            var marked = !state.RefreshPending; if (marked) await Save(state with { RefreshPending = true }, cancellationToken);
            lock (stateLock) refreshing = true;
            try
            {
                using var key = Import(state);
                var proof = await Proof("refresh", state.Activation, key, cancellationToken);
                JsonDocument grant;
                try { grant = await Post("/v1/licenses/refresh", new { activation_id = state.Activation, installation_id = state.Installation, nonce = proof.Nonce, signature = proof.Signature }, null, cancellationToken); }
                catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
                { await Save(state with { Lease = null, Activation = null, RefreshPending = false }, cancellationToken); throw; }
                using (grant) return await Accept(grant.RootElement, key, cancellationToken);
            }
            catch (Exception) when (marked && state.RefreshPending && state.Activation is not null)
            {
                // No authoritative answer (offline, timeout, bad response): the stored lease stays trusted.
                try { await Save(state with { RefreshPending = false }, cancellationToken); } catch (Exception) when (storageFault) { }
                throw;
            }
            finally { lock (stateLock) refreshing = false; }
        }
        finally { gate.Release(); }
    }
    public async Task Deactivate(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await Load(cancellationToken);
            if (state!.Activation is null) return;
            using var key = Import(state); var proof = await Proof("deactivate", state.Activation, key, cancellationToken);
            using var result = await Post("/v1/licenses/deactivate", new { activation_id = state.Activation, installation_id = state.Installation, nonce = proof.Nonce, signature = proof.Signature }, null, cancellationToken);
            await Save(state with { Lease = null, Activation = null }, cancellationToken);
        }
        finally { gate.Release(); }
    }
}
