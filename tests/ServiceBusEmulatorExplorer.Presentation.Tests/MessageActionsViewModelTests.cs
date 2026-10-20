using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class MessageActionsViewModelTests
{
    [Fact]
    public async Task Requires_a_workspace_and_a_host()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        Assert.Equal("workspace", Assert.Throws<ArgumentNullException>(() => new MessageActionsViewModel(null!, new Host())).ParamName);
        Assert.Equal("host", Assert.Throws<ArgumentNullException>(() => new MessageActionsViewModel(fixture.Workspace, null!)).ParamName);
    }

    [Fact]
    public async Task Delete_counts_checked_dead_letter_messages_and_explains_why_it_is_unavailable()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var actions = new MessageActionsViewModel(fixture.Workspace, new Host());

        Assert.Equal(new MessageActionState("Delete", false, "Check dead-letter messages to delete."), actions.Delete);
        fixture.Check(0);
        Assert.Equal(new MessageActionState("Delete", true, "Delete checked dead-letter messages after typed confirmation."), actions.Delete);
        fixture.Check(1);
        Assert.Equal(new MessageActionState("Delete (2)", true, "Delete checked dead-letter messages after typed confirmation."), actions.Delete);

        await fixture.Workspace.DisconnectAsync();

        Assert.Equal(new MessageActionState("Delete", false, "Connect before deleting dead-letter messages."), actions.Delete);
    }

    [Fact]
    public async Task Confirmed_delete_runs_for_the_confirmed_messages_and_logs_each_outcome()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var host = new Host();
        var actions = new MessageActionsViewModel(fixture.Workspace, host);
        var pending = new List<bool>();
        actions.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(actions.IsDeletePending)) pending.Add(actions.IsDeletePending); };
        fixture.Check(0, 1);

        await actions.DeleteAsync();

        Assert.Equal([1L, 2L], Assert.Single(host.DeleteRequests).Select(delivery => delivery.Identity.SequenceNumber));
        Assert.Equal([1L, 2L], fixture.Receiver.Completed);
        Assert.Equal([true, false], pending);
        Assert.Contains("DLQ orders / sequence 1: deleted.", fixture.Log);
        Assert.Contains("Delete: 2 confirmed, 0 not confirmed.", fixture.Log);
    }

    [Fact]
    public async Task Nothing_is_deleted_when_the_confirmation_is_cancelled_or_the_window_is_closing()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var host = new Host { Confirm = _ => null };
        var actions = new MessageActionsViewModel(fixture.Workspace, host);
        fixture.Check(0);

        await actions.DeleteAsync();
        host.Confirm = targets => targets;
        host.IsClosePending = true;
        await actions.DeleteAsync();
        host.IsClosePending = false;
        host.IsClosing = true;
        await actions.DeleteAsync();

        Assert.Equal(3, host.DeleteRequests.Count);
        Assert.Empty(fixture.Receiver.Completed);
        Assert.DoesNotContain(fixture.Log, message => message.StartsWith("Delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_is_dropped_when_the_connection_changes_during_confirmation()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var workspace = fixture.Workspace;
        var host = new Host
        {
            Confirm = targets =>
            {
                Task.Run(async () => { await workspace.DisconnectAsync(); await workspace.ConnectAsync(); }).GetAwaiter().GetResult();
                return targets;
            }
        };
        var actions = new MessageActionsViewModel(workspace, host);
        bool pendingAnnounced = false;
        actions.PropertyChanged += (_, e) => pendingAnnounced |= e.PropertyName == nameof(actions.IsDeletePending);
        fixture.Check(0);

        await actions.DeleteAsync();

        Assert.True(workspace.IsConnected);
        Assert.Empty(fixture.Receiver.Completed);
        Assert.False(pendingAnnounced);
        Assert.DoesNotContain(fixture.Log, message => message.StartsWith("Delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_turns_into_cancel_while_it_runs_and_a_cancelled_delete_logs_no_failure()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var actions = new MessageActionsViewModel(fixture.Workspace, new Host());
        var release = new TaskCompletionSource();
        fixture.Receiver.Complete = (_, token) => release.Task.WaitAsync(token);
        fixture.Check(0, 1);

        Task deleting = actions.DeleteAsync();

        Assert.True(actions.IsDeletePending);
        Assert.Equal(new MessageActionState("Cancel delete", true,
            "Stop acquiring further deliveries. Already confirmed deletions remain deleted."), actions.Delete);
        Assert.False(actions.Replay.IsEnabled);
        var announced = new List<string?>();
        actions.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        await actions.DeleteAsync();

        Assert.Equal([nameof(actions.Delete)], announced);
        Assert.False(actions.Delete.IsEnabled);
        await deleting;
        Assert.False(actions.IsDeletePending);
        Assert.DoesNotContain(fixture.Log, message => message.StartsWith("Delete could not complete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_delete_cancelled_while_it_waits_for_another_operation_logs_nothing()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var release = new TaskCompletionSource();
        fixture.Sender.OnSend = _ => release.Task;
        Task replaying = fixture.Workspace.ReplayAsync(fixture.Rows[2].Delivery);
        var actions = new MessageActionsViewModel(fixture.Workspace, new Host());
        fixture.Check(0);

        Task deleting = actions.DeleteAsync();
        await actions.DeleteAsync();
        await deleting;
        release.SetResult();
        await replaying;

        Assert.Empty(fixture.Receiver.Completed);
        Assert.False(actions.IsDeletePending);
        Assert.DoesNotContain(fixture.Log, message => message.StartsWith("Delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_delete_that_cannot_run_tells_the_user_to_inspect_before_retrying()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var current = fixture.Rows[0].Delivery;
        var stale = new MessageDelivery(current.Identity with { ConnectionGeneration = -1 }, current.Message);
        var actions = new MessageActionsViewModel(fixture.Workspace, new Host { Confirm = _ => [stale] });
        fixture.Check(0);

        await actions.DeleteAsync();

        Assert.Contains("Delete could not complete. Inspect the DLQ before retrying; no additional deletion is assumed.", fixture.Log);
        Assert.Empty(fixture.Receiver.Completed);
        Assert.False(actions.IsDeletePending);
    }

    [Fact]
    public async Task Replay_shows_for_dead_letter_messages_and_counts_the_checked_ones()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var actions = new MessageActionsViewModel(fixture.Workspace, new Host());
        fixture.Workspace.Browse.FocusedMessage = null;
        Assert.Equal(new ReplayActionState(false, "Replay", false, null, false), actions.Replay);

        fixture.Focus(0);
        Assert.Equal(new ReplayActionState(true, "Replay", true, null, false), actions.Replay);

        fixture.Check(0, 2);
        Assert.Equal("Replay (2)", actions.Replay.Label);

        fixture.Workspace.Inspector.Document.Text = "{\"edited\":true}";
        Assert.Equal("Edit and Replay", actions.Replay.Label);
        Assert.True(actions.Replay.ShowsActions);
        Assert.Equal("Select only this message to replay its edited JSON.", actions.Replay.Problem);
        Assert.False(actions.Replay.IsEnabled);
    }

    [Fact]
    public async Task Reviewed_replay_sends_each_reviewed_id_logs_the_batch_and_opens_history()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var host = new Host { Review = candidates => candidates.Select((_, index) => "reviewed-" + index).ToArray() };
        var actions = new MessageActionsViewModel(fixture.Workspace, host);
        var pending = new List<bool>();
        actions.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(actions.IsReplayPending)) pending.Add(actions.IsReplayPending); };
        fixture.Check(0, 1);

        await actions.ReplayAsync();

        Assert.Equal(2, Assert.Single(host.ReviewRequests).Count);
        Assert.Equal(["reviewed-0", "reviewed-1"], fixture.Sender.Sends.Select(message => message.MessageId));
        Assert.Contains("Replay reviewed-0: sent; original DLQ message retained.", fixture.Log);
        Assert.Contains("Replay batch: 2 sent, 0 uncertain, 0 not sent, 0 canceled.", fixture.Log);
        Assert.Equal([true, false], pending);
        Assert.Equal(1, host.HistoryShown);
    }

    [Fact]
    public async Task Replay_shows_replaying_and_blocks_delete_until_the_review_ends()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        MessageActionsViewModel? actions = null;
        ReplayActionState? during = null;
        MessageActionState? deleteDuring = null;
        Task? deleteDuringReplay = null;
        var host = new Host
        {
            Review = _ =>
            {
                during = actions!.Replay;
                deleteDuring = actions.Delete;
                deleteDuringReplay = actions.DeleteAsync();
                return null;
            }
        };
        actions = new MessageActionsViewModel(fixture.Workspace, host);
        fixture.Check(0);

        await actions.ReplayAsync();

        Assert.Equal("Replaying…", during!.Label);
        Assert.False(during.IsEnabled);
        Assert.False(deleteDuring!.IsEnabled);
        Assert.True(deleteDuringReplay!.IsCompletedSuccessfully);
        Assert.Empty(host.DeleteRequests);
        Assert.Empty(fixture.Sender.Sends);
        Assert.False(actions.IsReplayPending);
        Assert.Equal(0, host.HistoryShown);
    }

    [Fact]
    public async Task A_confirmed_edited_replay_discards_the_draft_it_sent()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var actions = new MessageActionsViewModel(fixture.Workspace, new Host());
        fixture.Focus(0);
        fixture.Workspace.Inspector.Document.Text = "{\"edited\":true}";
        Assert.Equal(new ReplayActionState(true, "Edit and Replay", true, null, true), actions.Replay);

        await actions.ReplayAsync();

        Assert.Equal("{\"edited\":true}", Assert.Single(fixture.Sender.Sends).Body.ToString());
        Assert.False(fixture.Workspace.Inspector.IsDirty);
        Assert.DoesNotContain(fixture.Log, message => message.StartsWith("Replay batch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_batch_stops_when_the_window_starts_closing_and_counts_the_rest_as_canceled()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var host = new Host();
        fixture.Sender.OnSend = _ => { host.IsClosePending = true; return Task.CompletedTask; };
        var actions = new MessageActionsViewModel(fixture.Workspace, host);
        fixture.Check(0, 1, 2);

        await actions.ReplayAsync();

        Assert.Single(fixture.Sender.Sends);
        Assert.Contains("Replay batch: 1 sent, 0 uncertain, 0 not sent, 2 canceled.", fixture.Log);
        Assert.Equal(1, host.HistoryShown);
    }

    [Fact]
    public async Task A_replay_that_cannot_be_prepared_is_reported()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var host = new Host { Review = _ => throw new InvalidOperationException("dialog failed") };
        var actions = new MessageActionsViewModel(fixture.Workspace, host);
        fixture.Focus(0);

        await actions.ReplayAsync();

        Assert.Contains("Replay review could not be prepared. Refresh and select current DLQ messages.", fixture.Log);
        Assert.False(actions.IsReplayPending);
    }

    private sealed class Host : IMessageActionsHost
    {
        public Func<IReadOnlyList<MessageDelivery>, IReadOnlyList<MessageDelivery>?> Confirm { get; set; } = targets => targets;
        public Func<IReadOnlyList<PreparedReplay>, IReadOnlyList<string>?> Review { get; set; } =
            candidates => candidates.Select(candidate => candidate.Reservation.MessageId).ToArray();
        public List<IReadOnlyList<MessageDelivery>> DeleteRequests { get; } = [];
        public List<IReadOnlyList<PreparedReplay>> ReviewRequests { get; } = [];
        public int HistoryShown { get; private set; }
        public bool IsClosing { get; set; }
        public bool IsClosePending { get; set; }

        public IReadOnlyList<MessageDelivery>? ConfirmDelete(IReadOnlyList<MessageDelivery> targets)
        {
            DeleteRequests.Add(targets);
            return Confirm(targets);
        }

        public IReadOnlyList<string>? ReviewReplays(IReadOnlyList<PreparedReplay> candidates)
        {
            ReviewRequests.Add(candidates);
            return Review(candidates);
        }

        public void ShowReplayHistory() => HistoryShown++;
    }
}
