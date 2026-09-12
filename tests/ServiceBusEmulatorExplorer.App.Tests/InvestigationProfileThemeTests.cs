using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationProfileThemeTests
{
    private static readonly string[] ApprovedProfileColors = ["#0069FA", "#7540BF", "#007F80", "#B85B00", "#C83B3B"];

    [Fact]
    public void Approved_profile_colors_keep_white_action_contrast_at_least_4_5()
    {
        RunOnSta(() =>
        {
            foreach (var hex in ApprovedProfileColors)
            {
                var resources = CreateResources();

                ProfileTheme.Apply(resources, hex);

                Assert.True(WhiteContrast(ColorOf(resources, "PrimaryBrush")) >= 4.5, $"Primary {hex}");
                Assert.True(WhiteContrast(ColorOf(resources, "PrimaryHoverBrush")) >= 4.5, $"Hover {hex}");
                Assert.True(WhiteContrast(ColorOf(resources, "PrimaryPressedBrush")) >= 4.5, $"Pressed {hex}");
            }
        });
    }

    [Fact]
    public void Applying_profile_theme_keeps_toolbar_bottom_divider_neutral()
    {
        RunOnSta(() =>
        {
            foreach (var hex in ApprovedProfileColors)
            {
                var resources = CreateResources();
                var divider = ColorOf(resources, "ToolbarDividerBrush");

                ProfileTheme.Apply(resources, hex);

                Assert.Equal(Color.FromRgb(0xDF, 0xE6, 0xEE), divider);
                Assert.Equal(divider, ColorOf(resources, "ToolbarDividerBrush"));
            }
        });
    }

    [Fact]
    public void Applying_profile_theme_keeps_semantic_dlq_delete_and_health_colors_fixed()
    {
        RunOnSta(() =>
        {
            var resources = CreateResources();
            var fixedColors = new Dictionary<string, Color>(StringComparer.Ordinal)
            {
                ["DeadLetterBrush"] = ColorOf(resources, "DeadLetterBrush"),
                ["DeadLetterTextBrush"] = ColorOf(resources, "DeadLetterTextBrush"),
                ["DestructiveBrush"] = ColorOf(resources, "DestructiveBrush"),
                ["DestructiveHoverBrush"] = ColorOf(resources, "DestructiveHoverBrush"),
                ["DestructivePressedBrush"] = ColorOf(resources, "DestructivePressedBrush"),
                ["DestructivePressedTextBrush"] = ColorOf(resources, "DestructivePressedTextBrush"),
                ["ConnectedHealthBrush"] = ColorOf(resources, "ConnectedHealthBrush"),
                ["WarningHealthBrush"] = ColorOf(resources, "WarningHealthBrush"),
                ["DisconnectedHealthBrush"] = ColorOf(resources, "DisconnectedHealthBrush")
            };

            ProfileTheme.Apply(resources, "#C83B3B");

            foreach (var (key, expected) in fixedColors)
                Assert.Equal(expected, ColorOf(resources, key));
        });
    }

    [Fact]
    public void Applying_to_window_updates_its_resource_dictionary()
    {
        RunOnSta(() =>
        {
            var window = new Window();

            ProfileTheme.Apply(window, "#7540BF");

            Assert.Equal(Color.FromRgb(0x75, 0x40, 0xBF), ColorOf(window.Resources, "PrimaryBrush"));
        });
    }

    private static ResourceDictionary CreateResources()
    {
        return (ResourceDictionary)Application.LoadComponent(
            new Uri("/ServiceBusEmulatorExplorer.App;component/Investigation/Resources/SharedStyles.xaml", UriKind.Relative));
    }

    private static Color ColorOf(ResourceDictionary resources, string key) => Assert.IsType<SolidColorBrush>(resources[key]).Color;

    private static double WhiteContrast(Color color)
    {
        static double Channel(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 1.05 / (0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B) + 0.05);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Profile theme proof exceeded its time bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
