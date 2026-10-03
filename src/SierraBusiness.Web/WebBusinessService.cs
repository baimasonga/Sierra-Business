using Microsoft.AspNetCore.Components.Authorization;
using SierraBusiness.Application;
using SierraBusiness.Infrastructure;

namespace SierraBusiness.Web;

public sealed class WebBusinessService(IHttpContextAccessor http, AuthenticationStateProvider authentication, IdentityActorValidator validator, BusinessService inner) : IBusinessService
{
    private readonly DateTimeOffset expires = DateTimeOffset.UtcNow.AddHours(8);
    private async Task<Actor> Current(Actor requested)
    {
        if (DateTimeOffset.UtcNow >= expires) throw new UnauthorizedAccessException("Your session has ended. Sign in again.");
        var principal = http.HttpContext?.User ?? (await authentication.GetAuthenticationStateAsync()).User;
        if (!long.TryParse(principal.FindFirst("session.expires")?.Value, out var deadline) || DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= deadline)
            throw new UnauthorizedAccessException("Your session has ended. Sign in again.");
        var actor = await validator.Validate(principal);
        if (actor.Id != requested.Id || actor.Branch != requested.Branch) throw new UnauthorizedAccessException("Your account scope has changed. Sign in again.");
        return actor;
    }
    public async Task<BusinessSnapshot> Read(Actor actor, CancellationToken cancellationToken = default) => await inner.Read(await Current(actor), cancellationToken);
    public async Task<Guid> AddProduct(Actor actor, ProductInput input, CancellationToken cancellationToken = default) => await inner.AddProduct(await Current(actor), input, cancellationToken);
    public async Task<Guid> Post(Actor actor, TradeCommand input, CancellationToken cancellationToken = default) => await inner.Post(await Current(actor), input, cancellationToken);
    public async Task<Guid> Reverse(Actor actor, ReverseCommand input, CancellationToken cancellationToken = default) => await inner.Reverse(await Current(actor), input, cancellationToken);
}
