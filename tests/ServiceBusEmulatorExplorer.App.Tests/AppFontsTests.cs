using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class AppFontsTests
{
    public static TheoryData<string, string> WeightsInUse => new()
    {
        { nameof(AppFonts.Ui), nameof(FontWeights.Normal) },
        { nameof(AppFonts.Ui), nameof(FontWeights.SemiBold) },
        { nameof(AppFonts.Ui), nameof(FontWeights.Bold) },
        { nameof(AppFonts.Code), nameof(FontWeights.Normal) },
        { nameof(AppFonts.Code), nameof(FontWeights.SemiBold) },
    };

    [Theory]
    [MemberData(nameof(WeightsInUse))]
    public void Every_weight_in_use_loads_from_the_font_files_inside_the_app(string font, string weightName) => OnSta(() =>
    {
        FontFamily family = font == nameof(AppFonts.Ui) ? AppFonts.Ui : AppFonts.Code;
        var weight = (FontWeight)new FontWeightConverter().ConvertFromInvariantString(weightName)!;

        Assert.True(new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal).TryGetGlyphTypeface(out GlyphTypeface glyphs));

        Assert.Contains("/ServiceBusEmulatorExplorer.App;component/Assets/Fonts/", Uri.UnescapeDataString(glyphs.FontUri.AbsoluteUri),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(weight, glyphs.Weight);
        Assert.Equal(font == nameof(AppFonts.Ui) ? "Selawik" : "Cascadia Mono", glyphs.FamilyNames[System.Globalization.CultureInfo.GetCultureInfo("en-US")]);
    });

    /// <summary>
    /// Tooltips and context menus open in their own popup and skip the window's font and implicit styles,
    /// so their font comes from these implicit styles, which App.xaml merges at application level.
    /// </summary>
    [Theory]
    [InlineData(typeof(ToolTip))]
    [InlineData(typeof(ContextMenu))]
    public void Popups_that_do_not_inherit_the_window_font_get_the_bundled_font_from_an_application_style(Type popup) => OnSta(() =>
    {
        var resources = (ResourceDictionary)Application.LoadComponent(
            new Uri("/ServiceBusEmulatorExplorer.App;component/Investigation/Resources/SharedStyles.xaml", UriKind.Relative));

        var style = Assert.IsType<Style>(resources[popup]);

        Setter font = Assert.Single(style.Setters.OfType<Setter>(), setter => setter.Property == Control.FontFamilyProperty);
        Assert.Same(AppFonts.Ui, font.Value);
    });

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Font proof exceeded its 15-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
