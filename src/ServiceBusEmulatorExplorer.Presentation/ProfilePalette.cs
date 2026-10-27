namespace ServiceBusEmulatorExplorer.Presentation;

/// <summary>An opaque colour of the <see cref="ProfilePalette"/>.</summary>
public readonly record struct PaletteColor(byte R, byte G, byte B)
{
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// The brush colours a connection profile's colour gives the app. The default blue keeps the hand-picked designer
/// values; any other colour darkens until white text on it reaches 4.5:1 contrast and tints the neutral surfaces.
/// Each UI framework applies these colours to its own resources under the same keys.
/// </summary>
public static class ProfilePalette
{
    public static readonly PaletteColor DefaultPrimary = new(0x00, 0x69, 0xFA);

    private static readonly PaletteColor White = new(0xFF, 0xFF, 0xFF);
    private static readonly PaletteColor Black = new(0x00, 0x00, 0x00);

    public static IReadOnlyDictionary<string, PaletteColor> For(PaletteColor selected)
    {
        var primary = selected;
        while (WhiteContrast(primary) < 4.5) primary = Mix(primary, Black, 0.025);
        bool baseline = selected == DefaultPrimary;
        var colors = new Dictionary<string, PaletteColor>(StringComparer.Ordinal)
        {
            ["PrimaryBrush"] = primary,
            ["PrimaryHoverBrush"] = baseline ? Hex(0x005BD8) : Mix(primary, Black, 0.14),
            ["PrimaryPressedBrush"] = baseline ? Hex(0x004FBD) : Mix(primary, Black, 0.24),
            ["InkBrush"] = baseline ? Hex(0x17213D) : Mix(primary, Hex(0x171B22), 0.88),
            ["SecondaryBrush"] = baseline ? Hex(0x627692) : Mix(primary, Hex(0x68717D), 0.85),
            ["ActionTextBrush"] = baseline ? Hex(0x234266) : Mix(primary, Hex(0x253342), 0.7),
            ["IconBrush"] = baseline ? Hex(0x52708D) : Mix(primary, Hex(0x5F6C7A), 0.72),
            ["EntityPrimaryBrush"] = baseline ? Hex(0x087BC1) : primary,
            ["CanvasBrush"] = Tint(0xFAFCFF, 0.018),
            ["RaisedBrush"] = Tint(0xFFFFFF, 0.008),
            ["ChromeBrush"] = Tint(0xF5F9FE, 0.04),
            ["SubtleSurfaceBrush"] = Tint(0xF3F7FC, 0.055),
            ["ColumnHeaderBrush"] = Tint(0xEEF4FA, 0.07),
            ["ControlBorderBrush"] = Tint(0xCBD8E8, 0.22),
            ["DividerBrush"] = Tint(0xDFE6EE, 0.115),
            ["NeutralHoverBrush"] = Tint(0xEAF4FF, 0.08),
            ["SelectionBrush"] = Tint(0xDBEDFF, 0.145),
            ["NeutralPressedBrush"] = Tint(0xC7E2FF, 0.22),
            ["DisabledBackgroundBrush"] = Tint(0xF1F4F8, 0.035),
            ["DisabledBorderBrush"] = Tint(0xDCE3EC, 0.10),
            ["DisabledTextBrush"] = baseline ? Hex(0x738196) : Mix(primary, Hex(0x79808A), 0.9)
        };
        return colors;

        PaletteColor Tint(int blueDefault, double amount) => baseline ? Hex(blueDefault) : Mix(White, primary, amount);
    }

    private static PaletteColor Hex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static PaletteColor Mix(PaletteColor from, PaletteColor toward, double amount) => new(
        (byte)Math.Round(from.R + (toward.R - from.R) * amount),
        (byte)Math.Round(from.G + (toward.G - from.G) * amount),
        (byte)Math.Round(from.B + (toward.B - from.B) * amount));

    private static double WhiteContrast(PaletteColor color)
    {
        static double Channel(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 1.05 / (0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B) + 0.05);
    }
}
