using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires Windows account encryption."; }
}
