using System.Globalization;
using System.Runtime.CompilerServices;

namespace ServiceBusEmulatorExplorer.App.Tests;

/// <summary>
/// Formats dates and numbers the same way on every machine, so rendered baselines and text assertions do not depend
/// on the Windows region of whoever runs the tests. Tests that need another culture set it explicitly and restore it.
/// </summary>
internal static class TestCulture
{
#pragma warning disable CA2255 // A test assembly is the intended place for a process-wide test setting.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void UseOneCulture()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
