using System.Globalization;
using System.Windows;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeReviewSurface
{
    private const int MaximumCancellationAttempts = 10;

    private void ShowCancellation_Click(object sender, RoutedEventArgs e)
    {
        SetSurface(CancellationSurface);
        CancellationError.Visibility = Visibility.Collapsed;
        CancellationTarget.Text = $"{ReviewProfile.Text} · localhost · {ReviewTarget.Text} · " +
            string.Join("; ", scheduledResults.Select(row => row.DueTime).Distinct());
        UpdateCancellationActions();
        SetWindowTitle("Scheduled results");
    }

    private void UpdateCancellationActions()
    {
        RetryCancellation.IsEnabled = scheduledResults.Any(row => row.Selected && CanCancel(row));
        bool capped = scheduledResults.Any(row => AttemptCount(row) >= MaximumCancellationAttempts);
        CancellationHistoryNotice.Text = capped
            ? "10 attempts reached for a receipt; further attempts for that receipt are disabled. Dispatch results remain unchanged."
            : "Dispatch results remain unchanged. Retry requires fresh confirmation; only future, unacknowledged receipts are eligible.";
    }

    private bool CanCancel(PrototypeScheduledResult row) =>
        row.Outcome is "Confirmed scheduled" or "Scheduled" &&
        row.CancellationStatus != "Cancellation acknowledged" &&
        AttemptCount(row) < MaximumCancellationAttempts &&
        long.TryParse(row.Receipt, NumberStyles.None, CultureInfo.InvariantCulture, out _) &&
        TryGetDueUtc(row, out DateTime dueUtc) && dueUtc > DateTime.UtcNow;

    private int AttemptCount(PrototypeScheduledResult row) =>
        cancellationAttempts.Count(attempt => attempt.Row == row.Row && attempt.Receipt == row.Receipt);

    private static bool TryGetDueUtc(PrototypeScheduledResult row, out DateTime dueUtc)
    {
        string value = row.DueTime.Split('·')[0].Replace("UTC", "", StringComparison.Ordinal).Trim();
        const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, styles, out dueUtc) ||
            DateTime.TryParse(value, CultureInfo.CurrentCulture, styles, out dueUtc);
    }

    private void BackToResults_Click(object sender, RoutedEventArgs e)
    {
        SetSurface(ResultsSurface);
        SetWindowTitle("Run results");
    }

    private void ConfirmCancellation_Click(object sender, RoutedEventArgs e)
    {
        var selected = scheduledResults.Where(row => row.Selected && CanCancel(row)).ToArray();
        if (selected.Length == 0)
        {
            CancellationError.Text = "Select at least one future scheduled receipt without an acknowledged cancellation or 10 prior attempts.";
            CancellationError.Visibility = Visibility.Visible;
            UpdateCancellationActions();
            return;
        }
        CancellationError.Visibility = Visibility.Collapsed;
        var confirmation = new MessageLibraryPrototypeDialog(PrototypeDialogMode.CancellationConfirmation,
            ReviewProfile.Text, ReviewTarget.Text, selected.Length) { Owner = Window.GetWindow(this) };
        if (TryFindResource("PrimaryBrush") is System.Windows.Media.SolidColorBrush accent)
            ProfileTheme.Apply(confirmation, accent.Color.ToString());
        confirmation.ConfigureCancellationConfirmation(
            $"Profile: {ReviewProfile.Text}\nEndpoint: localhost\nDestination: {ReviewTarget.Text}\nSelected: {selected.Length}\n\n" +
            string.Join("\n", selected.Select(CancellationReceiptSummary)));
        if (confirmation.ShowDialog() != true || !confirmation.CancellationConfirmed) return;

        bool stopped = false;
        foreach (var row in selected)
        {
            if (!CanCancel(row)) continue;
            row.CancellationStatus = stopped ? "Not attempted" :
                row.Row % 2 == 0 ? "Outcome unknown" : "Cancellation acknowledged";
            DateTimeOffset requestedAt = DateTimeOffset.UtcNow;
            row.AttemptTime = requestedAt.ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture);
            cancellationAttempts.Add(new PrototypeCancellationAttempt(row.Row, row.Receipt, row.Payload,
                row.CancellationStatus, requestedAt));
            if (row.CancellationStatus == "Cancellation acknowledged") row.Selected = false;
            else if (row.CancellationStatus == "Outcome unknown") stopped = true;
        }
        ScheduledGrid.Items.Refresh();
        UpdateCancellationActions();
        SetSurface(HistorySurface);
        SetWindowTitle("Cancellation history");
        if (CaptureRun() is { } run) RunCompleted?.Invoke(run);
    }

    private static string CancellationReceiptSummary(PrototypeScheduledResult row)
    {
        TryGetDueUtc(row, out DateTime utc);
        return $"Receipt {row.Receipt} · {row.DueTime}\nLocal: {utc.ToLocalTime():yyyy-MM-dd HH:mm zzz}";
    }
}
