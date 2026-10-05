using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SierraBusiness.Infrastructure;
using SierraBusiness.Infrastructure.Postgres;
using Xunit;
namespace SierraBusiness.IntegrationTests;

// These regressions run without a PostgreSQL server; live round trips are in PostgresTests.
public sealed class PostgresCompatibilityTests
{
    private static BusinessDb Model() => new(new DbContextOptionsBuilder<BusinessDb>().UsePostgres("Host=localhost;Database=unused").Options);

    [Theory]
    [InlineData("2026-10-04T12:00:00.1234567+02:00", "2026-10-04T10:00:00.1234560Z")]
    [InlineData("2026-10-04T12:00:00.1234560+02:00", "2026-10-04T10:00:00.1234560Z")]
    [InlineData("2026-10-04T12:00:00.1234567-03:30", "2026-10-04T15:30:00.1234560Z")]
    [InlineData("2026-10-04T12:00:00.1234567Z", "2026-10-04T12:00:00.1234560Z")]
    [InlineData("2026-10-04T12:00:00.1234560Z", "2026-10-04T12:00:00.1234560Z")]
    public void NormalizePreservesInstantAndProducesUtcMicroseconds(string input, string expected)
    {
        using var db = Model(); var type = db.Model.FindEntityType(typeof(Operation))!;
        var row = new Operation { Id = Guid.NewGuid(), Time = DateTimeOffset.Parse(input) };
        HistoryProtection.Normalize(type, row);
        Assert.Equal(DateTimeOffset.Parse(expected), row.Time);
        Assert.Equal(TimeSpan.Zero, row.Time.Offset);
        var fingerprint = HistoryProtection.RowFingerprint(type, row);
        HistoryProtection.Normalize(type, row);
        Assert.Equal(fingerprint, HistoryProtection.RowFingerprint(type, row));
    }

    [Fact] public void NullableTimesNormalizeAndNullsStayNull()
    {
        using var db = Model(); var type = db.Model.FindEntityType(typeof(SyncPeer))!;
        var row = new SyncPeer { Name = "BRANCH", LastAttempt = DateTimeOffset.Parse("2026-10-04T12:00:00.1234567+02:00") };
        HistoryProtection.Normalize(type, row);
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T10:00:00.1234560Z"), row.LastAttempt);
        Assert.Equal(TimeSpan.Zero, row.LastAttempt!.Value.Offset);
        Assert.Null(row.LastSuccess);
    }

    [Fact] public void PostgresMigrationMatchesCurrentModel()
    {
        using var db = Model(); Assert.False(db.Database.HasPendingModelChanges());
    }

    [WindowsFact] public async Task LauncherPreservesSpecialCharactersWithoutChangingConnectionOptions()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SierraBusiness.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var helper = Path.Combine(root.FullName, "scripts", "head-office-connection.ps1").Replace("'", "''");
        // Synthetic values only; no stored credentials or network connections are used.
        var passwords = new[] { "normal-demo", "demo;password", "demo;Timeout=1", "both\"and'quotes;=", " space at both ends ", "dollar$backtick`backslash\\", "café;秘密" };
        var json = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(passwords)));
        var script = $"$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'\n. '{helper}'\n$values = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{json}')) | ConvertFrom-Json\n$results = @($values | ForEach-Object {{ New-SierraPostgresConnectionString -DatabaseHost 'example.invalid' -Username 'postgres.demo' -Password $_ }})\n[Console]::Write([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $results -Compress))))";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var stderr = await errors;
        Assert.True(process.ExitCode == 0, stderr); Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        var strings = JsonSerializer.Deserialize<string[]>(Encoding.UTF8.GetString(Convert.FromBase64String((await output).Trim())))!;
        Assert.Equal(passwords.Length, strings.Length);
        for (var i = 0; i < passwords.Length; i++)
        {
            var parsed = new NpgsqlConnectionStringBuilder(strings[i]);
            Assert.Equal(passwords[i], parsed.Password); Assert.Equal("example.invalid", parsed.Host); Assert.Equal("postgres.demo", parsed.Username);
            Assert.Equal("postgres", parsed.Database); Assert.Equal(5432, parsed.Port); Assert.Equal(SslMode.Require, parsed.SslMode);
            Assert.Equal(30, parsed.Timeout); Assert.Equal(120, parsed.CommandTimeout); Assert.Equal(30, parsed.KeepAlive);
        }
    }
    private sealed class WindowsFactAttribute : FactAttribute { public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "The launcher runs in Windows PowerShell."; } }
}
