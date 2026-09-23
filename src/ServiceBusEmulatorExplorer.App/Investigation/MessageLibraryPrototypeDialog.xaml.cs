using System.Windows;
using System.Windows.Controls;
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
    private readonly int messageCount;
    private string originalCaptureBody = "{\n  \"customerId\": \"C1001\",\n  \"amount\": 149.90\n}";
    private string editedCaptureBody = "{\n  \"customerId\": \"C1001\",\n  \"amount\": 149.90\n}";
    private ExplorerMessage? capturedMessage;

    public MessageLibraryPrototypeDialog(PrototypeDialogMode mode, string profile, string target, int count)
    {
        InitializeComponent();
        ReviewSurfaceControl.BackRequested += () => Close();
        ReviewSurfaceControl.ViewDestinationRequested += targetName =>
        {
            ViewDestinationRequested?.Invoke(targetName);
            Close();
        };
        messageCount = count;
        SampleCsvSurface.Visibility = mode == PrototypeDialogMode.SampleCsv ? Visibility.Visible : Visibility.Collapsed;
        MappingSurface.Visibility = mode == PrototypeDialogMode.Mapping ? Visibility.Visible : Visibility.Collapsed;
        CaptureSurface.Visibility = mode == PrototypeDialogMode.Capture ? Visibility.Visible : Visibility.Collapsed;
        ValidationSurface.Visibility = mode == PrototypeDialogMode.Validation ? Visibility.Visible : Visibility.Collapsed;
        ConflictSurface.Visibility = mode == PrototypeDialogMode.Conflict ? Visibility.Visible : Visibility.Collapsed;
        DraftGuardSurface.Visibility = mode == PrototypeDialogMode.DraftGuard ? Visibility.Visible : Visibility.Collapsed;
        DestinationSurface.Visibility = mode == PrototypeDialogMode.Destination ? Visibility.Visible : Visibility.Collapsed;
        DestinationProfile.Text = profile;
        ReviewSurfaceControl.Configure(profile, target, count);
        ReviewSurfaceControl.SetPreparedMessages(CreateSamplePreparedMessages(count));
        if (mode == PrototypeDialogMode.Review)
            ReviewSurfaceControl.ShowReview();
        else if (mode == PrototypeDialogMode.Results)
            ReviewSurfaceControl.ShowResults();
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
    public string CaptureTopic => (CaptureAssociation.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
    public string CaptureCollectionName => ((ComboBoxItem)CaptureCollection.SelectedItem).Tag?.ToString()
        ?? ((ComboBoxItem)CaptureCollection.SelectedItem).Content.ToString()!.Split('/').Last();
    public PrototypeCaptureProperties? CaptureProperties => capturedMessage is { } message
        ? new PrototypeCaptureProperties(message.Subject, message.ContentType, message.CorrelationId,
            message.SessionId, CaptureCopyTtl.IsChecked == true && message.ExpiresAt is { } expiry &&
            message.EnqueuedTime is { } enqueued ? (int)Math.Ceiling((expiry - enqueued).TotalMinutes) : null,
            message.ApplicationProperties)
        : null;

    public void SetCaptureCollections(IEnumerable<string> collections, string? selected)
    {
        CaptureCollection.Items.Clear();
        foreach (string name in collections.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
            CaptureCollection.Items.Add(new ComboBoxItem { Content = $"Team messages/{name}", Tag = name });
        if (CaptureCollection.Items.Count == 0)
            CaptureCollection.Items.Add(new ComboBoxItem { Content = "Team messages/Orders", Tag = "Orders" });
        CaptureCollection.SelectedItem = CaptureCollection.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selected, StringComparison.OrdinalIgnoreCase))
            ?? CaptureCollection.Items[0];
    }
    public string ChosenDestination => (string)((ComboBoxItem)DestinationPicker.SelectedItem).Tag;
    public event Action<string>? ViewDestinationRequested;
    public event Action? EditMappingRequested;
    public event Action? ValidateAgainRequested;

    // Keep the dialog's long-standing test and automation lookup contract while
    // the review workflow is hosted by its reusable UserControl namescope.
    public new object? FindName(string name) =>
        ReviewSurfaceControl.NamedElements.TryGetValue(name, out FrameworkElement? element)
            ? element
            : base.FindName(name);

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

    public PrototypeRunSnapshot? CaptureRun() => ReviewSurfaceControl.CaptureRun();

    public void RestoreRun(PrototypeRunSnapshot run)
    {
        ReviewSurfaceControl.ShowPriorResults(run);
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
        ReviewSurfaceControl.SetReviewProperties(messageIds, ttl, details);
    }

    public void SetPreparedMessages(IReadOnlyList<PrototypePreparedMessage> messages)
    {
        ReviewSurfaceControl.SetPreparedMessages(messages);
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

    private sealed record ValidationRow(int Row, string CustomerId, string Status);

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
        var association = CaptureAssociation.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), topic,
                StringComparison.OrdinalIgnoreCase));
        if (association is null && message is not null && !string.IsNullOrWhiteSpace(topic))
        {
            association = new ComboBoxItem { Content = $"{topic} (Source)", Tag = topic };
            CaptureAssociation.Items.Add(association);
        }
        CaptureAssociation.SelectedItem = association ?? CaptureAssociation.Items.OfType<ComboBoxItem>().FirstOrDefault();
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
        var picker = new MessageLibraryPrototypeDialog(PrototypeDialogMode.SampleCsv, ReviewSurfaceControl.Profile,
            ReviewSurfaceControl.Target, 3) { Owner = this };
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

}
