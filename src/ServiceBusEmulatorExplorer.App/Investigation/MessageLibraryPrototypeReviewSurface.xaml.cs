using ServiceBusEmulatorExplorer.Core.ServiceBus;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using ServiceBusEmulatorExplorer.Presentation;
using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>
/// Reusable session-only review workflow for the Message Workbench prototype.
/// The host supplies the profile, target, and prepared messages; this control owns
/// the review, progress, results, cancellation, and history transitions.
/// </summary>
public partial class MessageLibraryPrototypeReviewSurface : UserControl
{
    private readonly DispatcherTimer dispatchTimer = new() { Interval = TimeSpan.FromMilliseconds(550) };
    private readonly WorkbenchRun workbenchRun;
    private IReadOnlyList<PrototypePreparedMessage> preparedMessages = [];
    private int messageCount;
    private readonly DispatcherTimer scheduleClock = new() { Interval = TimeSpan.FromSeconds(15) };
    private ScheduleProblem? scheduleProblem;
    private TimeProvider clock = TimeProvider.System;
    private Func<DateTime> utcNow;

    /// <summary>
    /// Times scheduling and cancellation by <paramref name="time"/> instead of the system clock, and picks the default
    /// schedule date (tomorrow) from it. Call it right after creating the review, before the user changes the schedule.
    /// </summary>
    public void UseClock(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        clock = time;
        ScheduleDateInput.SelectedDate = clock.GetLocalNow().Date.AddDays(1);
    }

    public MessageLibraryPrototypeReviewSurface() : this("Local emulator", "order-events", 3)
    {
    }

    public MessageLibraryPrototypeReviewSurface(string profile, string target, int count)
    {
        utcNow = () => clock.GetUtcNow().UtcDateTime;
        workbenchRun = new(() => clock.GetUtcNow());
        InitializeComponent();
        InitializeScheduleInputs();
        RefreshDatePresentation();
        dispatchTimer.Tick += DispatchTick;
        scheduleClock.Tick += (_, _) => UpdateReviewTiming();
        Unloaded += (_, _) =>
        {
            dispatchTimer.Stop();
            scheduleClock.Stop();
        };
        SizeChanged += (_, _) => UpdateReviewCardLayout();
        ReviewScroll.SizeChanged += (_, _) => UpdateReviewCardLayout();
        Configure(profile, target, count);
    }

    private void InitializeScheduleInputs()
    {
        ScheduleHourInput.ItemsSource = Enumerable.Range(0, 24)
            .Select(hour => hour.ToString("D2", CultureInfo.InvariantCulture))
            .ToArray();
        ScheduleMinuteInput.ItemsSource = Enumerable.Range(0, 60)
            .Select(minute => minute.ToString("D2", CultureInfo.InvariantCulture))
            .ToArray();
        ScheduleHourInput.SelectedIndex = 14;
        ScheduleMinuteInput.SelectedIndex = 30;
    }

