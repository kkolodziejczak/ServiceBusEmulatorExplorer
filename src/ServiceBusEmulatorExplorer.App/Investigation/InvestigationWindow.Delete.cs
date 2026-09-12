using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private CancellationTokenSource? deleteCancellation;

    private DeleteSelectionState CurrentDeleteSelection() => DeleteSelection.Evaluate(workspace.IsConnected,
        workspace.Surface.Messages, workspace.Surface.FocusedMessage);

    private void UpdateDeleteSurface()
    {
        var selection = CurrentDeleteSelection();
        bool pending = deleteCancellation is not null;
        string label = pending ? "Cancel delete" : selection.Targets.Count > 1 ? $"Delete ({selection.Targets.Count})" : "Delete";
        if (DeleteButton.Content is StackPanel panel && panel.Children.OfType<TextBlock>().FirstOrDefault() is { } text) text.Text = label;
        AutomationProperties.SetName(DeleteButton, label);
        DeleteButton.IsEnabled = pending ? !deleteCancellation!.IsCancellationRequested : !replayPending && selection.CanDelete;
        DeleteButton.ToolTip = pending ? "Stop acquiring further deliveries. Already confirmed deletions remain deleted."
            : selection.Problem ?? "Delete checked dead-letter messages, or the focused message, after typed confirmation.";
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (deleteCancellation is not null)
        {
            deleteCancellation.Cancel();
            UpdateDeleteSurface();
            return;
        }
        if (replayPending) return;
        var selection = CurrentDeleteSelection();
        if (!selection.CanDelete) return;
        string profileId = workspace.SelectedProfile.Id;
        long generation = workspace.ConnectionGeneration;
        var confirmation = new DeleteMessagesWindow(selection.Targets, workspace.SelectedProfile.ColorHex) { Owner = this };
        if (confirmation.ShowDialog() != true || closing || closePending
            || workspace.SelectedProfile.Id != profileId || workspace.ConnectionGeneration != generation) return;
        using var cancellation = new CancellationTokenSource();
        deleteCancellation = cancellation;
        UpdateInspector();
        try
        {
            var result = await workspace.DeleteAsync(confirmation.Targets, cancellation.Token);
            if (workspace.SelectedProfile.Id != profileId || workspace.ConnectionGeneration != generation) return;
            foreach (var outcome in result.Deletion.Outcomes)
            {
                string description = outcome.Status switch
                {
                    DlqDeleteStatus.Confirmed => "deleted",
                    DlqDeleteStatus.Uncertain => "outcome uncertain; retained in view until verified",
                    DlqDeleteStatus.Unavailable => "could not acquire and verify; retained in view",
                    _ => "not attempted; retained in view"
                };
                var source = outcome.Identity.Source;
                string path = source.TopicName is null ? source.Name : $"{source.TopicName}/{source.Name}";
                workspace.Log($"DLQ {path} / sequence {outcome.Identity.SequenceNumber}: {description}.", outcome.Status != DlqDeleteStatus.Confirmed);
            }
            int confirmed = result.Deletion.Outcomes.Count(outcome => outcome.Status == DlqDeleteStatus.Confirmed);
            workspace.Log($"Delete: {confirmed} confirmed, {result.Deletion.Outcomes.Count - confirmed} not confirmed.", confirmed != result.Deletion.Outcomes.Count);
            if (result.Deletion.ScanLimitReached) workspace.Log("Delete scan reached its limit. Unconfirmed deliveries were not removed from the view by this operation.", true);
            if (result.Deletion.CleanupIncomplete) workspace.Log("Some DLQ locks could not be released; they may remain held until their broker lock expires.", true);
            if (result.CleanupPersistenceFailed) workspace.Log("Deletion is confirmed, but replay-counter cleanup could not be saved. It remains pending in this session.", true);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (workspace.SelectedProfile.Id == profileId && workspace.ConnectionGeneration == generation)
                workspace.Log("Delete could not complete. Inspect the DLQ before retrying; no additional deletion is assumed.", true);
        }
        finally
        {
            deleteCancellation = null;
            if (!closing) UpdateInspector();
        }
    }
}
