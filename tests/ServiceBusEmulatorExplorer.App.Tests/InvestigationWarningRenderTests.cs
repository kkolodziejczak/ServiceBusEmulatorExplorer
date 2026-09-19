using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationWarningRenderTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [Trait("TestCategory", "UiRender")]
    public void Warning_modal_keeps_safe_default_and_accessible_actions_with_contained_content(
        bool minimum, bool discard, bool accept)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RenderModal(minimum, discard, accept); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The warning modal proof exceeded its 15-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void RenderModal(bool minimum, bool discard, bool accept)
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        string profile = minimum
            ? "Production operations — " + new string('W', 180)
            : "Production operations";
        string message = minimum
            ? string.Join(Environment.NewLine, Enumerable.Range(1, 45)
                .Select(index => $"Warning line {index}: Verify the selected connection before continuing with this investigation."))
            : discard
                ? "Discard all unsaved message drafts and continue? The original broker messages will remain unchanged."
                : "Verify the selected connection before continuing with this investigation.";
        string heading = discard ? "Unsaved message drafts" : "Connection warning";
        string action = discard ? "Discard" : "Continue";
        var window = new ProfileWarningWindow(profile, message, "#2563EB", heading, action);
        Exception? modalFailure = null;
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        watchdog.Tick += (_, _) =>
        {
            modalFailure ??= new TimeoutException("The warning dialog did not complete its modal interaction.");
            window.Close();
        };
        try
        {
            Assert.Equal(520, window.Width);
            Assert.Equal(390, window.Height);
            Assert.Equal(460, window.MinWidth);
            Assert.Equal(320, window.MinHeight);
            if (minimum) { window.Width = window.MinWidth; window.Height = window.MinHeight; }
            window.Loaded += (_, _) => dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                try
                {
                    window.UpdateLayout();
                    var cancel = (Button)window.FindName("CancelButton");
                    var proceed = (Button)window.FindName("ContinueButton");
                    var profileText = (TextBlock)window.FindName("ProfileNameText");
                    var warningText = (TextBlock)window.FindName("WarningMessageText");
                    var headingText = (TextBlock)window.FindName("WarningHeading");
                    var actionText = (TextBlock)window.FindName("ContinueLabel");
                    ScrollViewer scroll = Descendants<ScrollViewer>(window).Single();
                    Assert.Equal(heading + " message", AutomationProperties.GetName(scroll));
                    Assert.True(cancel.IsDefault);
                    Assert.True(cancel.IsCancel);
                    Assert.True(cancel.IsKeyboardFocused, "Cancel must receive initial keyboard focus.");
                    Assert.False(proceed.IsDefault);
                    Assert.Equal(heading, window.Title);
                    Assert.Equal(heading, headingText.Text);
                    Assert.Equal(action, actionText.Text);
                    Assert.Equal(action, AutomationProperties.GetName(proceed));
                    Assert.Equal("Cancel", AutomationProperties.GetName(cancel));
                    Assert.Equal(profile, profileText.Text);
                    Assert.Equal(profile, profileText.ToolTip);
                    Assert.Equal(TextTrimming.CharacterEllipsis, profileText.TextTrimming);
                    Assert.Equal(message, warningText.Text);
                    Assert.Equal(TextWrapping.Wrap, warningText.TextWrapping);
                    AssertVisibleBounds(window, headingText, profileText, scroll, cancel, proceed);
                    Assert.Equal(0, scroll.ScrollableWidth);
                    if (minimum)
                    {
                        Assert.True(scroll.ScrollableHeight > 0, "Long warnings must scroll inside their panel.");
                        Assert.Equal(Visibility.Visible, scroll.ComputedVerticalScrollBarVisibility);
                        scroll.ScrollToBottom();
                        window.UpdateLayout();
                        Assert.True(scroll.VerticalOffset > 0);
                        AssertVisibleBounds(window, cancel, proceed);
                        scroll.ScrollToTop();
                        window.UpdateLayout();
                    }
                    CaptureIfEnabled(window, $"warning-{(discard ? "discard" : "connection")}-{(minimum ? "minimum" : "default")}");
                    var peer = new ButtonAutomationPeer(accept ? proceed : cancel);
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
                }
                catch (Exception exception)
                {
                    modalFailure = exception;
                    window.Close();
                }
            }));
            watchdog.Start();
            bool? result = window.ShowDialog();
            if (modalFailure is not null) ExceptionDispatchInfo.Capture(modalFailure).Throw();
            Assert.Equal(accept, result);
            Assert.False(window.IsVisible);
        }
        finally
        {
            watchdog.Stop();
            if (window.IsVisible) window.Close();
            dispatcher.InvokeShutdown();
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void AssertVisibleBounds(Window window, params FrameworkElement[] elements)
    {
        foreach (FrameworkElement element in elements)
        {
            Assert.True(element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0);
            Rect bounds = element.TransformToAncestor(window)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            Assert.True(bounds.Left >= -1 && bounds.Top >= -1 &&
                bounds.Right <= window.ActualWidth + 1 && bounds.Bottom <= window.ActualHeight + 1,
                $"{element.Name} is clipped outside the warning dialog: {bounds}.");
        }
    }

    private static void CaptureIfEnabled(Window window, string name)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_INVESTIGATION_UI"), "true", StringComparison.OrdinalIgnoreCase)) return;
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git"))) root = root.Parent;
        Assert.NotNull(root);
        string directory = Path.Combine(root.FullName, "artifacts", "investigation-ui");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }
}
