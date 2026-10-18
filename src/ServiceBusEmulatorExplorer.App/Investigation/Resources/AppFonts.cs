using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace ServiceBusEmulatorExplorer.App.Investigation.Resources;

/// <summary>
/// The two typefaces the app ships with, so text renders the same on every machine instead of depending
/// on installed system fonts (DEC-050). XAML references them with <c>{x:Static res:AppFonts.Ui}</c>.
/// </summary>
public static class AppFonts
{
    private static readonly Uri FontFolder = BundledFontFolder();

    /// <summary>Selawik, an open stand-in for Segoe UI with matching widths: every label, control and heading.</summary>
    public static readonly FontFamily Ui = new(FontFolder, "./#Selawik");

    /// <summary>Cascadia Mono: JSON, the activity log and other fixed-width text.</summary>
    public static readonly FontFamily Code = new(FontFolder, "./#Cascadia Mono");

    private static Uri BundledFontFolder()
    {
        // Application's type initializer registers the pack scheme and the application resource package. It has
        // normally run by the time XAML loads, but not when a font is the first WPF resource anything asks for.
        RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle);
        // WPF finds font files in a resource folder by comparing against the lowercased compiled resource names,
        // so the folder is spelled in lowercase here even though it is Assets/Fonts on disk.
        return new Uri("pack://application:,,,/ServiceBusEmulatorExplorer.App;component/assets/fonts/");
    }
}
