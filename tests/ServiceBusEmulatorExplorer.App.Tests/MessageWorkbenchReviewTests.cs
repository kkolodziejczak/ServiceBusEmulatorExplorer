using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchReviewTests
{
    [Theory]
    [InlineData(1, "This message")]
    [InlineData(3, "3 messages")]
    public void Review_disables_scheduling_until_time_is_valid_and_uses_queue_notice(int count, string expectedNotice) => OnSta(() =>
    {
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Review, "Local", "order-replies", count);
        try
        {
            dialog.Show();
            string notice = ((TextBlock)dialog.FindName("ReviewNotice")!).Text;
            Assert.Contains("queue", notice, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(expectedNotice, notice, StringComparison.Ordinal);
            var schedule = (RadioButton)dialog.FindName("ReviewSchedule")!;
            schedule.IsChecked = true;
            var confirm = (Button)dialog.FindName("ConfirmDispatch")!;
            ((DatePicker)dialog.FindName("ScheduleDateInput")!).SelectedDate = DateTime.Today.AddDays(-1);
            Assert.False(confirm.IsEnabled);
            ((DatePicker)dialog.FindName("ScheduleDateInput")!).SelectedDate = DateTime.Today.AddDays(1);
            Assert.True(confirm.IsEnabled);
        }
        finally { dialog.Close(); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Review_dispatch_uses_prepared_ids_and_reports_utf8_sample_body_size() => OnSta(() =>
    {
        var prepared = CreatePreparedMessages(
            (1, "11111111-1111-1111-1111-111111111111", "event-1", "2026-09-22T10:00:00Z", "żółw"),
            (2, "22222222-2222-2222-2222-222222222222", "event-2", "2026-09-22T10:00:01Z", "second body"));
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Review, "Local", "order-events", 2);
        try
        {
            SetPreparedMessages(dialog, prepared);
            dialog.SetReviewProperties("generic placeholder", "Time to live: inherit entity default", "Subject: sample");
            dialog.Show();
            dialog.UpdateLayout();

            var ids = (TextBlock)dialog.FindName("ReviewMessageIds")!;
            var totalSize = (TextBlock)dialog.FindName("ReviewTotalSize")!;
            Assert.Contains("11111111-1111-1111-1111-111111111111", ids.Text);
            Assert.Contains("22222222-2222-2222-2222-222222222222", ids.Text);
            Assert.DoesNotContain("generic placeholder", ids.Text);
            Assert.Contains("bytes UTF-8", totalSize.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("18 bytes", totalSize.Text, StringComparison.OrdinalIgnoreCase);

            Invoke((Button)dialog.FindName("ConfirmDispatch")!);
            Wait(() => ((DataGrid)dialog.FindName("ResultsGrid")!).Items.Count == 2);

            var resultIds = ResultMessageIds((DataGrid)dialog.FindName("ResultsGrid")!);
            Assert.Equal(
                ["11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"],
                resultIds);
        }
        finally
        {
            dialog.Close();
        }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Review_stop_uses_prepared_ids_for_unknown_and_not_attempted_rows() => OnSta(() =>
    {
        var prepared = CreatePreparedMessages(
            (1, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "event-a", "2026-09-22T10:00:00Z", "first"),
            (2, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "event-b", "2026-09-22T10:00:01Z", "second"),
            (3, "cccccccc-cccc-cccc-cccc-cccccccccccc", "event-c", "2026-09-22T10:00:02Z", "third"));
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Review, "Local", "order-events", 3);
        try
        {
            SetPreparedMessages(dialog, prepared);
            dialog.Show();
            dialog.UpdateLayout();
            Invoke((Button)dialog.FindName("ConfirmDispatch")!);
            Invoke((Button)dialog.FindName("StopDispatch")!);
            Wait(() => ((DataGrid)dialog.FindName("ResultsGrid")!).Items.Count == 3);

            Assert.Equal(
                [
                    "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                    "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                    "cccccccc-cccc-cccc-cccc-cccccccccccc"
                ],
                ResultMessageIds((DataGrid)dialog.FindName("ResultsGrid")!));
            Assert.Equal(["Outcome unknown", "Not attempted", "Not attempted"], ResultOutcomes((DataGrid)dialog.FindName("ResultsGrid")!));
        }
        finally
        {
            dialog.Close();
        }
    });

    [Theory]
    [InlineData(760, 620)]
    [InlineData(640, 500)]
    [Trait("TestCategory", "UiRender")]
    public void Review_footer_stays_reachable_with_schedule_and_long_summary(int width, int height) => OnSta(() =>
    {
        string longPropertyDetails = string.Join("\n", Enumerable.Repeat("Application property: a long value that remains reviewable without moving the actions.", 30));
        var prepared = CreatePreparedMessages(
            (1, new string('1', 120), "event-1", "2026-09-22T10:00:00Z", "body"),
            (2, new string('2', 120), "event-2", "2026-09-22T10:00:01Z", "body"));
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Review, "Local", "order-events", 2)
        {
            Width = width,
            Height = height
        };
        try
        {
            dialog.SetReviewProperties("ignored", "Time to live: 30 minutes", longPropertyDetails);
            SetPreparedMessages(dialog, prepared);
            dialog.Show();
            dialog.UpdateLayout();
            var schedule = (RadioButton)dialog.FindName("ReviewSchedule")!;
            ((ISelectionItemProvider)UIElementAutomationPeer.CreatePeerForElement(schedule)!
                .GetPattern(PatternInterface.SelectionItem)).Select();
            dialog.UpdateLayout();

            var confirm = (Button)dialog.FindName("ConfirmDispatch")!;
            Rect bounds = confirm.TransformToAncestor(dialog).TransformBounds(new Rect(confirm.RenderSize));
            Assert.True(confirm.IsVisible && bounds.Width > 0 && bounds.Height > 0,
                $"Confirm action must render at {width}x{height}: {bounds}");
            Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= dialog.ActualWidth && bounds.Bottom <= dialog.ActualHeight,
                $"Confirm action clipped at {width}x{height}: {bounds} within {dialog.ActualWidth}x{dialog.ActualHeight}");
            Assert.Equal(Visibility.Visible, ((FrameworkElement)dialog.FindName("ScheduleInputs")!).Visibility);
            Assert.Contains("same instant", ((TextBlock)dialog.FindName("ReviewNotice")!).Text);
            var notice = (TextBlock)dialog.FindName("ReviewNotice")!;
            Rect noticeBounds = notice.TransformToAncestor(dialog).TransformBounds(new Rect(notice.RenderSize));
            Assert.True(noticeBounds.Top >= 0 && noticeBounds.Bottom <= bounds.Top,
                $"Schedule notice must be visible above confirmation: {noticeBounds}; confirm {bounds}.");
        }
        finally
        {
            dialog.Close();
        }
    });

    private static IReadOnlyList<object> CreatePreparedMessages(
        params (int Row, string MessageId, string EventId, string OccurredAt, string Body)[] values)
    {
        Type preparedType = GetPreparedMessageType();
        ConstructorInfo constructor = preparedType.GetConstructor(
            [typeof(int), typeof(string), typeof(string), typeof(string), typeof(string)])
            ?? throw new Xunit.Sdk.XunitException("PrototypePreparedMessage must expose the approved five-field record constructor.");
        return values.Select(value => constructor.Invoke([
            value.Row, value.MessageId, value.EventId, value.OccurredAt, value.Body])).ToArray();
    }

    private static void SetPreparedMessages(MessageLibraryPrototypeDialog dialog, IReadOnlyList<object> messages)
    {
        Type preparedType = GetPreparedMessageType();
        MethodInfo method = typeof(MessageLibraryPrototypeDialog).GetMethod("SetPreparedMessages", [typeof(IReadOnlyList<>).MakeGenericType(preparedType)])
            ?? throw new Xunit.Sdk.XunitException("MessageLibraryPrototypeDialog.SetPreparedMessages(IReadOnlyList<PrototypePreparedMessage>) is required.");
        Type listType = typeof(List<>).MakeGenericType(preparedType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (object message in messages) list.Add(message);
        method.Invoke(dialog, [list]);
    }

    private static Type GetPreparedMessageType() =>
        typeof(MessageLibraryPrototypeDialog).Assembly.GetType(
            "ServiceBusEmulatorExplorer.App.Investigation.PrototypePreparedMessage")
        ?? throw new Xunit.Sdk.XunitException("PrototypePreparedMessage is required by the review contract.");

    private static IReadOnlyList<string> ResultMessageIds(DataGrid grid) =>
        grid.Items.Cast<object>().Select(item => (string)item.GetType().GetProperty("MessageId")!.GetValue(item)!).ToArray();

    private static IReadOnlyList<string> ResultOutcomes(DataGrid grid) =>
        grid.Items.Cast<object>().Select(item => (string)item.GetType().GetProperty("Outcome")!.GetValue(item)!).ToArray();

    private static void Invoke(ButtonBase button) =>
        ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!
            .GetPattern(PatternInterface.Invoke)).Invoke();

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
            catch (Exception exception)
            {
                failure = exception;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Message Workbench review proof exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Wait(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Message Workbench review did not reach the expected state.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }
}
