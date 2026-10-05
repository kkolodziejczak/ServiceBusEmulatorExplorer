using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class ReplayReviewWindow : Window
{
    public IReadOnlyList<ReplayReviewRow> Rows { get; }
    public IReadOnlyList<string> ReviewedIds => Rows.Select(row => row.MessageId).ToArray();

    public ReplayReviewWindow(InvestigationProfile profile, IReadOnlyList<PreparedReplay> candidates)
    {
        if (candidates.Count == 0) throw new ArgumentException("Select messages to replay.");
        InitializeComponent();
        ProfileTheme.Apply(this, profile.ColorHex);
        Rows = candidates.Select(candidate => new ReplayReviewRow(candidate)).ToArray();
        foreach (var row in Rows) row.PropertyChanged += (_, _) => UpdateValidation();
        ReplayRows.ItemsSource = Rows;
        ReviewHeading.Text = ConfirmLabel.Text = candidates.Count == 1 ? "Replay 1 message" : $"Replay {candidates.Count} messages";
        ProfileText.Text = profile.Connection.Name;
        System.Windows.Automation.AutomationProperties.SetName(ReplayConfirmButton, ConfirmLabel.Text);
        Loaded += (_, _) => CancelButton.Focus();
        UpdateValidation();
    }

    private void UpdateValidation() => ReplayConfirmButton.IsEnabled = Rows.All(row => row.Problem is null);
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ReplayReviewRow row) row.MessageId = row.Prepared.Reservation.MessageId;
    }

    private void FixId_Click(object sender, RoutedEventArgs e)
    {
        var row = ((FrameworkElement)sender).DataContext;
        var container = ReplayRows.ItemContainerGenerator.ContainerFromItem(row);
        FindIdBox(container)?.Focus();
    }

    private static TextBox? FindIdBox(DependencyObject? element)
    {
        if (element is null) return null;
        if (element is TextBox box && box.Name == "MessageIdBox") return box;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindIdBox(VisualTreeHelper.GetChild(element, i)) is { } match) return match;
        return null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (Rows.All(row => row.Problem is null)) DialogResult = true;
    }
}

public sealed class ReplayReviewRow(PreparedReplay prepared) : ObservableObject
{
    private string messageId = prepared.Reservation.MessageId;
    public PreparedReplay Prepared { get; } = prepared;
    public string MessageId
    {
        get => messageId;
        set
        {
            if (!SetProperty(ref messageId, value)) return;
            OnPropertyChanged(nameof(Problem));
            OnPropertyChanged(nameof(ProblemVisibility));
        }
    }
    public string? Problem => ReplayAttempt.MessageIdProblem(MessageId);
    public Visibility ProblemVisibility => Problem is null ? Visibility.Collapsed : Visibility.Visible;
    public string Source => Path(Prepared.Delivery.Identity.Source) + " · Dead letter";
    public string Destination => Path(ReplayLineage.Destination(Prepared.Delivery.Identity.Source))
        + (Prepared.Delivery.Identity.Source.Kind == EntityKind.Subscription ? " (Topic)" : " (Queue)");
    public string OriginalId => Prepared.Delivery.Message.MessageId;
    public string Notice => Prepared.Delivery.Identity.Source.Kind == EntityKind.Subscription
        ? "Other matching subscriptions may receive this copy. The original DLQ message stays."
        : "A new active copy will be sent. The original DLQ message stays.";
    public string BodySummary => Prepared.EditedBody is null ? "Original body" : "Modified JSON";
    private static string Path(EntityAddress source) => source.TopicName is null ? source.Name : $"{source.TopicName} / {source.Name}";
}
