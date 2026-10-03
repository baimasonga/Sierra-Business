using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using Xunit;
namespace SierraBusiness.IntegrationTests;

/// <summary>Simulates someone editing the database file directly, bypassing the application.</summary>
public sealed class IntegrityTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"sierra-integrity-{Guid.NewGuid():N}.db");
    private Factory factory = null!; private BusinessService business = null!; private IntegrityService integrity = null!;
    private readonly Session session = new(); private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid product, sale;
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        business = new(factory, Options.Create(new TradingOptions())); integrity = new(factory, session, Options.Create(new TradingOptions()));
        product = await business.AddProduct(session.Actor, new(Guid.NewGuid(), "PAPER", "Paper", "Office", "ream", 100, TrackingMode.Quantity, MinimumPrice: 1));
        await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Receipt, "Supplier", today, [new(product, 10, 60)], null));
        sale = await business.Post(session.Actor, new(Guid.NewGuid(), DocumentKind.Sale, "Walk-in", today, [new(product, 2, 100)], PaymentMethod.Cash));
    }
    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }

    private async Task Sql(string command) { await using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); await connection.OpenAsync(); await using var cmd = connection.CreateCommand(); cmd.CommandText = command; await cmd.ExecuteNonQueryAsync(); }
    private Task RemoveGuards() => Sql(string.Join(';', new[] { "Movements", "Documents", "Journal", "IntegritySeals" }.SelectMany(t => new[] { $"DROP TRIGGER IF EXISTS sb_protect_{t}_update", $"DROP TRIGGER IF EXISTS sb_protect_{t}_delete" })));

    [Fact] public async Task UntouchedHistoryVerifiesIncludingSeededAccounts()
    {
        var report = await integrity.Verify();
        Assert.True(report.Ok, string.Join("\n", report.Problems));
        Assert.True(report.DatabaseGuards); Assert.True(report.Seals >= 4); Assert.Equal(16, report.Head.Length);
        Assert.Contains(report.Notes, x => x.StartsWith("Protection started", StringComparison.Ordinal)); // migration seed rows are covered by the baseline seal
    }

    [Fact] public async Task DatabaseGuardsBlockEditsAndDeletesFromOutsideTools()
    {
        var edit = await Assert.ThrowsAsync<SqliteException>(() => Sql($"UPDATE Movements SET Quantity = 99 WHERE DocumentId = '{sale.ToString().ToUpperInvariant()}'"));
        Assert.Contains("immutable", edit.Message);
        await Assert.ThrowsAsync<SqliteException>(() => Sql("DELETE FROM Journal"));
        await Assert.ThrowsAsync<SqliteException>(() => Sql("DELETE FROM IntegritySeals"));
        Assert.True((await integrity.Verify()).Ok);
    }

    [Fact] public async Task EditingASaleAfterRemovingGuardsIsDetected()
    {
        await RemoveGuards();
        await Sql("UPDATE Documents SET Total = 1 WHERE Kind = 1");
        var report = await integrity.Verify();
        Assert.False(report.Ok); Assert.False(report.DatabaseGuards);
        Assert.Contains(report.Problems, x => x.Contains("Documents") && x.Contains("changed after it was posted"));
    }

    [Fact] public async Task DeletingAndInsertingRowsIsDetected()
    {
        await RemoveGuards();
        await Sql("DELETE FROM Journal WHERE rowid = (SELECT MIN(rowid) FROM Journal)");
        await Sql($"INSERT INTO Movements (Id, DocumentId, ProductId, Branch, Location, Quantity, Value) VALUES ('{Guid.NewGuid().ToString().ToUpperInvariant()}', '{sale.ToString().ToUpperInvariant()}', '{product.ToString().ToUpperInvariant()}', 'FREETOWN', 'MAIN', '5.0', '300.0')");
        var problems = (await integrity.Verify()).Problems;
        Assert.Contains(problems, x => x.Contains("Journal") && x.Contains("was deleted"));
        Assert.Contains(problems, x => x.Contains("Movements") && x.Contains("added outside the application"));
    }

    [Fact] public async Task RewritingTheSealChainIsDetected()
    {
        await RemoveGuards();
        await Sql("DELETE FROM IntegritySeals WHERE Sequence = 2");
        Assert.Contains((await integrity.Verify()).Problems, x => x.Contains("sequence is broken") || x.Contains("does not link"));
    }

    [Fact] public async Task UpgradeReinstatesGuardsAndKeepsHistoryVerified()
    {
        await RemoveGuards();
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var report = await integrity.Verify();
        Assert.True(report.Ok, string.Join("\n", report.Problems)); Assert.True(report.DatabaseGuards);
        await Assert.ThrowsAsync<SqliteException>(() => Sql("DELETE FROM Movements"));
    }

    [Fact] public async Task ReversalsAndMultiStepWorkflowsStayVerified()
    {
        await business.Reverse(session.Actor, new(Guid.NewGuid(), sale, "Customer cancelled"));
        var management = new InventoryManagement(factory, session, Options.Create(new TradingOptions()));
        var count = await management.SubmitCount(new(Guid.NewGuid(), "Shelf", [new(Guid.NewGuid(), product, "MAIN", 10, 9, 0, "Missing")]));
        await management.DecideCount(new(Guid.NewGuid(), count, true, "Checked"));
        Assert.True((await integrity.Verify()).Ok);
    }

    [Fact] public async Task VerificationRequiresAuditPermission()
    {
        session.Actor = session.Actor with { Permissions = new HashSet<string> { "business.read" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => integrity.Verify());
    }

    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
