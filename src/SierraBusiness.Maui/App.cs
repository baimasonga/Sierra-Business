namespace SierraBusiness.Maui;

public sealed class App(MainPage page) : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(page)
    {
        Title = "Sierra Business",
        Width = 1280,
        Height = 850,
        MinimumWidth = 800,
        MinimumHeight = 600
    };
}
