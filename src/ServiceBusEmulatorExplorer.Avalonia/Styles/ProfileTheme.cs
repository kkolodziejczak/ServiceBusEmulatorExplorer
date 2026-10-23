using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ServiceBusEmulatorExplorer.Presentation;

namespace ServiceBusEmulatorExplorer.Avalonia.Styles;

/// <summary>Applies the <see cref="ProfilePalette"/> of a profile colour to a window's resources.</summary>
public static class ProfileTheme
{
    public static void Apply(Window window, string hex)
    {
        ArgumentNullException.ThrowIfNull(window);
        PaletteColor selected = Color.TryParse(hex, out Color color) ? new(color.R, color.G, color.B) : ProfilePalette.DefaultPrimary;
        foreach (var (key, palette) in ProfilePalette.For(selected))
            window.Resources[key] = new ImmutableSolidColorBrush(Color.FromRgb(palette.R, palette.G, palette.B));
    }
}
