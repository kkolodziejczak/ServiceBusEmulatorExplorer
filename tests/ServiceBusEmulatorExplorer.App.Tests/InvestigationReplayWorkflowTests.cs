using System.IO;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationReplayWorkflowTests
{
    [Theory]
    [InlineData(false, "Confirmed")]
    [InlineData(true, "Uncertain")]
    public async Task Replay_PersistsAttemptBeforeSendingAndFinalizesItsOutcome(bool failSend, string finalStatus)
    {
        var store = new Store();
        IReadOnlyList<ReplayAttempt>? attemptsAtSend = null;
        var sender = new Sender
        {
            OnSend = (_, _, _) =>
            {
                attemptsAtSend = store.Saved.ReplayAttempts.ToArray();
                return failSend ? Task.FromException(new IOException("Response lost")) : Task.CompletedTask;
            }
        };
        await using var workspace = await OpenAsync(store, sender);
        var delivery = Delivery();

        var outcome = await workspace.ReplayAsync(delivery);

        Assert.Equal(failSend ? ReplaySendStatus.Uncertain : ReplaySendStatus.Confirmed, outcome.Status);
        var persistedBeforeSend = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ReplayAttempt>>(attemptsAtSend));
        AssertAttempt(persistedBeforeSend, delivery, outcome.Reservation.MessageId, "Uncertain", sentAtUtc: null);
        var persistedAfterOutcome = Assert.Single(store.Saved.ReplayAttempts);
        if (failSend) Assert.Contains("Transport error", new ReplayHistoryRow(persistedAfterOutcome, store.Saved).Detail);
        AssertAttempt(persistedAfterOutcome, delivery, outcome.Reservation.MessageId, finalStatus,
            sentAtUtc: failSend ? null : persistedAfterOutcome.SentAtUtc);
    }

    [Theory]
    [InlineData("timeout", ReplaySendFailure.Timeout, "timed out")]
    [InlineData("authorization", ReplaySendFailure.Authorization, "Authorization failed")]
    [InlineData("unknown", ReplaySendFailure.Unknown, "Send failed")]
    public async Task Replay_PersistsSafeDiagnosticWithoutRawException(string kind, ReplaySendFailure expected, string text)
    {
        const string sensitive = "SharedAccessKey=DO-NOT-PERSIST";
        Exception error = kind switch
        {
            "timeout" => new TimeoutException(sensitive),
            "authorization" => new UnauthorizedAccessException(sensitive),
            _ => new InvalidOperationException(sensitive)
        };
        var store = new Store();
        var sender = new Sender { OnSend = (_, _, _) => Task.FromException(error) };
        await using var workspace = await OpenAsync(store, sender);
        await workspace.ReplayAsync(Delivery());
        var saved = Assert.Single(store.Saved.ReplayAttempts);
        Assert.Equal(expected, saved.SendFailure);
        Assert.Contains(text, new ReplayHistoryRow(saved, store.Saved).Detail);
        Assert.DoesNotContain(sensitive, System.Text.Json.JsonSerializer.Serialize(saved));
    }

    [Fact]
    public async Task PrepareReplay_IsSideEffectFreeAndFreezesTheReviewedBody()
    {
        var store = new Store();
        await using var workspace = await OpenAsync(store, new Sender());
        var delivery = Delivery();
        int savesBeforePrepare = store.SaveCount;

        var prepared = workspace.PrepareReplay(delivery, "{\"reviewed\":true}");

        Assert.Equal(savesBeforePrepare, store.SaveCount);
        Assert.Empty(store.Saved.ReplayFamilies);
        Assert.Empty(store.Saved.ReplayAttempts);
        Assert.Equal("{\"reviewed\":true}", prepared.EditedBody);
        Assert.Equal(ReplayNamespace.Fingerprint(ConnectionProfileDefaults.LocalEmulator), prepared.NamespaceFingerprint);
        Assert.Equal(ReplayLineage.Fingerprint(delivery), prepared.OriginalFingerprint);
        Assert.Equal("original", prepared.Delivery.Message.MessageId);
        Assert.Equal(new byte[] { 0, 255, 42 }, prepared.Delivery.Message.RawBody!.ToArray());
    }

    [Fact]
    public async Task CurrentReplayHistory_IsScopedToSelectedProfileAndItsCurrentNamespace()
    {
        var currentProfile = new InvestigationProfile("profile", ConnectionProfileDefaults.LocalEmulator);
        var otherProfile = new InvestigationProfile("other", ConnectionProfileDefaults.LocalEmulator with { Name = "Other" });
        var currentAttempt = HistoryAttempt("profile", currentProfile.Connection, "current-namespace-replay");
        var otherAttempt = HistoryAttempt("other", otherProfile.Connection, "other-profile-replay");
        var store = new Store(new WorkspacePreferences
        {
            Profiles = [currentProfile, otherProfile],
            SelectedProfileId = "profile",
            ReplayAttempts = [currentAttempt, otherAttempt]
        });

        await using var workspace = await OpenAsync(store, new Sender());
        Assert.Equal(currentAttempt.AttemptId, Assert.Single(workspace.CurrentReplayHistory).AttemptId);

        var changedConnection = currentProfile.Connection with
        {
            RuntimeConnectionString = currentProfile.Connection.RuntimeConnectionString.Replace(
                "sb://localhost", "sb://different.localhost", StringComparison.Ordinal)
        };
        await workspace.ApplyPreferencesAsync(workspace.Preferences with
        {
            Profiles = [currentProfile with { Connection = changedConnection }, otherProfile]
        });
        Assert.Empty(workspace.CurrentReplayHistory);
        Assert.Equal(2, workspace.Preferences.ReplayAttempts.Count);

        Assert.True(await workspace.SwitchProfileAsync(otherProfile));
        Assert.Equal(otherAttempt.AttemptId, Assert.Single(workspace.CurrentReplayHistory).AttemptId);
        Assert.Equal(currentAttempt, Assert.Single(workspace.Preferences.ReplayAttempts,
            attempt => attempt.AttemptId == currentAttempt.AttemptId));
    }

    [Fact]
    public async Task SendReplay_UsesReviewedMessageIdAndBodyExactlyOnce()
    {
        var store = new Store();
        var sender = new Sender();
        await using var workspace = await OpenAsync(store, sender);
        var prepared = workspace.PrepareReplay(Delivery(), "{\"reviewed\":true}");
        const string reviewedId = "reviewed-replay-42";

        var outcome = await workspace.SendReplayAsync(prepared, reviewedId);
        int savesAfterSend = store.SaveCount;

        Assert.Equal(ReplaySendStatus.Confirmed, outcome.Status);
        var sent = Assert.Single(sender.Sends);
        Assert.Equal(reviewedId, sent.Message.MessageId);
        Assert.Equal(BinaryData.FromString("{\"reviewed\":true}").ToArray(), sent.Message.Body.ToArray());
        Assert.Equal("original", prepared.Delivery.Message.MessageId);
        Assert.Equal(reviewedId, outcome.Reservation.MessageId);
        Assert.Equal(ReplaySendStatus.Confirmed, Assert.Single(store.Saved.ReplayAttempts).SendStatus);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.SendReplayAsync(prepared, reviewedId));
        Assert.Single(sender.Sends);
        Assert.Equal(savesAfterSend, store.SaveCount);
    }

    [Fact]
    public async Task SendReplay_RejectsCandidateAfterReconnectWithoutSavingOrSending()
    {
        var store = new Store();
        var sender = new Sender();
        await using var workspace = await OpenAsync(store, sender);
        var prepared = workspace.PrepareReplay(Delivery());
        await workspace.DisconnectAsync();
        await workspace.ConnectAsync();
        int savesAfterReconnect = store.SaveCount;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workspace.SendReplayAsync(prepared, prepared.Reservation.MessageId));

        Assert.Equal(savesAfterReconnect, store.SaveCount);
        Assert.Empty(sender.Sends);
        Assert.Empty(store.Saved.ReplayAttempts);
    }

    [Fact]
    public async Task SendReplay_RejectsSupersededCandidateWithoutSendingAgain()
    {
        var store = new Store();
        var sender = new Sender();
        await using var workspace = await OpenAsync(store, sender);
        var prepared = workspace.PrepareReplay(Delivery());
        await workspace.SendReplayAsync(prepared, prepared.Reservation.MessageId);
        int savesAfterFirstSend = store.SaveCount;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workspace.SendReplayAsync(prepared, prepared.Reservation.MessageId));

        Assert.Equal(savesAfterFirstSend, store.SaveCount);
        Assert.Single(sender.Sends);
        Assert.Single(store.Saved.ReplayAttempts);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task SendReplay_InvalidReviewedIdDoesNotSaveOrSend(string reviewedId)
    {
        var store = new Store();
        var sender = new Sender();
        await using var workspace = await OpenAsync(store, sender);
        var prepared = workspace.PrepareReplay(Delivery());
        int savesBeforeSend = store.SaveCount;

        await Assert.ThrowsAsync<ArgumentException>(() => workspace.SendReplayAsync(prepared, reviewedId));

        Assert.Equal(savesBeforeSend, store.SaveCount);
        Assert.Empty(sender.Sends);
        Assert.Empty(store.Saved.ReplayAttempts);
    }

    [Fact]
    public async Task PrepareReplays_ReservesDistinctAttemptsForRepeatedMembersOfOneFamily()
    {
        var store = new Store();
        await using var workspace = await OpenAsync(store, new Sender());
        var delivery = Delivery();

        var prepared = workspace.PrepareReplays([delivery, delivery]);

        Assert.Equal(new long[] { 1, 2 }, prepared.Select(item => item.Reservation.Family.LastAttempt));
        Assert.Equal(prepared[0].Reservation.Family.FamilyId, prepared[1].Reservation.Family.FamilyId);
        Assert.NotEqual(prepared[0].Reservation.MessageId, prepared[1].Reservation.MessageId);
        Assert.NotEqual(prepared[0].AttemptId, prepared[1].AttemptId);
        Assert.Empty(store.Saved.ReplayAttempts);
        Assert.Empty(store.Saved.ReplayFamilies);
    }

    [Fact]
    public async Task ConfirmedSend_LeavesConservativeUncertainHistoryWhenOutcomeSaveFails()
    {
        var store = new Store();
        var sender = new Sender
        {
            OnSend = (_, _, _) =>
            {
                store.FailSave = true;
                return Task.CompletedTask;
            }
        };
        await using var workspace = await OpenAsync(store, sender);
        var prepared = workspace.PrepareReplay(Delivery());

        var outcome = await workspace.SendReplayAsync(prepared, prepared.Reservation.MessageId);

        Assert.Equal(ReplaySendStatus.Confirmed, outcome.Status);
        Assert.True(outcome.HistoryPersistenceFailed);
        Assert.Equal(ReplaySendStatus.Uncertain, Assert.Single(store.Saved.ReplayAttempts).SendStatus);
        Assert.Equal(ReplaySendStatus.Uncertain, Assert.Single(workspace.Preferences.ReplayAttempts).SendStatus);
        Assert.Single(sender.Sends);
    }

    [Fact]
    public async Task FailedReservationSave_DoesNotSendOrAdvanceInMemoryCounter()
    {
        var store = new Store();
        var sender = new Sender();
        await using var workspace = await OpenAsync(store, sender);
        store.FailSave = true;

        await Assert.ThrowsAsync<IOException>(() => workspace.ReplayAsync(Delivery()));

        Assert.Empty(sender.Sends);
        Assert.Empty(workspace.Preferences.ReplayFamilies);
        Assert.Empty(store.Saved.ReplayFamilies);
        store.FailSave = false;
        Assert.Equal(1, (await workspace.ReplayAsync(Delivery())).Reservation.Family.LastAttempt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{\"edited\":true}")]
    public async Task Replay_PersistsReservationBeforeSendingSubscriptionCopyToParentTopic(string? editedBody)
    {
        var store = new Store();
        var sender = new Sender();
        await using var workspace = await OpenAsync(store, sender);
        var delivery = Delivery(new(EntityKind.Subscription, "billing", "orders"));
        sender.OnSend = (_, message, _) =>
        {
            var persisted = Assert.Single(store.Saved.ReplayFamilies["profile"]);
            Assert.Equal(1, persisted.LastAttempt);
            Assert.Equal(persisted.FamilyId.ToString("N"), message.ApplicationProperties[ReplayLineage.FamilyProperty]);
            Assert.Equal(persisted, Assert.Single(workspace.Preferences.ReplayFamilies["profile"]));
            return Task.CompletedTask;
        };

        var outcome = await workspace.ReplayAsync(delivery, editedBody);

        Assert.Equal(ReplaySendStatus.Confirmed, outcome.Status);
        var sent = Assert.Single(sender.Sends);
        Assert.Equal(new EntityAddress(EntityKind.Topic, "orders"), sent.Destination);
        Assert.Equal(editedBody is null ? new byte[] { 0, 255, 42 } : BinaryData.FromString(editedBody).ToArray(), sent.Message.Body.ToArray());
        Assert.Equal("original", delivery.Message.MessageId);
        Assert.Equal(new byte[] { 0, 255, 42 }, delivery.Message.RawBody!.ToArray());
        Assert.Equal(outcome.Reservation.MessageId, sent.Message.MessageId);
        Assert.Equal(1, sender.DisposeCount);
    }

    [Fact]
    public async Task UncertainSend_IsNotRetriedAndNextAttemptSurvivesWorkspaceReload()
    {
        var store = new Store();
        var firstSender = new Sender { OnSend = (_, _, _) => throw new IOException("Response lost") };
        ReplayCopyOutcome first;
        await using (var workspace = await OpenAsync(store, firstSender))
        {
            first = await workspace.ReplayAsync(Delivery());
            Assert.Equal(ReplaySendStatus.Uncertain, first.Status);
            Assert.Single(firstSender.Sends);
            Assert.Equal(1, Assert.Single(store.Saved.ReplayFamilies["profile"]).LastAttempt);
        }
        var secondSender = new Sender { OnSend = (_, _, _) => throw new IOException("Response lost") };
        await using var reloaded = await OpenAsync(store, secondSender);

        var second = await reloaded.ReplayAsync(Delivery());

        Assert.Equal(ReplaySendStatus.Uncertain, second.Status);
        Assert.Single(secondSender.Sends);
        Assert.Equal(first.Reservation.Family.FamilyId, second.Reservation.Family.FamilyId);
        Assert.Equal(2, second.Reservation.Family.LastAttempt);
        Assert.NotEqual(first.Reservation.MessageId, second.Reservation.MessageId);
        Assert.Contains("-replay-2-", second.Reservation.MessageId);
        Assert.Equal(2, Assert.Single(store.Saved.ReplayFamilies["profile"]).LastAttempt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrDisconnectDuringSend_RetainsAttemptAndReportsUncertainty(bool disconnect)
    {
        var store = new Store();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new Sender
        {
            OnSend = async (_, _, cancellationToken) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        await using var workspace = await OpenAsync(store, sender);
        using var cancellation = new CancellationTokenSource();
        var prepared = workspace.PrepareReplay(Delivery());
        var replay = workspace.SendReplayAsync(prepared, prepared.Reservation.MessageId, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (disconnect) await workspace.DisconnectAsync();
        else cancellation.Cancel();

        var outcome = await replay.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ReplaySendStatus.Uncertain, outcome.Status);
        Assert.Single(sender.Sends);
        Assert.Equal(1, Assert.Single(store.Saved.ReplayFamilies["profile"]).LastAttempt);
        Assert.Equal(1, sender.DisposeCount);
    }

    [Fact]
    public async Task WorkspaceDisposal_WaitsForCanceledSendAndSenderCleanupBeforeCompleting()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Store();
        var sender = new Sender
        {
            OnSend = async (_, _, token) =>
            {
                using var registration = token.Register(() => canceled.TrySetResult());
                entered.TrySetResult();
                await releaseSend.Task;
                token.ThrowIfCancellationRequested();
            },
            OnDispose = async () =>
            {
                cleanupEntered.TrySetResult();
                await releaseCleanup.Task;
            }
        };
        var workspace = await OpenAsync(store, sender);
        var replay = workspace.ReplayAsync(Delivery());
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var disposal = workspace.DisposeAsync().AsTask();
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(disposal.IsCompleted);
            Assert.False(replay.IsCompleted);

            releaseSend.TrySetResult();
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(disposal.IsCompleted);
            Assert.False(replay.IsCompleted);

            releaseCleanup.TrySetResult();
            var outcome = await replay.WaitAsync(TimeSpan.FromSeconds(5));
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ReplaySendStatus.Uncertain, outcome.Status);
            Assert.Equal(1, sender.DisposeCount);
            Assert.Equal(1, Assert.Single(store.Saved.ReplayFamilies["profile"]).LastAttempt);
        }
        finally
        {
            releaseSend.TrySetResult();
            releaseCleanup.TrySetResult();
            await workspace.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ConfirmedSend_RemainsConfirmedWhenSenderCleanupFails()
    {
        var sender = new Sender { FailDispose = true };
        await using var workspace = await OpenAsync(new Store(), sender);

        Assert.Equal(ReplaySendStatus.Confirmed, (await workspace.ReplayAsync(Delivery())).Status);
        Assert.Single(sender.Sends);
        Assert.Equal(1, sender.DisposeCount);
    }

    [Fact]
    public async Task ReconnectedWorkspace_RejectsStaleDeliveryBeforeSavingOrSending()
    {
        var store = new Store();
        var sender = new Sender();
        await using var workspace = await OpenAsync(store, sender);
        await workspace.DisconnectAsync();
        await workspace.ConnectAsync();
        int saves = store.SaveCount;

        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.ReplayAsync(Delivery()));

        Assert.Equal(saves, store.SaveCount);
        Assert.Empty(sender.Sends);
        Assert.Empty(workspace.Preferences.ReplayFamilies);
    }

    [Fact]
    public async Task ActiveDelivery_IsRejectedBeforeReservationOrSend()
    {
        var store = new Store();
        var sender = new Sender();
        await using var workspace = await OpenAsync(store, sender);
        var original = Delivery();
        var active = new MessageDelivery(original.Identity with { Bucket = MessageBucket.Active }, original.Message);
        int saves = store.SaveCount;

        await Assert.ThrowsAsync<ArgumentException>(() => workspace.ReplayAsync(active));

        Assert.Empty(sender.Sends);
        Assert.Equal(saves, store.SaveCount);
        Assert.Empty(store.Saved.ReplayFamilies);
    }

    [Fact]
    public async Task SettingsSnapshotFromBeforeReplay_CannotEraseNewReservation()
    {
        var store = new Store();
        await using var workspace = await OpenAsync(store, new Sender());
        var beforeReplay = workspace.Preferences;
        var replay = await workspace.ReplayAsync(Delivery());

        await workspace.ApplyPreferencesAsync(beforeReplay with { CloseToTray = false });

        Assert.False(workspace.Preferences.CloseToTray);
        Assert.Equal(replay.Reservation.Family, Assert.Single(store.Saved.ReplayFamilies["profile"]));
        Assert.Equal(replay.Reservation.Family, Assert.Single(workspace.Preferences.ReplayFamilies["profile"]));
        Assert.Equal(ReplaySendStatus.Confirmed, Assert.Single(store.Saved.ReplayAttempts).SendStatus);
        Assert.Equal(store.Saved.ReplayAttempts, workspace.Preferences.ReplayAttempts);
    }

    private static async Task<InvestigationWorkspace> OpenAsync(Store store, Sender sender)
    {
        var workspace = new InvestigationWorkspace(store,
            new BrokerConnectionWorkflow(() => new Factory(), _ => new Browser(), _ => new Messages()),
            profile =>
            {
                Assert.Equal(ConnectionProfileDefaults.LocalEmulator, profile);
                return sender;
            });
        await workspace.InitializeAsync();
        if (!workspace.IsConnected) await workspace.ConnectAsync();
        Assert.True(workspace.IsConnected);
        return workspace;
    }

    private static void AssertAttempt(ReplayAttempt attempt, MessageDelivery original, string outgoingMessageId,
        string expectedStatus, DateTimeOffset? sentAtUtc)
    {
        Assert.NotEqual(Guid.Empty, attempt.AttemptId);
        Assert.Equal("profile", attempt.ProfileId);
        Assert.Equal(ReplayNamespace.Fingerprint(ConnectionProfileDefaults.LocalEmulator),
            attempt.NamespaceFingerprint);
        Assert.Equal(outgoingMessageId, attempt.Reservation.MessageId);
        Assert.Equal(original.Identity.SequenceNumber, attempt.OriginalSequenceNumber);
        Assert.Equal(ReplayLineage.Fingerprint(original), attempt.OriginalFingerprint);
        Assert.Equal(original.Message.MessageId, attempt.OriginalMessageId);
        Assert.Equal(original.Identity.Source, attempt.OriginalSource);
        Assert.InRange(attempt.RequestedAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(Enum.Parse<ReplaySendStatus>(expectedStatus), attempt.SendStatus);
        Assert.Equal(sentAtUtc, attempt.SentAtUtc);
    }

    private static MessageDelivery Delivery(EntityAddress? source = null) => new(
        new DeliveryIdentity(1, source ?? new(EntityKind.Queue, "orders"), MessageBucket.DeadLetter, 7),
        new ExplorerMessage("original", 7, "display body", "display body", 2, null, null, 0,
            "application/octet-stream", null, null, null,
            new Dictionary<string, object?>(), new Dictionary<string, object?>())
        { RawBody = new BinaryData(new byte[] { 0, 255, 42 }) });

    private static ReplayAttempt HistoryAttempt(string profileId, ConnectionProfile connection, string messageId)
    {
        var delivery = Delivery();
        var family = ReplayLineage.Reserve(delivery, []).Family;
        return new(Guid.NewGuid(), profileId, ReplayNamespace.Fingerprint(connection),
            new ReplayReservation(family, messageId), delivery.Identity.Source, delivery.Identity.SequenceNumber,
            ReplayLineage.Fingerprint(delivery), delivery.Message.MessageId, DateTimeOffset.UtcNow,
            ReplaySendStatus.Confirmed, DateTimeOffset.UtcNow);
    }

    private sealed class Store : IWorkspacePreferencesStore
    {
        public Store(WorkspacePreferences? initial = null)
        {
            Saved = initial ?? new WorkspacePreferences
            {
                Profiles = [new("profile", ConnectionProfileDefaults.LocalEmulator)],
                SelectedProfileId = "profile"
            };
        }
        public WorkspacePreferences Saved { get; private set; }
        public bool FailSave { get; set; }
        public int SaveCount { get; private set; }
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(Saved));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
        {
            if (FailSave) throw new IOException("Storage unavailable");
            Saved = preferences;
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class Sender : IReplayCopySender
    {
        public List<(EntityAddress Destination, ServiceBusMessage Message)> Sends { get; } = [];
        public Func<EntityAddress, ServiceBusMessage, CancellationToken, Task> OnSend { get; set; } = (_, _, _) => Task.CompletedTask;
        public Func<Task> OnDispose { get; init; } = () => Task.CompletedTask;
        public bool FailDispose { get; init; }
        public int DisposeCount { get; private set; }
        public Task SendAsync(EntityAddress destination, ServiceBusMessage message, CancellationToken cancellationToken)
        {
            Sends.Add((destination, message));
            return OnSend(destination, message, cancellationToken);
        }
        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (FailDispose) throw new IOException("Cleanup failed");
            await OnDispose();
        }
    }

    private sealed class Factory : IServiceBusClientFactory
    {
        public ServiceBusAdministrationClient AdministrationClient => throw new InvalidOperationException("No administration mutation expected.");
        public ServiceBusClient RuntimeClient => throw new InvalidOperationException("No receiver or shared runtime access expected.");
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Browser : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(
            new EntityDiscoverySnapshot([new EntityObservation(new ServiceBusEntityNode(EntityKind.Queue, "orders", null,
                new EntityRuntimeCounts(0, 0, 0, 0), new EntityMetadata("orders", "Active", null, null, null, null, null, null, null)),
                new EntityCountObservation(new(0, CountAvailability.Known), new(0, CountAvailability.Known), new(0, CountAvailability.Known)))],
                DateTimeOffset.UtcNow, true, []));
    }

    private sealed class Messages : IServiceBusMessageService
    {
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExplorerMessage>>([]);
        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Replay must use the dedicated sender.");
    }
}
