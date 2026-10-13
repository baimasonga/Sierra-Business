using Microsoft.AspNetCore.Components.WebView.Maui;
namespace SierraBusiness.Maui;

public sealed class MainPage : ContentPage
{
    public MainPage()
    {
        Title = "Rokela Business Admin";
        var view = new BlazorWebView { HostPage = "wwwroot/index.html" };
        view.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Components.DesktopRoot) });
        Content = view;
    }
}