    private void UpdateReviewCardLayout()
    {
        bool stacked = ActualWidth < 800;
        ReviewCards.ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0.9, GridUnitType.Star);
        ReviewCards.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1.1, GridUnitType.Star);
        Grid.SetRow(ReviewSummaryCard, stacked ? 1 : 0);
        Grid.SetColumn(ReviewSummaryCard, stacked ? 0 : 1);
        ReviewTargetCard.Margin = stacked ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 9, 0);
        ReviewSummaryCard.Margin = stacked ? new Thickness(0) : new Thickness(9, 0, 0, 0);
        double availableCardHeight = Math.Max(0, ReviewScroll.ActualHeight - ReviewHeading.ActualHeight
            - ReviewHeading.Margin.Bottom - ReviewCards.Margin.Bottom - 2);
        ReviewTargetCard.MinHeight = stacked ? 0 : availableCardHeight;
        ReviewSummaryCard.MinHeight = stacked ? 0 : availableCardHeight;
    }

    public void Configure(string profile, string target, int count)
    {
        messageCount = count;
        ResultsGrid.ItemsSource = workbenchRun.Results;
        ScheduledGrid.ItemsSource = workbenchRun.ScheduledReceipts;
        HistoryGrid.ItemsSource = workbenchRun.CancellationAttempts;
        ReviewProfile.Text = profile;
        ReviewTarget.Text = target;
        ReviewTargetKind.Text = target == "order-replies" ? "Queue" : "Topic";
        ReviewMessageCount.Text = $"{count} valid message{(count == 1 ? "" : "s")}";
        ReviewHeading.Text = $"Review {count} message{(count == 1 ? "" : "s")}";
        SetPreparedMessages(CreateSamplePreparedMessages(count));
        ScheduleDateInput.SelectedDate = clock.GetLocalNow().Date.AddDays(1);
        ScheduleHourInput.SelectedIndex = 14;
        ScheduleMinuteInput.SelectedIndex = 30;
        ReviewSurface.Visibility = Visibility.Visible;
        ReviewFooter.Visibility = Visibility.Visible;
        UpdateReviewTiming();
    }

    private Func<bool>? destinationAvailable;

    public void SetTargetDetails(EntityKind kind, string endpoint, Func<bool>? isAvailable = null)
    {
        ReviewTargetKind.Text = kind.ToString();
        ReviewEndpoint.Text = endpoint;
        destinationAvailable = isAvailable;
        UpdateReviewTiming();
    }

    public event Action? BackRequested;
    public event Action<PrototypeRunSnapshot>? RunCompleted;
    public event Action<string>? ViewDestinationRequested;
    public string Profile => ReviewProfile.Text;
    public string Target => ReviewTarget.Text;

    public IReadOnlyDictionary<string, FrameworkElement> NamedElements => new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
    {
        [nameof(ReviewSurface)] = ReviewSurface,
        [nameof(ReviewFooter)] = ReviewFooter,
        [nameof(ReviewHeading)] = ReviewHeading,
        [nameof(ReviewProfile)] = ReviewProfile,
        [nameof(ReviewEndpoint)] = ReviewEndpoint,
        [nameof(ReviewTargetKind)] = ReviewTargetKind,
        [nameof(ReviewTarget)] = ReviewTarget,
        [nameof(ReviewSendNow)] = ReviewSendNow,
        [nameof(ReviewSchedule)] = ReviewSchedule,
        [nameof(ScheduleInputs)] = ScheduleInputs,
        [nameof(ScheduleDateInput)] = ScheduleDateInput,
        [nameof(ScheduleHourInput)] = ScheduleHourInput,
        [nameof(ScheduleMinuteInput)] = ScheduleMinuteInput,
        [nameof(ScheduleUtc)] = ScheduleUtc,
        [nameof(ScheduleLocal)] = ScheduleLocal,
        [nameof(ResolvedSchedule)] = ResolvedSchedule,
        [nameof(ScheduleWarning)] = ScheduleWarning,
        [nameof(ScheduleWarningText)] = ScheduleWarningText,
        [nameof(ScheduleWarningAction)] = ScheduleWarningAction,
        [nameof(ReviewMessageCount)] = ReviewMessageCount,
        [nameof(ReviewTotalSize)] = ReviewTotalSize,
        [nameof(ReviewTtl)] = ReviewTtl,
        [nameof(ReviewPropertyDetails)] = ReviewPropertyDetails,
        [nameof(ReviewError)] = ReviewError,
        [nameof(ProgressSurface)] = ProgressSurface,
        [nameof(ProgressHeading)] = ProgressHeading,
        [nameof(ProgressTarget)] = ProgressTarget,
        [nameof(ProgressStatus)] = ProgressStatus,
        [nameof(DispatchProgress)] = DispatchProgress,
        [nameof(StopDispatch)] = StopDispatch,
        [nameof(ResultsSurface)] = ResultsSurface,
        [nameof(ResultsHeading)] = ResultsHeading,
        [nameof(ResultsGrid)] = ResultsGrid,
        [nameof(CancelScheduled)] = CancelScheduled,
        [nameof(CancellationSurface)] = CancellationSurface,
        [nameof(CancellationTarget)] = CancellationTarget,
        [nameof(ScheduledGrid)] = ScheduledGrid,
        [nameof(CancellationError)] = CancellationError,
        [nameof(HistorySurface)] = HistorySurface,
        [nameof(HistoryGrid)] = HistoryGrid,
        [nameof(ReviewNotice)] = ReviewNotice,
        [nameof(ReviewNoticeFrame)] = ReviewNoticeFrame,
        [nameof(ConfirmDispatch)] = ConfirmDispatch
    };

    public void ShowReview()
    {
        Visibility = Visibility.Visible;
        SetSurface(ReviewSurface);
        ReviewFooter.Visibility = Visibility.Visible;
    }

    public void ShowResults()
    {
        Visibility = Visibility.Visible;
        SetSurface(ResultsSurface);
        ReviewFooter.Visibility = Visibility.Collapsed;
        SetWindowTitle("Run results");
    }

    public void ShowPriorResults(PrototypeRunSnapshot run)
    {
        Visibility = Visibility.Visible;
        RestoreRun(run);
        ShowResults();
    }

    public void SetReviewProperties(string ttl, string details)
    {
        ReviewTtl.Text = ttl.StartsWith("Time to live: ", StringComparison.OrdinalIgnoreCase)
            ? ttl["Time to live: ".Length..] : ttl;
        ReviewPropertyDetails.Text = details;
    }

    public void SetPreparedMessages(IReadOnlyList<PrototypePreparedMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count != messageCount)
            throw new ArgumentException($"Expected {messageCount} prepared messages, received {messages.Count}.", nameof(messages));

        preparedMessages = messages.ToArray();
        long totalBytes = preparedMessages.Sum(message => (long)Encoding.UTF8.GetByteCount(message.Body));
        ReviewTotalSize.Text = $"{totalBytes:N0} bytes UTF-8 across prepared messages";
    }

    public PrototypeRunSnapshot? CaptureRun() => workbenchRun.Capture(ReviewProfile.Text, ReviewTarget.Text);

    public void RestoreRun(PrototypeRunSnapshot run)
    {
        ArgumentNullException.ThrowIfNull(run);
        ReviewProfile.Text = run.Profile;
        ReviewTarget.Text = run.Target;
        workbenchRun.Restore(run, DatePresentation.GetFormat(this));
        UpdateCancellationActions();
        CancelScheduled.Visibility = workbenchRun.ScheduledReceipts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static IReadOnlyList<PrototypePreparedMessage> CreateSamplePreparedMessages(int count) =>
        Enumerable.Range(1, count)
            .Select(row => new PrototypePreparedMessage(
                row,
                $"00000000-0000-0000-0000-{row:D12}",
                $"10000000-0000-0000-0000-{row:D12}",
                "2026-09-22T10:00:00Z",
                "{\"customerId\":\"C1001\",\"amount\":149.90}"))
            .ToArray();

    private void ReviewTiming_Checked(object sender, RoutedEventArgs e)
    {
        if (ScheduleInputs is not null) UpdateReviewTiming();
    }

    private void ScheduleZone_Checked(object sender, RoutedEventArgs e)
    {
        if (ScheduleInputs is not null) UpdateReviewTiming();
    }

    private void ScheduleInput_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ScheduleInputs is not null) UpdateReviewTiming();
    }

    private void UpdateReviewTiming()
    {
        if (ReviewSchedule is null || ScheduleInputs is null || ConfirmDispatch is null || ScheduleWarning is null) return;
        bool schedule = ReviewSchedule.IsChecked == true;
        ScheduleInputs.Visibility = schedule ? Visibility.Visible : Visibility.Collapsed;
        if (!schedule)
        {
            scheduleClock.Stop();
            ShowTiming(WorkbenchSchedule.SendNow(messageCount, ReviewTargetKind.Text == "Queue"));
            return;
        }
        scheduleClock.Start();
        DateTime nowUtc = utcNow();
        ScheduleDateInput.DisplayDateStart = WorkbenchSchedule.EarliestDay(clock.GetLocalNow().Date, nowUtc, ScheduleDateInput.SelectedDate);
        ShowTiming(WorkbenchSchedule.Schedule(messageCount, ChosenSchedule(), nowUtc, TimeZoneInfo.Local, DatePresentation.GetFormat(this)));
    }

    private void ShowTiming(ReviewTiming timing)
    {
        ConfirmDispatch.Content = timing.ConfirmText;
        ReviewNotice.Text = timing.Notice;
        ConfirmDispatch.IsEnabled = timing.CanConfirm;
        ShowScheduleProblem(timing.Problem);
        if (timing.ResolvedText is { } resolved) ResolvedSchedule.Text = resolved;
    }

    private ScheduleChoice ChosenSchedule() => new(ScheduleDateInput.SelectedDate, ScheduleHourInput.SelectedItem as string,
        ScheduleMinuteInput.SelectedItem as string, ScheduleLocal.IsChecked == true);

    private void ShowScheduleProblem(ScheduleProblem? problem)
    {
        scheduleProblem = problem;
        ScheduleWarning.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        // The dispatch notice describes what will happen; it is misleading while a problem blocks it.
        ReviewNoticeFrame.Visibility = problem is null ? Visibility.Visible : Visibility.Collapsed;
        ScheduleWarningText.Text = problem?.Message ?? string.Empty;
        string action = WorkbenchSchedule.ActionFor(problem);
        ScheduleWarningAction.Content = action;
        AutomationProperties.SetName(ScheduleWarningAction, action);
        ConfirmDispatch.ToolTip = problem?.Message;
    }

    private void ScheduleWarningAction_Click(object sender, RoutedEventArgs e)
    {
        ScheduleInputs.BringIntoView();
        if (scheduleProblem?.Field == ScheduleField.Time)
        {
            ScheduleHourInput.Focus();
            return;
        }
        ScheduleDateInput.Focus();
        ScheduleDateInput.IsDropDownOpen = true;
    }

    private void Dispatch_Click(object sender, RoutedEventArgs e)
    {
        if (destinationAvailable?.Invoke() == false)
        {
            ReviewError.Text = "Destination is unavailable or not verified. Return to preparation and refresh discovery.";
            ReviewError.Visibility = Visibility.Visible;
            ConfirmDispatch.IsEnabled = false;
            return;
        }
        bool scheduled = ReviewSchedule.IsChecked == true;
        ScheduleResolution? schedule = scheduled
            ? WorkbenchSchedule.Resolve(ChosenSchedule(), utcNow(), TimeZoneInfo.Local, DatePresentation.GetFormat(this)) : null;
        if (schedule?.Problem is { } problem)
        {
            // Time can pass while the review is open, before the schedule clock ticks again.
            ShowScheduleProblem(problem);
            ConfirmDispatch.IsEnabled = false;
            return;
        }
        scheduleClock.Stop();
        ReviewError.Visibility = Visibility.Collapsed;
        SetSurface(ProgressSurface);
        ReviewFooter.Visibility = Visibility.Collapsed;
        ProgressHeading.Text = $"{(scheduled ? "Scheduling" : "Sending")} {messageCount} messages";
        ProgressTarget.Text = $"Target: {ReviewProfile.Text} / {ReviewEndpoint.Text} / {ReviewTarget.Text}";
        DispatchProgress.Maximum = messageCount;
        workbenchRun.Start(preparedMessages.Select(message => message.MessageId).ToArray(), schedule is null ? null : new DateTimeOffset(schedule.Utc), ReviewTarget.Text);
        dispatchTimer.Start();
    }

    private void DispatchTick(object? sender, EventArgs e)
    {
        bool finished = workbenchRun.SendNext(DatePresentation.GetFormat(this));
        DispatchProgress.Value = workbenchRun.Sent;
        ProgressStatus.Text = $"{workbenchRun.Sent} of {messageCount} acknowledged";
        if (finished) FinishDispatch();
    }

    private void StopDispatch_Click(object sender, RoutedEventArgs e)
    {
        dispatchTimer.Stop();
        workbenchRun.Stop();
        FinishDispatch();
    }

    private void FinishDispatch()
    {
        dispatchTimer.Stop();
        SetSurface(ResultsSurface);
        ResultsHeading.Text = "Run results";
        CancelScheduled.Visibility = workbenchRun.ScheduledReceipts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetWindowTitle("Run results");
        if (CaptureRun() is { } run) RunCompleted?.Invoke(run);
    }

    private void ExportResults_Click(object sender, RoutedEventArgs e)
    {
        var save = new SaveFileDialog { Title = "Export sample results", Filter = "JSON files (*.json)|*.json", FileName = "message-workbench-results.json" };
        if (save.ShowDialog(Window.GetWindow(this)) != true) return;
        PrototypeRunSnapshot? snapshot = CaptureRun();
        if (snapshot is null) return;
        var export = new
        {
            snapshot.Profile,
            snapshot.Target,
            snapshot.Scheduled,
            Results = snapshot.Results.Select(row => new { row.Row, row.MessageId, row.Outcome, row.Details }),
            ScheduledResults = snapshot.ScheduledResults.Select(row => new { row.Row, row.Receipt, row.Payload, row.Outcome, row.DueTime, row.Selected, row.CancellationStatus, row.AttemptTime }),
            cancellationAttempts = snapshot.CancellationAttempts.Select(attempt => new
            {
                row = attempt.Row,
                receipt = attempt.Receipt,
                payload = attempt.Payload,
                outcome = attempt.Outcome,
                requestedAtUtc = attempt.RequestedAtUtc
            })
        };
        File.WriteAllText(save.FileName, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ViewDestination_Click(object sender, RoutedEventArgs e)
    {
        ViewDestinationRequested?.Invoke(ReviewTarget.Text);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void SetSurface(FrameworkElement surface)
    {
        ReviewSurface.Visibility = surface == ReviewSurface ? Visibility.Visible : Visibility.Collapsed;
        ProgressSurface.Visibility = surface == ProgressSurface ? Visibility.Visible : Visibility.Collapsed;
        ResultsSurface.Visibility = surface == ResultsSurface ? Visibility.Visible : Visibility.Collapsed;
        CancellationSurface.Visibility = surface == CancellationSurface ? Visibility.Visible : Visibility.Collapsed;
        HistorySurface.Visibility = surface == HistorySurface ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetWindowTitle(string title)
    {
        if (Window.GetWindow(this) is MessageLibraryPrototypeDialog dialog) dialog.Title = title;
    }

}
