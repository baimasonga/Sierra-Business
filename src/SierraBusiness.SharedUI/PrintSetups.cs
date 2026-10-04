using Microsoft.Extensions.DependencyInjection;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.SharedUI;

/// <summary>Loads the company letterhead and document settings for printed documents. Printing never fails because the settings are missing.</summary>
public static class PrintSetups
{
    public static PrintSetup Default { get; } = new(new(), new());
    public static async Task<PrintSetup> Load(IServiceProvider services)
    {
        if (services.GetService<IComplianceService>() is not { } compliance) return Default;
        try { return await compliance.PrintSetup(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or BusinessException or InvalidOperationException) { return Default; }
    }
    public static async Task<FiscalDocumentView?> Fiscal(IServiceProvider services, Guid documentId)
    {
        if (services.GetService<IComplianceService>() is not { } compliance) return null;
        try { return await compliance.FiscalFor(documentId); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or BusinessException or InvalidOperationException) { return null; }
    }
}
