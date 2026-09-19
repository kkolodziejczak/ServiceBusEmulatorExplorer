using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationWatchNotificationRenderTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Notification_persists_and_keeps_long_content_and_response_controls_accessible()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Render(dispatcher); }
                catch (Exception exception) { failure = exception; }
                finally { dispatcher.InvokeShutdown(); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Notification rendering exceeded its 25-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static async Task Render(Dispatcher dispatcher)
    {
        int investigations = 0, dismissals = 0;
        var anchor = new Window { Width = 500, Height = 400, Title = "Notification render anchor" };
        WatchNotificationWindow? window = null;
        try
        {
            anchor.Show();
            window = new WatchNotificationWindow(() => investigations++, () => dismissals++, anchor);
            window.Update(Row("checkout-requests", false), 1, 0, "Local emulator");
            Assert.False(window.ShowActivated);
            Assert.False(window.ShowInTaskbar);
            Assert.True(window.Topmost);
            window.Show();
            await Idle(dispatcher);
            Assert.Equal(360, window.ActualWidth);
            Assert.Equal("1 new active message", ById<TextBlock>(window, "WatchNotificationSummary").Text);
            Assert.Equal("Local emulator / checkout-requests", ById<TextBlock>(window, "WatchNotificationSource").Text);
            AssertControls(window);
            AssertInAnchorMonitor(window, anchor);
            Capture(window, "watch-notification-normal");

            // Exercise persistence beyond the approved 15-second polling interval.
            await Task.Delay(TimeSpan.FromSeconds(16));
            Assert.True(window.IsVisible);
            Assert.Equal(0, investigations);
            Assert.Equal(0, dismissals);

            string profile = "Production Europe — " + string.Join(" ", Enumerable.Repeat("fulfilment audit connection", 14));
            string subscription = string.Join("-", Enumerable.Repeat("analytics", 14));
            MessageRow message = Row(subscription, true, "order-events");
            window.Update(message, 123456, 19, profile);
            await Idle(dispatcher);
            Assert.Equal("123456 new dead-letter messages", ById<TextBlock>(window, "WatchNotificationSummary").Text);
            TextBlock source = ById<TextBlock>(window, "WatchNotificationSource");
            Assert.Equal($"{profile} / order-events / {subscription}\n+ 19 other watched locations", source.Text);
            ScrollViewer scroll = Descendants<ScrollViewer>(window).Single();
            Assert.True(scroll.ScrollableHeight > 0, "Long source text should remain scrollable rather than enlarge the desktop alert indefinitely.");
            AssertControls(window);
            AssertInAnchorMonitor(window, anchor);
            Capture(window, "watch-notification-long-content");
            scroll.ScrollToBottom();
            await Idle(dispatcher);
            Assert.Equal(scroll.ScrollableHeight, scroll.VerticalOffset);
            AssertControls(window);

            Invoke(ById<Button>(window, "InvestigateWatchNotification"));
            await Idle(dispatcher);
            Assert.Equal(1, investigations);
            Invoke(ById<Button>(window, "DismissWatchNotification"));
            await Idle(dispatcher);
            Assert.Equal(1, dismissals);
            Invoke(ById<Button>(window, "CloseWatchNotification"));
            await Idle(dispatcher);
            Assert.Equal(2, dismissals);
        }
        finally
        {
            window?.Close();
            anchor.Close();
        }
    }

    private static Task Idle(Dispatcher dispatcher) => dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;

    private static void AssertControls(Window window)
    {
        foreach (string id in new[] { "InvestigateWatchNotification", "DismissWatchNotification", "CloseWatchNotification" })
        {
            Button button = ById<Button>(window, id);
            Assert.True(button.IsEnabled && button.IsVisible && button.Focusable);
            Assert.Equal(id == "InvestigateWatchNotification" ? "Investigate notification" : "Dismiss notification",
                new ButtonAutomationPeer(button).GetName());
            Rect bounds = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
            Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= window.ActualWidth && bounds.Bottom <= window.ActualHeight,
                $"{id} is clipped: {bounds} in {window.RenderSize}.");
        }
    }

    private static void AssertInAnchorMonitor(Window window, Window anchor)
    {
        var workArea = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(anchor).Handle).WorkingArea;
        Assert.True(GetWindowRect(new WindowInteropHelper(window).Handle, out NativeRect bounds));
        Assert.True(bounds.Left >= workArea.Left && bounds.Top >= workArea.Top && bounds.Right <= workArea.Right && bounds.Bottom <= workArea.Bottom,
            "Notification must fit within the available anchor monitor's working area.");
    }

    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    private static T ById<T>(DependencyObject parent, string id) where T : DependencyObject => Descendants<T>(parent)
        .Single(element => AutomationProperties.GetAutomationId(element) == id);

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static MessageRow Row(string name, bool deadLetter, string? topic = null)
    {
        var message = new ExplorerMessage("notification-proof", 42, "{}", "{}", 2, DateTimeOffset.UtcNow,
            null, 1, "application/json", null, null, "Checkout", new Dictionary<string, object?>(), new Dictionary<string, object?>());
        return new MessageRow(new MessageDelivery(new DeliveryIdentity(1,
            new EntityAddress(topic is null ? EntityKind.Queue : EntityKind.Subscription, name, topic),
            deadLetter ? MessageBucket.DeadLetter : MessageBucket.Active, 42), message), TimestampDisplay.Utc);
    }

    private static void Capture(Window window, string name)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_INVESTIGATION_UI"), "true", StringComparison.OrdinalIgnoreCase)) return;
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git"))) root = root.Parent;
        Assert.NotNull(root);
        string directory = Path.Combine(root.FullName, "artifacts", "investigation-ui");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);
}
