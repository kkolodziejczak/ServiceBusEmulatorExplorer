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
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationDeleteDialogTests
{
    [Theory]
    [InlineData(470, false)]
    [InlineData(470, true)]
    [InlineData(400, false)]
    [InlineData(400, true)]
    [Trait("TestCategory", "UiRender")]
    public void ModalRequiresExactTokenAndKeepsSafeAccessibleActionsContained(int width, bool accept) =>
        OnSta(() => Render(width, accept));

    [Fact]
    public void ConstructorRejectsEmptyActiveAndDuplicateTargets() => OnSta(() =>
    {
        var target = Delivery(1);
        Assert.Throws<ArgumentException>(() => new DeleteMessagesWindow([], "#2563EB"));
        Assert.Throws<ArgumentException>(() => new DeleteMessagesWindow([target, target], "#2563EB"));
        var active = new MessageDelivery(target.Identity with { Bucket = MessageBucket.Active }, target.Message);
        Assert.Throws<ArgumentException>(() => new DeleteMessagesWindow([active], "#2563EB"));
    });

    [Fact]
    public void ConstructorCapturesTargetsIndependentlyOfCallerList() => OnSta(() =>
    {
        var original = Delivery(1);
        var caller = new List<MessageDelivery> { original };
        var dialog = new DeleteMessagesWindow(caller, "#2563EB");
        try
        {
            caller.Clear();
            caller.Add(Delivery(2));
            Assert.Equal([original], dialog.Targets);
        }
        finally { dialog.Close(); }
    });

    private static void Render(int width, bool accept)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        // Extremely long entity names and metadata must not leak into the compact count-only summary.
        var targets = Enumerable.Range(1, width == 400 ? 125 : 1).Select(Delivery).ToArray();
        var window = new DeleteMessagesWindow(targets, "#2563EB");
        Exception? failure = null;
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        watchdog.Tick += (_, _) => { failure ??= new TimeoutException("Delete modal interaction timed out."); window.Close(); };
        try
        {
            Assert.Equal(470, window.Width);
            Assert.Equal(400, window.MinWidth);
            window.Width = width;
            window.Loaded += (_, _) => dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                try
                {
                    window.UpdateLayout();
                    var input = (TextBox)window.FindName("DeleteConfirmationInput");
                    var cancel = (Button)window.FindName("CancelButton");
                    var confirm = (Button)window.FindName("ConfirmDeleteButton");
                    var summary = (TextBlock)window.FindName("DeleteSummary");
                    var heading = (TextBlock)window.FindName("DeleteHeading");
                    Assert.True(cancel.IsCancel);
                    Assert.True(cancel.IsDefault);
                    Assert.True(cancel.IsKeyboardFocused, "Cancel should receive initial modal focus.");
                    Assert.False(confirm.IsDefault);
                    Assert.False(confirm.IsEnabled);
                    Assert.Equal("Type DELETE to confirm deletion", AutomationProperties.GetName(input));
                    Assert.Equal("Confirm delete messages", AutomationProperties.GetName(confirm));
                    Assert.Equal("Cancel", new ButtonAutomationPeer(cancel).GetName());
                    Assert.Equal("DeleteConfirmationInput", AutomationProperties.GetAutomationId(input));
                    Assert.Equal("ConfirmDeleteButton", AutomationProperties.GetAutomationId(confirm));
                    Assert.Contains(targets.Length.ToString(), summary.Text);
                    Assert.DoesNotContain("WWWW", summary.Text);
                    Assert.Equal(TextWrapping.Wrap, summary.TextWrapping);
                    Bounds(window, heading, summary, input, cancel, confirm);
                    Capture(window, $"delete-dialog-{width}-initial");
                    foreach (string text in new[] { "wrong", "delete", "Delete", "DELETE ", " DELETE", "DELETE\n", "" })
                    {
                        input.Text = text;
                        Assert.False(confirm.IsEnabled, $"'{text}' must not enable deletion.");
                    }
                    input.Text = "DELETE";
                    Assert.True(confirm.IsEnabled);
                    input.Text = "";
                    Assert.False(confirm.IsEnabled);
                    input.Text = "DELETE";
                    window.UpdateLayout();
                    Bounds(window, input, cancel, confirm);
                    Capture(window, $"delete-dialog-{width}-ready");
                    var peer = new ButtonAutomationPeer(accept ? confirm : cancel);
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
                }
                catch (Exception exception) { failure = exception; window.Close(); }
            }));
            watchdog.Start();
            bool? result = window.ShowDialog();
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            Assert.Equal(accept, result);
            Assert.False(window.IsVisible);
            Assert.Equal(targets, window.Targets);
        }
        finally
        {
            watchdog.Stop();
            if (window.IsVisible) window.Close();
            dispatcher.InvokeShutdown();
        }
    }

    private static MessageDelivery Delivery(int sequence)
    {
        string longText = new('W', 300);
        var message = new ExplorerMessage(longText, sequence, longText, longText, 300, null, null, 0, "text/plain", null, null, null,
            new Dictionary<string, object?>(), new Dictionary<string, object?>());
        return new(new(1, new(EntityKind.Queue, longText + sequence), MessageBucket.DeadLetter, sequence), message);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Delete dialog proof exceeded its 15-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Bounds(Window window, params FrameworkElement[] elements)
    {
        foreach (var element in elements)
        {
            Assert.True(element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0);
            var bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
            Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= window.ActualWidth + 1
                && bounds.Bottom <= window.ActualHeight + 1, $"{element.Name} is clipped: {bounds}.");
        }
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
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }
}
