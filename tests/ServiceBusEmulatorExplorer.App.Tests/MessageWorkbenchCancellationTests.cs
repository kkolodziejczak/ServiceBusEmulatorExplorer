using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchCancellationTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Restored_scheduled_result_keeps_target_due_time_and_receipt_in_cancellation_header()
        => OnSta(() =>
        {
            var dialog = CreateResultsDialog();
            try
            {
                dialog.RestoreRun(Snapshot(
                    target: "order-events",
                    scheduled: [(1, "701", "order-events-1.json", "Confirmed scheduled", "23 Sep 2099 14:30 UTC", true, "Eligible", "—")]));
                dialog.Show();
                dialog.UpdateLayout();

                OpenCancellation(dialog);
                string text = Get<TextBlock>(dialog, "CancellationTarget").Text;
                Assert.Contains("order-events", text, StringComparison.Ordinal);
                Assert.Contains("23 Sep 2099 14:30 UTC", text, StringComparison.Ordinal);

                Assert.True(Get<StackPanel>(dialog, "CancellationSurface").Visibility == Visibility.Visible);
                Assert.Empty(dialog.CaptureRun()!.CancellationAttempts);
            }
            finally { dialog.Close(); }
        });

    [Fact]
    public void Confirmed_cancellation_records_acknowledgement_without_rewriting_dispatch_result()
        => OnSta(() =>
        {
            var dialog = CreateResultsDialog();
            try
            {
                dialog.RestoreRun(Snapshot(
                    scheduled: [(1, "701", "order-events-1.json", "Confirmed scheduled", "23 Sep 2099 14:30 UTC", true, "Eligible", "—")]));
                dialog.Show();
                OpenCancellation(dialog);
                ConfirmCancellation(dialog, confirmation =>
                {
                    string text = string.Join(" ", Descendants(confirmation).OfType<TextBlock>().Select(block => block.Text));
                    Assert.Contains("order-events", text, StringComparison.Ordinal);
                    Assert.Contains("23 Sep 2099 14:30 UTC", text, StringComparison.Ordinal);
                    Assert.Contains("701", text, StringComparison.Ordinal);
                    Assert.Contains("1", text, StringComparison.Ordinal);
                    CaptureProof(confirmation, "cancellation-confirmation");
                });

                Wait(() => Get<StackPanel>(dialog, "HistorySurface").Visibility == Visibility.Visible);
                PrototypeRunSnapshot result = dialog.CaptureRun()!;
                Assert.Equal("Confirmed scheduled", Assert.Single(result.Results).Outcome);
                var scheduled = Assert.Single(result.ScheduledResults);
                Assert.Equal("Confirmed scheduled", scheduled.Outcome);
                Assert.Equal("Cancellation acknowledged", scheduled.CancellationStatus);
                var attempt = Assert.Single(result.CancellationAttempts);
                Assert.Equal(1, attempt.Row);
                Assert.Equal("701", attempt.Receipt);
                Assert.Equal("order-events-1.json", attempt.Payload);
                Assert.Equal("Cancellation acknowledged", attempt.Outcome);
                CaptureProof(dialog, "cancellation-history");
                dialog.RestoreRun(result);
                Assert.Equal(result.CancellationAttempts, dialog.CaptureRun()!.CancellationAttempts);
            }
            finally { dialog.Close(); }
        });

    [Fact]
    public void Declining_cancellation_leaves_schedule_and_attempt_history_unchanged() => OnSta(() =>
    {
        var dialog = CreateResultsDialog();
        try
        {
            var before = Snapshot();
            dialog.RestoreRun(before);
            dialog.Show();
            OpenCancellation(dialog);
            QueueConfirmation(dialog, confirm: false);
            Descendants(dialog).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "PrototypeCancelScheduled")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var after = dialog.CaptureRun()!;
            Assert.Empty(after.CancellationAttempts);
            Assert.Equal(before.ScheduledResults, after.ScheduledResults);
        }
        finally { dialog.Close(); }
    });

    private static void CaptureProof(Window window, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("SBE_UI_PROOF_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        ServiceBusEmulatorExplorer.ReadmeScreenshot.WpfScreenshot.SaveWindowContent(window,
            System.IO.Path.Combine(directory, name + ".png"), (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight));
    }

    [Fact]
    public void Unknown_cancellation_records_later_selected_rows_as_not_attempted() => OnSta(() =>
    {
        var dialog = CreateResultsDialog();
        try
        {
            dialog.RestoreRun(Snapshot(scheduled:
            [(2, "702", "second.json", "Confirmed scheduled", "23 Sep 2099 14:30 UTC", true, "Eligible", "—"),
             (3, "703", "third.json", "Confirmed scheduled", "23 Sep 2099 14:30 UTC", true, "Eligible", "—")]));
            dialog.Show();
            OpenCancellation(dialog);
            ConfirmCancellation(dialog);
            var snapshot = dialog.CaptureRun()!;
            Assert.Equal(new[] { "Outcome unknown", "Not attempted" }, snapshot.CancellationAttempts.Select(attempt => attempt.Outcome));
            Assert.Equal("Not attempted", snapshot.ScheduledResults[1].CancellationStatus);
            Assert.All(snapshot.ScheduledResults, row => Assert.Equal("Confirmed scheduled", row.Outcome));
        }
        finally { dialog.Close(); }
    });

    [Fact]
    public void Unknown_cancellation_can_be_retried_and_prior_attempt_is_retained()
        => OnSta(() =>
        {
            var dialog = CreateResultsDialog();
            try
            {
                dialog.RestoreRun(Snapshot(
                    scheduled: [(2, "702", "order-events-2.json", "Confirmed scheduled", "23 Sep 2099 14:30 UTC", true, "Eligible", "—")]));
                dialog.Show();
                OpenCancellation(dialog);
                ConfirmCancellation(dialog);
                Wait(() => Get<StackPanel>(dialog, "HistorySurface").Visibility == Visibility.Visible);

                PrototypeRunSnapshot first = dialog.CaptureRun()!;
                Assert.Equal("Outcome unknown", Assert.Single(first.CancellationAttempts).Outcome);

                Button retry = Descendants(dialog).OfType<Button>().Single(button =>
                    string.Equals(button.Content?.ToString(), "Retry cancellation", StringComparison.Ordinal));
                Assert.True(retry.IsEnabled);
                QueueConfirmation(dialog, confirm: true);
                retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Wait(() => dialog.CaptureRun()!.CancellationAttempts.Count == 2);

                PrototypeRunSnapshot second = dialog.CaptureRun()!;
                Assert.Equal(2, second.CancellationAttempts.Count);
                Assert.All(second.CancellationAttempts, attempt =>
                    Assert.Equal("Outcome unknown", attempt.Outcome));
                Assert.Equal(first.CancellationAttempts[0].RequestedAtUtc,
                    second.CancellationAttempts[0].RequestedAtUtc);
                Assert.Equal("Confirmed scheduled", Assert.Single(second.Results).Outcome);
            }
            finally { dialog.Close(); }
        });

    [Fact]
    public void Retry_is_disabled_with_explanation_after_ten_attempts()
        => OnSta(() =>
        {
            var dialog = CreateResultsDialog();
            try
            {
                var attempts = Enumerable.Range(1, 9)
                    .Select(index => new PrototypeCancellationAttempt(
                        2, "702", "order-events-2.json", "Outcome unknown", DateTimeOffset.UtcNow.AddMinutes(-index)))
                    .ToArray();
                dialog.RestoreRun(Snapshot(
                    scheduled: [(2, "702", "order-events-2.json", "Confirmed scheduled", "23 Sep 2099 14:30 UTC", true, "Outcome unknown", "14:31 UTC")],
                    attempts: attempts));
                dialog.Show();
                OpenCancellation(dialog);
                ConfirmCancellation(dialog);
                Wait(() => Get<StackPanel>(dialog, "HistorySurface").Visibility == Visibility.Visible);

                Button retry = Descendants(dialog).OfType<Button>().Single(button =>
                    string.Equals(button.Content?.ToString(), "Retry cancellation", StringComparison.Ordinal));
                Assert.False(retry.IsEnabled);
                Assert.Contains(Descendants(dialog).OfType<TextBlock>(), block =>
                    block.Text.Contains("10", StringComparison.Ordinal) &&
                    (block.Text.Contains("attempt", StringComparison.OrdinalIgnoreCase) ||
                     block.Text.Contains("retry", StringComparison.OrdinalIgnoreCase)));
                Assert.Equal(10, dialog.CaptureRun()!.CancellationAttempts.Count);
            }
            finally { dialog.Close(); }
        });

    private static MessageLibraryPrototypeDialog CreateResultsDialog() =>
        new(PrototypeDialogMode.Results, "Local emulator", "order-events", 1)
        { Width = 1200, Height = 760, ShowActivated = false, ShowInTaskbar = false };

    private static PrototypeRunSnapshot Snapshot(
        string target = "order-events",
        IReadOnlyList<(int Row, string Receipt, string Payload, string Outcome, string DueTime, bool Selected, string CancellationStatus, string AttemptTime)>? scheduled = null,
        IReadOnlyList<PrototypeCancellationAttempt>? attempts = null) =>
        new(
            "Local emulator",
            target,
            true,
            [(1, "message-1", "Confirmed scheduled", "Sample receipt retained")],
            scheduled ?? [(1, "701", "order-events-1.json", "Confirmed scheduled", "23 Sep 2099 14:30 UTC", true, "Eligible", "—")])
        { CancellationAttempts = attempts ?? [] };

    private static void OpenCancellation(MessageLibraryPrototypeDialog dialog)
    {
        var results = Get<StackPanel>(dialog, "ResultsSurface");
        Assert.Equal(Visibility.Visible, results.Visibility);
        var cancel = Descendants(dialog).OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "PrototypeShowCancellation");
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Visibility.Visible, Get<StackPanel>(dialog, "CancellationSurface").Visibility);
    }

    private static void ConfirmCancellation(
        MessageLibraryPrototypeDialog dialog,
        Action<MessageLibraryPrototypeDialog>? inspect = null)
    {
        QueueConfirmation(dialog, confirm: true, inspect);
        var confirm = Descendants(dialog).OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "PrototypeCancelScheduled");
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void QueueConfirmation(
        MessageLibraryPrototypeDialog dialog,
        bool confirm,
        Action<MessageLibraryPrototypeDialog>? inspect = null)
    {
        dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var confirmation = dialog.OwnedWindows.OfType<MessageLibraryPrototypeDialog>()
                .Single(window => window.FindName("ConfirmCancellationButton") is not null);
            inspect?.Invoke(confirmation);
            string buttonName = confirm ? "ConfirmCancellationButton" : "CancelCancellationButton";
            ((Button)confirmation.FindName(buttonName)!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
    }

    private static T Get<T>(FrameworkElement root, string name) where T : class =>
        Assert.IsAssignableFrom<T>(root is MessageLibraryPrototypeDialog dialog ? dialog.FindName(name) : root.FindName(name));

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                action();
            }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Message Workbench cancellation proof exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Wait(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Message Workbench cancellation did not reach the expected state.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }
}
