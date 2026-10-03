using SierraBusiness.Application;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

/// <summary>The native host derives current authorization; stale UI claims never authorize a command.</summary>
public sealed class SessionBusinessService(LocalAccountService session, BusinessService inner) : IBusinessService
{
    private async Task<Actor> Authorize(Actor requested)
    {
        var current = await session.CurrentActor();
        if (current.Id != requested.Id || current.Branch != requested.Branch) throw new UnauthorizedAccessException("Sign in to the assigned branch again.");
        return current;
    }
    public async Task<BusinessSnapshot> Read(Actor actor, CancellationToken cancellationToken = default) => await inner.Read(await Authorize(actor), cancellationToken);
    public async Task<Guid> AddProduct(Actor actor, ProductInput input, CancellationToken cancellationToken = default) => await inner.AddProduct(await Authorize(actor), input, cancellationToken);
    public async Task<Guid> Post(Actor actor, TradeCommand input, CancellationToken cancellationToken = default) => await inner.Post(await Authorize(actor), input, cancellationToken);
    public async Task<Guid> Reverse(Actor actor, ReverseCommand input, CancellationToken cancellationToken = default) => await inner.Reverse(await Authorize(actor), input, cancellationToken);
}
