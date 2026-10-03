using System.Text.Json;
using SierraBusiness.Infrastructure;

namespace SierraBusiness.Desktop;

public static class DesktopTradingConfiguration
{
    public static TradingOptions Load(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "trading-settings.json");
        if (!File.Exists(path))
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(file, new TradingOptions(), new JsonSerializerOptions { WriteIndented = true });
        }
        var options = JsonSerializer.Deserialize<TradingOptions>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Trading settings are empty.");
        if (string.IsNullOrWhiteSpace(options.Branch) || options.Branch.Length > 60 || string.IsNullOrWhiteSpace(options.Device) || options.Device.Length > 60 || options.TaxRate is < 0 or > 1)
            throw new InvalidOperationException("Trading settings require branch/device identifiers (up to 60 characters) and a tax rate between 0 and 1.");
        return options;
    }
}
