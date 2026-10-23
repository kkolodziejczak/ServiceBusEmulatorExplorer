namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class ProfilePaletteTests
{
    [Fact]
    public void The_default_blue_keeps_the_designer_colours()
    {
        var palette = ProfilePalette.For(ProfilePalette.DefaultPrimary);

        Assert.Equal(21, palette.Count);
        Assert.Equal("#0069FA", palette["PrimaryBrush"].ToString());
        Assert.Equal("#005BD8", palette["PrimaryHoverBrush"].ToString());
        Assert.Equal("#17213D", palette["InkBrush"].ToString());
        Assert.Equal("#087BC1", palette["EntityPrimaryBrush"].ToString());
        Assert.Equal("#FAFCFF", palette["CanvasBrush"].ToString());
        Assert.Equal("#CBD8E8", palette["ControlBorderBrush"].ToString());
        Assert.Equal("#738196", palette["DisabledTextBrush"].ToString());
    }

    [Fact]
    public void Another_colour_is_mixed_into_text_and_tints_the_surfaces()
    {
        var palette = ProfilePalette.For(new(0x25, 0x63, 0xEB));

        Assert.Equal("#2563EB", palette["PrimaryBrush"].ToString());
        Assert.Equal("#2563EB", palette["EntityPrimaryBrush"].ToString());
        Assert.Equal("#19243A", palette["InkBrush"].ToString());
        Assert.Equal("#FBFCFF", palette["CanvasBrush"].ToString());
        Assert.Equal("#CFDDFB", palette["ControlBorderBrush"].ToString());
    }

    [Theory]
    [InlineData(0xFF, 0xD7, 0x00)]
    [InlineData(0x00, 0xC8, 0xFF)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    public void A_light_colour_darkens_only_until_white_text_on_it_is_readable(byte r, byte g, byte b)
    {
        var primary = ProfilePalette.For(new(r, g, b))["PrimaryBrush"];
        var oneStepLighter = new PaletteColor(
            (byte)Math.Min(255, Math.Round(primary.R / 0.975)), (byte)Math.Min(255, Math.Round(primary.G / 0.975)),
            (byte)Math.Min(255, Math.Round(primary.B / 0.975)));

        Assert.True(WhiteContrast(primary) >= 4.5, primary.ToString());
        Assert.True(WhiteContrast(oneStepLighter) < 4.5, oneStepLighter.ToString());
        Assert.True(primary.R <= r && primary.G <= g && primary.B <= b);
    }

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
