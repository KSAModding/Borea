using System.Globalization;
using System.Runtime.CompilerServices;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Assertions use English unless a test explicitly selects another UI language.
        // Keep the suite independent of the languages installed on the host.
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
    }
}
