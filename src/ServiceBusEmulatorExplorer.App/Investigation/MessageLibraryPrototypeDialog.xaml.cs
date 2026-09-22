using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public enum PrototypeDialogMode { SampleCsv, Mapping, Capture, Validation, Conflict, DraftGuard, Destination, Review, Results }
public enum PrototypeDraftChoice { Cancel, Save, Discard, Reload, SaveAs, KeepEditing }
public sealed record PrototypeRunSnapshot(string Profile, string Target, bool Scheduled,
    IReadOnlyList<(int Row, string MessageId, string Outcome, string Details)> Results,
    IReadOnlyList<(int Row, string Receipt, string Payload, string Outcome, string DueTime, bool Selected, string CancellationStatus, string AttemptTime)> ScheduledResults);
public sealed record PrototypeCaptureProperties(string? Subject, string? ContentType, string? CorrelationId,
    string? SessionId, int? TtlMinutes, IReadOnlyDictionary<string, object?> ApplicationProperties);

/// <summary>Session-only sample interactions for the approved Message Workbench dialog states.</summary>
public partial class MessageLibraryPrototypeDialog : Window
{
    private readonly DispatcherTimer dispatchTimer = new() { Interval = TimeSpan.FromMilliseconds(550) };
    private readonly ObservableCollection<PrototypeResult> results = [];
    private readonly ObservableCollection<PrototypeScheduledResult> scheduledResults = [];
    private IReadOnlyList<PrototypePreparedMessage> preparedMessages = [];
    private readonly int messageCount;
    private int dispatchIndex;
    private bool dispatchScheduled;
    private string originalCaptureBody = "{\n  \"customerId\": \"C1001\",\n  \"amount\": 149.90\n}";
    private string editedCaptureBody = "{\n  \"customerId\": \"C1001\",\n  \"amount\": 149.90\n}";
    private ExplorerMessage? capturedMessage;

    public MessageLibraryPrototypeDialog(PrototypeDialogMode mode, string profile, string target, int count)
    {
        InitializeComponent();
        messageCount = count;
        ResultsGrid.ItemsSource = results;
        ScheduledGrid.ItemsSource = scheduledResults;
        HistoryGrid.ItemsSource = scheduledResults;
        SampleCsvSurface.Visibility = mode == PrototypeDialogMode.SampleCsv ? Visibility.Visible : Visibility.Collapsed;
        MappingSurface.Visibility = mode == PrototypeDialogMode.Mapping ? Visibility.Visible : Visibility.Collapsed;
        CaptureSurface.Visibility = mode == PrototypeDialogMode.Capture ? Visibility.Visible : Visibility.Collapsed;
        ValidationSurface.Visibility = mode == PrototypeDialogMode.Validation ? Visibility.Visible : Visibility.Collapsed;
        ConflictSurface.Visibility = mode == PrototypeDialogMode.Conflict ? Visibility.Visible : Visibility.Collapsed;
        DraftGuardSurface.Visibility = mode == PrototypeDialogMode.DraftGuard ? Visibility.Visible : Visibility.Collapsed;
        DestinationSurface.Visibility = mode == PrototypeDialogMode.Destination ? Visibility.Visible : Visibility.Collapsed;
        DestinationProfile.Text = profile;
        ReviewSurface.Visibility = mode == PrototypeDialogMode.Review ? Visibility.Visible : Visibility.Collapsed;
        ReviewFooter.Visibility = mode == PrototypeDialogMode.Review ? Visibility.Visible : Visibility.Collapsed;
        ResultsSurface.Visibility = mode == PrototypeDialogMode.Results ? Visibility.Visible : Visibility.Collapsed;
        ReviewProfile.Text = profile;
        ReviewTarget.Text = target;
        ReviewTargetKind.Text = target == "order-replies" ? "Queue" : "Topic";
        ReviewMessageCount.Text = $"{count} valid message{(count == 1 ? "" : "s")}";
        ReviewHeading.Text = $"Review {count} message{(count == 1 ? "" : "s")}";
        SetPreparedMessages(CreateSamplePreparedMessages(count));
        ScheduleDateInput.SelectedDate = DateTime.Today.AddDays(1);
        UpdateReviewTiming();
        dispatchTimer.Tick += DispatchTick;
        Closed += (_, _) => dispatchTimer.Stop();
        if (mode == PrototypeDialogMode.SampleCsv)
        {
            Title = "Choose sample CSV";
            Height = 300;
        }
        else if (mode == PrototypeDialogMode.Mapping)
        {
            Title = "Map CSV inputs";
            Height = 590;
        }
        else if (mode == PrototypeDialogMode.Capture)
        {
            Title = "Save as template";
            Height = 925;
            MaxHeight = Math.Max(500, SystemParameters.WorkArea.Height - 30);
            CapturePreview.Text = originalCaptureBody;
        }
        else if (mode == PrototypeDialogMode.Validation)
        {
            Title = "CSV validation results";
            Height = 520;
        }
        else if (mode == PrototypeDialogMode.Conflict)
        {
            Title = "File conflict";
            Height = 425;
        }
        else if (mode == PrototypeDialogMode.DraftGuard)
        {
            Title = "Save changes?";
            Height = 245;
        }
        else if (mode == PrototypeDialogMode.Destination)
        {
            Title = "Choose destination";
            Height = 350;
        }
        else if (mode == PrototypeDialogMode.Results)
        {
            Title = "Run results";
            Height = 620;
        }
        else
        {
            Title = "Review messages";
            Height = 620;
        }
    }

