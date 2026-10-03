using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SierraBusiness.Application;

namespace SierraBusiness.Infrastructure;

public sealed class IdentityActorValidator(IServiceScopeFactory scopes)
{
    public async Task<Actor> Validate(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException("Sign in again.");
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByIdAsync(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
        var stamp = principal.FindFirstValue("AspNet.Identity.SecurityStamp");
        if (user is null || stamp is null || await users.IsLockedOutAsync(user) || stamp != await users.GetSecurityStampAsync(user))
            throw new UnauthorizedAccessException("Your session is no longer valid. Sign in again.");
        var claims = await users.GetClaimsAsync(user);
        return new Actor(user.Id, claims.FirstOrDefault(x => x.Type == "branch")?.Value ?? "", claims.Where(x => x.Type == "permission").Select(x => x.Value).ToHashSet());
    }
}
