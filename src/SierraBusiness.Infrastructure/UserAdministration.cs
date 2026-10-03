using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public sealed class UserAdministration(IServiceScopeFactory scopes, IActorSession session, IOptions<TradingOptions> options) : IUserAdministration
{
    private async Task<Actor> Authorize()
    {
        var actor = await session.CurrentActor(); actor.Demand("users.manage"); if (actor.Branch != options.Value.Branch) throw new UnauthorizedAccessException("Wrong branch."); return actor;
    }
    public async Task<IReadOnlyList<UserView>> Read()
    {
        var actor = await Authorize(); using var scope = scopes.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>(); var db = scope.ServiceProvider.GetRequiredService<BusinessDb>();
        var ids = await db.UserClaims.Where(x => x.ClaimType == "branch" && x.ClaimValue == actor.Branch).Select(x => x.UserId).ToListAsync(); var list = new List<UserView>();
        foreach (var user in await users.Users.Where(x => ids.Contains(x.Id)).ToListAsync()) list.Add(new(user.Id, user.Email ?? user.UserName ?? "", await users.IsLockedOutAsync(user), (await users.GetClaimsAsync(user)).Where(x => x.Type == "permission").Select(x => x.Value).ToArray()));
        return list.OrderBy(x => x.Email).ToArray();
    }
    public async Task<string> Create(CreateUserCommand input)
    {
        var actor = await Authorize(); ValidatePermissions(input.Permissions); var email = Rules.Required(input.Email, "Email", 160);
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _)) throw new BusinessException("Enter a valid email address.");
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BusinessDb>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>(); await using var tx = await db.Database.BeginTransactionAsync();
        var user = new IdentityUser(email) { Email = email, EmailConfirmed = true, LockoutEnabled = true };
        Check(await users.CreateAsync(user, input.Password)); Check(await users.AddClaimsAsync(user, input.Permissions.Distinct().Select(p => new Claim("permission", p)).Append(new Claim("branch", actor.Branch))));
        Audit(db, actor, "user.create", user.Id, new { email, input.Permissions }); await db.SaveChangesAsync(); await tx.CommitAsync(); return user.Id;
    }
    public async Task Update(UpdateUserCommand input)
    {
        var actor = await Authorize(); ValidatePermissions(input.Permissions);
        if (input.UserId == actor.Id && (input.Disabled || !input.Permissions.Contains("users.manage"))) throw new BusinessException("You cannot disable your own account or remove your own administration permission.");
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BusinessDb>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>(); await using var tx = await db.Database.BeginTransactionAsync();
        var user = await Find(users, input.UserId, actor.Branch); var current = await users.GetClaimsAsync(user);
        Check(await users.RemoveClaimsAsync(user, current.Where(x => x.Type == "permission"))); Check(await users.AddClaimsAsync(user, input.Permissions.Distinct().Select(x => new Claim("permission", x))));
        Check(await users.SetLockoutEnabledAsync(user, true)); Check(await users.SetLockoutEndDateAsync(user, input.Disabled ? DateTimeOffset.MaxValue : null)); Check(await users.ResetAccessFailedCountAsync(user)); Check(await users.UpdateSecurityStampAsync(user));
        Audit(db, actor, "user.permissions", user.Id, new { input.Disabled, input.Permissions }); await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task ResetPassword(string userId, string password)
    {
        var actor = await Authorize(); using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BusinessDb>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>(); await using var tx = await db.Database.BeginTransactionAsync();
        var user = await Find(users, userId, actor.Branch);
        // Both writes are in one transaction; failed password validation rolls back password removal.
        Check(await users.RemovePasswordAsync(user)); Check(await users.AddPasswordAsync(user, password)); Check(await users.UpdateSecurityStampAsync(user));
        Audit(db, actor, "user.password-reset", user.Id, new { PasswordChanged = true }); await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    private static async Task<IdentityUser> Find(UserManager<IdentityUser> users, string id, string branch)
    {
        var user = await users.FindByIdAsync(id) ?? throw new BusinessException("User not found."); if (!(await users.GetClaimsAsync(user)).Any(x => x.Type == "branch" && x.Value == branch)) throw new UnauthorizedAccessException("User belongs to another branch."); return user;
    }
    private static void ValidatePermissions(List<string> permissions) { if (permissions is null || permissions.Count == 0 || permissions.Any(x => !BusinessPermissions.All.Contains(x)) || !permissions.Contains("business.read")) throw new BusinessException("Select valid permissions including business.read."); if (permissions.Contains("accounting.write") && !permissions.Contains("accounting.read")) throw new BusinessException("Accounting write requires accounting read."); }
    private static void Check(IdentityResult result) { if (!result.Succeeded) throw new BusinessException(string.Join(" ", result.Errors.Select(x => x.Description))); }
    private static void Audit<T>(BusinessDb db, Actor actor, string action, string userId, T detail) => db.Operations.Add(new() { Id = Guid.NewGuid(), ResultId = Guid.TryParse(userId, out var id) ? id : Guid.Empty, Branch = actor.Branch, Actor = actor.Id, Action = action, Time = DateTimeOffset.UtcNow, Payload = JsonSerializer.Serialize(new { UserId = userId, Detail = detail }), Fingerprint = "admin-audit" });
}
