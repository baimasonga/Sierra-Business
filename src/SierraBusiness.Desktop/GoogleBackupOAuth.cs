using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SierraBusiness.Domain;
namespace SierraBusiness.Desktop;

internal static class GoogleBackupOAuth
{
    internal const string Scope = "https://www.googleapis.com/auth/drive.file";
    internal static string UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    internal static async Task<string> Token(HttpClient client,Dictionary<string,string> fields,string property,CancellationToken ct)
    {
        using var response = await client.PostAsync("https://oauth2.googleapis.com/token",new FormUrlEncodedContent(fields),ct);
        if (!response.IsSuccessStatusCode) throw new BusinessException("Google authorization expired or was refused. Reconnect the backup account.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!json.RootElement.TryGetProperty(property,out var value) || string.IsNullOrWhiteSpace(value.GetString())) throw new BusinessException("Google did not supply the required authorization. Reconnect and grant backup access.");
        if (json.RootElement.TryGetProperty("scope",out var scopes) && !(scopes.GetString() ?? "").Split(' ').Contains(Scope)) throw new BusinessException("Google Drive backup access was not granted."); return value.GetString()!;
    }
    internal static async Task<string> Connect(HttpClient client,string clientId,string clientSecret,CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Google backup sign-in is available in the Windows desktop.");
        var verifier = UrlEncode(RandomNumberGenerator.GetBytes(32)); var state = UrlEncode(RandomNumberGenerator.GetBytes(32));
        var listener = new TcpListener(IPAddress.Loopback,0); listener.Start();
        try
        {
            var redirect = "http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";
            var parameters = new Dictionary<string,string> { ["client_id"] = clientId,["redirect_uri"] = redirect,["response_type"] = "code",["scope"] = Scope,["state"] = state,["code_challenge"] = UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),["code_challenge_method"] = "S256",["access_type"] = "offline",["prompt"] = "consent" };
            var url = "https://accounts.google.com/o/oauth2/v2/auth?"+string.Join('&',parameters.Select(p => Uri.EscapeDataString(p.Key)+"="+Uri.EscapeDataString(p.Value)));
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            string? code = null;
            while (code is null)
            {
                using var connection = await listener.AcceptTcpClientAsync(ct); await using var stream = connection.GetStream(); var buffer = new byte[8192]; var count = 0;
                using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct); requestDeadline.CancelAfter(TimeSpan.FromSeconds(10));
                while (count < buffer.Length)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(count),requestDeadline.Token); if (read == 0) break; count += read; if (Encoding.ASCII.GetString(buffer,0,count).Contains("\r\n\r\n",StringComparison.Ordinal)) break;
                }
                var line = Encoding.ASCII.GetString(buffer,0,count).Split("\r\n")[0].Split(' ');
                var good = line.Length == 3 && line[0] == "GET" && line[1].StartsWith("/?",StringComparison.Ordinal);
                var query = new Dictionary<string,List<string>>(StringComparer.Ordinal);
                if (good) foreach (var part in new Uri(redirect.TrimEnd('/')+line[1]).Query.TrimStart('?').Split('&')) { var pieces = part.Split('=',2); var name = Uri.UnescapeDataString(pieces[0].Replace('+',' ')); var value = pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1].Replace('+',' ')) : ""; if (!query.TryGetValue(name,out var values)) query[name] = values = []; values.Add(value); }
                good = good && query.TryGetValue("state",out var returned) && returned.Count == 1 && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(returned[0]),Encoding.ASCII.GetBytes(state));
                var denied = good && query.ContainsKey("error"); if (good && !denied && query.TryGetValue("code",out var result) && result.Count == 1 && result[0].Length is > 0 and < 4096) code = result[0];
                var text = good ? "Return to Rokela Business Admin to finish backup setup." : "This callback is not a valid Rokela backup sign-in."; var body = Encoding.UTF8.GetBytes(text); var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(good ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header,ct); await stream.WriteAsync(body,ct); if (denied) throw new BusinessException("Google backup sign-in was cancelled or access was denied.");
            }
            var fields = new Dictionary<string,string> { ["client_id"] = clientId,["code"] = code,["redirect_uri"] = redirect,["code_verifier"] = verifier,["grant_type"] = "authorization_code" }; if (clientSecret.Length > 0) fields["client_secret"] = clientSecret;
            return await Token(client,fields,"refresh_token",ct);
        }
        finally { listener.Stop(); }
    }
}
