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
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<TradingOptions>().Configure(options =>
        {
            var configured = DesktopTradingConfiguration.Load(dataDirectory);
            options.Branch = configured.Branch;
            options.Device = configured.Device;
            options.TaxRate = configured.TaxRate;
            options.ClosedThrough = configured.ClosedThrough;
        });
        services.AddDbContextFactory<BusinessDb>(o => o.UseSqlite(store.ConnectionString));
        services.AddIdentityCore<IdentityUser>(o =>
        {
            o.Password.RequiredLength = 12;
            o.User.RequireUniqueEmail = true;
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<BusinessDb>();
        services.AddSingleton<LocalAccountService>();
        services.AddSingleton<IActorSession>(sp => sp.GetRequiredService<LocalAccountService>());
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
        services.AddTransient<IIntegrityService, IntegrityService>();
        services.AddTransient<DocumentCaptureService>();
        services.AddTransient<IDocumentStorage, SqliteDocumentStorage>();
        services.AddTransient<BusinessService>();
        services.AddTransient<IBusinessService, SessionBusinessService>();
        return services;
    }
}



