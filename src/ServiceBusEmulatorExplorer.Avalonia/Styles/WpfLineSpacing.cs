using System.Globalization;
using Avalonia.Data.Converters;

namespace ServiceBusEmulatorExplorer.Avalonia.Styles;

/// <summary>
/// Spaces lines the way WPF does for the bundled Selawik: 1.330078125 em, from the font's Windows ascent and descent.
/// Avalonia measures lines from other font tables, which made every line about 2 px shorter than in WPF.
/// </summary>
public sealed class WpfLineSpacing : IValueConverter
{
    public const double SelawikEm = 1.330078125;

    public static WpfLineSpacing Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double fontSize ? fontSize * SelawikEm : double.NaN;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
