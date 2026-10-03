using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)dialog.FindName("ScheduleInputs")!).Visibility);
            Select(schedule);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)dialog.FindName("ScheduleInputs")!).Visibility);
            var confirm = (Button)dialog.FindName("ConfirmDispatch")!;
            var hour = (ComboBox)dialog.FindName("ScheduleHourInput")!;
            var minute = (ComboBox)dialog.FindName("ScheduleMinuteInput")!;
            ((DatePicker)dialog.FindName("ScheduleDateInput")!).SelectedDate = DateTime.Today.AddDays(-1);
            Assert.False(confirm.IsEnabled);
            ((DatePicker)dialog.FindName("ScheduleDateInput")!).SelectedDate = DateTime.Today.AddDays(1);
            hour.SelectedItem = "23";
            minute.SelectedItem = "59";
            Assert.True(confirm.IsEnabled);
            Assert.Contains("23:59 UTC", ((TextBlock)dialog.FindName("ResolvedSchedule")!).Text);
        }
        finally { dialog.Close(); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Schedule_selector_actions_resolve_utc_and_local_instants_and_reject_past_dates() => OnSta(() =>
    {
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Review, "Local", "orders", 1);
        try
        {
            dialog.Show();
            var schedule = (RadioButton)dialog.FindName("ReviewSchedule")!;
            Select(schedule);
            Assert.True(schedule.Focus() && schedule.IsKeyboardFocusWithin);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)dialog.FindName("ScheduleInputs")!).Visibility);
            var date = (DatePicker)dialog.FindName("ScheduleDateInput")!;
            var hour = (ComboBox)dialog.FindName("ScheduleHourInput")!;
            var minute = (ComboBox)dialog.FindName("ScheduleMinuteInput")!;
            var utc = (RadioButton)dialog.FindName("ScheduleUtc")!;
            var local = (RadioButton)dialog.FindName("ScheduleLocal")!;
            var confirm = (Button)dialog.FindName("ConfirmDispatch")!;
            var resolved = (TextBlock)dialog.FindName("ResolvedSchedule")!;

            DateTime futureDate = DateTime.Today.AddDays(2);
            date.SelectedDate = futureDate;
            SelectComboItem(hour, "23");
            SelectComboItem(minute, "59");
            Assert.True(confirm.IsEnabled);
            Assert.Contains(futureDate.ToString("dd MMM yyyy 23:59", System.Globalization.CultureInfo.CurrentCulture), resolved.Text);
            Assert.Contains("UTC", resolved.Text);

            Select(local);
            DateTime localWallTime = DateTime.SpecifyKind(futureDate.AddHours(23).AddMinutes(59), DateTimeKind.Unspecified);
            DateTime localUtc = TimeZoneInfo.ConvertTimeToUtc(localWallTime, TimeZoneInfo.Local);
            Assert.True(confirm.IsEnabled);
            Assert.Contains(localUtc.ToString("dd MMM yyyy HH:mm", System.Globalization.CultureInfo.CurrentCulture), resolved.Text);

            Select(utc);
            date.SelectedDate = DateTime.Today.AddDays(-1);
            Assert.False(confirm.IsEnabled);
            Assert.Contains("Choose a future date", resolved.Text);

            Select((RadioButton)dialog.FindName("ReviewSendNow")!);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)dialog.FindName("ScheduleInputs")!).Visibility);
            Assert.True(confirm.IsEnabled);
            Assert.Equal("Send 1 message", confirm.Content);
            Select(schedule);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)dialog.FindName("ScheduleInputs")!).Visibility);
            Assert.False(confirm.IsEnabled);
        }
        finally { dialog.Close(); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Schedule_rejects_invalid_and_ambiguous_local_wall_times()
    {
        if (!TimeZoneInfo.Local.SupportsDaylightSavingTime) return;
        DateTime? invalidTime = FindLocalWallTime(TimeZoneInfo.Local.IsInvalidTime);
        DateTime? ambiguousTime = FindLocalWallTime(TimeZoneInfo.Local.IsAmbiguousTime);
        if (invalidTime is null || ambiguousTime is null) return;

        OnSta(() =>
        {
            var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Review, "Local", "orders", 1);
            try
            {
                dialog.Show();
                Select((RadioButton)dialog.FindName("ReviewSchedule")!);
                Select((RadioButton)dialog.FindName("ScheduleLocal")!);
                var date = (DatePicker)dialog.FindName("ScheduleDateInput")!;
                var hour = (ComboBox)dialog.FindName("ScheduleHourInput")!;
                var minute = (ComboBox)dialog.FindName("ScheduleMinuteInput")!;
                var confirm = (Button)dialog.FindName("ConfirmDispatch")!;
                var resolved = (TextBlock)dialog.FindName("ResolvedSchedule")!;

                date.SelectedDate = invalidTime.Value.Date;
                SelectComboItem(hour, invalidTime.Value.ToString("HH", System.Globalization.CultureInfo.InvariantCulture));
                SelectComboItem(minute, invalidTime.Value.ToString("mm", System.Globalization.CultureInfo.InvariantCulture));
                Assert.False(confirm.IsEnabled);
                Assert.Contains("unambiguous time", resolved.Text);

                date.SelectedDate = ambiguousTime.Value.Date;
                SelectComboItem(hour, ambiguousTime.Value.ToString("HH", System.Globalization.CultureInfo.InvariantCulture));
                SelectComboItem(minute, ambiguousTime.Value.ToString("mm", System.Globalization.CultureInfo.InvariantCulture));
                Assert.False(confirm.IsEnabled);
                Assert.Contains("unambiguous time", resolved.Text);
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Schedule_calendar_supports_routed_month_navigation_day_selection_and_dismissal() => OnSta(() =>
    {
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Review, "Local", "orders", 1);
        try
        {
            dialog.Show();
            Select((RadioButton)dialog.FindName("ReviewSchedule")!);
            var date = (DatePicker)dialog.FindName("ScheduleDateInput")!;
            date.ApplyTemplate();
            var openButton = (ButtonBase)date.Template.FindName("PART_Button", date)!;
            Invoke(openButton);
            Drain(dialog.Dispatcher);

            var popup = (Popup)date.Template.FindName("PART_Popup", date)!;
            Assert.True(date.IsDropDownOpen && popup.IsOpen);
            var calendar = popup.Child as Calendar ?? Descendants(popup.Child!).OfType<Calendar>().Single();
            calendar.ApplyTemplate();
            Drain(dialog.Dispatcher);

            DateTime displayedDate = date.SelectedDate!.Value;
            DateTime targetDate = new DateTime(displayedDate.Year, displayedDate.Month, 1).AddMonths(1).AddDays(2);
            var nextMonth = Descendants(calendar).OfType<ButtonBase>().Single(button =>
                ((FrameworkElement)button).Name == "PART_NextButton");
            Invoke(nextMonth);
            Drain(dialog.Dispatcher);
            Assert.Equal(targetDate.Month, calendar.DisplayDate.Month);

            var day = Descendants(calendar).OfType<CalendarDayButton>().Single(button =>
                button.DataContext is DateTime displayedDay && displayedDay.Date == targetDate.Date);
            string dayName = AutomationProperties.GetName(day);
            if (string.IsNullOrWhiteSpace(dayName))
                dayName = UIElementAutomationPeer.CreatePeerForElement(day)?.GetName() ?? "";
            Assert.False(string.IsNullOrWhiteSpace(dayName), "Calendar day buttons must expose an accessible date label.");
            RaiseRoutedCalendarMouseClick(day);
            Drain(dialog.Dispatcher);

            Assert.Equal(targetDate, date.SelectedDate?.Date);
            Assert.False(date.IsDropDownOpen);
            Assert.True(((Button)dialog.FindName("ConfirmDispatch")!).IsEnabled);
            Assert.Contains(targetDate.ToString("dd MMM yyyy 14:30", System.Globalization.CultureInfo.CurrentCulture),
                ((TextBlock)dialog.FindName("ResolvedSchedule")!).Text);

            Invoke(openButton);
            Drain(dialog.Dispatcher);
            Assert.True(date.IsDropDownOpen);
            Invoke(openButton);
            Drain(dialog.Dispatcher);
            Assert.False(date.IsDropDownOpen, "Invoking the calendar toggle should dismiss the popup.");
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
            dialog.SetReviewProperties("inherit entity default", "Subject: sample");
            dialog.Show();
            dialog.UpdateLayout();

            Assert.Null(dialog.FindName("ReviewMessageIds"));
            Assert.Equal("2 valid messages", ((TextBlock)dialog.FindName("ReviewMessageCount")!).Text);
            Assert.IsType<TextBlock>(dialog.FindName("ReviewProfile"));
            Assert.IsType<TextBlock>(dialog.FindName("ReviewEndpoint"));
            Assert.IsType<TextBlock>(dialog.FindName("ReviewTarget"));
            var totalSize = (TextBlock)dialog.FindName("ReviewTotalSize")!;
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
            dialog.SetReviewProperties("30 minutes", longPropertyDetails);
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

    private static void RaiseRoutedCalendarMouseClick(CalendarDayButton day)
    {
        day.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.MouseDownEvent
        });
        day.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.MouseUpEvent
        });
    }

    private static void Select(FrameworkElement element) =>
        ((ISelectionItemProvider)UIElementAutomationPeer.CreatePeerForElement(element)!
            .GetPattern(PatternInterface.SelectionItem)).Select();

    private static void SelectComboItem(ComboBox combo, string value)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(combo)!;
        var expand = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);
        expand.Expand();
        Drain(combo.Dispatcher);

        var itemPeer = Assert.Single(peer.GetChildren()!, child => child.GetName() == value);
        var selection = itemPeer.GetPattern(PatternInterface.SelectionItem);
        Assert.NotNull(selection);
        ((ISelectionItemProvider)selection!).Select();
        expand.Collapse();
        Drain(combo.Dispatcher);
    }

    private static void Drain(Dispatcher dispatcher) => dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static DateTime? FindLocalWallTime(Func<DateTime, bool> matches)
    {
        DateTime firstDate = DateTime.Today;
        for (int day = 0; day < 365 * 4; day++)
        {
            DateTime date = firstDate.AddDays(day);
            for (int minuteOfDay = 0; minuteOfDay < 24 * 60; minuteOfDay++)
            {
                DateTime wallTime = DateTime.SpecifyKind(date.AddMinutes(minuteOfDay), DateTimeKind.Unspecified);
                if (matches(wallTime)) return wallTime;
            }
        }
        return null;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

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
