using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using SierraBusiness.Desktop;
namespace SierraBusiness.Maui;

public static class MauiProgram
{
    /// <summary>Held for the life of the process. Setup and uninstall check for running copies; a second copy of the same company exits instead of hanging.</summary>
    internal static Mutex? CompanyLock { get; private set; }
    public const string ProcessMutex = "SierraBusinessDesktop";
    internal static Mutex? ProcessLock { get; private set; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SierraBusiness", "Data");
        var companies = new CompanyDirectory(directory);
        var arguments = Environment.GetCommandLineArgs();
        var companyArgument = Array.IndexOf(arguments, "--company");
        var companyId = Guid.Empty;
        if (companyArgument >= 0 && companyArgument + 1 < arguments.Length && !Guid.TryParse(arguments[companyArgument + 1], out companyId)) throw new InvalidOperationException("Invalid company identifier.");
        var company = companies.Resolve(companyId);
        ProcessLock = new Mutex(false, ProcessMutex);
        CompanyLock = new Mutex(true, @"Local\SierraBusiness-" + company.Id.ToString("N"), out var first);
        if (!first)
        {
            // Another window already has this company open (or an earlier one stopped responding). Opening a second copy would wait on the
            // same database and browser profile and look frozen, so say so plainly and leave.
            MessageBoxW(IntPtr.Zero, $"Rokela Business Admin is already open for {company.Name}.\n\nSwitch to that window from the taskbar. If it is not responding, end \"SierraBusiness.Maui\" in Task Manager (or restart Windows), then open Rokela Business Admin again. Posted records are never half-saved.", "Rokela Business Admin", 0x40);
            Environment.Exit(0);
        }
        // Keep the embedded browser's cache with the user's data, not in the program folder, so updates and uninstall can always replace the program files.
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SierraBusiness", "WebView2", company.Id.ToString("N")));
        builder.Services.AddSingleton(companies);
        builder.Services.AddSingleton(company);
        builder.Services.AddSierraDesktop(company.DataDirectory);
        // Release configuration contains only a product code, API origin and pinned PUBLIC lease keys.
        var licensingFile = Path.Combine(AppContext.BaseDirectory, "licensing.json");
        var requireLicensing = typeof(MauiProgram).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().Any(a => a.Key == "SierraLicenseRequired" && bool.TryParse(a.Value, out var required) && required);
        PlatformLicenseOptions? licensing = null;
        if (File.Exists(licensingFile))
        {
            try
            {
                licensing = System.Text.Json.JsonSerializer.Deserialize<PlatformLicenseOptions>(File.ReadAllText(licensingFile));
                if (licensing is null) throw new InvalidOperationException();
                licensing.Validate();
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or ArgumentException or FormatException or IOException)
            { licensing = null; requireLicensing = true; }
        }
        if (licensing is not null)
        {
            var configuredLicensing = licensing;
            builder.Services.AddSingleton<PlatformLicenseClient>(sp => new PlatformLicenseClient(configuredLicensing,
                new WindowsLicenseStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SierraBusiness", "Licensing", company.Id.ToString("N")), configuredLicensing.ProductCode),
                new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) }, TimeProvider.System));
            builder.Services.AddSingleton<SierraBusiness.Application.ILicenseService>(sp => sp.GetRequiredService<PlatformLicenseClient>());
        }
        else if (requireLicensing) builder.Services.AddSingleton<SierraBusiness.Application.ILicenseService, SierraBusiness.Application.UnconfiguredLicenseService>();
        builder.Services.AddSingleton<IOfflineDocumentReader, WindowsDocumentReader>();
        builder.Services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(90) });
        builder.Services.AddTransient<OnlineCaptureAssistant>();
        builder.Services.AddTransient<SierraBusiness.Application.IDocumentTextReader, OfflineTextReader>();
        builder.Services.AddTransient<SierraBusiness.Application.IDocumentAssistant, OnlineDocumentAssistant>();
        builder.Services.AddTransient<SierraBusiness.Application.IDocumentScanner, WiaDocumentScanner>();
        builder.Services.AddSingleton<MainPage>();
        var app = builder.Build();
        app.Services.GetRequiredService<SierraBusiness.Infrastructure.JobScheduler>().Start(); // reminders, fiscal retries and integrity checks while the app is open
        return app;
    }
}
