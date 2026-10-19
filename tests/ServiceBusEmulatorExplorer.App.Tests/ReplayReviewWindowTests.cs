using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

/// <summary>Pins the view side of <c>ReplayReviewViewModel</c>: bound texts, Edit ID focus and the dialog result.</summary>
[Collection("WPF presentation")]
public sealed class ReplayReviewWindowTests
{
    private static readonly InvestigationProfile Profile = new("primary", new ConnectionProfile("Primary",
        "Endpoint=sb://runtime.example/;SharedAccessKeyName=Root;SharedAccessKey=secret",
        "Endpoint=sb://admin.example/;SharedAccessKeyName=Root;SharedAccessKey=secret"), "#0069FA");

    [Fact]
    public void Edit_id_focuses_the_invalid_rows_id_and_cancel_closes_without_replaying() => OnSta(() =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var window = new ReplayReviewWindow(Profile, [Prepared(1), Prepared(2)]);
        Exception? failure = null;
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        watchdog.Tick += (_, _) => { failure ??= new TimeoutException("Replay review interaction timed out."); window.Close(); };
        window.Loaded += (_, _) => dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            try
            {
                var confirm = (Button)window.FindName("ReplayConfirmButton");
                Assert.Equal("Replay 2 messages", ((TextBlock)window.FindName("ReviewHeading")).Text);
                Assert.Equal("Replay 2 messages", ((TextBlock)window.FindName("ConfirmLabel")).Text);
                Assert.Equal("Replay 2 messages", AutomationProperties.GetName(confirm));
                Assert.Equal("Primary", ((TextBlock)window.FindName("ProfileText")).Text);
                Assert.True(((Button)window.FindName("CancelButton")).IsKeyboardFocused);
                Assert.True(confirm.IsEnabled);

                TextBox[] ids = Named<TextBox>(window, "MessageIdBox");
                Border[] notices = Named<Border>(window, "ValidationNotice");
                Assert.All(notices, notice => Assert.Equal(Visibility.Collapsed, notice.Visibility));
                ids[1].Text = "";
                window.UpdateLayout();
                Assert.Equal([Visibility.Collapsed, Visibility.Visible], notices.Select(notice => notice.Visibility));
                Assert.False(confirm.IsEnabled);

                Invoke(Named<Button>(window, "FixMessageIdButton")[1]);
                Then(() =>
                {
                    Assert.True(ids[1].IsKeyboardFocused, "Edit ID should move focus to the invalid row's message ID.");
                    Invoke(Named<Button>(window, "ResetButton")[1]);
                    Then(() =>
                    {
                        window.UpdateLayout();
                        Assert.Equal(window.Rows[1].Prepared.Reservation.MessageId, ids[1].Text);
                        Assert.Equal(Visibility.Collapsed, notices[1].Visibility);
                        Assert.True(confirm.IsEnabled);
                        Invoke((Button)window.FindName("CancelButton"));
                    });
                });
            }
            catch (Exception exception) { failure = exception; window.Close(); }
        }));

        // UI Automation queues each click at Input priority, so check its effect once the dispatcher has run it.
        void Then(Action check) => dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            try { check(); }
            catch (Exception exception) { failure = exception; window.Close(); }
        }));

        try
        {
            watchdog.Start();
            bool? result = window.ShowDialog();
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            Assert.False(result);
        }
        finally
        {
            watchdog.Stop();
            if (window.IsVisible) window.Close();
            dispatcher.InvokeShutdown();
        }
    });

    private static T[] Named<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants(root).OfType<T>().Where(element => element.Name == name).ToArray();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void Invoke(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    private static PreparedReplay Prepared(int sequence)
    {
        var message = new ExplorerMessage($"order-{sequence}", sequence, "{}", "{}", 2, null, null, 0, "application/json",
            null, null, null, new Dictionary<string, object?>(), new Dictionary<string, object?>());
        MessageDelivery delivery = new(new(1, new(EntityKind.Queue, "orders"), MessageBucket.DeadLetter, sequence), message);
        return new PreparedReplay(Profile.Id, "namespace", delivery, ReplayLineage.Reserve(delivery, []), null);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Replay review proof exceeded its 15-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
