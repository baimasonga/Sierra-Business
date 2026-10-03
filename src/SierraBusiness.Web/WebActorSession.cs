using Microsoft.AspNetCore.Components.Authorization;
using SierraBusiness.Application;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Web;
public sealed class WebActorSession(IHttpContextAccessor http, AuthenticationStateProvider authentication, IdentityActorValidator validator) : IActorSession
{
    public async Task<Actor> CurrentActor()
    {
        var principal = http.HttpContext?.User ?? (await authentication.GetAuthenticationStateAsync()).User;
        if (!long.TryParse(principal.FindFirst("session.expires")?.Value, out var deadline) || DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= deadline) throw new UnauthorizedAccessException("Your session has ended. Sign in again.");
        return await validator.Validate(principal);
    }
}
