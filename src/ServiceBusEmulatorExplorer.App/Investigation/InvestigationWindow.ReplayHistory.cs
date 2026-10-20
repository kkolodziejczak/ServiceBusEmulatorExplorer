using System.Windows;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private ReplayHistoryWindow? replayHistoryWindow;

    private void ReplayHistory_Click(object sender, RoutedEventArgs e) => ShowReplayHistory();
    private void ShowReplayHistory()
    {
        if (replayHistoryWindow is not null) { replayHistoryWindow.Activate(); return; }
        replayHistoryWindow = new ReplayHistoryWindow(workspace, FindReplayIdsAsync, FindReplayOriginalIdAsync,
            DeleteReplayOriginalAsync, () => workspace.CheckReplayStatusAsync(), copyText) { Owner = this };
        replayHistoryWindow.Closed += (_, _) => replayHistoryWindow = null;
        replayHistoryWindow.Show();
    }

    private async Task FindReplayIdsAsync(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0 || !workspace.IsConnected) return;
        SelectWorkspaceTab(false);
        SearchBox.Text = searchSurface.QueryForReplays(ids);
        await BeginGlobalSearchAsync(true);
    }

    private async Task FindReplayOriginalIdAsync(ReplayAttempt attempt)
    {
        if (!workspace.IsConnected || !workspace.CurrentReplayHistory.Any(item => item.AttemptId == attempt.AttemptId)) return;
        SelectWorkspaceTab(false);
        SearchBox.Text = searchSurface.QueryForOriginal(attempt.OriginalMessageId);
        await BeginGlobalSearchAsync(true);
    }

    private async Task DeleteReplayOriginalAsync(ReplayAttempt attempt)
    {
        var prepared = await workspace.PrepareReplayCleanupAsync(attempt);
        if (prepared is null) return;
        var confirmation = new DeleteMessagesWindow([prepared.Original], workspace.SelectedProfile.ColorHex) { Owner = replayHistoryWindow ?? (Window)this };
        if (confirmation.ShowDialog() != true || closing || closePending) return;
        await workspace.DeleteReplayOriginalAsync(prepared);
    }
}
