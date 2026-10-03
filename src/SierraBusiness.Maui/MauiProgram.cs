using Microsoft.Extensions.DependencyInjection;
using SierraBusiness.Desktop;
namespace SierraBusiness.Maui;

public static class MauiProgram
{
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
        builder.Services.AddSingleton(companies);
        builder.Services.AddSingleton(company);
        builder.Services.AddSierraDesktop(company.DataDirectory);
        builder.Services.AddSingleton<IOfflineDocumentReader, WindowsDocumentReader>();
        builder.Services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(90) });
        builder.Services.AddTransient<OnlineCaptureAssistant>();
        builder.Services.AddTransient<SierraBusiness.Application.IDocumentTextReader, OfflineTextReader>();
        builder.Services.AddTransient<SierraBusiness.Application.IDocumentAssistant, OnlineDocumentAssistant>();
        builder.Services.AddTransient<SierraBusiness.Application.IDocumentScanner, WiaDocumentScanner>();
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
