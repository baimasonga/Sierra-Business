using SierraBusiness.Application;

namespace SierraBusiness.Desktop;

public sealed class DesktopBackupService(LocalAccountService session, DesktopStore store)
{
    public async Task<string> Create(Actor requested)
    {
        var current = await session.CurrentActor();
        if (current.Id != requested.Id || current.Branch != requested.Branch || !current.Permissions.Contains("backups.create"))
            throw new UnauthorizedAccessException("Your account cannot create database backups.");
        return await store.CreateBackup();
    }
}
