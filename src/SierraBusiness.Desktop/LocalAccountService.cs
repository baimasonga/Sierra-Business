using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

/// <summary>Identity password hashes stay local. Sessions are in memory, expire, and recheck claims on every command.</summary>
public sealed class LocalAccountService(IServiceScopeFactory scopes, IOptions<TradingOptions> options, TimeProvider clock, DesktopStore? store = null) : IActorSession
{
    private readonly SemaphoreSlim setupGate = new(1, 1);
    private string? userId, securityStamp;
    private DateTimeOffset expires;
    private static readonly string[] OwnerPermissions = BusinessPermissions.All;

    public async Task<bool> HasOwner()
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().Users.AnyAsync();
    }

    public async Task CreateOwner(string email, string password, string confirm)
    {
        if (password != confirm) throw new BusinessException("The passwords do not match.");
        email = Rules.Required(email, "Email", 200);
        if (!System.Net.Mail.MailAddress.TryCreate(email, out var address) || address.Address != email) throw new BusinessException("Enter a valid email address.");
        await setupGate.WaitAsync();
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BusinessDb>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            if (await users.Users.AnyAsync()) throw new BusinessException("An owner already exists. Sign in instead.");
            var user = new IdentityUser(email) { Email = email, EmailConfirmed = true };
            Check(await users.CreateAsync(user, password));
            Check(await users.AddClaimsAsync(user, OwnerPermissions.Select(x => new Claim("permission", x)).Append(new Claim("branch", options.Value.Branch))));
            await transaction.CommitAsync();
        }
        finally { setupGate.Release(); }
    }

    public async Task<Actor> SignIn(string email, string password)
    {
        SignOut();
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null || await users.IsLockedOutAsync(user)) throw new BusinessException("Sign-in failed. Check your details or wait if your account is locked.");
        if (!await users.CheckPasswordAsync(user, password))
        {
            Check(await users.AccessFailedAsync(user));
            throw new BusinessException("Sign-in failed. Check your details or wait if your account is locked.");
        }
        Check(await users.ResetAccessFailedCountAsync(user));
        userId = user.Id; securityStamp = await users.GetSecurityStampAsync(user);
        expires = clock.GetUtcNow().AddHours(8);
        return await CurrentActor();
    }

    public async Task<Actor> CurrentActor()
    {
        if (userId is null || clock.GetUtcNow() >= expires) { SignOut(); throw new UnauthorizedAccessException("Your session has ended. Sign out and sign in again."); }
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByIdAsync(userId);
        if (user is null || await users.IsLockedOutAsync(user) || await users.GetSecurityStampAsync(user) != securityStamp)
        { SignOut(); throw new UnauthorizedAccessException("This session is no longer valid. Sign in again."); }
        var claims = await users.GetClaimsAsync(user);
        var permissions = claims.Where(x => x.Type == "permission").Select(x => x.Value).ToHashSet();
        if (store is not null && CompanyPeriodService.IsArchive(store.DirectoryPath)) permissions.RemoveWhere(p => !p.EndsWith(".read", StringComparison.Ordinal) && p != "backups.create");
        return new Actor(user.Id, claims.FirstOrDefault(x => x.Type == "branch")?.Value ?? "", permissions);
    }

    public void SignOut() { userId = securityStamp = null; expires = default; }
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new BusinessException(string.Join(" ", result.Errors.Select(x => x.Description)));
    }
}
