using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.SharedUI;
using Xunit;
namespace SierraBusiness.IntegrationTests;

public sealed class BankingProductivityTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "rokela-banking-depth-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly Session session = new(); private Factory factory = null!; private FinanceService finance = null!; private BankingService banking = null!; private BankingExtrasService extras = null!;
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task InitializeAsync()
    {
        factory = new(new DbContextOptionsBuilder<BusinessDb>().UseSqlite("Data Source=" + path + ";Pooling=False").Options);
        await using (var db = factory.CreateDbContext()) await HistoryProtection.MigrateAsync(db);
        var options = Options.Create(new TradingOptions()); finance = new(factory, session, options); banking = new(factory, session, options); extras = new(factory, session, options);
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); return Task.CompletedTask; }
    private async Task<StatementRowView> Row(decimal amount)
    {
        await banking.Import(new(Guid.NewGuid(), 1010, Guid.NewGuid().ToString(), 100, 100 + amount, $"Date,Reference,Amount\n{today:yyyy-MM-dd},Bank fee,{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        return (await banking.Read()).Rows.Last();
    }
    [Fact] public async Task StatementVoucherCommitsMatchAndRejectsCancelledMatchReuse()
    {
        var row = await Row(-20); var command = new VoucherCommand(Guid.NewGuid(), VoucherType.Payment, today, "Reviewed bank fee", [new(1010, 0, 20), new(5100, 20, 0)], [], StatementRowId: row.Id);
        var id = await finance.PostVoucher(command); Assert.Equal(id, await finance.PostVoucher(command));
        var match = (await banking.Read()).Rows.Single(r => r.Id == row.Id).MatchId; Assert.NotNull(match);
        await finance.CancelMatch(new(Guid.NewGuid(), match!.Value, "Check the match"));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(command with { OperationId = Guid.NewGuid() }));
        await using var db = factory.CreateDbContext(); Assert.Equal(1, await db.Documents.CountAsync(d => d.Id == id)); Assert.True((await HistoryProtection.Verify(db)).Ok);
    }
    [Fact] public async Task StatementVoucherRejectsWrongAmountAndVoidedOrOtherBranchRowsWithoutPosting()
    {
        var row = await Row(-20); var command = new VoucherCommand(Guid.NewGuid(), VoucherType.Payment, today, "Fee", [new(1010, 0, 19), new(5100, 19, 0)], [], StatementRowId: row.Id);
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(command)); await using (var db = factory.CreateDbContext()) Assert.Empty(await db.Documents.ToListAsync());
        await banking.VoidStatement(new(Guid.NewGuid(), row.StatementId, "Wrong import"));
        await Assert.ThrowsAsync<BusinessException>(() => finance.PostVoucher(command with { Lines = [new(1010,0,20),new(5100,20,0)] }));
        session.Actor = new("owner", "OTHER", BusinessPermissions.All.ToHashSet()); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => finance.PostVoucher(command));
    }
    [Fact] public async Task ChequeBooksReserveNumbersAndChequePaymentIsAtomic()
    {
        var book = new ChequeBookCommand(Guid.NewGuid(), null, 0, 1010, "First book", "", 1, 25, 6, true); var bookId = await extras.SaveChequeBook(book);
        await Assert.ThrowsAsync<BusinessException>(() => extras.SaveChequeBook(book with { OperationId = Guid.NewGuid(), First = 20, Last = 40 }));
        var command = new IssueChequeCommand(Guid.NewGuid(), 1010, "Supplier", "000001", today, 10, [], 5100, BookId: bookId);
        var cheque = await extras.IssueCheque(command); Assert.Equal(cheque, await extras.IssueCheque(command));
        await Assert.ThrowsAsync<BusinessException>(() => extras.IssueCheque(command with { Amount = 11 }));
        Assert.Equal("000002", Assert.Single(await extras.ChequeBooks()).NextNumber);
        await Assert.ThrowsAsync<BusinessException>(() => extras.IssueCheque(command with { OperationId = Guid.NewGuid(), ChequeNumber = "000100" }));
        var invalid = command with { OperationId = Guid.NewGuid(), ChequeNumber = "000002", ExpenseAccount = null, Allocations = [new(Guid.NewGuid(),10)] };
        await Assert.ThrowsAsync<BusinessException>(() => extras.IssueCheque(invalid));
        await using (var db = factory.CreateDbContext()) { Assert.Single(await db.IssuedCheques.ToListAsync()); Assert.Single(await db.Documents.ToListAsync()); Assert.True((await HistoryProtection.Verify(db)).Ok); }
        await extras.SaveChequeBook(book with { OperationId = Guid.NewGuid(), Id = bookId, ExpectedVersion = 1, Active = false });
        await Assert.ThrowsAsync<BusinessException>(() => extras.IssueCheque(command with { OperationId = Guid.NewGuid(), ChequeNumber = "000002" }));
    }
    [Fact] public async Task PrintingRejectsWrongBanksCancelledChequesAndStaleLayouts()
    {
        var point = new ChequePoint(10,10,60); var layout = new ChequeLayoutCommand(Guid.NewGuid(), null, 0, 1010, "Bank layout", 210, 80, 10, point, point, point, point, point);
        var layoutId = await extras.SaveChequeLayout(layout);
        await Assert.ThrowsAsync<BusinessException>(() => extras.SaveChequeLayout(layout with { OperationId = Guid.NewGuid(), Payee = new(200,10,60) }));
        var id = await extras.IssueCheque(new(Guid.NewGuid(),1010,"Supplier","PRINT-1",today,10,[],5100));
        Assert.True((await extras.PreviewChequePrint(new(layoutId,[id],true,1))).Test);
        await extras.SaveChequeLayout(layout with { OperationId = Guid.NewGuid(), Id = layoutId, ExpectedVersion = 1, Name = "Corrected layout" });
        await Assert.ThrowsAsync<BusinessException>(() => extras.PreviewChequePrint(new(layoutId,[id],false,1)));
        await extras.RecordChequePrint(new(Guid.NewGuid(),new(layoutId,[id],true,2)));
        await extras.ActOnIssuedCheque(new(Guid.NewGuid(),id,IssuedChequeState.Cancelled,today,"Stop cheque"));
        await Assert.ThrowsAsync<BusinessException>(() => extras.PreviewChequePrint(new(layoutId,[id],false,2)));
        session.Actor = new("reader","FREETOWN",new HashSet<string> { "business.read","accounting.read" }); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => extras.SaveChequeLayout(layout with { OperationId = Guid.NewGuid() }));
    }
    [Fact] public async Task StatementVoucherScreenRequiresReviewAndPostsMatchedVoucher()
    {
        var row = await Row(-20); using var context = new BunitContext(); context.Services.AddSingleton<IBankingService>(banking); context.Services.AddSingleton<IFinanceService>(finance); context.JSInterop.Mode = JSRuntimeMode.Loose;
        var screen = context.Render<StatementVoucherWorkspace>(); screen.WaitForAssertion(() => Assert.Contains("Bank fee", screen.Markup));
        screen.FindAll("select")[0].Change(row.Id.ToString()); screen.FindAll("select")[1].Change("5100"); screen.Find("input").Change("Reviewed fee");
        screen.FindAll("button").Single(b => b.TextContent == "Review statement voucher").Click(); screen.WaitForAssertion(() => Assert.Contains("Post voucher and match",screen.Markup));
        screen.FindAll("button").Single(b => b.TextContent == "Post voucher and match").Click(); screen.WaitForAssertion(() => Assert.Contains("posted together",screen.Markup)); Assert.NotNull((await banking.Read()).Rows.Single(r => r.Id == row.Id).MatchId);
    }
    private sealed class Session : IActorSession { public Actor Actor = new("owner", "FREETOWN", BusinessPermissions.All.ToHashSet()); public Task<Actor> CurrentActor() => Task.FromResult(Actor); }
    private sealed class Factory(DbContextOptions<BusinessDb> options) : IDbContextFactory<BusinessDb> { public BusinessDb CreateDbContext() => new(options); }
}
