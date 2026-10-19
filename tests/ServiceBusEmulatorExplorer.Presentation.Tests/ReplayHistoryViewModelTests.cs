using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class ReplayHistoryViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 30, 0, TimeSpan.Zero);
    private const string ObservedNotice = "Last observed state; not proof of API processing.";
    private const string WorkingNotice = "Local history. Observations are last checked states, not proof of API processing.";
    private const string FailedNotice = "Action could not complete. Check the connection, then retry the action.";

    [Fact]
    public void Requires_the_workspace_and_every_action()
    {
        var history = new FakeHistory();
        Func<IReadOnlyList<string>, Task> find = _ => Task.CompletedTask;
        Func<ReplayAttempt, Task> attemptAction = _ => Task.CompletedTask;
        Func<Task> checkStatus = () => Task.CompletedTask;

        Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => new ReplayHistoryViewModel(null!, find, attemptAction, attemptAction, checkStatus, _ => { }, run => run())).ParamName);
        Assert.Equal("find", Assert.Throws<ArgumentNullException>(() => new ReplayHistoryViewModel(history, null!, attemptAction, attemptAction, checkStatus, _ => { }, run => run())).ParamName);
        Assert.Equal("checkStatus", Assert.Throws<ArgumentNullException>(() => new ReplayHistoryViewModel(history, find, attemptAction, attemptAction, null!, _ => { }, run => run())).ParamName);
        Assert.Equal("onUiThread", Assert.Throws<ArgumentNullException>(() => new ReplayHistoryViewModel(history, find, attemptAction, attemptAction, checkStatus, _ => { }, null!)).ParamName);
    }

    [Fact]
    public void Opens_with_the_profile_summary_its_colour_and_the_first_attempt_selected()
    {
        var history = new FakeHistory(Attempt(1), Attempt(2));

        var viewModel = Open(history);

        Assert.Equal("Local · 2 attempts", viewModel.Summary);
        Assert.Equal("#0069FA", viewModel.ProfileColorHex);
        Assert.Equal(ObservedNotice, viewModel.Notice);
        Assert.Equal(history.Attempts, viewModel.Rows.Select(row => row.Attempt));
        Assert.Same(viewModel.Rows[0], viewModel.SelectedRow);
        Assert.True(viewModel.IsIdle);
    }

    [Fact]
    public void An_empty_history_says_it_is_local_to_the_profile_and_offers_no_broker_actions()
    {
        var viewModel = Open(new FakeHistory());

        Assert.Equal("Local · 0 attempts", viewModel.Summary);
        Assert.Equal("No replay attempts have been saved for this connection. Replay history is local to this profile.", viewModel.Notice);
        Assert.Null(viewModel.SelectedRow);
        Assert.False(viewModel.FindAllCommand.CanExecute(null));
        Assert.False(viewModel.CheckStatusCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rows_carry_what_the_workspace_allows_and_original_cleanup_needs_a_connection(bool connected)
    {
        ReplayAttempt removable = Attempt(1), cleanable = Attempt(2);
        var history = new FakeHistory(removable, cleanable) { IsConnected = connected };
        history.Removable.Add(removable.AttemptId);
        history.Cleanable.Add(cleanable.AttemptId);

        var viewModel = Open(history);

        Assert.Equal([true, false], viewModel.Rows.Select(row => row.CanRemove));
        Assert.Equal([false, connected], viewModel.Rows.Select(row => row.CanDeleteOriginal));
        Assert.All(viewModel.Rows, row => Assert.Equal(connected, row.BrokerAvailable));
        Assert.Equal(connected, viewModel.FindAllCommand.CanExecute(null));
        Assert.Equal(connected, viewModel.CheckStatusCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(nameof(IReplayHistorySource.Preferences))]
    [InlineData(nameof(IReplayHistorySource.IsConnected))]
    [InlineData(nameof(IReplayHistorySource.SelectedProfile))]
    public void Workspace_changes_rebuild_the_rows_on_the_ui_thread_and_keep_the_selected_attempt(string change)
    {
        ReplayAttempt first = Attempt(1), second = Attempt(2);
        var history = new FakeHistory(first, second);
        var uiThreadRuns = new List<string>();
        var viewModel = Open(history, onUiThread: run => { uiThreadRuns.Add("ui"); run(); });
        viewModel.SelectedRow = viewModel.Rows[1];
        int availabilityChanges = 0;
        viewModel.FindAllCommand.CanExecuteChanged += (_, _) => availabilityChanges++;

        history.Attempts.Insert(0, Attempt(3));
        history.Announce(change);

        Assert.Equal(["ui"], uiThreadRuns);
        Assert.Equal("Local · 3 attempts", viewModel.Summary);
        Assert.Equal(second.AttemptId, viewModel.SelectedRow!.Attempt.AttemptId);
        Assert.Equal(1, availabilityChanges);
    }

    [Fact]
    public void Other_workspace_changes_are_ignored()
    {
        var history = new FakeHistory(Attempt(1));
        var viewModel = Open(history);
        IReadOnlyList<ReplayHistoryRow> before = viewModel.Rows;

        history.Attempts.Add(Attempt(2));
        history.Announce("Activity");

        Assert.Same(before, viewModel.Rows);
    }

    [Fact]
    public void When_the_selected_attempt_disappears_the_first_attempt_is_selected()
    {
        ReplayAttempt first = Attempt(1), second = Attempt(2);
        var history = new FakeHistory(first, second);
        var viewModel = Open(history);
        viewModel.SelectedRow = viewModel.Rows[1];

        history.Attempts.Remove(second);
        history.Announce(nameof(IReplayHistorySource.Preferences));

        Assert.Equal(first.AttemptId, viewModel.SelectedRow!.Attempt.AttemptId);
    }

    [Fact]
    public void Find_all_searches_every_distinct_replay_id_once_in_history_order()
    {
        var history = new FakeHistory(Attempt(1), Attempt(2), Attempt(1, replayId: "order-2-replay-1"));
        var searches = new List<IReadOnlyList<string>>();
        var viewModel = Open(history, find: ids => { searches.Add(ids); return Task.CompletedTask; });

        viewModel.FindAllCommand.Execute(null);

        Assert.Equal([["order-1-replay-1", "order-2-replay-1"]], searches);
    }

    [Fact]
    public void Row_actions_pass_their_own_replay_id_or_attempt()
    {
        var history = new FakeHistory(Attempt(1), Attempt(2));
        var searches = new List<IReadOnlyList<string>>();
        var originals = new List<ReplayAttempt>();
        var viewModel = Open(history, find: ids => { searches.Add(ids); return Task.CompletedTask; },
            findOriginal: attempt => { originals.Add(attempt); return Task.CompletedTask; });

        viewModel.FindReplayCommand.Execute(viewModel.Rows[1]);
        viewModel.FindOriginalCommand.Execute(viewModel.Rows[0]);

        Assert.Equal([["order-2-replay-1"]], searches);
        Assert.Equal([history.Attempts[0]], originals);
    }

    [Fact]
    public void While_an_action_runs_the_history_is_busy_and_a_second_action_is_ignored()
    {
        var history = new FakeHistory(Attempt(1));
        var running = new TaskCompletionSource();
        int checks = 0, searches = 0;
        var viewModel = Open(history, find: _ => { searches++; return Task.CompletedTask; },
            checkStatus: () => { checks++; return running.Task; });

        viewModel.CheckStatusCommand.Execute(null);

        Assert.False(viewModel.IsIdle);
        Assert.Equal(WorkingNotice, viewModel.Notice);
        Assert.False(viewModel.FindAllCommand.CanExecute(null));
        Assert.False(viewModel.CheckStatusCommand.CanExecute(null));
        viewModel.FindReplayCommand.Execute(viewModel.Rows[0]);
        Assert.Equal(0, searches);

        running.SetResult();

        Assert.True(viewModel.IsIdle);
        Assert.Equal(ObservedNotice, viewModel.Notice);
        Assert.True(viewModel.CheckStatusCommand.CanExecute(null));
        Assert.Equal(1, checks);
    }

    [Fact]
    public void Broker_actions_need_a_connection()
    {
        var history = new FakeHistory(Attempt(1)) { IsConnected = false };
        int calls = 0;
        var viewModel = Open(history, find: _ => { calls++; return Task.CompletedTask; },
            findOriginal: _ => { calls++; return Task.CompletedTask; }, checkStatus: () => { calls++; return Task.CompletedTask; });

        viewModel.FindAllCommand.Execute(null);
        viewModel.CheckStatusCommand.Execute(null);
        viewModel.FindReplayCommand.Execute(viewModel.Rows[0]);
        viewModel.FindOriginalCommand.Execute(viewModel.Rows[0]);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void A_failed_action_is_logged_and_its_notice_stays_until_the_next_action()
    {
        var history = new FakeHistory(Attempt(1));
        bool fail = true;
        var viewModel = Open(history, checkStatus: () => fail ? Task.FromException(new InvalidOperationException("broker down")) : Task.CompletedTask);

        viewModel.CheckStatusCommand.Execute(null);
        history.Announce(nameof(IReplayHistorySource.Preferences));

        Assert.Equal(FailedNotice, viewModel.Notice);
        Assert.Equal([FailedNotice], history.Warnings);
        Assert.True(viewModel.IsIdle);

        fail = false;
        viewModel.CheckStatusCommand.Execute(null);

        Assert.Equal(ObservedNotice, viewModel.Notice);
    }

    [Fact]
    public void A_cancelled_action_is_neither_reported_nor_logged()
    {
        var history = new FakeHistory(Attempt(1));
        var viewModel = Open(history, checkStatus: () => Task.FromCanceled(new CancellationToken(true)));

        viewModel.CheckStatusCommand.Execute(null);

        Assert.Equal(ObservedNotice, viewModel.Notice);
        Assert.Empty(history.Warnings);
        Assert.True(viewModel.IsIdle);
    }

    [Fact]
    public void Copying_an_id_reports_success_or_how_to_copy_it_manually()
    {
        var history = new FakeHistory(Attempt(1));
        var copied = new List<string>();
        bool fail = false;
        var viewModel = Open(history, copyText: text =>
        {
            if (fail) throw new InvalidOperationException("clipboard busy");
            copied.Add(text);
        });

        viewModel.CopyOriginalIdCommand.Execute(viewModel.Rows[0]);
        viewModel.CopyReplayIdCommand.Execute(viewModel.Rows[0]);

        Assert.Equal(["order-1", "order-1-replay-1"], copied);
        Assert.Equal("Message ID copied.", viewModel.Notice);

        fail = true;
        viewModel.CopyReplayIdCommand.Execute(viewModel.Rows[0]);

        Assert.Equal("Could not copy the message ID. Select it and copy manually.", viewModel.Notice);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Removing_asks_first_and_removes_only_when_confirmed_even_while_disconnected(bool confirmed, bool removed)
    {
        ReplayAttempt attempt = Attempt(1);
        var history = new FakeHistory(attempt) { IsConnected = false };
        history.Removable.Add(attempt.AttemptId);
        var questions = new List<string>();
        var viewModel = Open(history);
        viewModel.ConfirmRemove = question => { questions.Add(question); return confirmed; };

        viewModel.RemoveCommand.Execute(viewModel.Rows[0]);

        Assert.Equal(["Remove this replay from local history?\n\norder-1-replay-1\n\nNo Service Bus messages will be deleted. Internal replay safety records remain."], questions);
        Assert.Equal(removed ? [attempt] : [], history.Removed);
        Assert.True(viewModel.IsIdle);
    }

    [Fact]
    public void An_attempt_the_workspace_may_not_remove_is_not_asked_about()
    {
        var history = new FakeHistory(Attempt(1));
        var viewModel = Open(history);
        viewModel.ConfirmRemove = _ => throw new InvalidOperationException("Nothing should be asked.");

        viewModel.RemoveCommand.Execute(viewModel.Rows[0]);

        Assert.Empty(history.Removed);
    }

    [Fact]
    public void A_removal_that_did_not_happen_says_so_after_the_refresh()
    {
        ReplayAttempt attempt = Attempt(1);
        var history = new FakeHistory(attempt) { RemoveResult = false };
        history.Removable.Add(attempt.AttemptId);
        var viewModel = Open(history);
        viewModel.ConfirmRemove = _ => true;

        viewModel.RemoveCommand.Execute(viewModel.Rows[0]);

        Assert.Equal("History entry was not removed. Check status and retry; if saving failed, check storage access.", viewModel.Notice);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void Deleting_an_original_needs_a_connection_and_the_workspaces_approval(bool connected, bool approved, bool deleted)
    {
        ReplayAttempt attempt = Attempt(1);
        var history = new FakeHistory(attempt) { IsConnected = connected };
        if (approved) history.Cleanable.Add(attempt.AttemptId);
        var deletions = new List<ReplayAttempt>();
        var viewModel = Open(history, deleteOriginal: target => { deletions.Add(target); return Task.CompletedTask; });

        viewModel.DeleteOriginalCommand.Execute(viewModel.Rows[0]);

        Assert.Equal(deleted ? [attempt] : [], deletions);
    }

    [Fact]
    public void After_disposing_workspace_changes_are_no_longer_followed()
    {
        var history = new FakeHistory(Attempt(1));
        var viewModel = Open(history);

        viewModel.Dispose();
        history.Attempts.Add(Attempt(2));
        history.Announce(nameof(IReplayHistorySource.Preferences));

        Assert.Single(viewModel.Rows);
    }

    private static ReplayHistoryViewModel Open(FakeHistory history, Func<IReadOnlyList<string>, Task>? find = null,
        Func<ReplayAttempt, Task>? findOriginal = null, Func<ReplayAttempt, Task>? deleteOriginal = null,
        Func<Task>? checkStatus = null, Action<string>? copyText = null, Action<Action>? onUiThread = null) =>
        new(history, find ?? (_ => Task.CompletedTask), findOriginal ?? (_ => Task.CompletedTask),
            deleteOriginal ?? (_ => Task.CompletedTask), checkStatus ?? (() => Task.CompletedTask), copyText ?? (_ => { }),
            onUiThread ?? (run => run()));

    private static ReplayAttempt Attempt(int number, string? replayId = null)
    {
        var source = new EntityAddress(EntityKind.Queue, "orders");
        var family = new ReplayFamilyState(Guid.NewGuid(), new string('A', 64), $"order-{number}", source, 1);
        return new ReplayAttempt(Guid.NewGuid(), "local", "namespace", new ReplayReservation(family, replayId ?? $"order-{number}-replay-1"),
            source, number, family.RootFingerprint, $"order-{number}", Now, ReplaySendStatus.Confirmed, Now);
    }

    /// <summary>Stands in for the investigation workspace and records the history changes asked of it.</summary>
    private sealed class FakeHistory(params ReplayAttempt[] attempts) : ObservableObject, IReplayHistorySource
    {
        private static readonly InvestigationProfile Profile = new("local", ConnectionProfileDefaults.LocalEmulator with { Name = "Local" }, "#0069FA");

        public List<ReplayAttempt> Attempts { get; } = [.. attempts];

        public HashSet<Guid> Removable { get; } = [];

        public HashSet<Guid> Cleanable { get; } = [];

        public List<ReplayAttempt> Removed { get; } = [];

        public List<string> Warnings { get; } = [];

        public bool RemoveResult { get; init; } = true;

        public WorkspacePreferences Preferences { get; } = new() { Profiles = [Profile], SelectedProfileId = Profile.Id };

        public InvestigationProfile SelectedProfile => Profile;

        public bool IsConnected { get; set; } = true;

        public IReadOnlyList<ReplayAttempt> CurrentReplayHistory => Attempts.ToArray();

        public bool CanRemoveReplayHistory(ReplayAttempt requested) => Removable.Contains(requested.AttemptId);

        public bool CanReviewReplayOriginalCleanup(ReplayAttempt requested) => Cleanable.Contains(requested.AttemptId);

        public Task<bool> RemoveReplayHistoryAsync(ReplayAttempt requested, CancellationToken cancellationToken = default)
        {
            if (RemoveResult) Removed.Add(requested);
            return Task.FromResult(RemoveResult);
        }

        public void LogWarning(string message) => Warnings.Add(message);

        public void Announce(string propertyName) => OnPropertyChanged(propertyName);
    }
}