    public string SampleFile { get; set; } = "orders.csv";
    public string SelectedSampleCsv => (string)((ComboBoxItem)SampleCsvPicker.SelectedItem).Tag;
    public string? CustomerDeclaredDefault { get; set; }
    public string? AmountDeclaredDefault { get; set; }
    public string CustomerInputSource => ((ComboBoxItem)CustomerSource.SelectedItem).Content.ToString()!;
    public string AmountInputSource => ((ComboBoxItem)AmountSource.SelectedItem).Content.ToString()!;
    public string CustomerInputValue => CustomerValue.Text;
    public string AmountInputValue => AmountValue.Text;
    public string Delimiter => MappingDelimiter.SelectedIndex == 0 ? "," : ";";
    public PrototypeDraftChoice DraftChoice { get; private set; } = PrototypeDraftChoice.Cancel;
    public string CaptureTemplateName => CaptureName.Text.Trim();
    public string CaptureTemplateBody => CaptureEdited.IsChecked == true ? editedCaptureBody : originalCaptureBody;
    public string CaptureTopic => CaptureAssociation.Text.Trim();
    public string CaptureCollectionName => ((ComboBoxItem)CaptureCollection.SelectedItem).Content.ToString()!.Split('/').Last();
    public PrototypeCaptureProperties? CaptureProperties => capturedMessage is { } message
        ? new PrototypeCaptureProperties(message.Subject, message.ContentType, message.CorrelationId,
            message.SessionId, CaptureCopyTtl.IsChecked == true && message.ExpiresAt is { } expiry &&
            message.EnqueuedTime is { } enqueued ? (int)Math.Ceiling((expiry - enqueued).TotalMinutes) : null,
            message.ApplicationProperties)
        : null;
    public string ChosenDestination => (string)((ComboBoxItem)DestinationPicker.SelectedItem).Tag;
    public event Action<string>? ViewDestinationRequested;
    public event Action? EditMappingRequested;
    public event Action? ValidateAgainRequested;

