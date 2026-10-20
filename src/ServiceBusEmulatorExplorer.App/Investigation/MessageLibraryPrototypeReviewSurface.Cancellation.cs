using System.Windows;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Presentation;
using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeReviewSurface
{
    private void ShowCancellation_Click(object sender, RoutedEventArgs e)
    {
        SetSurface(CancellationSurface);
        CancellationError.Visibility = Visibility.Collapsed;
        CancellationTarget.Text = $"{ReviewProfile.Text} · localhost · {ReviewTarget.Text} · " + workbenchRun.DueTimes;
        UpdateCancellationActions();
        SetWindowTitle("Scheduled results");
    }

    private void UpdateCancellationActions()
    {
        RetryCancellation.IsEnabled = workbenchRun.CanRetryCancellation;
        CancellationHistoryNotice.Text = workbenchRun.CancellationNotice;
    }

    private void BackToResults_Click(object sender, RoutedEventArgs e)
    {
        SetSurface(ResultsSurface);
        SetWindowTitle("Run results");
    }

    private void ConfirmCancellation_Click(object sender, RoutedEventArgs e)
    {
        var selected = workbenchRun.CancellableSelection();
        if (selected.Count == 0)
        {
            CancellationError.Text = "Select at least one future scheduled receipt without an acknowledged cancellation or 10 prior attempts.";
            CancellationError.Visibility = Visibility.Visible;
            UpdateCancellationActions();
            return;
        }
        CancellationError.Visibility = Visibility.Collapsed;
        var confirmation = new MessageLibraryPrototypeDialog(PrototypeDialogMode.CancellationConfirmation,
            ReviewProfile.Text, ReviewTarget.Text, selected.Count) { Owner = Window.GetWindow(this) };
        if (TryFindResource("PrimaryBrush") is System.Windows.Media.SolidColorBrush accent)
            ProfileTheme.Apply(confirmation, accent.Color.ToString());
        confirmation.ConfigureCancellationConfirmation(
            $"Profile: {ReviewProfile.Text}\nEndpoint: localhost\nDestination: {ReviewTarget.Text}\nSelected: {selected.Count}\n\n" +
            string.Join("\n", selected.Select(CancellationReceiptSummary)));
        if (confirmation.ShowDialog() != true || !confirmation.CancellationConfirmed) return;

        workbenchRun.Cancel(selected);
        ScheduledGrid.Items.Refresh();
        UpdateCancellationActions();
        SetSurface(HistorySurface);
        SetWindowTitle("Cancellation history");
        if (CaptureRun() is { } run) RunCompleted?.Invoke(run);
    }

    private string CancellationReceiptSummary(WorkbenchScheduledReceipt row)
    {
        DateTime utc = row.DueAtUtc?.UtcDateTime ?? default;
        return $"Receipt {row.Receipt} · {row.DueTimeDisplay}\nLocal: {DateDisplay.Timestamp(new DateTimeOffset(utc).ToLocalTime(), DatePresentation.GetFormat(this), seconds: false)} {new DateTimeOffset(utc).ToLocalTime():zzz}";
    }
}
