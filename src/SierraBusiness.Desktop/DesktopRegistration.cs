using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SierraBusiness.Application;
using SierraBusiness.Infrastructure;
namespace SierraBusiness.Desktop;

public static class DesktopRegistration
{
    public static IServiceCollection AddSierraDesktop(this IServiceCollection services, string dataDirectory)
    {
        var store = new DesktopStore(dataDirectory);
        services.AddSingleton(_ => store);
        var companyFolder = new DirectoryInfo(Path.GetFullPath(dataDirectory));
        var companyRoot = companyFolder.Parent?.Name == "Companies" && Guid.TryParseExact(companyFolder.Name, "N", out _) ? companyFolder.Parent.Parent!.FullName : companyFolder.FullName;
        services.TryAddSingleton(new CompanyDirectory(companyRoot));
        services.AddTransient<ICompanyPeriodService, CompanyPeriodService>();
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<TradingOptions>().Configure(options =>
        {
            var configured = DesktopTradingConfiguration.Load(dataDirectory);
            options.Branch = configured.Branch;
            options.Device = configured.Device;
            options.TaxRate = configured.TaxRate;
            options.ClosedThrough = configured.ClosedThrough;
        });
        var syncSettings = new DesktopSyncConfiguration(dataDirectory);
        var sync = syncSettings.Load();
        var branch = DesktopTradingConfiguration.Load(dataDirectory).Branch;
        services.AddSingleton<ISyncSettings>(syncSettings);
        services.AddOptions<UpdateOptions>().Configure(o =>
        {
            var file = Path.Combine(dataDirectory, "update-settings.json"); // {"Feed": "https://…/sierra-business/latest.json"}
            try { if (File.Exists(file)) o.Feed = System.Text.Json.JsonSerializer.Deserialize<UpdateOptions>(File.ReadAllText(file))?.Feed ?? ""; } catch (System.Text.Json.JsonException) { }
        });
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(sync));
        services.AddDbContextFactory<BusinessDb>(o =>
        {
            o.UseSqlite(store.ConnectionString);
            o.AddInterceptors(new CompanyArchiveInterceptor(dataDirectory));
            if (sync.Role == SyncRole.Branch) o.AddInterceptors(new SyncCaptureInterceptor(SyncRole.Branch, branch)); // changes queue for head office in the same save
        });
        services.AddIdentityCore<IdentityUser>(o =>
        {
            o.Password.RequiredLength = 12;
            o.User.RequireUniqueEmail = true;
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<BusinessDb>();
        services.AddSingleton<LocalAccountService>();
        services.AddSingleton<IActorSession>(sp => sp.GetRequiredService<LocalAccountService>());
        services.AddSingleton<ISaleDraftRecovery>(sp => new DesktopSaleDraftRecovery(dataDirectory, sp.GetRequiredService<IActorSession>(), sp.GetRequiredService<IDbContextFactory<BusinessDb>>()));
        services.AddSingleton<IDocumentDraftRecovery>(sp => new DesktopDocumentDraftRecovery(dataDirectory, sp.GetRequiredService<IActorSession>(), sp.GetRequiredService<IDbContextFactory<BusinessDb>>()));
        services.AddTransient<IPosService, PosService>();
        services.AddTransient<IBankingService, BankingService>();
        services.AddTransient<ICommerceService, CommerceService>();
        services.AddTransient<IFinanceService, FinanceService>();
        services.AddTransient<IOrderService, OrderService>();
        services.AddTransient<IInventoryService, InventoryService>();
        services.AddTransient<IInventoryManagement, InventoryManagement>();
        services.AddTransient<IPayrollService, PayrollService>();
        services.AddTransient<IPartyService, PartyService>();
        services.AddTransient<IReturnService, ReturnService>();
        services.AddTransient<IUserAdministration, UserAdministration>();
        services.AddTransient<IPeriodService, PeriodService>();
        services.AddTransient<DesktopBackupService>();
        services.AddTransient<IBackupManager, DesktopBackupManager>();
        services.AddTransient<ICloudBackupManager, GoogleDriveBackupManager>();
        services.AddTransient<IIntegrityService, IntegrityService>();
        services.AddTransient<IImportService, SierraBusiness.Infrastructure.Imports.ImportService>();
        services.AddSingleton<IFileExport, DesktopFileExport>();
        services.AddTransient<IPurchasingService, PurchasingService>();
        services.AddTransient<IEquipmentService, EquipmentService>();
        services.AddTransient<ITenderService, TenderService>();
        services.AddTransient<IServiceDesk, ServiceDesk>();
        services.AddTransient<IReportService, ReportService>();
        services.AddTransient<IControlService, ControlService>();
        services.AddTransient<IComplianceService, ComplianceService>();
        services.AddTransient<IMerchandiseService, MerchandiseService>();
        services.AddTransient<IPricingService, PricingService>();
        services.AddTransient<IBankingExtras, BankingExtrasService>();
        services.AddTransient<IStatementsService, StatementsService>();
        services.AddSierraOperations();
        services.AddTransient<ISyncService, SyncService>();
        if (sync.Role == SyncRole.Branch) services.AddSingleton<IBackgroundJob, SyncJob>();
        services.TryAddSingleton<IFiscalIntegration, NoFiscalDevice>();
        services.AddSingleton<IChangeFeed, ChangeFeed>();
        services.AddSingleton<IFileStore, DatabaseFileStore>();
        services.AddTransient<IDocumentLibrary, DocumentLibrary>();
        services.AddTransient<DocumentCaptureService>();
        services.AddTransient<IDocumentStorage, SqliteDocumentStorage>();
        services.AddTransient<BusinessService>();
        services.AddTransient<IBusinessService, SessionBusinessService>();
        return services;
    }
}



