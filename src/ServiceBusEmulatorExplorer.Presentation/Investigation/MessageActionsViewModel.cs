using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>What the window does for the message actions: the confirmation dialogs and its closing state.</summary>
public interface IMessageActionsHost
{
    /// <summary>Asks for the typed delete confirmation; returns the deliveries to delete, or <c>null</c> when cancelled.</summary>
    IReadOnlyList<MessageDelivery>? ConfirmDelete(IReadOnlyList<MessageDelivery> targets);

    /// <summary>Shows the replay review; returns the reviewed message ID of each candidate, or <c>null</c> when cancelled.</summary>
    IReadOnlyList<string>? ReviewReplays(IReadOnlyList<PreparedReplay> candidates);

    /// <summary>The window has finished closing.</summary>
    bool IsClosing { get; }

    /// <summary>The window is asking to close and waits for drafts and saves.</summary>
    bool IsClosePending { get; }

    void ShowReplayHistory();
}

/// <summary>The Delete and Replay actions on the message surface: what each button shows and the delete and replay runs.</summary>
public sealed class MessageActionsViewModel : ObservableObject
{
    private readonly InvestigationWorkspace workspace;
    private readonly IMessageActionsHost host;
    private CancellationTokenSource? deleteCancellation;
    private bool isReplayPending;

