namespace SierraBusiness.Maui;

public sealed class App(MainPage page) : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(page) { Title = "Rokela Business Admin", Width = 1280, Height = 850, MinimumWidth = 800, MinimumHeight = 600 };
        window.Created += (_, _) =>
        {
            if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow) return;
            var appWindow = nativeWindow.AppWindow;
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(appWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            if (area is null) return;
            var work = area.WorkArea;
            var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow));
            var scale = dpi > 0 ? dpi / 96d : 1d;
            var margin = (int)Math.Round(24 * scale);
            var width = Math.Min((int)Math.Round(1280 * scale), Math.Max(1, work.Width - margin * 2));
            var height = Math.Min((int)Math.Round(850 * scale), Math.Max(1, work.Height - margin * 2));
            window.MinimumWidth = Math.Min(800, width / scale);
            window.MinimumHeight = Math.Min(600, height / scale);
            appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
        };
        // When the window closes the process must end, or Setup and uninstall find the program files still in use. Normal shutdown
        // finishes well within the grace period; anything still running after it (a background task, the embedded browser) is ended.
        window.Destroying += (_, _) => _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(8)); Environment.Exit(0); });
        return window;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);
}
