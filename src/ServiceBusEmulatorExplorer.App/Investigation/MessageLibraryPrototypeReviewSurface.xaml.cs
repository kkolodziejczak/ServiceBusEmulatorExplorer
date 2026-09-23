using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>
/// Reusable session-only review workflow for the Message Workbench prototype.
/// The host supplies the profile, target, and prepared messages; this control owns
/// the review, progress, results, cancellation, and history transitions.
/// </summary>
public partial class MessageLibraryPrototypeReviewSurface : UserControl
{
    private readonly DispatcherTimer dispatchTimer = new() { Interval = TimeSpan.FromMilliseconds(550) };
    private readonly ObservableCollection<PrototypeResult> results = [];
    private readonly ObservableCollection<PrototypeScheduledResult> scheduledResults = [];
    private IReadOnlyList<PrototypePreparedMessage> preparedMessages = [];
    private int messageCount;
    private int dispatchIndex;
    private bool dispatchScheduled;

    public MessageLibraryPrototypeReviewSurface() : this("Local emulator", "order-events", 3)
    {
    }

    public MessageLibraryPrototypeReviewSurface(string profile, string target, int count)
    {
        InitializeComponent();
        dispatchTimer.Tick += DispatchTick;
        Unloaded += (_, _) => dispatchTimer.Stop();
        SizeChanged += (_, _) => UpdateReviewCardLayout();
        ReviewScroll.SizeChanged += (_, _) => UpdateReviewCardLayout();
        Configure(profile, target, count);
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
        ResultsGrid.ItemsSource = results;
        ScheduledGrid.ItemsSource = scheduledResults;
        HistoryGrid.ItemsSource = scheduledResults;
        ReviewProfile.Text = profile;
        ReviewTarget.Text = target;
        ReviewTargetKind.Text = target == "order-replies" ? "Queue" : "Topic";
        ReviewMessageCount.Text = $"{count} valid message{(count == 1 ? "" : "s")}";
        ReviewHeading.Text = $"Review {count} message{(count == 1 ? "" : "s")}";
        SetPreparedMessages(CreateSamplePreparedMessages(count));
        ScheduleDateInput.SelectedDate = DateTime.Today.AddDays(1);
        ReviewSurface.Visibility = Visibility.Visible;
        ReviewFooter.Visibility = Visibility.Visible;
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
        [nameof(ReviewTargetKind)] = ReviewTargetKind,
        [nameof(ReviewTarget)] = ReviewTarget,
        [nameof(ReviewSendNow)] = ReviewSendNow,
        [nameof(ReviewSchedule)] = ReviewSchedule,
        [nameof(ScheduleInputs)] = ScheduleInputs,
        [nameof(ScheduleDateInput)] = ScheduleDateInput,
        [nameof(ScheduleTimeInput)] = ScheduleTimeInput,
        [nameof(ScheduleUtc)] = ScheduleUtc,
        [nameof(ScheduleLocal)] = ScheduleLocal,
        [nameof(ResolvedSchedule)] = ResolvedSchedule,
        [nameof(ReviewMessageCount)] = ReviewMessageCount,
        [nameof(ReviewTotalSize)] = ReviewTotalSize,
        [nameof(ReviewMessageIds)] = ReviewMessageIds,
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

    public void SetReviewProperties(string messageIds, string ttl, string details)
    {
        if (preparedMessages.Count == 0)
            ReviewMessageIds.Text = messageIds;
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
        ReviewMessageIds.Text = preparedMessages.Count == 0
            ? "Message IDs: none"
            : string.Join("\n", preparedMessages.Select(message => $"{message.Row}. {message.MessageId}"));
        long totalBytes = preparedMessages.Sum(message => (long)Encoding.UTF8.GetByteCount(message.Body));
        ReviewTotalSize.Text = $"{totalBytes:N0} bytes UTF-8 across prepared messages";
    }

    public PrototypeRunSnapshot? CaptureRun() => results.Count == 0 ? null : new PrototypeRunSnapshot(
        ReviewProfile.Text, ReviewTarget.Text, dispatchScheduled,
        results.Select(row => (row.Row, row.MessageId, row.Outcome, row.Details)).ToArray(),
        scheduledResults.Select(row => (row.Row, row.Receipt, row.Payload, row.Outcome, row.DueTime,
            row.Selected, row.CancellationStatus, row.AttemptTime)).ToArray());

    public void RestoreRun(PrototypeRunSnapshot run)
    {
        ArgumentNullException.ThrowIfNull(run);
        ReviewProfile.Text = run.Profile;
        ReviewTarget.Text = run.Target;
        dispatchScheduled = run.Scheduled;
        results.Clear();
        foreach (var row in run.Results)
            results.Add(new PrototypeResult(row.Row, row.MessageId, row.Outcome, row.Details));
        scheduledResults.Clear();
        foreach (var row in run.ScheduledResults)
            scheduledResults.Add(new PrototypeScheduledResult(row.Row, row.Receipt, row.Payload, row.Outcome, row.DueTime)
            { Selected = row.Selected, CancellationStatus = row.CancellationStatus, AttemptTime = row.AttemptTime });
        CancelScheduled.Visibility = scheduledResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private void ScheduleInput_Changed(object sender, EventArgs e)
    {
        if (ScheduleInputs is not null) UpdateReviewTiming();
    }

    private void UpdateReviewTiming()
    {
        if (ReviewSchedule is null || ScheduleInputs is null || ConfirmDispatch is null) return;
        bool schedule = ReviewSchedule.IsChecked == true;
        ScheduleInputs.Visibility = schedule ? Visibility.Visible : Visibility.Collapsed;
        ConfirmDispatch.Content = $"{(schedule ? "Schedule" : "Send")} {messageCount} message{(messageCount == 1 ? "" : "s")}";
        ReviewNotice.Text = schedule
            ? $"{messageCount} sample message{(messageCount == 1 ? "" : "s")} will be scheduled for the same instant."
            : ReviewTarget.Text == "order-replies" ? messageCount == 1
                ? "This message will be sent to the queue."
                : $"{messageCount} messages will be sent to the queue."
                : "Subscriptions receive messages according to their rules.";
        ConfirmDispatch.IsEnabled = !schedule;
        if (!schedule) return;
        if (!TryResolveSchedule(out DateTime utc) || utc <= DateTime.UtcNow)
        {
            ResolvedSchedule.Text = "Choose a future date and a valid, unambiguous time (HH:mm).";
            return;
        }
        ConfirmDispatch.IsEnabled = true;
        ResolvedSchedule.Text = $"Resolved time: {utc:dd MMM yyyy HH:mm} UTC · one time for all messages";
    }

    private bool TryResolveSchedule(out DateTime utc)
    {
        utc = default;
        if (ScheduleDateInput?.SelectedDate is not DateTime date ||
            !TimeOnly.TryParseExact(ScheduleTimeInput?.Text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
            return false;
        DateTime selected = DateTime.SpecifyKind(date.Date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
        if (ScheduleLocal?.IsChecked == true)
        {
            if (TimeZoneInfo.Local.IsInvalidTime(selected) || TimeZoneInfo.Local.IsAmbiguousTime(selected)) return false;
            utc = TimeZoneInfo.ConvertTimeToUtc(selected, TimeZoneInfo.Local);
        }
        else utc = DateTime.SpecifyKind(selected, DateTimeKind.Utc);
        return true;
    }

    private void Dispatch_Click(object sender, RoutedEventArgs e)
    {
        dispatchScheduled = ReviewSchedule.IsChecked == true;
        if (dispatchScheduled && (!TryResolveSchedule(out DateTime utc) || utc <= DateTime.UtcNow))
        {
            ReviewError.Text = "Choose a future date and a valid time before scheduling.";
            ReviewError.Visibility = Visibility.Visible;
            return;
        }
        ReviewError.Visibility = Visibility.Collapsed;
        SetSurface(ProgressSurface);
        ReviewFooter.Visibility = Visibility.Collapsed;
        ProgressHeading.Text = $"{(dispatchScheduled ? "Scheduling" : "Sending")} {messageCount} messages";
        ProgressTarget.Text = $"Target: {ReviewProfile.Text} / localhost / {ReviewTarget.Text}";
        DispatchProgress.Maximum = messageCount;
        dispatchIndex = 0;
        results.Clear();
        scheduledResults.Clear();
        dispatchTimer.Start();
    }

    private void DispatchTick(object? sender, EventArgs e)
    {
        dispatchIndex++;
        string id = preparedMessages[dispatchIndex - 1].MessageId;
        string outcome = dispatchScheduled ? "Confirmed scheduled" : "Confirmed sent";
        results.Add(new PrototypeResult(dispatchIndex, id, outcome, dispatchScheduled ? "Sample receipt retained" : "Sample acknowledgement"));
        if (dispatchScheduled)
            scheduledResults.Add(new PrototypeScheduledResult(dispatchIndex, (700 + dispatchIndex).ToString(CultureInfo.InvariantCulture), $"{ReviewTarget.Text}-{dispatchIndex}.json", outcome, ResolvedSchedule.Text.Replace("Resolved time: ", "")) { Selected = true });
        DispatchProgress.Value = dispatchIndex;
        ProgressStatus.Text = $"{dispatchIndex} of {messageCount} acknowledged";
        if (dispatchIndex == messageCount) FinishDispatch();
    }

    private void StopDispatch_Click(object sender, RoutedEventArgs e)
    {
        dispatchTimer.Stop();
        if (dispatchIndex < messageCount)
        {
            int unknown = dispatchIndex + 1;
            results.Add(new PrototypeResult(unknown, preparedMessages[unknown - 1].MessageId, "Outcome unknown", "In flight when stopped"));
            for (int row = unknown + 1; row <= messageCount; row++)
                results.Add(new PrototypeResult(row, preparedMessages[row - 1].MessageId, "Not attempted", "Not submitted"));
        }
        FinishDispatch();
    }

    private void FinishDispatch()
    {
        dispatchTimer.Stop();
        SetSurface(ResultsSurface);
        ResultsHeading.Text = "Run results";
        CancelScheduled.Visibility = scheduledResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
            ScheduledResults = snapshot.ScheduledResults.Select(row => new { row.Row, row.Receipt, row.Payload, row.Outcome, row.DueTime, row.Selected, row.CancellationStatus, row.AttemptTime })
        };
        File.WriteAllText(save.FileName, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ViewDestination_Click(object sender, RoutedEventArgs e)
    {
        ViewDestinationRequested?.Invoke(ReviewTarget.Text);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void ShowCancellation_Click(object sender, RoutedEventArgs e)
    {
        SetSurface(CancellationSurface);
        CancellationError.Visibility = Visibility.Collapsed;
        CancellationTarget.Text = $"{ReviewProfile.Text} · {ReviewTarget.Text} · {ResolvedSchedule.Text}";
        SetWindowTitle("Scheduled results");
    }

    private void BackToResults_Click(object sender, RoutedEventArgs e)
    {
        SetSurface(ResultsSurface);
        SetWindowTitle("Run results");
    }

    private void ConfirmCancellation_Click(object sender, RoutedEventArgs e)
    {
        var selected = scheduledResults.Where(row => row.Selected && row.CancellationStatus != "Cancellation acknowledged").ToArray();
        if (selected.Length == 0)
        {
            CancellationError.Visibility = Visibility.Visible;
            return;
        }
        CancellationError.Visibility = Visibility.Collapsed;
        if (MessageBox.Show(Window.GetWindow(this),
            $"Cancel {selected.Length} selected scheduled message{(selected.Length == 1 ? "" : "s")}? Activation may race this request.",
            "Confirm cancellation", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var row in selected)
        {
            row.CancellationStatus = row.Row % 2 == 0 ? "Outcome unknown" : "Cancellation acknowledged";
            row.AttemptTime = DateTime.UtcNow.ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture);
        }
        HistoryGrid.Items.Refresh();
        ScheduledGrid.Items.Refresh();
        SetSurface(HistorySurface);
        SetWindowTitle("Cancellation history");
    }

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

    private sealed record PrototypeResult(int Row, string MessageId, string Outcome, string Details);

    private sealed class PrototypeScheduledResult(int row, string receipt, string payload, string outcome, string dueTime)
    {
        public int Row { get; } = row;
        public string Receipt { get; } = receipt;
        public string Payload { get; } = payload;
        public string Outcome { get; } = outcome;
        public string DueTime { get; } = dueTime;
        public bool Selected { get; set; }
        public string CancellationStatus { get; set; } = "Eligible";
        public string AttemptTime { get; set; } = "—";
    }
}