    public MessageActionsViewModel(InvestigationWorkspace workspace, IMessageActionsHost host)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(host);
        this.workspace = workspace;
        this.host = host;
    }

    public bool IsDeletePending => deleteCancellation is not null;

    public bool IsReplayPending
    {
        get => isReplayPending;
        private set => SetProperty(ref isReplayPending, value);
    }

    /// <summary>The Delete button for the checked messages; while a delete runs it cancels the delete.</summary>
    public MessageActionState Delete
    {
        get
        {
            var selection = CurrentDeleteSelection();
            if (deleteCancellation is { } pending)
                return new("Cancel delete", !pending.IsCancellationRequested,
                    "Stop acquiring further deliveries. Already confirmed deletions remain deleted.");
            string label = selection.Targets.Count > 1 ? $"Delete ({selection.Targets.Count})" : "Delete";
            return new(label, !IsReplayPending && selection.CanDelete,
                selection.Problem ?? "Delete checked dead-letter messages after typed confirmation.");
        }
    }

    /// <summary>The Replay button and the replay notice for the focused or checked dead-letter messages.</summary>
    public ReplayActionState Replay
    {
        get
        {
            var selection = CurrentReplaySelection();
            bool visible = workspace.Surface.FocusedMessage?.IsDeadLetter == true
                || selection.Targets.Any(delivery => delivery.Identity.Bucket == MessageBucket.DeadLetter);
            string label = IsReplayPending ? "Replaying…" : workspace.Inspector.IsDirty ? "Edit and Replay"
                : selection.Targets.Count > 1 ? $"Replay ({selection.Targets.Count})" : "Replay";
            return new(visible, label, !IsReplayPending && !IsDeletePending && selection.CanReplay, selection.Problem,
                visible && (workspace.Inspector.IsDirty || selection.Problem is not null));
        }
    }

    /// <summary>Deletes the checked dead-letter messages after confirmation, or cancels the delete that is running.</summary>
    public async Task DeleteAsync()
    {
        if (deleteCancellation is not null)
        {
            deleteCancellation.Cancel();
            OnPropertyChanged(nameof(Delete));
            return;
        }
        if (IsReplayPending) return;
        var selection = CurrentDeleteSelection();
        if (!selection.CanDelete) return;
        string profileId = workspace.SelectedProfile.Id;
        long generation = workspace.ConnectionGeneration;
        var confirmed = host.ConfirmDelete(selection.Targets);
        if (confirmed is null || host.IsClosing || host.IsClosePending
            || workspace.SelectedProfile.Id != profileId || workspace.ConnectionGeneration != generation) return;
        using var cancellation = new CancellationTokenSource();
        SetDeleteCancellation(cancellation);
        try
        {
            var result = await workspace.DeleteAsync(confirmed, cancellation.Token);
            if (workspace.SelectedProfile.Id != profileId || workspace.ConnectionGeneration != generation) return;
            LogDeletion(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (workspace.SelectedProfile.Id == profileId && workspace.ConnectionGeneration == generation)
                workspace.Log("Delete could not complete. Inspect the DLQ before retrying; no additional deletion is assumed.", true);
        }
        finally
        {
            SetDeleteCancellation(null);
        }
    }

    /// <summary>Replays the focused or checked dead-letter messages after review, one at a time while the session lasts.</summary>
    public async Task ReplayAsync()
    {
        if (IsReplayPending || IsDeletePending) return;
        var selection = CurrentReplaySelection();
        if (!selection.CanReplay) return;
        string profileId = workspace.SelectedProfile.Id;
        long generation = workspace.ConnectionGeneration;
        int confirmed = 0, uncertain = 0, notSent = 0, attempted = 0;
        bool currentSession() => workspace.SelectedProfile.Id == profileId && workspace.ConnectionGeneration == generation;
        IsReplayPending = true;
        try
        {
            var candidates = workspace.PrepareReplays(selection.Targets, selection.EditedBody);
            var reviewedIds = host.ReviewReplays(candidates);
            if (reviewedIds is null) return;
            for (int index = 0; index < candidates.Count; index++)
            {
                if (host.IsClosing || host.IsClosePending || !currentSession() || !workspace.IsConnected) break;
                var delivery = candidates[index].Delivery;
                ReplayCopyOutcome outcome;
                attempted++;
                try
                {
                    outcome = await workspace.SendReplayAsync(candidates[index], reviewedIds[index]);
                }
                catch (Exception)
                {
                    if (!currentSession()) break;
                    notSent++;
                    workspace.Log($"Replay of {delivery.Message.MessageId} was not sent: its reservation could not be saved or the delivery is no longer current.", true);
                    continue;
                }
                if (!currentSession()) break;
                if (outcome.Status == ReplaySendStatus.Confirmed) confirmed++;
                else if (outcome.Status == ReplaySendStatus.Uncertain) uncertain++;
                else notSent++;
                string result = outcome.Status switch
                {
                    ReplaySendStatus.Confirmed => "sent; original DLQ message retained",
                    ReplaySendStatus.Uncertain => "send outcome uncertain; inspect before manually retrying",
                    _ => "not sent; reserved attempt retained"
                };
                workspace.Log($"Replay {outcome.Reservation.MessageId}: {result}.", outcome.Status != ReplaySendStatus.Confirmed);
                if (outcome.Status == ReplaySendStatus.Confirmed && selection.EditedBody is not null)
                    workspace.Inspector.DiscardReplayedDraft(delivery.Identity, selection.EditedBody);
            }
        }
        catch (Exception)
        {
            if (currentSession()) workspace.Log("Replay review could not be prepared. Refresh and select current DLQ messages.", true);
        }
        finally
        {
            if (currentSession() && !host.IsClosing && selection.Targets.Count > 1)
                workspace.Log($"Replay batch: {confirmed} sent, {uncertain} uncertain, {notSent} not sent, {selection.Targets.Count - attempted} canceled.",
                    uncertain > 0 || notSent > 0);
            IsReplayPending = false;
            if (attempted > 0 && currentSession() && !host.IsClosing) host.ShowReplayHistory();
        }
    }

    private DeleteSelectionState CurrentDeleteSelection() =>
        DeleteSelection.Evaluate(workspace.IsConnected, workspace.Surface.Messages);

    private ReplaySelectionState CurrentReplaySelection() => ReplaySelection.Evaluate(workspace.IsConnected,
        workspace.Surface.Messages, workspace.Surface.FocusedMessage, workspace.Inspector);

    private void SetDeleteCancellation(CancellationTokenSource? cancellation)
    {
        deleteCancellation = cancellation;
        OnPropertyChanged(nameof(IsDeletePending));
    }

    private void LogDeletion(WorkspaceDeleteResult result)
    {
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
}

/// <summary>A message action button: its label, whether it can be used, and its tooltip.</summary>
public sealed record MessageActionState(string Label, bool IsEnabled, string ToolTip);

/// <summary>
/// The Replay button and its notice: shown for dead-letter messages, with the reason replay is blocked and whether the
/// replay actions row (edit notice and Discard) shows.
/// </summary>
public sealed record ReplayActionState(bool IsVisible, string Label, bool IsEnabled, string? Problem, bool ShowsActions);
