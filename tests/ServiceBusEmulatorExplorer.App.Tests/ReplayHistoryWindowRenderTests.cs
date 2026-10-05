using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.ReadmeScreenshot;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed partial class ReplayWindowsTests
{
    [Fact]
    public void Replay_history_projection_counts_mixed_locations_DLQ_first_and_keeps_full_checked_timestamp()
    {
        WorkspacePreferences preferences = CreateStatusStatePreferences() with
        {
            TimestampDisplay = TimestampDisplay.Utc
        };
        ReplayAttempt attempt = preferences.ReplayAttempts[0];
        DateTimeOffset checkedAt = new(2031, 4, 5, 4, 5, 6, TimeSpan.Zero);
        var observation = new ReplayObservation(checkedAt, true, 3, 14,
        [
            new(new(EntityKind.Queue, "active-one"), MessageBucket.Active, "Active"),
            new(new(EntityKind.Queue, "dead-letter"), MessageBucket.DeadLetter, "Dead-letter"),
            new(new(EntityKind.Queue, "active-two"), MessageBucket.Active, "Active")
        ]);
        ReplayHistoryRow row = new(attempt with { Observation = observation }, preferences);

        Assert.Equal("1 DLQ · 2 Active", row.Observation);
        Assert.True(row.ObservedStates[0].IsDeadLetter);
        Assert.Equal(["1 DLQ", "2 Active"], row.ObservedStates.Select(state => state.Label));
        Assert.Contains("04:05:06 UTC", row.ObservationTime, StringComparison.Ordinal);
        Assert.Contains("2031", row.ObservationTime, StringComparison.Ordinal);
        Assert.Contains("Send uncertain", row.Sent, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Replay_history_stays_modeless_and_filters_all_or_one_exact_ID_at_supported_widths()
    {
        OnSta(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var messages = new FakeMessages();
            var sender = new CapturingReplaySender(messages);
            var deleteReceiver = new CapturingDeleteReceiver(messages);
            InvestigationWorkspace workspace = CreateWorkspace(sender, messages, deleteReceiver);
            var owner = new InvestigationWindow(workspace)
            {
                Width = 1500,
                Height = 1000,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            ReplayHistoryWindow? history = null;
            IDataObject? priorClipboard = null;
            bool clipboardSaved = false;
            try
            {
                owner.Show();
                PumpUntil(dispatcher, () => owner.IsVisible, "Investigation to show");
                Complete(dispatcher, workspace.ConnectAsync());
                var historyToolbarButton = (Button)owner.FindName("ReplayHistoryButton")!;
                var watchAllButton = (Button)owner.FindName("GlobalWatchButton")!;
                foreach (double width in new[] { 1100d, 980d })
                {
                    owner.Width = width;
                    SettleLayout(dispatcher, owner);
                    Assert.Equal(Visibility.Visible, historyToolbarButton.Visibility);
                    AssertVisibleInside(historyToolbarButton, owner);
                    AssertNotOverlapping(historyToolbarButton, watchAllButton, owner);
                    Capture(owner, $"Replay-32-investigation-toolbar-{(int)width}.png");
                }
                owner.Width = 1500;
                SettleLayout(dispatcher, owner);
                EntityNode queue = workspace.Browse.AllEntities().Single(node => node.Kind == nameof(EntityKind.Queue));
                Complete(dispatcher, workspace.Browse.SelectAsync(queue, deadLetter: true));
                PumpUntil(dispatcher, () => workspace.Surface.FocusedMessage?.IsDeadLetter == true,
                    "the queue DLQ delivery to become the focused message");
                MessageDelivery delivery = workspace.Surface.FocusedMessage!.Delivery;
                Complete(dispatcher, workspace.ReplayAsync(delivery));
                PreparedReplay longIdCandidate = workspace.PrepareReplay(delivery);
                string longReplayId = "replay-" + new string('x', 100);
                Complete(dispatcher, workspace.SendReplayAsync(longIdCandidate, longReplayId));

                var reopenButton = (Button)owner.FindName("ReplayHistoryButton")!;
                Click(reopenButton);
                var firstHistory = owner.OwnedWindows.OfType<ReplayHistoryWindow>().Single();
                PumpUntil(dispatcher, () => firstHistory.IsVisible
                    && ((DataGrid)firstHistory.FindName("HistoryGrid")!).Items.Count == 2,
                    "the toolbar Replay history window with both saved attempts");
                firstHistory.Close();
                PumpUntil(dispatcher, () => !firstHistory.IsVisible, "the first history window to close");
                Click(reopenButton);
                var reopenedHistory = owner.OwnedWindows.OfType<ReplayHistoryWindow>().Single();
                Assert.NotSame(firstHistory, reopenedHistory);
                PumpUntil(dispatcher, () => reopenedHistory.IsVisible
                    && ((DataGrid)reopenedHistory.FindName("HistoryGrid")!).Items.Count == 2,
                    "Replay history to reopen with retained attempts");
                SettleHistoryLayout(dispatcher, reopenedHistory);
                Capture(reopenedHistory, "Replay-32-history-reopened-toolbar.png");
                history = reopenedHistory;
                var historyGrid = (DataGrid)history.FindName("HistoryGrid")!;
                PumpUntil(dispatcher, () => history.IsVisible && historyGrid.Items.Count == 2,
                    "the two saved attempts to appear in Replay history");
                history.Width = 1060;
                history.Height = 640;
                SettleLayout(dispatcher, history);
                AssertHistoryColumnsFit(historyGrid);
                Capture(history, "Replay-47-after-inline-1060.png");
                Assert.NotNull(historyGrid.RowDetailsTemplate);
                Assert.Equal(DataGridRowDetailsVisibilityMode.VisibleWhenSelected, historyGrid.RowDetailsVisibilityMode);
                Assert.Equal("Original message", historyGrid.Columns[0].Header);
                Assert.Equal("Replay", historyGrid.Columns[1].Header);
                Assert.Equal("Observed copies", historyGrid.Columns[2].Header);
                Capture(history, "Replay-32-history-1060x640.png");
                Assert.Contains("2 attempts", ((TextBlock)history.FindName("HistorySummary")!).Text, StringComparison.Ordinal);
                AssertSelectedDetails(history, historyGrid);
                Assert.True(((Button)history.FindName("FindAllButton")!).IsEnabled);
                Assert.True(((Button)history.FindName("CheckStatusButton")!).IsEnabled);
                Assert.False(SelectedDetailButton(history, historyGrid, "DeleteOriginalButton").IsEnabled,
                    "An unchecked replay must not qualify for original cleanup.");
                Assert.Equal(workspace.CurrentReplayHistory[0].OriginalMessageId,
                    ((TextBox)SelectedDetailElement(history, historyGrid, "DetailOriginalId")).Text);
                string selectedReplayId = Assert.IsType<ReplayHistoryRow>(historyGrid.SelectedItem).MessageId;
                priorClipboard = Clipboard.GetDataObject();
                clipboardSaved = true;
                Click(SelectedDetailButton(history, historyGrid, "CopyOriginalIdButton"));
                AssertCopyResult(history, workspace.CurrentReplayHistory[0].OriginalMessageId);
                Button copyReplayButton = SelectedDetailButton(history, historyGrid, "CopyReplayIdButton");
                string buttonReplayId = Assert.IsType<ReplayHistoryRow>(copyReplayButton.DataContext).MessageId;
                Click(copyReplayButton);
                Assert.Equal(selectedReplayId, buttonReplayId);
                AssertCopyResult(history, selectedReplayId);
                AssertNotOverlapping(SelectedDetailElement(history, historyGrid, "DetailReplayId"),
                    SelectedDetailButton(history, historyGrid, "CopyReplayIdButton"), SelectedRow(historyGrid));
                Capture(history, "Replay-32-history-not-checked.png");

                string[] expectedIds = workspace.CurrentReplayHistory.Select(attempt => attempt.Reservation.MessageId)
                    .Distinct(StringComparer.Ordinal).ToArray();
                messages.Move(expectedIds[0], MessageBucket.Active, MessageBucket.DeadLetter);
                Click((Button)history.FindName("CheckStatusButton")!);
                PumpUntil(dispatcher, () => workspace.CurrentReplayHistory.All(attempt => attempt.Observation?.CheckedAtUtc is not null),
                    "the actual history toolbar status check to finish");
                ReplayAttempt deadLetterReplay = workspace.CurrentReplayHistory.Single(attempt => attempt.Reservation.MessageId == expectedIds[0]);
                ReplayAttempt activeReplay = workspace.CurrentReplayHistory.Single(attempt => attempt.Reservation.MessageId == expectedIds[1]);
                Assert.True(deadLetterReplay.Observation!.IsComplete);
                Assert.Equal(MessageBucket.DeadLetter, Assert.Single(deadLetterReplay.Observation.Locations).Bucket);
                Assert.True(activeReplay.Observation!.IsComplete);
                Assert.Equal(MessageBucket.Active, Assert.Single(activeReplay.Observation.Locations).Bucket);
                Assert.False(SelectedDetailButton(history, historyGrid, "DeleteOriginalButton").IsEnabled,
                    "A replay still found in Active or DLQ must not qualify for original cleanup.");
                Capture(history, "Replay-32-history-found.png");
                Assert.True(history.IsVisible, "Checking status must leave the modeless history window open.");

                Click((Button)history.FindName("FindAllButton")!);
                PumpUntil(dispatcher, () => workspace.Search.IsComplete, "the main Investigation search for all replay IDs");
                string expectedAllQuery = string.Join(" OR ", expectedIds.Select(id => "message:" + MessageSearchQuery.QuoteLiteral(id)));
                Assert.Equal(expectedAllQuery, ((TextBox)owner.FindName("SearchBox")!).Text);
                Assert.Equal(expectedAllQuery, workspace.Search.QueryText);
                Assert.Equal(2, workspace.Search.Messages.Count);
                Assert.Equal("2 replay IDs · 2 deliveries across 1 queue", ((TextBlock)owner.FindName("SearchSummary")!).Text);
                owner.Width = 980;
                owner.Height = 640;
                owner.UpdateLayout();
                Assert.Contains("2 replay IDs · 2 deliveries across 1 queue", ((TextBlock)owner.FindName("SearchStatusText")!).Text);
                Assert.Contains("Search complete", ((TextBlock)owner.FindName("SearchStatusText")!).Text);
                Assert.True(history.IsVisible, "Find all replays must leave its modeless history window open.");

                historyGrid.UpdateLayout();
                var findReplay = Descendants(history).OfType<Button>().First(button => button.Name == "FindReplayButton");
                var selectedRow = Assert.IsType<ReplayHistoryRow>(findReplay.DataContext);
                Click(findReplay);
                string expectedOneQuery = "message:" + MessageSearchQuery.QuoteLiteral(selectedRow.MessageId);
                PumpUntil(dispatcher, () => workspace.Search.IsComplete && workspace.Search.QueryText == expectedOneQuery,
                    "the main Investigation search for the selected replay ID");
                Assert.Equal(expectedOneQuery, ((TextBox)owner.FindName("SearchBox")!).Text);
                Assert.Single(workspace.Search.Messages);
                Assert.Contains("1 replay ID · 1 delivery across 1 queue", ((TextBlock)owner.FindName("SearchStatusText")!).Text);
                Assert.True(history.IsVisible, "Find replay must leave its modeless history window open.");

                SettleHistoryLayout(dispatcher, history);
                var findOriginal = Descendants(history).OfType<Button>().First(button => button.Name == "FindOriginalButton");
                Click(findOriginal);
                string originalQuery = "message:" + MessageSearchQuery.QuoteLiteral(delivery.Message.MessageId);
                PumpUntil(dispatcher, () => workspace.Search.IsComplete && workspace.Search.QueryText == originalQuery, "the original ID search");
                Assert.Equal(delivery.Message.MessageId, Assert.Single(workspace.Search.Messages).Delivery.Message.MessageId);
                Assert.True(history.IsVisible);
                SettleHistoryLayout(dispatcher, history);
                Assert.All(Descendants(history).OfType<Button>().Where(button => button.Name == "RemoveHistoryButton"),
                    button => Assert.Equal(Visibility.Collapsed, button.Visibility));

                ReplayObservation?[] previous = workspace.CurrentReplayHistory.Select(attempt => attempt.Observation).ToArray();
                messages.Failure = (FakeMessages.Queue, MessageBucket.Active);
                Click((Button)history.FindName("CheckStatusButton")!);
                PumpUntil(dispatcher, () => workspace.CurrentReplayHistory.Select((attempt, index) =>
                        !ReferenceEquals(attempt.Observation, previous[index])).All(changed => changed),
                    "the actual status check with an unavailable Active source");
                Assert.All(workspace.CurrentReplayHistory, attempt => Assert.False(attempt.Observation!.IsComplete));
                Assert.False(SelectedDetailButton(history, historyGrid, "DeleteOriginalButton").IsEnabled,
                    "Incomplete namespace coverage must not qualify for original cleanup.");
                Capture(history, "Replay-32-history-incomplete.png");
                historyGrid.SelectedItem = historyGrid.Items.Cast<ReplayHistoryRow>().Single(row => row.MessageId == expectedIds[1]);
                SettleHistoryLayout(dispatcher, history);
                Assert.Equal("Incomplete / unavailable", Assert.IsType<ReplayHistoryRow>(historyGrid.SelectedItem).Observation);
                Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<ReplayHistoryRow>(historyGrid.SelectedItem).ObservationReason));
                Capture(history, "Replay-47-debug-incomplete.png");
                Assert.Contains("scan incomplete", ((TextBlock)SelectedDetailElement(history, historyGrid, "LastCheckDetails")).Text, StringComparison.OrdinalIgnoreCase);
                messages.Failure = null;

                previous = workspace.CurrentReplayHistory.Select(attempt => attempt.Observation).ToArray();
                messages.ClearReplayCopies(expectedIds);
                Click((Button)history.FindName("CheckStatusButton")!);
                PumpUntil(dispatcher, () => workspace.CurrentReplayHistory.Select((attempt, index) =>
                        !ReferenceEquals(attempt.Observation, previous[index])).All(changed => changed),
                    "a fresh complete absence check for every replay");
                Assert.All(workspace.CurrentReplayHistory, attempt => Assert.True(attempt.Observation!.IsAbsent));
                Assert.True(SelectedDetailButton(history, historyGrid, "DeleteOriginalButton").IsEnabled);
                SettleHistoryLayout(dispatcher, history);
                Assert.All(Descendants(history).OfType<Button>().Where(button => button.Name == "RemoveHistoryButton"),
                    button => Assert.Equal(Visibility.Visible, button.Visibility));
                Assert.Equal("Delete original", System.Windows.Automation.AutomationProperties.GetName(SelectedDetailButton(history, historyGrid, "DeleteOriginalButton")));
                Assert.Equal("Saved replay attempts", System.Windows.Automation.AutomationProperties.GetName(historyGrid));
                RouteToDeleteConfirmation(dispatcher, history, dialog =>
                {
                    var cancel = (Button)dialog.FindName("CancelButton")!;
                    Assert.True(cancel.IsKeyboardFocused, "The typed-delete dialog must provide a keyboard starting point.");
                    Click(cancel);
                }, out DeleteMessagesWindow? canceledDelete);
                Assert.NotNull(canceledDelete);
                Assert.False(canceledDelete!.DialogResult);
                Assert.Empty(deleteReceiver.Completed);
                Assert.True(messages.ContainsOriginal, "Cancel must leave the original DLQ message available.");

                RouteToDeleteConfirmation(dispatcher, history, dialog =>
                {
                    var confirmation = (TextBox)dialog.FindName("DeleteConfirmationInput")!;
                    var confirm = (Button)dialog.FindName("ConfirmDeleteButton")!;
                    Assert.False(confirm.IsEnabled);
                    confirmation.Text = "DELETE";
                    Assert.True(confirm.IsEnabled);
                    Click(confirm);
                }, out DeleteMessagesWindow? confirmedDelete);
                Assert.NotNull(confirmedDelete);
                PumpUntil(dispatcher, () => deleteReceiver.Completed.Count == 1,
                    "the explicitly confirmed original DLQ settlement");
                Assert.False(messages.ContainsOriginal);
                Assert.All(workspace.CurrentReplayHistory, attempt => Assert.Equal(ReplayOriginalStatus.Deleted, attempt.OriginalStatus));
                Assert.True(history.IsVisible, "History actions must not close the window or clear its attempts.");
                Assert.Equal(2, historyGrid.Items.Count);

                history.Width = 780;
                history.Height = 480;
                PumpUntil(dispatcher, () => Math.Abs(history.ActualWidth - 780) < 1 && Math.Abs(history.ActualHeight - 480) < 1,
                    "the minimum supported replay history viewport");
                SettleLayout(dispatcher, history);
                AssertHistoryColumnsFit(historyGrid);
                foreach (string name in new[] { "FindAllButton", "CheckStatusButton" })
                    AssertVisibleInside((FrameworkElement)history.FindName(name)!, (FrameworkElement)history.Content);
                historyGrid.SelectedItem = historyGrid.Items.Cast<ReplayHistoryRow>().Single(row => row.MessageId == longReplayId);
                historyGrid.UpdateLayout();
                ScrollSelectedDetailsIntoView(dispatcher, history, historyGrid, "CopyReplayIdButton");
                AssertVisibleInside(SelectedDetailButton(history, historyGrid, "CopyReplayIdButton"), historyGrid);
                ScrollSelectedDetailsIntoView(dispatcher, history, historyGrid, "DeleteOriginalButton");
                SettleLayout(dispatcher, history);
                Capture(history, "Replay-47-compact-detail-debug.png");
                Capture(history, "Replay-32-history-780x480.png");
                Assert.Equal(longReplayId, ((TextBox)SelectedDetailElement(history, historyGrid, "DetailReplayId")).Text);
                Assert.True(((TextBox)SelectedDetailElement(history, historyGrid, "DetailReplayId")).ActualHeight > 20,
                    "Long replay IDs should wrap in their selected row details.");
                AssertNotOverlapping(SelectedDetailElement(history, historyGrid, "DetailReplayId"),
                    SelectedDetailButton(history, historyGrid, "CopyReplayIdButton"), SelectedRow(historyGrid));
                Assert.Equal(Visibility.Visible, ((DataGridRow)historyGrid.ItemContainerGenerator
                    .ContainerFromItem(historyGrid.SelectedItem)).DetailsVisibility);
                AssertVisibleInside(SelectedDetailButton(history, historyGrid, "DeleteOriginalButton"), historyGrid);
                PumpUntil(dispatcher, () => historyGrid.IsEnabled, "history actions to finish");
                foreach (bool confirm in new[] { false, true })
                {
                    Descendants(historyGrid).OfType<ScrollViewer>()
                        .Single(viewer => ReferenceEquals(viewer.TemplatedParent, historyGrid)).ScrollToTop();
                    SettleHistoryLayout(dispatcher, history);
                    Assert.Contains(historyGrid.Items.Cast<ReplayHistoryRow>(), row => row.CanRemove);
                    var remove = Descendants(history).OfType<Button>().First(button => button.Name == "RemoveHistoryButton" && button.IsVisible);
                    Assert.True(remove.ActualWidth >= 28);
                    AssertVisibleInside(remove, historyGrid);
                    var target = ((ReplayHistoryRow)remove.DataContext).Attempt;
                    dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                    {
                        var dialog = history.OwnedWindows.OfType<ProfileWarningWindow>().Single();
                        Assert.Contains(target.Reservation.MessageId, ((TextBlock)dialog.FindName("WarningMessageText")).Text);
                        Assert.Contains("No Service Bus messages", ((TextBlock)dialog.FindName("WarningMessageText")).Text);
                        Capture(dialog, "Replay-45-remove-confirmation.png");
                        Click((Button)dialog.FindName(confirm ? "ContinueButton" : "CancelButton"));
                    }));
                    Click(remove);
                    PumpUntil(dispatcher, () => historyGrid.IsEnabled, "history removal to finish");
                    Assert.Equal(confirm ? 1 : 2, workspace.CurrentReplayHistory.Count);
                    Assert.Equal(2, workspace.Preferences.ReplayAttempts.Count);
                }
                Capture(history, "Replay-45-removed-780x480.png");

            }
            finally
            {
                if (clipboardSaved)
                {
                    if (priorClipboard is null) Clipboard.Clear();
                    else Clipboard.SetDataObject(priorClipboard);
                }
                workspace.ConfirmDiscard = () => Task.FromResult(true);
                if (history?.IsVisible == true) history.Close();
                if (owner.IsVisible)
                {
                    owner.Close();
                    PumpUntil(dispatcher, () => !owner.IsVisible, "Investigation to close");
                }
                workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Replay_history_renders_empty_uncertain_not_sent_and_incomplete_observation_states()
    {
        OnSta(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var emptyWorkspace = CreateWorkspace(new CapturingReplaySender());
            ReplayHistoryWindow? emptyWindow = null;
            InvestigationWorkspace? populatedWorkspace = null;
            ReplayHistoryWindow? populatedWindow = null;
            try
            {
                Complete(dispatcher, emptyWorkspace.InitializeAsync());
                emptyWindow = CreateHistoryWindow(emptyWorkspace);
                emptyWindow.Width = 980;
                emptyWindow.Height = 640;
                emptyWindow.Show();
                PumpUntil(dispatcher, () => emptyWindow.IsVisible
                    && ((DataGrid)emptyWindow.FindName("HistoryGrid")!).Items.Count == 0,
                    "empty replay history to render");
                SettleLayout(dispatcher, emptyWindow);
                Assert.Contains("0 attempts", ((TextBlock)emptyWindow.FindName("HistorySummary")!).Text, StringComparison.Ordinal);
                Assert.Contains("No replay attempts", ((TextBlock)emptyWindow.FindName("HistoryNotice")!).Text, StringComparison.Ordinal);
                Assert.False(((Button)emptyWindow.FindName("FindAllButton")!).IsEnabled);
                Assert.False(((Button)emptyWindow.FindName("CheckStatusButton")!).IsEnabled);
                Assert.Equal("Find all replays", System.Windows.Automation.AutomationProperties.GetName((Button)emptyWindow.FindName("FindAllButton")!));
                Capture(emptyWindow, "Replay-32-history-empty-980x640.png");
                emptyWindow.Close();

                var preferences = CreateStatusStatePreferences();
                var messages = new FakeMessages();
                populatedWorkspace = CreateWorkspace(new CapturingReplaySender(messages), messages, initialPreferences: preferences);
                Complete(dispatcher, populatedWorkspace.InitializeAsync());
                Complete(dispatcher, populatedWorkspace.ConnectAsync());
                populatedWindow = CreateHistoryWindow(populatedWorkspace);
                populatedWindow.Show();
                var grid = (DataGrid)populatedWindow.FindName("HistoryGrid")!;
                PumpUntil(dispatcher, () => populatedWindow.IsVisible && grid.Items.Count == 5,
                    "uncertain, not-sent, and incomplete attempts to render");
                populatedWindow.Width = 780;
                populatedWindow.Height = 480;
                SettleHistoryLayout(dispatcher, populatedWindow);
                var rows = grid.Items.Cast<ReplayHistoryRow>().ToArray();
                ReplayHistoryRow uncertain = Assert.Single(rows, row => row.Attempt.SendStatus == ReplaySendStatus.Uncertain);
                ReplayHistoryRow notSent = Assert.Single(rows, row => row.Attempt.SendStatus == ReplaySendStatus.NotSent);
                ReplayHistoryRow incomplete = Assert.Single(rows, row => row.Attempt.Observation is { IsComplete: false });
                ReplayHistoryRow mixed = Assert.Single(rows, row => row.Attempt.Observation?.Locations.Count == 3);
                ReplayHistoryRow topic = Assert.Single(rows, row => row.Attempt.OriginalSource.Kind == EntityKind.Subscription);
                Assert.Contains("Send uncertain", uncertain.Sent, StringComparison.Ordinal);
                Assert.Contains("Not sent", notSent.Sent, StringComparison.Ordinal);
                Assert.Equal("Incomplete / unavailable", incomplete.Observation);
                Assert.Equal("1 DLQ · 2 Active", mixed.Observation);
                Assert.True(topic.SourceLocation.IsSubscription);
                Assert.Equal("retail-orders-topic-with-long-name", topic.SourceLocation.SourceTopic);
                Assert.Equal("retail-orders-subscription-with-long-name", topic.SourceLocation.SourceName);

                grid.SelectedItem = mixed;
                ScrollSelectedDetailsIntoView(dispatcher, populatedWindow, grid, "CopyOriginalIdButton");
                Capture(populatedWindow, "Replay-47-mixed-copies-780x480.png");
                grid.SelectedItem = topic;
                ScrollSelectedDetailsIntoView(dispatcher, populatedWindow, grid, "DeleteOriginalButton");
                Capture(populatedWindow, "Replay-47-topic-source-780x480.png");

                grid.SelectedItem = uncertain;
                grid.UpdateLayout();
                Assert.False(SelectedDetailButton(populatedWindow, grid, "DeleteOriginalButton").IsEnabled);
                Assert.Contains("timed out", ((TextBlock)SelectedDetailElement(populatedWindow, grid, "LastCheckDetails")).Text);
                SettleHistoryLayout(dispatcher, populatedWindow);
                Capture(populatedWindow, "Replay-45-send-diagnostic.png");
                grid.SelectedItem = notSent;
                SettleHistoryLayout(dispatcher, populatedWindow);
                Assert.False(SelectedDetailButton(populatedWindow, grid, "DeleteOriginalButton").IsEnabled);
                grid.SelectedItem = incomplete;
                SettleHistoryLayout(dispatcher, populatedWindow);
                Assert.False(SelectedDetailButton(populatedWindow, grid, "DeleteOriginalButton").IsEnabled,
                    "Incomplete observations cannot qualify for original cleanup.");
                string detail = ((TextBlock)SelectedDetailElement(populatedWindow, grid, "LastCheckDetails")).Text;
                Assert.Contains("scan incomplete", detail, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("review limitation", detail, StringComparison.OrdinalIgnoreCase);
                ScrollSelectedDetailsIntoView(dispatcher, populatedWindow, grid, "DeleteOriginalButton");
                AssertVisibleInside(SelectedDetailButton(populatedWindow, grid, "DeleteOriginalButton"), grid);
                Capture(populatedWindow, "Replay-32-history-status-states-780x480.png");

                Complete(dispatcher, populatedWorkspace.DisconnectAsync());
                PumpUntil(dispatcher, () => !populatedWorkspace.IsConnected, "the history profile to disconnect");
                SettleHistoryLayout(dispatcher, populatedWindow);
                Assert.False(((Button)populatedWindow.FindName("FindAllButton")!).IsEnabled);
                Assert.False(((Button)populatedWindow.FindName("CheckStatusButton")!).IsEnabled);
                ReplayHistoryRow offlineRow = Assert.IsType<ReplayHistoryRow>(grid.SelectedItem);
                Assert.False(offlineRow.BrokerAvailable, "Per-replay broker actions are unavailable while disconnected.");
                Assert.False(string.IsNullOrEmpty(offlineRow.ObservationTime), "Stored observation dates remain available while disconnected.");
            }
            finally
            {
                if (emptyWindow?.IsVisible == true) emptyWindow.Close();
                if (populatedWindow?.IsVisible == true) populatedWindow.Close();
                if (populatedWorkspace is not null) Complete(dispatcher, populatedWorkspace.DisposeAsync().AsTask());
                Complete(dispatcher, emptyWorkspace.DisposeAsync().AsTask());
            }
        });
    }

    private static void RouteToDeleteConfirmation(Dispatcher dispatcher, ReplayHistoryWindow owner,
        Action<DeleteMessagesWindow> interact, out DeleteMessagesWindow? confirmation)
    {
        DeleteMessagesWindow? found = null;
        bool done = false;
        Exception? callbackFailure = null;
        Action inspect = null!;
        inspect = () =>
        {
            try
            {
                found = owner.OwnedWindows.OfType<DeleteMessagesWindow>().SingleOrDefault(candidate => candidate.IsVisible);
                if (found is null)
                {
                    dispatcher.BeginInvoke(DispatcherPriority.Background, inspect);
                    return;
                }
                interact(found);
                done = true;
            }
            catch (Exception exception)
            {
                callbackFailure = exception;
                if (found?.IsVisible == true) Click((Button)found.FindName("CancelButton")!);
                done = true;
            }
        };
        dispatcher.BeginInvoke(DispatcherPriority.Normal, inspect);
        Click(SelectedDetailButton(owner, (DataGrid)owner.FindName("HistoryGrid")!, "DeleteOriginalButton"));
        PumpUntil(dispatcher, () => done, "the guarded delete confirmation dialog");
        confirmation = found;
        if (callbackFailure is not null) ExceptionDispatchInfo.Capture(callbackFailure).Throw();
    }

    private static void AssertVisibleInside(FrameworkElement element, FrameworkElement container)
    {
        Rect bounds = element.TransformToAncestor(container).TransformBounds(new Rect(element.RenderSize));
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1
            && bounds.Right <= container.ActualWidth + 1 && bounds.Bottom <= container.ActualHeight + 1,
            $"{element.Name} is clipped by {container.Name}: bounds {bounds}, size {container.ActualWidth}x{container.ActualHeight}.");
    }

    private static void AssertNotOverlapping(FrameworkElement first, FrameworkElement second, FrameworkElement container)
    {
        Rect a = first.TransformToAncestor(container).TransformBounds(new Rect(first.RenderSize));
        Rect b = second.TransformToAncestor(container).TransformBounds(new Rect(second.RenderSize));
        Assert.True(a.Right <= b.Left || b.Right <= a.Left || a.Bottom <= b.Top || b.Bottom <= a.Top,
            $"{first.Name} overlaps {second.Name}: {a} and {b}.");
    }

    private static void SettleHistoryLayout(Dispatcher dispatcher, ReplayHistoryWindow history)
    {
        SettleLayout(dispatcher, history);
        AssertHistoryColumnsFit((DataGrid)history.FindName("HistoryGrid")!);
    }

    private static void ScrollSelectedDetailsIntoView(Dispatcher dispatcher, ReplayHistoryWindow history, DataGrid grid, string actionName)
    {
        grid.ScrollIntoView(grid.SelectedItem);
        SettleLayout(dispatcher, history);
        FrameworkElement action = SelectedDetailElement(history, grid, actionName);
        ScrollViewer scroll = Descendants(grid).OfType<ScrollViewer>().Single(viewer => ReferenceEquals(viewer.TemplatedParent, grid));
        Rect bounds = action.TransformToAncestor(scroll).TransformBounds(new Rect(action.RenderSize));
        double offset = scroll.VerticalOffset;
        if (bounds.Bottom > scroll.ViewportHeight) offset += bounds.Bottom - scroll.ViewportHeight + 32;
        else if (bounds.Top < 0) offset += bounds.Top - 32;
        scroll.ScrollToVerticalOffset(Math.Clamp(offset, 0, scroll.ScrollableHeight));
        PumpUntil(dispatcher, () => Math.Abs(scroll.VerticalOffset - Math.Clamp(offset, 0, scroll.ScrollableHeight)) < 1,
            "the selected replay action to scroll into the DataGrid viewport");
        SettleLayout(dispatcher, history);
    }

    private static void AssertHistoryColumnsFit(DataGrid grid)
    {
        double[] minimums = [150, 180, 120, 48];
        Assert.Equal(minimums.Length, grid.Columns.Count);
        for (int index = 0; index < minimums.Length; index++)
            Assert.True(grid.Columns[index].ActualWidth >= minimums[index],
                $"Replay history column {index} is too narrow ({grid.Columns[index].ActualWidth:0}px; expected at least {minimums[index]:0}px). {HistoryGridMetrics(grid)}");
        double total = grid.Columns.Sum(column => column.ActualWidth);
        Assert.True(total <= grid.ActualWidth + 1,
            $"Replay history columns require {total:0}px but the grid is only {grid.ActualWidth:0}px wide. {HistoryGridMetrics(grid)}");
        Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetHorizontalScrollBarVisibility(grid));
        ScrollViewer scroll = Descendants(grid).OfType<ScrollViewer>().Single(viewer => ReferenceEquals(viewer.TemplatedParent, grid));
        Assert.True(scroll.ScrollableWidth < 1,
            $"Default replay history layout overflows horizontally ({scroll.ScrollableWidth:0}px). {HistoryGridMetrics(grid)}");
    }

    private static DataGridRow SelectedRow(DataGrid grid)
    {
        grid.ScrollIntoView(grid.SelectedItem);
        grid.UpdateLayout();
        PumpUntil(grid.Dispatcher, () => grid.ItemContainerGenerator.ContainerFromItem(grid.SelectedItem) is DataGridRow,
            "the selected replay history row to realize");
        return Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(grid.SelectedItem));
    }

    private static FrameworkElement SelectedDetailElement(ReplayHistoryWindow history, DataGrid grid, string name)
    {
        var row = SelectedRow(grid);
        Assert.Equal(Visibility.Visible, row.DetailsVisibility);
        string automationName = name switch
        {
            "DeleteOriginalButton" => "Delete original",
            "CopyOriginalIdButton" => "Copy original message ID",
            "CopyReplayIdButton" => "Copy replay ID",
            "DetailOriginalId" => "Original message ID",
            "DetailReplayId" => "Replay ID",
            "LastCheckDetails" => "Replay send or observation detail",
            _ => name
        };
        return Descendants(row).OfType<FrameworkElement>().Single(element =>
            System.Windows.Automation.AutomationProperties.GetName(element) == automationName);
    }

    private static Button SelectedDetailButton(ReplayHistoryWindow history, DataGrid grid, string name) =>
        Assert.IsType<Button>(SelectedDetailElement(history, grid, name));

    private static void AssertCopyResult(ReplayHistoryWindow history, string expected)
    {
        string notice = ((TextBlock)history.FindName("HistoryNotice")!).Text;
        Assert.Equal("Message ID copied.", notice);
        string copied = string.Empty;
        PumpUntil(history.Dispatcher, () => (copied = Clipboard.GetText()) == expected,
            "the exact copied message ID to become available on the clipboard");
        Assert.Equal(expected, copied);
    }

    private static void AssertSelectedDetails(ReplayHistoryWindow history, DataGrid grid)
    {
        Assert.Equal(Visibility.Visible, SelectedRow(grid).DetailsVisibility);
        Assert.All(grid.Items.Cast<object>().Where(item => !ReferenceEquals(item, grid.SelectedItem)), item =>
        {
            if (grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                Assert.Equal(Visibility.Collapsed, row.DetailsVisibility);
        });
    }

    private static string HistoryGridMetrics(DataGrid grid)
    {
        string columns = string.Join("; ", grid.Columns.Select((column, index) =>
            $"{index}:{column.Width}/{column.ActualWidth:0}"));
        ScrollViewer? scroll = Descendants(grid).OfType<ScrollViewer>().FirstOrDefault();
        string scrolling = scroll is null ? "no ScrollViewer" :
            $"scroll extent={scroll.ExtentWidth:0}, viewport={scroll.ViewportWidth:0}, offset={scroll.HorizontalOffset:0}";
        return $"grid={grid.ActualWidth:0}x{grid.ActualHeight:0}; columns=[{columns}]; {scrolling}";
    }

    private static ReplayHistoryWindow CreateHistoryWindow(InvestigationWorkspace workspace) =>
        new(workspace, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask);

    private static WorkspacePreferences CreateStatusStatePreferences()
    {
        var delivery = new MessageDelivery(new(1, FakeMessages.Queue, MessageBucket.DeadLetter, FakeMessages.Message.SequenceNumber), FakeMessages.Message);
        ReplayReservation reserved = ReplayLineage.Reserve(delivery, []);
        ReplayFamilyState family = reserved.Family with { LastAttempt = 3 };
        string namespaceFingerprint = ReplayNamespace.Fingerprint(ConnectionProfileDefaults.LocalEmulator);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ReplayAttempt Create(string id, long sequence, ReplaySendStatus sendStatus, ReplayObservation? observation) =>
            new(Guid.NewGuid(), "replay-windows", namespaceFingerprint,
                new ReplayReservation(family, id), FakeMessages.Queue, sequence, reserved.Family.RootFingerprint,
                FakeMessages.Message.MessageId, now.AddSeconds(-sequence), sendStatus,
                sendStatus == ReplaySendStatus.Confirmed ? now : null) { Observation = observation };
        var uncertainObservation = new ReplayObservation(now, true, 1, 0, []);
        var incompleteObservation = new ReplayObservation(now, false, 2, 25, [],
            "Review limitation: this deliberately long detail proves text wraps and scrolls within the history window. "
            + string.Concat(Enumerable.Repeat("Receiving source unavailable. ", 20)));
        var mixedObservation = new ReplayObservation(now, true, 3, 14,
        [
            new(new(EntityKind.Queue, "active-one"), MessageBucket.Active, "Active"),
            new(new(EntityKind.Queue, "dead-letter"), MessageBucket.DeadLetter, "Dead letter"),
            new(new(EntityKind.Queue, "active-two"), MessageBucket.Active, "Active")
        ]);
        var topicSource = new EntityAddress(EntityKind.Subscription,
            "retail-orders-subscription-with-long-name", "retail-orders-topic-with-long-name");
        var topicDelivery = new MessageDelivery(new(1, topicSource, MessageBucket.DeadLetter, FakeMessages.Message.SequenceNumber), FakeMessages.Message);
        ReplayFamilyState topicFamily = ReplayLineage.Reserve(topicDelivery, []).Family with { LastAttempt = 1 };
        var topicObservation = new ReplayObservation(now, true, 3, 18,
        [
            new(topicSource, MessageBucket.Active, "Active"),
            new(topicSource, MessageBucket.DeadLetter, "Dead letter")
        ]);
        var topicAttempt = new ReplayAttempt(Guid.NewGuid(), "replay-windows", namespaceFingerprint,
            new ReplayReservation(topicFamily, "replay-topic"), topicSource, FakeMessages.Message.SequenceNumber,
            topicFamily.RootFingerprint, FakeMessages.Message.MessageId, now, ReplaySendStatus.Confirmed, now)
        { Observation = topicObservation };
        return new WorkspacePreferences
        {
            Profiles = [new InvestigationProfile("replay-windows", ConnectionProfileDefaults.LocalEmulator with { Name = "Replay window proof" })],
            SelectedProfileId = "replay-windows",
            ReplayFamilies = new Dictionary<string, IReadOnlyList<ReplayFamilyState>> { ["replay-windows"] = [family, topicFamily] },
            ReplayAttempts =
            [
                Create("replay-uncertain", 3, ReplaySendStatus.Uncertain, uncertainObservation) with { SendFailure = ReplaySendFailure.Timeout },
                Create("replay-not-sent", 2, ReplaySendStatus.NotSent, null),
                Create("replay-incomplete", 1, ReplaySendStatus.Confirmed, incompleteObservation),
                Create("replay-mixed", 4, ReplaySendStatus.Confirmed, mixedObservation),
                topicAttempt
            ],
            LogExpanded = false
        };
    }
}
