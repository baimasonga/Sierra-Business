using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed partial class BankingExtrasService
{
    private static bool InBook(ChequeBookCommand book, string number, out int value)
    {
        value = 0; return number.StartsWith(book.Prefix, StringComparison.Ordinal) && number.Length == book.Prefix.Length + book.Digits
            && int.TryParse(number.AsSpan(book.Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= book.First && value <= book.Last;
    }
    private static async Task<List<(Guid Id, int Version, ChequeBookCommand Input)>> Books(BusinessDb db, string branch)
    {
        var ops = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == "cheque.book").ToListAsync();
        return ops.Select(o => JsonSerializer.Deserialize<ChequeBookCommand>(o.Payload)!).GroupBy(b => b.Id ?? b.OperationId)
            .Select(g => { var b = g.OrderByDescending(b => b.ExpectedVersion).First(); return (g.Key, b.ExpectedVersion + 1, b); }).ToList();
    }
    internal static async Task ValidateBook(BusinessDb db, Actor actor, IssueChequeCommand input, string number)
    {
        var books = await Books(db, actor.Branch);
        var candidates = books.Where(b => b.Input.BankAccount == input.BankAccount && InBook(b.Input, number, out _)).ToArray();
        if (input.BookId is Guid id && !candidates.Any(b => b.Id == id && b.Input.Active)) throw new BusinessException("Choose an active cheque book and an unused number within its range.");
        if (candidates.Length > 0 && !candidates.Any(b => b.Input.Active)) throw new BusinessException("That cheque number belongs to a closed cheque book.");
    }
    public async Task<IReadOnlyList<ChequeBookView>> ChequeBooks()
    {
        var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync();
        var cheques = await db.IssuedCheques.AsNoTracking().Where(c => c.Branch == actor.Branch).ToListAsync();
        return (await Books(db, actor.Branch)).Select(b =>
        {
            var used = cheques.Where(c => c.BankAccount == b.Input.BankAccount && InBook(b.Input, c.Number, out _)).Select(c => int.Parse(c.Number[b.Input.Prefix.Length..], CultureInfo.InvariantCulture)).ToHashSet();
            int? next = null; for (var i = b.Input.First; i <= b.Input.Last; i++) if (!used.Contains(i)) { next = i; break; }
            return new ChequeBookView(b.Id, b.Version, b.Input.BankAccount, b.Input.Name, b.Input.Prefix, b.Input.First, b.Input.Last, b.Input.Digits, b.Input.Active, used.Count, next is int n && b.Input.Active ? b.Input.Prefix + n.ToString("D" + b.Input.Digits, CultureInfo.InvariantCulture) : null);
        }).OrderBy(b => b.BankAccount).ThenBy(b => b.Name).ToArray();
    }
    public async Task<Guid> SaveChequeBook(ChequeBookCommand input)
    {
        var actor = await Authorize("accounting.write");
        return await Commands.Run(factory, actor, input.OperationId, "cheque.book", input, async db =>
        {
            var name = Rules.Required(input.Name, "Cheque book name", 80); var prefix = (input.Prefix ?? "").Trim().ToUpperInvariant();
            if (prefix != input.Prefix || prefix.Length > 8 || prefix.Any(c => !char.IsAsciiLetterOrDigit(c)) || input.First < 1 || input.Last < input.First || input.Last > 99999999 || input.Last - input.First > 99999 || input.Digits is < 1 or > 8 || input.Last.ToString(CultureInfo.InvariantCulture).Length > input.Digits) throw new BusinessException("Use an uppercase alphanumeric prefix, a positive range of at most 100,000 numbers and 1–8 digits wide enough for its last number.");
            if (!await db.LedgerAccounts.AnyAsync(a => a.Id == input.BankAccount && a.CashOrBank && a.Id != 1000)) throw new BusinessException("Choose a bank ledger.");
            var books = await Books(db, actor.Branch); var id = input.Id ?? input.OperationId; var old = books.SingleOrDefault(b => b.Id == id);
            if (input.Id is not null && (old.Input is null || old.Version != input.ExpectedVersion) || input.Id is null && input.ExpectedVersion != 0) throw new BusinessException("The cheque book changed. Refresh it before saving.");
            if (old.Input is not null && (old.Input.BankAccount != input.BankAccount || old.Input.Prefix != prefix || old.Input.First != input.First || old.Input.Last != input.Last || old.Input.Digits != input.Digits)) throw new BusinessException("Keep an existing book's bank, prefix, range and digits unchanged. Create a different book instead.");
            if (input.Active && books.Any(b => b.Id != id && b.Input.Active && b.Input.BankAccount == input.BankAccount && b.Input.Prefix == prefix && b.Input.Digits == input.Digits && b.Input.First <= input.Last && b.Input.Last >= input.First)) throw new BusinessException("This range overlaps another active cheque book.");
            return id;
        });
    }
    private static async Task<List<ChequeLayoutView>> Layouts(BusinessDb db, string branch)
    {
        var ops = await db.Operations.AsNoTracking().Where(o => o.Branch == branch && o.Action == "cheque.layout").ToListAsync();
        return ops.Select(o => JsonSerializer.Deserialize<ChequeLayoutCommand>(o.Payload)!).GroupBy(l => l.Id ?? l.OperationId).Select(g => { var l = g.OrderByDescending(l => l.ExpectedVersion).First(); return new ChequeLayoutView(g.Key, l.ExpectedVersion + 1, l); }).ToList();
    }
    public async Task<IReadOnlyList<ChequeLayoutView>> ChequeLayouts() { var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync(); return await Layouts(db, actor.Branch); }
    public async Task<Guid> SaveChequeLayout(ChequeLayoutCommand input)
    {
        var actor = await Authorize("accounting.write");
        return await Commands.Run(factory, actor, input.OperationId, "cheque.layout", input, async db =>
        {
            Rules.Required(input.Name, "Cheque layout name", 80);
            if (input.Width is < 100 or > 300 || input.Height is < 50 or > 150 || input.FontSize is < 8 or > 18) throw new BusinessException("Use cheque dimensions of 100–300 by 50–150 mm and text of 8–18 points.");
            if (new[] { input.Date, input.Payee, input.Amount, input.Words, input.Number }.Any(p => p is null || p.X < 0 || p.Y < 0 || p.Width <= 0 || p.X + p.Width > input.Width || p.Y + 12 > input.Height)) throw new BusinessException("Position every field inside the cheque, leaving at least 12 mm below it.");
            if (!await db.LedgerAccounts.AnyAsync(a => a.Id == input.BankAccount && a.CashOrBank && a.Id != 1000)) throw new BusinessException("Choose a bank ledger.");
            var layouts = await Layouts(db, actor.Branch); var id = input.Id ?? input.OperationId; var old = layouts.SingleOrDefault(l => l.Id == id);
            if (input.Id is not null && (old is null || old.Version != input.ExpectedVersion) || input.Id is null && input.ExpectedVersion != 0) throw new BusinessException("The cheque layout changed. Refresh before saving.");
            return id;
        });
    }
    private async Task<ChequePrintPreview> PrintPlan(ChequePrintRequest input)
    {
        if (input.ChequeIds is null || input.ChequeIds.Count is < 1 or > 25 || input.ChequeIds.Distinct().Count() != input.ChequeIds.Count) throw new BusinessException("Choose 1–25 different issued cheques.");
        var layout = (await ChequeLayouts()).SingleOrDefault(l => l.Id == input.LayoutId) ?? throw new BusinessException("Choose an existing cheque layout.");
        if (input.ExpectedLayoutVersion is int version && version != layout.Version) throw new BusinessException("The print layout changed. Review the cheque preview again.");
        var all = await IssuedCheques(); var selected = input.ChequeIds.Select(id => all.SingleOrDefault(c => c.Id == id) ?? throw new BusinessException("Cheque not found in this branch.")).ToArray();
        if (selected.Any(c => c.BankAccount != layout.Layout.BankAccount || c.State != IssuedChequeState.Issued)) throw new BusinessException("Print only unpresented, uncancelled cheques from the layout's bank.");
        return new(layout, selected, input.Test);
    }
    public async Task<ChequePrintPreview> PreviewChequePrint(ChequePrintRequest input) { await Authorize("accounting.read"); return await PrintPlan(input); }
    public async Task<Guid> RecordChequePrint(ChequePrintCommand input)
    {
        var actor = await Authorize("accounting.write"); await PrintPlan(input.Request);
        return await Commands.Run(factory, actor, input.OperationId, "cheque.print", input, db => Task.FromResult(input.OperationId));
    }
}
