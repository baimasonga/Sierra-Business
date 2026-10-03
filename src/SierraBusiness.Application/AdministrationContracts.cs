namespace SierraBusiness.Application;
public sealed record UserView(string Id, string Email, bool Locked, IReadOnlyList<string> Permissions);
public sealed record CreateUserCommand(string Email, string Password, List<string> Permissions);
public sealed record UpdateUserCommand(string UserId, bool Disabled, List<string> Permissions);
public interface IUserAdministration
{
    Task<IReadOnlyList<UserView>> Read();
    Task<string> Create(CreateUserCommand input);
    Task Update(UpdateUserCommand input);
    Task ResetPassword(string userId, string password);
}
public static class BusinessPermissions
{
    public static readonly string[] All = ["business.read", "catalogue.write", "sales.create", "inventory.receive", "documents.reverse", "accounting.read", "accounting.write", "audit.read", "backups.create", "users.manage", "pricing.manage", "tills.use", "tills.manage"];
    public static readonly IReadOnlyDictionary<string, string[]> Presets = new Dictionary<string, string[]> {
        ["Owner"] = All,
        ["Accountant"] = ["business.read", "accounting.read", "accounting.write", "documents.reverse", "audit.read", "backups.create", "tills.use", "tills.manage"],
        ["Sales clerk"] = ["business.read", "sales.create", "tills.use"],
        ["Stock clerk"] = ["business.read", "inventory.receive"],
        ["Read only"] = ["business.read"]
    };
}


