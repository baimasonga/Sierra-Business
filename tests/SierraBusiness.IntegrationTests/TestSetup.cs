using System.Runtime.CompilerServices;
namespace SierraBusiness.IntegrationTests;

/// <summary>Screen tests wait for asynchronous loads. Slower or busy machines (the Windows packaging run executes every test in parallel) need more than bUnit's one-second default.</summary>
internal static class TestSetup
{
    [ModuleInitializer] internal static void Initialize() => Bunit.BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(20);
}
