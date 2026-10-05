using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class ReplayHistoryWindow : Window
{
    private readonly InvestigationWorkspace workspace;
    private readonly Func<IReadOnlyList<string>, Task> find;
    private readonly Func<ReplayAttempt, Task> findOriginal;
    private readonly Func<ReplayAttempt, Task> deleteOriginal;
    private readonly Func<Task> checkStatus;
    private bool busy;
    private string? actionNotice;

    public ReplayHistoryWindow(InvestigationWorkspace workspace, Func<IReadOnlyList<string>, Task> find,
        Func<ReplayAttempt, Task> findOriginal, Func<ReplayAttempt, Task> deleteOriginal, Func<Task> checkStatus)
    {
        InitializeComponent();
        this.workspace = workspace;
        this.find = find;
        this.findOriginal = findOriginal;
        this.deleteOriginal = deleteOriginal;
        this.checkStatus = checkStatus;
        workspace.PropertyChanged += WorkspaceChanged;
        Closed += (_, _) => workspace.PropertyChanged -= WorkspaceChanged;
        RefreshRows();
    }

    private void WorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InvestigationWorkspace.Preferences) or nameof(InvestigationWorkspace.IsConnected)
            or nameof(InvestigationWorkspace.SelectedProfile)) RefreshRows();
    }

    private void RefreshRows()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(RefreshRows); return; }
        var selectedId = (HistoryGrid.SelectedItem as ReplayHistoryRow)?.Attempt.AttemptId;
        var rows = workspace.CurrentReplayHistory.Select(attempt => new ReplayHistoryRow(attempt,
            workspace.Preferences, workspace.CanRemoveReplayHistory(attempt), workspace.IsConnected,
            workspace.IsConnected && workspace.CanReviewReplayOriginalCleanup(attempt))).ToArray();
        ProfileTheme.Apply(this, workspace.SelectedProfile.ColorHex);
        HistorySummary.Text = $"{workspace.SelectedProfile.Connection.Name} · {rows.Length} attempts";
        HistoryNotice.Text = actionNotice ?? (rows.Length == 0
            ? "No replay attempts have been saved for this connection. Replay history is local to this profile."
            : "Last observed state; not proof of API processing.");
        HistoryGrid.ItemsSource = rows;
        HistoryGrid.SelectedItem = rows.FirstOrDefault(row => row.Attempt.AttemptId == selectedId) ?? rows.FirstOrDefault();
        UpdateActions();
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => UpdateActions();

    private void UpdateActions()
    {
        if (HistoryGrid is null) return;
        bool available = !busy && workspace.IsConnected;
        FindAllButton.IsEnabled = CheckStatusButton.IsEnabled = available && HistoryGrid.Items.Count > 0;
        HistoryGrid.IsEnabled = !busy;
    }

    private async Task RunAsync(Func<Task> action, bool requireConnection = true)
    {
        if (busy || (requireConnection && !workspace.IsConnected)) return;
        busy = true;
        actionNotice = null;
        HistoryNotice.Text = "Local history. Observations are last checked states, not proof of API processing.";
        UpdateActions();
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            actionNotice = "Action could not complete. Check the connection, then retry the action.";
            HistoryNotice.Text = actionNotice;
            workspace.Log(HistoryNotice.Text, true);
        }
        finally { busy = false; RefreshRows(); }
    }

    private async void FindAll_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => find(workspace.CurrentReplayHistory.Select(attempt => attempt.Reservation.MessageId)
            .Distinct(StringComparer.Ordinal).ToArray()));

    private async void FindReplay_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ReplayHistoryRow row)
            await RunAsync(() => find([row.MessageId]));
    }

    private async void CheckStatus_Click(object sender, RoutedEventArgs e) => await RunAsync(checkStatus);

    private async void FindOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ReplayHistoryRow row)
            await RunAsync(() => findOriginal(row.Attempt));
    }

    private void CopyOriginalId_Click(object sender, RoutedEventArgs e) => CopyId(sender, row => row.OriginalMessageId);
    private void CopyReplayId_Click(object sender, RoutedEventArgs e) => CopyId(sender, row => row.MessageId);

    private void CopyId(object sender, Func<ReplayHistoryRow, string> selectId)
    {
        if (((FrameworkElement)sender).DataContext is not ReplayHistoryRow row) return;
        try
        {
            Clipboard.SetText(selectId(row));
            HistoryNotice.Text = "Message ID copied.";
        }
        catch (Exception)
        {
            HistoryNotice.Text = "Could not copy the message ID. Select it and copy manually.";
        }
    }

    private async void RemoveHistory_Click(object sender, RoutedEventArgs e)
    {
        if (busy || ((FrameworkElement)sender).DataContext is not ReplayHistoryRow row
            || !workspace.CanRemoveReplayHistory(row.Attempt)) return;
        await RunAsync(async () =>
        {
            var confirmation = new ProfileWarningWindow(workspace.SelectedProfile.Connection.Name,
                "Remove this replay from local history?\n\n" + row.MessageId
                + "\n\nNo Service Bus messages will be deleted. Internal replay safety records remain.",
                workspace.SelectedProfile.ColorHex, "Remove from history", "Remove from history") { Owner = this };
            if (confirmation.ShowDialog() != true) return;
            if (!await workspace.RemoveReplayHistoryAsync(row.Attempt))
                actionNotice = HistoryNotice.Text = "History entry was not removed. Check status and retry; if saving failed, check storage access.";
        }, requireConnection: false);
    }

    private async void DeleteOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ReplayHistoryRow row
            && workspace.IsConnected && workspace.CanReviewReplayOriginalCleanup(row.Attempt))
            await RunAsync(() => deleteOriginal(row.Attempt));
    }
}