    private void ApplyDestination_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    public void ConfigureAssociationPicker(string? current, bool editing)
    {
        Title = editing ? "Edit association" : "Add association";
        DestinationHeading.Text = editing ? "Edit association" : "Add association";
        DestinationDescription.Text = editing
            ? "Choose the dummy queue or topic for this association."
            : "Choose a dummy queue or topic to associate with this template.";
        ApplyDestinationButton.Content = editing ? "Save association" : "Add association";
        DestinationPicker.SelectedItem = DestinationPicker.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), current,
                StringComparison.OrdinalIgnoreCase))
            ?? DestinationPicker.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }
    private void UseSampleCsv_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    public PrototypeRunSnapshot? CaptureRun() => results.Count == 0 ? null : new PrototypeRunSnapshot(
        ReviewProfile.Text, ReviewTarget.Text, dispatchScheduled,
        results.Select(row => (row.Row, row.MessageId, row.Outcome, row.Details)).ToArray(),
        scheduledResults.Select(row => (row.Row, row.Receipt, row.Payload, row.Outcome, row.DueTime,
            row.Selected, row.CancellationStatus, row.AttemptTime)).ToArray());

    public void RestoreRun(PrototypeRunSnapshot run)
    {
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

    public void SetValidationRows(IEnumerable<(int Row, string CustomerId, string Status)> rows)
    {
        var sampleRows = rows.ToArray();
        int invalid = sampleRows.Count(value => value.Status != "Ready");
        ValidationGrid.ItemsSource = sampleRows.Select(value => new ValidationRow(value.Row, value.CustomerId, value.Status)).ToArray();
        ValidationHeading.Text = invalid == 0 ? "Validation results" : "Validation errors";
        ValidationBannerText.Text = invalid == 0 ? $"{sampleRows.Length} of {sampleRows.Length} rows valid" :
            $"{invalid} invalid row{(invalid == 1 ? "" : "s")} · Nothing can be sent";
        ValidationBanner.Background = (System.Windows.Media.Brush)FindResource(invalid == 0 ? "NeutralHoverBrush" : "DestructiveHoverBrush");
        ValidationBanner.BorderBrush = (System.Windows.Media.Brush)FindResource(invalid == 0 ? "ControlBorderBrush" : "DestructiveBrush");
    }

    public void SetReviewProperties(string messageIds, string ttl, string details)
    {
        if (preparedMessages.Count == 0)
            ReviewMessageIds.Text = messageIds;
        ReviewTtl.Text = ttl;
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
        ReviewTotalSize.Text = $"{totalBytes:N0} bytes UTF-8 (sample body size; sum of prepared bodies)";
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

    public void SetConflictVersions(string savedFingerprint, string draftFingerprint)
    {
        ConflictSavedFingerprint.Text = $"Saved fingerprint: {savedFingerprint}";
        ConflictDraftFingerprint.Text = $"Draft fingerprint: {draftFingerprint}";
    }

    private void EditMapping_Click(object sender, RoutedEventArgs e)
    {
        EditMappingRequested?.Invoke();
        Close();
    }

    private void ValidateAgain_Click(object sender, RoutedEventArgs e)
    {
        ValidateAgainRequested?.Invoke();
        Close();
    }

    public void SetCaptureSource(string source, string originalBody, string editedBody, string topic,
        ExplorerMessage? message = null)
    {
        capturedMessage = message;
        CaptureSource.Text = source;
        originalCaptureBody = originalBody;
        editedCaptureBody = editedBody;
        CaptureAssociation.Text = topic;
        CaptureEdited.IsEnabled = editedBody != originalBody;
        CapturePreview.Text = originalBody;
        if (message is not null)
        {
            string excluded = string.Join(", ", message.SystemProperties.Keys.OrderBy(value => value));
            CaptureExclusions.Text = $"Observed Message ID ({message.MessageId}) becomes Generate new. " +
                $"Sequence number ({message.SequenceNumber}) and delivery count ({message.DeliveryCount}) are excluded. " +
                (excluded.Length == 0 ? "No other system properties were observed. " :
                    $"System properties excluded: {excluded}. ") +
                "Subject, content type, correlation/session IDs and application properties are copied. Source message is unchanged.";
            CaptureCopyTtl.IsEnabled = message.ExpiresAt is not null && message.EnqueuedTime is not null;
        }
    }

    private void CaptureBody_Checked(object sender, RoutedEventArgs e)
    {
        if (CapturePreview is not null)
            CapturePreview.Text = CaptureEdited.IsChecked == true ? editedCaptureBody : originalCaptureBody;
    }

    private void OpenDraft_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CaptureName.Text) || string.IsNullOrWhiteSpace(CaptureFileName.Text))
        {
            CaptureError.Text = "Enter a template name and filename.";
            CaptureError.Visibility = Visibility.Visible;
            return;
        }
        if (capturedMessage is not null && CaptureAcknowledge.IsChecked != true)
        {
            CaptureError.Text = "Review and acknowledge the copied and excluded properties.";
            CaptureError.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ConflictReload_Click(object sender, RoutedEventArgs e) => CompleteDraftChoice(PrototypeDraftChoice.Reload);
    private void ConflictSaveAs_Click(object sender, RoutedEventArgs e) => CompleteDraftChoice(PrototypeDraftChoice.SaveAs);
    private void ConflictKeep_Click(object sender, RoutedEventArgs e) => CompleteDraftChoice(PrototypeDraftChoice.KeepEditing);
    private void DraftSave_Click(object sender, RoutedEventArgs e) => CompleteDraftChoice(PrototypeDraftChoice.Save);
    private void DraftDiscard_Click(object sender, RoutedEventArgs e) => CompleteDraftChoice(PrototypeDraftChoice.Discard);

    private void CompleteDraftChoice(PrototypeDraftChoice choice)
    {
        DraftChoice = choice;
        DialogResult = true;
    }

    private void BrowseSample_Click(object sender, RoutedEventArgs e)
    {
        var picker = new MessageLibraryPrototypeDialog(PrototypeDialogMode.SampleCsv, ReviewProfile.Text,
            ReviewTarget.Text, 3) { Owner = this };
        picker.SampleCsvPicker.SelectedIndex = SampleFile == "orders-invalid.csv" ? 1 : 0;
        if (picker.ShowDialog() != true) return;
        SampleFile = picker.SelectedSampleCsv;
        MappingFile.Text = SampleFile;
    }

    private void MappingSource_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CustomerSource is null || AmountSource is null || CustomerValue is null || AmountValue is null) return;
        if (sender == CustomerSource)
        {
            CustomerValue.Text = CustomerSource.SelectedIndex == 0 ? "CustomerId" : CustomerSource.SelectedIndex == 1 ? "C1001" : CustomerDeclaredDefault ?? "";
            CustomerValue.IsEnabled = CustomerSource.SelectedIndex != 2;
        }
        if (sender == AmountSource)
        {
            AmountValue.Text = AmountSource.SelectedIndex == 0 ? "Amount" : AmountSource.SelectedIndex == 1 ? "149.90" : AmountDeclaredDefault ?? "";
            AmountValue.IsEnabled = AmountSource.SelectedIndex != 2;
        }
    }

    private void ApplyMapping_Click(object sender, RoutedEventArgs e)
    {
        bool missingCustomer = string.IsNullOrWhiteSpace(CustomerValue.Text);
        bool missingAmount = string.IsNullOrWhiteSpace(AmountValue.Text);
        if (missingCustomer || missingAmount)
        {
            MappingError.Text = "CustomerId and Amount are required. Map a CSV column, enter a constant, or declare a default in Variables.";
            MappingError.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }

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
        if (ReviewNotice is not null)
            ReviewNotice.Text = schedule
                ? $"{messageCount} sample message{(messageCount == 1 ? "" : "s")} will be scheduled for the same instant."
                : "Subscriptions receive according to their rules.";
        if (!schedule) return;
        if (!TryResolveSchedule(out DateTime utc))
        {
            ResolvedSchedule.Text = "Enter a valid, unambiguous date and time (HH:mm).";
            return;
        }
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
        ReviewSurface.Visibility = Visibility.Collapsed;
        ReviewFooter.Visibility = Visibility.Collapsed;
        ProgressSurface.Visibility = Visibility.Visible;
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
        ProgressSurface.Visibility = Visibility.Collapsed;
        ResultsSurface.Visibility = Visibility.Visible;
        ResultsHeading.Text = "Run results";
        CancelScheduled.Visibility = scheduledResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Title = "Run results";
    }

    private void ExportResults_Click(object sender, RoutedEventArgs e)
    {
        var save = new SaveFileDialog { Title = "Export sample results", Filter = "JSON files (*.json)|*.json", FileName = "message-workbench-results.json" };
        if (save.ShowDialog(this) != true) return;
        File.WriteAllText(save.FileName, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ViewDestination_Click(object sender, RoutedEventArgs e)
    {
        ViewDestinationRequested?.Invoke(ReviewTarget.Text);
        Close();
    }

    private void ShowCancellation_Click(object sender, RoutedEventArgs e)
    {
        ResultsSurface.Visibility = Visibility.Collapsed;
        CancellationSurface.Visibility = Visibility.Visible;
        CancellationTarget.Text = $"{ReviewProfile.Text} · {ReviewTarget.Text} · {ResolvedSchedule.Text}";
        Title = "Scheduled results";
    }

    private void BackToResults_Click(object sender, RoutedEventArgs e)
    {
        CancellationSurface.Visibility = Visibility.Collapsed;
        HistorySurface.Visibility = Visibility.Collapsed;
        ResultsSurface.Visibility = Visibility.Visible;
        Title = "Run results";
    }

    private void ConfirmCancellation_Click(object sender, RoutedEventArgs e)
    {
        var selected = scheduledResults.Where(row => row.Selected && row.CancellationStatus != "Cancellation acknowledged").ToArray();
        if (selected.Length == 0) return;
        if (MessageBox.Show(this,
            $"Cancel {selected.Length} selected scheduled message{(selected.Length == 1 ? "" : "s")}? Activation may race this request.",
            "Confirm cancellation", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var row in selected)
        {
            row.CancellationStatus = row.Row % 2 == 0 ? "Outcome unknown" : "Cancellation acknowledged";
            row.AttemptTime = DateTime.UtcNow.ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture);
        }
        HistoryGrid.Items.Refresh();
        ScheduledGrid.Items.Refresh();
        CancellationSurface.Visibility = Visibility.Collapsed;
        HistorySurface.Visibility = Visibility.Visible;
        Title = "Cancellation history";
    }

    private sealed record PrototypeResult(int Row, string MessageId, string Outcome, string Details);
    private sealed record ValidationRow(int Row, string CustomerId, string Status);

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
