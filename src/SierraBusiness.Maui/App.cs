namespace SierraBusiness.Maui;

public sealed class App(MainPage page) : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(page) { Title = "Sierra Business", Width = 1280, Height = 850, MinimumWidth = 800, MinimumHeight = 600 };
        // When the window closes the process must end, or Setup and uninstall find the program files still in use. Normal shutdown
        // finishes well within the grace period; anything still running after it (a background task, the embedded browser) is ended.
        window.Destroying += (_, _) => _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(8)); Environment.Exit(0); });
        return window;
    }
}
