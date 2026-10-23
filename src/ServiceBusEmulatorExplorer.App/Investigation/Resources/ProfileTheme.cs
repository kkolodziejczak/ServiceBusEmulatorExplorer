using System.Windows;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.Presentation;

namespace ServiceBusEmulatorExplorer.App.Investigation.Resources;

/// <summary>Applies the <see cref="ProfilePalette"/> of a profile colour to WPF resources.</summary>
public static class ProfileTheme
{
    public static void Apply(Window window, string hex)
    {
        ArgumentNullException.ThrowIfNull(window);
        Apply(window.Resources, hex);
    }

    public static void Apply(ResourceDictionary resources, string hex)
    {
        ArgumentNullException.ThrowIfNull(resources);

        PaletteColor selected;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            selected = new(color.R, color.G, color.B);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or NotSupportedException) { selected = ProfilePalette.DefaultPrimary; }
        foreach (var (key, color) in ProfilePalette.For(selected))
        {
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            resources[key] = brush;
        }
    }
}
