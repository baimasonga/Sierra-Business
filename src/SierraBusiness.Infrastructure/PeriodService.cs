using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed class PeriodClosure { public Guid Id { get; set; } public string Branch { get; set; } = ""; public DateOnly Through { get; set; } public string Reason { get; set; } = ""; }
public sealed class SalesTaxRate { public Guid Id { get; set; } public string Branch { get; set; } = ""; public DateOnly From { get; set; } public decimal Rate { get; set; } public string Description { get; set; } = ""; }
public static class PeriodControls
{
    public static async Task<DateOnly> ClosedThrough(BusinessDb db, string branch, DateOnly configured)
    {
        var dates = await db.PeriodClosures.Where(x => x.Branch == branch).Select(x => x.Through).ToListAsync(); return dates.Append(configured).Max();
    }
}
public sealed class PeriodService(IDbContextFactory<BusinessDb> factory, IActorSession session, IOptions<TradingOptions> options) : IPeriodService
{
    private async Task<Actor> Authorize(bool write = false) { var actor = await session.CurrentActor(); actor.Demand("accounting.read"); if (write) actor.Demand("accounting.write"); if (actor.Branch != options.Value.Branch) throw new UnauthorizedAccessException("Wrong branch."); return actor; }
    public async Task<PeriodStatus> Read()
    {
        var actor = await Authorize(); await using var db = await factory.CreateDbContextAsync();
        var rates = await db.SalesTaxRates.AsNoTracking().Where(x => x.Branch == actor.Branch).OrderByDescending(x => x.From).Select(x => new TaxRateView(x.From, x.Rate, x.Description)).ToListAsync();
        rates.Add(new(DateOnly.MinValue, options.Value.TaxRate, "Configured fallback before effective-dated rates"));
        return new(await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough), rates);
    }
    public Task Close(ClosePeriodCommand input) => Execute(input.OperationId, "period.close", input, async (db, actor) =>
    {
        var closed = await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough);
        if (input.Through <= closed || input.Through >= DateOnly.FromDateTime(DateTime.UtcNow)) throw new BusinessException("Close through a date after the current lock and before today. Period closure cannot be undone in this release.");
        db.PeriodClosures.Add(new() { Id = input.OperationId, Branch = actor.Branch, Through = input.Through, Reason = Rules.Required(input.Reason, "Closure reason") });
    });
    public Task SetTax(TaxRateCommand input) => Execute(input.OperationId, "tax-rate.create", input, async (db, actor) =>
    {
        if (input.Rate is < 0 or > 1 || decimal.Round(input.Rate, 4) != input.Rate) throw new BusinessException("Tax rate must be between 0 and 1 with up to four decimals.");
        if (input.From <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough) || await db.Documents.AnyAsync(x => x.Branch == actor.Branch && x.BusinessDate >= input.From)) throw new BusinessException("A tax rate can start only in an open period after all existing document dates. Previously posted tax is unchanged.");
        if (await db.SalesTaxRates.AnyAsync(x => x.Branch == actor.Branch && x.From == input.From)) throw new BusinessException("A tax rate already starts on that date.");
        db.SalesTaxRates.Add(new() { Id = input.OperationId, Branch = actor.Branch, From = input.From, Rate = input.Rate, Description = Rules.Required(input.Description, "Tax description") });
    });
    private async Task Execute<T>(Guid operation, string action, T input, Func<BusinessDb, Actor, Task> work)
    {
        var actor = await Authorize(true); if (operation == Guid.Empty) throw new BusinessException("Operation ID is required."); var payload = JsonSerializer.Serialize(input);
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.Operations.SingleOrDefaultAsync(x => x.Id == operation); if (previous is not null) { if (previous.Actor != actor.Id || previous.Branch != actor.Branch || previous.Action != action || previous.Payload != payload) throw new BusinessException("Operation ID already used with different data or identity."); return; }
        await work(db, actor); db.Operations.Add(new() { Id = operation, ResultId = operation, Actor = actor.Id, Branch = actor.Branch, Action = action, Time = DateTimeOffset.UtcNow, Payload = payload, Fingerprint = "period-audit" }); await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
