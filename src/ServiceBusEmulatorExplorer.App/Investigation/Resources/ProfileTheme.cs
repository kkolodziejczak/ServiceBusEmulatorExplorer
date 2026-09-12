using System.Windows;
using System.Windows.Media;

namespace ServiceBusEmulatorExplorer.App.Investigation.Resources;

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

        Color selected;
        try { selected = (Color)ColorConverter.ConvertFromString(hex); }
        catch (Exception exception) when (exception is FormatException or ArgumentException or NotSupportedException) { selected = Parse("#0069FA"); }
        selected.A = 255;
        var primary = selected;
        while (WhiteContrast(primary) < 4.5) primary = Mix(primary, Colors.Black, 0.025);
        var baseline = selected == Parse("#0069FA");
        Set("PrimaryBrush", primary);
        Set("PrimaryHoverBrush", baseline ? Parse("#005BD8") : Mix(primary, Colors.Black, 0.14));
        Set("PrimaryPressedBrush", baseline ? Parse("#004FBD") : Mix(primary, Colors.Black, 0.24));
        Set("InkBrush", baseline ? Parse("#17213D") : Mix(primary, Parse("#171B22"), 0.88));
        Set("SecondaryBrush", baseline ? Parse("#627692") : Mix(primary, Parse("#68717D"), 0.85));
        Set("ActionTextBrush", baseline ? Parse("#234266") : Mix(primary, Parse("#253342"), 0.7));
        Set("IconBrush", baseline ? Parse("#52708D") : Mix(primary, Parse("#5F6C7A"), 0.72));
        Set("EntityPrimaryBrush", baseline ? Parse("#087BC1") : primary);
        Tint("CanvasBrush", "#FAFCFF", 0.018);
        Tint("RaisedBrush", "#FFFFFF", 0.008);
        Tint("ChromeBrush", "#F5F9FE", 0.04);
        Tint("SubtleSurfaceBrush", "#F3F7FC", 0.055);
        Tint("ColumnHeaderBrush", "#EEF4FA", 0.07);
        Tint("ControlBorderBrush", "#CBD8E8", 0.22);
        Tint("DividerBrush", "#DFE6EE", 0.115);
        Tint("NeutralHoverBrush", "#EAF4FF", 0.08);
        Tint("SelectionBrush", "#DBEDFF", 0.145);
        Tint("NeutralPressedBrush", "#C7E2FF", 0.22);
        Tint("DisabledBackgroundBrush", "#F1F4F8", 0.035);
        Tint("DisabledBorderBrush", "#DCE3EC", 0.10);
        Set("DisabledTextBrush", baseline ? Parse("#738196") : Mix(primary, Parse("#79808A"), 0.9));

        void Tint(string key, string blueDefault, double amount) => Set(key, baseline ? Parse(blueDefault) : Mix(Colors.White, primary, amount));
        void Set(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }
    }

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);

    private static Color Mix(Color from, Color toward, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (toward.R - from.R) * amount),
        (byte)Math.Round(from.G + (toward.G - from.G) * amount),
        (byte)Math.Round(from.B + (toward.B - from.B) * amount));

    private static double WhiteContrast(Color color)
    {
        static double Channel(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 1.05 / (0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B) + 0.05);
    }
}
