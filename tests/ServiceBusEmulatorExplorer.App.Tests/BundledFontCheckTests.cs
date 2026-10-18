using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.App.Tests.VisualBaselines;
using Xunit.Sdk;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class BundledFontCheckTests
{
    [Fact]
    public void A_button_label_in_the_bundled_font_passes() => OnSta(() =>
        WithButton(new Button { Content = "Send 3 messages" }, window => BundledFontCheck.AssertBundledOnly(window, null, "check-pass")));

    [Fact]
    public void A_button_label_with_a_character_missing_from_the_bundled_font_fails() => OnSta(() =>
        WithButton(new Button { Content = "Send ⚠" }, window =>
        {
            var failure = Assert.ThrowsAny<XunitException>(() => BundledFontCheck.AssertBundledOnly(window, null, "check-glyph"));
            Assert.Contains("U+26A0", failure.Message);
        }));

    [Fact]
    public void A_button_label_in_an_installed_font_fails() => OnSta(() =>
        WithButton(new Button { Content = "Send", FontFamily = new FontFamily("Segoe UI") }, window =>
        {
            var failure = Assert.ThrowsAny<XunitException>(() => BundledFontCheck.AssertBundledOnly(window, null, "check-family"));
            Assert.Contains("\"Send\" uses Segoe UI", failure.Message);
        }));

    private static void WithButton(Button button, Action<Window> check)
    {
        var window = new Window
        {
            Content = button, FontFamily = AppFonts.Ui, Width = 240, Height = 120, Left = -10000, Top = -10000,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
        };
        try
        {
            window.Show();
            check(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Font check proof exceeded its 15-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
