using System.IO;
using System.Reflection;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class ReplayHistoryCleanupTests
{
    private static readonly ConnectionProfile Profile = ConnectionProfileDefaults.LocalEmulator;
    private static readonly EntityAddress Source = new(EntityKind.Queue, "orders");
    private static readonly ServiceBusReceivedMessage OriginalReceived = ServiceBusModelFactory.ServiceBusReceivedMessage(
        body: BinaryData.FromString("original body"), messageId: "original-order", sequenceNumber: 7,
        enqueuedTime: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task CheckStatus_SavesCompleteFoundAndAbsentObservationsAndKeepsThemAfterReload()
    {
        var delivery = OriginalDelivery();
        var family = Family(delivery);
        var found = Attempt(delivery, family, 1, "replay-found");
        var absent = Attempt(delivery, family, 2, "replay-absent");
        var store = new Store(found, absent);
        var messages = new Messages();
        messages.Add(Source, MessageBucket.Active, Message("replay-found", 30));
        await using (var workspace = await OpenAsync(store, messages, new Receiver()))
        {
            await workspace.CheckReplayStatusAsync();

            var checkedFound = store.Saved.ReplayAttempts.Single(item => item.AttemptId == found.AttemptId);
            var checkedAbsent = store.Saved.ReplayAttempts.Single(item => item.AttemptId == absent.AttemptId);
            Assert.True(checkedFound.Observation!.IsComplete);
            Assert.Single(checkedFound.Observation.Locations);
            Assert.Equal(MessageBucket.Active, Assert.Single(checkedFound.Observation.Locations).Bucket);
            Assert.True(checkedAbsent.Observation!.IsAbsent);
            Assert.NotEqual(default, checkedAbsent.Observation.CheckedAtUtc);
        }

        await using var reloaded = await OpenAsync(store, messages, new Receiver());
        Assert.True(reloaded.Preferences.ReplayAttempts.Single(item => item.AttemptId == absent.AttemptId).Observation!.IsAbsent);
    }

    [Fact]
    public async Task CheckStatus_PeekFailureIsIncompleteAndCannotCreateAbsence()
    {
        var delivery = OriginalDelivery();
        var attempt = Attempt(delivery, Family(delivery), 1, "replay-absent");
        var store = new Store(attempt);
        var messages = new Messages { Failure = (Source, MessageBucket.DeadLetter) };
        await using var workspace = await OpenAsync(store, messages, new Receiver());

        await workspace.CheckReplayStatusAsync();

        var result = Assert.Single(store.Saved.ReplayAttempts).Observation!;
        Assert.False(result.IsComplete);
        Assert.False(result.IsAbsent);
        Assert.False(string.IsNullOrWhiteSpace(result.Limitation));
    }

    [Fact]
    public async Task RemoveHistory_HidesOnlyEligibleAttemptAndPreservesItsLineage()
    {
        var delivery = OriginalDelivery();
        var family = Family(delivery) with { LastAttempt = 2 };
        var first = Attempt(delivery, family, 1, "replay-one") with { Observation = CompleteAbsence() };
        var sibling = Attempt(delivery, family, 2, "replay-two", ReplaySendStatus.Uncertain);
        var store = new Store(first, sibling);
        store.SeedReplayFamily(family);
        await using var workspace = await OpenAsync(store, new Messages(), new Receiver());

        Assert.True(workspace.CanRemoveReplayHistory(first));
        Assert.True(await workspace.RemoveReplayHistoryAsync(first));

        Assert.DoesNotContain(workspace.CurrentReplayHistory, item => item.AttemptId == first.AttemptId);
        Assert.Contains(workspace.CurrentReplayHistory, item => item.AttemptId == sibling.AttemptId);
        Assert.True(workspace.CurrentReplayAttempts.Single(item => item.AttemptId == first.AttemptId).HiddenFromHistory);
        Assert.Equal(2, store.Saved.ReplayAttempts.Count);
        Assert.Equal(2, Assert.Single(store.Saved.ReplayFamilies["profile"]).LastAttempt);
        Assert.Equal(ReplaySendStatus.Uncertain, store.Saved.ReplayAttempts.Single(item => item.AttemptId == sibling.AttemptId).SendStatus);
    }

    [Theory]
    [InlineData("unchecked")]
    [InlineData("incomplete")]
    [InlineData("found")]
    [InlineData("not-sent")]
    public async Task RemoveHistory_RequiresCurrentCompleteAbsence(string state)
    {
        var delivery = OriginalDelivery();
        var attempt = state switch
        {
            "unchecked" => Attempt(delivery, Family(delivery), 1, "replay-one"),
            "incomplete" => Attempt(delivery, Family(delivery), 1, "replay-one") with { Observation = PartialAbsence() },
            "found" => Attempt(delivery, Family(delivery), 1, "replay-one") with { Observation = FoundObservation() },
            "not-sent" => Attempt(delivery, Family(delivery), 1, "replay-one", ReplaySendStatus.NotSent) with { Observation = CompleteAbsence() },
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        await using var workspace = await OpenAsync(new Store(attempt), new Messages(), new Receiver());

        Assert.False(workspace.CanRemoveReplayHistory(attempt));
        Assert.False(await workspace.RemoveReplayHistoryAsync(attempt));
        Assert.Contains(workspace.CurrentReplayHistory, item => item.AttemptId == attempt.AttemptId);
    }

    [Fact]
    public async Task RemoveHistory_UsesCurrentPersistedObservationAndRejectsOtherProfile()
    {
        var delivery = OriginalDelivery();
        var family = Family(delivery);
        var stored = Attempt(delivery, family, 1, "replay-one");
        var staleAbsence = stored with { Observation = CompleteAbsence() };
        var store = new Store(stored);
        await using var workspace = await OpenAsync(store, new Messages(), new Receiver());

        Assert.False(workspace.CanRemoveReplayHistory(staleAbsence));
        Assert.False(await workspace.RemoveReplayHistoryAsync(staleAbsence));
        var other = new InvestigationProfile("other", Profile);
        await workspace.ApplyPreferencesAsync(workspace.Preferences with { Profiles = [.. workspace.Preferences.Profiles, other] });
        Assert.True(await workspace.SwitchProfileAsync(other));

        Assert.False(workspace.CanRemoveReplayHistory(staleAbsence));
        Assert.False(await workspace.RemoveReplayHistoryAsync(staleAbsence));
        Assert.False(store.Saved.ReplayAttempts.Single().HiddenFromHistory);
    }

    [Fact]
    public async Task RemoveHistory_SaveFailureLeavesVisibleAndUnhiddenAttempt()
    {
        var delivery = OriginalDelivery();
        var attempt = Attempt(delivery, Family(delivery), 1, "replay-one") with { Observation = CompleteAbsence() };
        var store = new Store(attempt);
        await using var workspace = await OpenAsync(store, new Messages(), new Receiver());
        store.FailNextSave = true;

        Assert.False(await workspace.RemoveReplayHistoryAsync(attempt));

        Assert.Contains(workspace.CurrentReplayHistory, item => item.AttemptId == attempt.AttemptId);
        Assert.False(workspace.CurrentReplayAttempts.Single().HiddenFromHistory);
        Assert.False(store.Saved.ReplayAttempts.Single().HiddenFromHistory);
    }

    [Fact]
    public async Task StaleOutcomeSave_PreservesPersistedHiddenFlag()
    {
        var delivery = OriginalDelivery();
        var hidden = Attempt(delivery, Family(delivery), 1, "replay-one") with
        {
            Observation = CompleteAbsence(),
            HiddenFromHistory = true
        };
        var store = new Store(hidden);
        await using var workspace = await OpenAsync(store, new Messages(), new Receiver());
        var staleOutcome = hidden with { HiddenFromHistory = false, SendStatus = ReplaySendStatus.Uncertain, SentAtUtc = null };
        var saveOutcome = typeof(InvestigationWorkspace).GetMethod("SaveReplayOutcomeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.True(await (Task<bool>)saveOutcome.Invoke(workspace, [staleOutcome])!);

        Assert.True(store.Saved.ReplayAttempts.Single().HiddenFromHistory);
        Assert.DoesNotContain(workspace.CurrentReplayHistory, item => item.AttemptId == hidden.AttemptId);
        Assert.Equal(ReplaySendStatus.Uncertain, workspace.CurrentReplayAttempts.Single().SendStatus);
    }

    [Fact]
    public async Task RemoveHistory_IsUnavailableWhileStatusScanIsInFlight()
    {
        var delivery = OriginalDelivery();
        var attempt = Attempt(delivery, Family(delivery), 1, "replay-one") with { Observation = CompleteAbsence() };
        var store = new Store(attempt);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = new Messages();
        await using var workspace = await OpenAsync(store, messages, new Receiver());
        messages.HeldPeek = async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [];
        };
        var checking = workspace.CheckReplayStatusAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(workspace.CanRemoveReplayHistory(attempt));

        await workspace.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checking);
        Assert.True(workspace.CanRemoveReplayHistory(attempt));
        Assert.True(await workspace.RemoveReplayHistoryAsync(attempt));
    }

    [Theory]
    [InlineData("not-sent", true)]
    [InlineData("uncertain", false)]
    [InlineData("found", false)]
    [InlineData("incomplete", false)]
    [InlineData("hidden-uncertain", false)]
    [InlineData("hidden-found", false)]
    public async Task PrepareCleanup_RequiresEveryConfirmedSiblingToBeCompletelyAbsent(string siblingState, bool expectedPrepared)
    {
        var delivery = OriginalDelivery();
        var family = Family(delivery) with { LastAttempt = 2 };
        var first = Attempt(delivery, family, 1, "replay-one") with { Observation = CompleteAbsence() };
        var sibling = siblingState switch
        {
            "not-sent" => Attempt(delivery, family, 2, "replay-two", ReplaySendStatus.NotSent),
            "uncertain" => Attempt(delivery, family, 2, "replay-two", ReplaySendStatus.Uncertain) with { Observation = CompleteAbsence() },
            "found" => Attempt(delivery, family, 2, "replay-two") with { Observation = FoundObservation() },
            "incomplete" => Attempt(delivery, family, 2, "replay-two") with { Observation = PartialAbsence() },
            "hidden-uncertain" => Attempt(delivery, family, 2, "replay-two", ReplaySendStatus.Uncertain) with { HiddenFromHistory = true },
            "hidden-found" => Attempt(delivery, family, 2, "replay-two") with { Observation = FoundObservation(), HiddenFromHistory = true },
            _ => throw new ArgumentOutOfRangeException(nameof(siblingState))
        };
        var store = new Store(first, sibling);
        var messages = new Messages
        {
            Failure = siblingState == "incomplete" ? (Source, MessageBucket.DeadLetter) : null
        };
        messages.Add(Source, MessageBucket.DeadLetter, Explorer(OriginalReceived));
        if (siblingState is "found" or "hidden-found")
            messages.Add(Source, MessageBucket.Active, Message("replay-two", 30));
        await using var workspace = await OpenAsync(store, messages, new Receiver());

        PreparedReplayCleanup? prepared = await workspace.PrepareReplayCleanupAsync(first);

        Assert.Equal(expectedPrepared, prepared is not null);
        if (expectedPrepared)
        {
            Assert.Equal(first.AttemptId, prepared!.Attempt.AttemptId);
            Assert.Equal(first.OriginalSource, prepared.Original.Identity.Source);
            Assert.Equal(first.OriginalSequenceNumber, prepared.Original.Identity.SequenceNumber);
            Assert.Equal(first.OriginalFingerprint, ReplayLineage.Fingerprint(prepared.Original));
        }
    }

    [Fact]
    public async Task PriorFoundObservation_DoesNotBlockCleanupAfterFreshCompleteAbsence()
    {
        var delivery = OriginalDelivery();
        var family = Family(delivery);
        var attempt = Attempt(delivery, family, 1, "replay-one") with { Observation = FoundObservation() };
        var store = new Store(attempt);
        var messages = new Messages();
        messages.Add(Source, MessageBucket.DeadLetter, Explorer(OriginalReceived));
        await using var workspace = await OpenAsync(store, messages, new Receiver());

        PreparedReplayCleanup? prepared = await workspace.PrepareReplayCleanupAsync(attempt);

        Assert.NotNull(prepared);
        var refreshed = Assert.Single(store.Saved.ReplayAttempts);
        Assert.True(refreshed.Observation!.IsAbsent);
    }

    [Theory]
    [InlineData(false, ReplayOriginalStatus.Deleted)]
    [InlineData(true, ReplayOriginalStatus.Uncertain)]
    public async Task DeleteOriginal_UpdatesHistoryFromExactSettlementOutcome(bool failComplete, ReplayOriginalStatus expectedStatus)
    {
        var delivery = OriginalDelivery();
        var attempt = Attempt(delivery, Family(delivery), 1, "replay-one") with { Observation = CompleteAbsence() };
        var store = new Store(attempt);
        var messages = new Messages();
        messages.Add(Source, MessageBucket.DeadLetter, delivery.Message);
        var receiver = new Receiver { FailComplete = failComplete };
        await using var workspace = await OpenAsync(store, messages, receiver);
        var prepared = await workspace.PrepareReplayCleanupAsync(attempt);
        Assert.NotNull(prepared);

        await workspace.DeleteReplayOriginalAsync(prepared!);

        Assert.Equal(expectedStatus, Assert.Single(store.Saved.ReplayAttempts).OriginalStatus);
        Assert.Equal([7L], receiver.CompletedSequences);
        Assert.Equal(1, receiver.ReceiveCalls);
    }

    [Fact]
    public async Task MissingOriginalIsUnavailableAndNeverSettled()
    {
        var delivery = OriginalDelivery();
        var attempt = Attempt(delivery, Family(delivery), 1, "replay-one") with { Observation = CompleteAbsence() };
        var store = new Store(attempt);
        var receiver = new Receiver();
        await using var workspace = await OpenAsync(store, new Messages(), receiver);

        PreparedReplayCleanup? prepared = await workspace.PrepareReplayCleanupAsync(attempt);

        Assert.Null(prepared);
        Assert.Equal(ReplayOriginalStatus.Unavailable, Assert.Single(store.Saved.ReplayAttempts).OriginalStatus);
        Assert.Equal(0, receiver.ReceiveCalls);
        Assert.Empty(receiver.CompletedSequences);
    }

    [Fact]
    public async Task OriginalLookupRejectsSameSequenceWithDifferentFingerprint()
    {
        var delivery = OriginalDelivery();
        var attempt = Attempt(delivery, Family(delivery), 1, "replay-one");
        var store = new Store(attempt);
        var changed = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("changed body"), messageId: OriginalReceived.MessageId,
            sequenceNumber: OriginalReceived.SequenceNumber, enqueuedTime: OriginalReceived.EnqueuedTime);
        var messages = new Messages();
        messages.Add(Source, MessageBucket.DeadLetter, Explorer(changed));
        var receiver = new Receiver();
        await using var workspace = await OpenAsync(store, messages, receiver);

        MessageDelivery? found = await workspace.FindReplayOriginalAsync(attempt);

        Assert.Null(found);
        Assert.Equal(0, receiver.ReceiveCalls);
    }

    [Fact]
    public async Task CanceledCleanupDoesNotSettleOrMarkOriginalDeleted()
    {
        var delivery = OriginalDelivery();
        var attempt = Attempt(delivery, Family(delivery), 1, "replay-one") with { Observation = CompleteAbsence() };
        var store = new Store(attempt);
        var messages = new Messages();
        messages.Add(Source, MessageBucket.DeadLetter, delivery.Message);
        var receiver = new Receiver();
        await using var workspace = await OpenAsync(store, messages, receiver);
        var prepared = await workspace.PrepareReplayCleanupAsync(attempt);
        Assert.NotNull(prepared);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            workspace.DeleteReplayOriginalAsync(prepared!, cancellation.Token));

        Assert.Equal(0, receiver.ReceiveCalls);
        Assert.Equal(ReplayOriginalStatus.Retained, Assert.Single(store.Saved.ReplayAttempts).OriginalStatus);
    }

    [Fact]
    public async Task PreparedCleanupFromPriorGenerationCannotSettleAfterReconnect()
    {
        var delivery = OriginalDelivery();
        var attempt = Attempt(delivery, Family(delivery), 1, "replay-one") with { Observation = CompleteAbsence() };
        var store = new Store(attempt);
        var messages = new Messages();
        messages.Add(Source, MessageBucket.DeadLetter, delivery.Message);
        var receiver = new Receiver();
        await using var workspace = await OpenAsync(store, messages, receiver);
        var prepared = await workspace.PrepareReplayCleanupAsync(attempt);
        Assert.NotNull(prepared);
        await workspace.DisconnectAsync();
        await workspace.ConnectAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.DeleteReplayOriginalAsync(prepared!));

        Assert.Equal(0, receiver.ReceiveCalls);
        Assert.Equal(ReplayOriginalStatus.Retained, Assert.Single(store.Saved.ReplayAttempts).OriginalStatus);
    }

    [Fact]
    public async Task ConnectionChangeDuringObservationCannotCommitStaleAbsence()
    {
        var delivery = OriginalDelivery();
        var attempt = Attempt(delivery, Family(delivery), 1, "replay-one");
        var store = new Store(attempt);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = new Messages();
        await using var workspace = await OpenAsync(store, messages, new Receiver());
        messages.HeldPeek = async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [];
        };
        var check = workspace.CheckReplayStatusAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await workspace.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);

        Assert.Null(Assert.Single(store.Saved.ReplayAttempts).Observation);
    }

    [Fact]
    public async Task NewlyUncertainSiblingAfterPreparationBlocksOriginalCleanup()
    {
        var delivery = OriginalDelivery();
        var attempt = Attempt(delivery, Family(delivery), 1, "replay-one") with { Observation = CompleteAbsence() };
        var store = new Store(attempt);
        var messages = new Messages();
        messages.Add(Source, MessageBucket.DeadLetter, delivery.Message);
        var receiver = new Receiver();
        await using var workspace = await OpenAsync(store, messages, receiver);
        var preparedCleanup = await workspace.PrepareReplayCleanupAsync(attempt);
        Assert.NotNull(preparedCleanup);

        var sibling = workspace.PrepareReplay(delivery);
        await workspace.SendReplayAsync(sibling, sibling.Reservation.MessageId);

        await workspace.DeleteReplayOriginalAsync(preparedCleanup!);

        Assert.Equal(0, receiver.ReceiveCalls);
        Assert.Contains(store.Saved.ReplayAttempts, item => item.AttemptId == sibling.AttemptId
            && item.SendStatus == ReplaySendStatus.Uncertain);
        Assert.All(store.Saved.ReplayAttempts, item => Assert.Equal(ReplayOriginalStatus.Retained, item.OriginalStatus));
    }

    [Fact]
    public async Task ConfirmedDeleteWithHistorySaveFailureRetainsDurableStatusAndKeepsSessionFact()
    {
        var delivery = OriginalDelivery();
        var attempt = Attempt(delivery, Family(delivery), 1, "replay-one") with { Observation = CompleteAbsence() };
        var store = new Store(attempt);
        var messages = new Messages();
        messages.Add(Source, MessageBucket.DeadLetter, delivery.Message);
        var receiver = new Receiver();
        await using var workspace = await OpenAsync(store, messages, receiver);
        var prepared = await workspace.PrepareReplayCleanupAsync(attempt);
        Assert.NotNull(prepared);
        store.FailOriginalStatusSave = true;

        await workspace.DeleteReplayOriginalAsync(prepared!);

        Assert.Equal([7L], receiver.CompletedSequences);
        Assert.Equal(ReplayOriginalStatus.Deleted, Assert.Single(workspace.Preferences.ReplayAttempts).OriginalStatus);
        Assert.Equal(ReplayOriginalStatus.Retained, Assert.Single(store.Saved.ReplayAttempts).OriginalStatus);
    }

    [Theory]
    [InlineData(false, ReplayOriginalStatus.Deleted, DlqDeleteStatus.Confirmed)]
    [InlineData(true, ReplayOriginalStatus.Uncertain, DlqDeleteStatus.Uncertain)]
    public async Task OrdinaryDeleteOfTrackedOriginalUpdatesReplayHistory(bool failComplete,
        ReplayOriginalStatus expectedHistoryStatus, DlqDeleteStatus expectedDeleteStatus)
    {
        var delivery = OriginalDelivery();
        var attempt = Attempt(delivery, Family(delivery), 1, "replay-one");
        var store = new Store(attempt);
        var receiver = new Receiver { FailComplete = failComplete };
        await using var workspace = await OpenAsync(store, new Messages(), receiver);

        var result = await workspace.DeleteAsync([delivery]);

        Assert.Equal(expectedDeleteStatus, Assert.Single(result.Deletion.Outcomes).Status);
        Assert.Equal([7L], receiver.CompletedSequences);
        Assert.Equal(expectedHistoryStatus, Assert.Single(store.Saved.ReplayAttempts).OriginalStatus);
    }

    [Fact]
    public async Task RemovingUnusedReplayCounterPreservesAttemptHistory()
    {
        var delivery = OriginalDelivery();
        var attempt = Attempt(delivery, Family(delivery), 1, "replay-one");
        var store = new Store(attempt);
        var counter = Family(delivery) with { CleanupNamespace = ReplayNamespace.Fingerprint(Profile) };
        store.SeedReplayFamily(counter);
        await using var workspace = await OpenAsync(store, new Messages(), new Receiver());

        _ = await workspace.CleanupReplayFamiliesAsync();

        Assert.False(store.Saved.ReplayFamilies.ContainsKey("profile"));
        Assert.Equal(attempt, Assert.Single(workspace.Preferences.ReplayAttempts));
        Assert.Equal(attempt, Assert.Single(store.Saved.ReplayAttempts));
    }

    private static MessageDelivery OriginalDelivery(long generation = 1) => new(
        new DeliveryIdentity(generation, Source, MessageBucket.DeadLetter, OriginalReceived.SequenceNumber),
        Explorer(OriginalReceived));

    private static ExplorerMessage Explorer(ServiceBusReceivedMessage message) => MessageProjection.Create(message);

    private static ExplorerMessage Message(string id, long sequence) => new(
        id, sequence, id, id, id.Length, null, null, 0, "application/json", null, null, null,
        new Dictionary<string, object?>(), new Dictionary<string, object?>());

    private static ReplayFamilyState Family(MessageDelivery delivery) => ReplayLineage.Reserve(delivery, []).Family;

    private static ReplayAttempt Attempt(MessageDelivery original, ReplayFamilyState family, long number, string id,
        ReplaySendStatus status = ReplaySendStatus.Confirmed) =>
        new(Guid.NewGuid(), "profile", ReplayNamespace.Fingerprint(Profile),
            new ReplayReservation(family with { LastAttempt = number }, id), original.Identity.Source,
            original.Identity.SequenceNumber, ReplayLineage.Fingerprint(original), original.Message.MessageId,
            DateTimeOffset.UtcNow.AddMinutes(-1), status,
            status == ReplaySendStatus.Confirmed ? DateTimeOffset.UtcNow.AddSeconds(-50) : null);

    private static ReplayObservation CompleteAbsence() => new(DateTimeOffset.UtcNow, true, 1, 0, []);

    private static ReplayObservation FoundObservation() => new(DateTimeOffset.UtcNow, true, 1, 1,
        [new ReplayLocation(Source, MessageBucket.Active, "Main queue")]);

    private static ReplayObservation PartialAbsence() => new(DateTimeOffset.UtcNow, false, 1, 0, [], "Scan incomplete.");

    private static async Task<InvestigationWorkspace> OpenAsync(Store store, Messages messages, Receiver receiver)
    {
        var workspace = new InvestigationWorkspace(store,
            new BrokerConnectionWorkflow(() => new Factory(), _ => new Browser(), _ => messages),
            _ => new Sender(), (_, source) =>
            {
                Assert.Equal(Source, source);
                return receiver;
            });
        await workspace.InitializeAsync();
        if (!workspace.IsConnected) await workspace.ConnectAsync();
        Assert.True(workspace.IsConnected);
        return workspace;
    }

    private sealed class Store(params ReplayAttempt[] attempts) : IWorkspacePreferencesStore
    {
        public WorkspacePreferences Saved { get; private set; } = new()
        {
            Profiles = [new("profile", Profile)],
            SelectedProfileId = "profile",
            ReplayAttempts = attempts
        };
        public bool FailOriginalStatusSave { get; set; }
        public bool FailNextSave { get; set; }
        public void SeedReplayFamily(ReplayFamilyState family) => Saved = Saved with
        {
            ReplayFamilies = new Dictionary<string, IReadOnlyList<ReplayFamilyState>> { ["profile"] = [family] }
        };
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(Saved));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("Preferences storage unavailable.");
            }
            if (FailOriginalStatusSave && preferences.ReplayAttempts.Any(attempt =>
                    Saved.ReplayAttempts.SingleOrDefault(existing => existing.AttemptId == attempt.AttemptId)?.OriginalStatus != attempt.OriginalStatus))
                throw new IOException("Replay status storage unavailable.");
            Saved = preferences;
            return Task.CompletedTask;
        }
    }

    private sealed class Browser : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(
            new EntityDiscoverySnapshot(
                [new EntityObservation(new DiscoveredEntity(EntityKind.Queue, "orders", null,
                    new EntityMetadata("orders", "Active", null, null, null, null, null, null, null)),
                    new EntityCountObservation(new(0, CountAvailability.Known), new(0, CountAvailability.Known), new(0, CountAvailability.Known)))],
                DateTimeOffset.UtcNow, true, []));
    }

    private sealed class Messages : IServiceBusMessageService
    {
        private readonly Dictionary<(EntityAddress Address, MessageBucket Bucket), IReadOnlyList<ExplorerMessage>> messages = [];
        public (EntityAddress Address, MessageBucket Bucket)? Failure { get; init; }
        public Func<CancellationToken, Task<IReadOnlyList<ExplorerMessage>>>? HeldPeek { get; set; }
        public void Add(EntityAddress address, MessageBucket bucket, params ExplorerMessage[] values) => messages[(address, bucket)] = values;

        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            if (HeldPeek is not null) return HeldPeek(cancellationToken);
            if (Failure is { } failure && failure.Address == address && failure.Bucket == bucket)
                throw new IOException("Peek unavailable.");
            IReadOnlyList<ExplorerMessage> result = messages.TryGetValue((address, bucket), out var found)
                ? found.Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber)
                    .Take(take).ToArray()
                : [];
            return Task.FromResult(result);
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("History cleanup must not send messages.");
    }

    private sealed class Receiver : IDlqDeleteReceiver
    {
        private bool returned;
        public int ReceiveCalls { get; private set; }
        public bool FailComplete { get; init; }
        public List<long> CompletedSequences { get; } = [];

        public Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAsync(int take, CancellationToken cancellationToken)
        {
            ReceiveCalls++;
            bool returnMessage = !returned;
            returned = true;
            return Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(returnMessage ? [OriginalReceived] : []);
        }

        public Task CompleteAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken)
        {
            CompletedSequences.Add(message.SequenceNumber);
            return FailComplete ? Task.FromException(new IOException("Settlement response lost.")) : Task.CompletedTask;
        }

        public Task AbandonAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Sender : IReplayCopySender
    {
        public Task SendAsync(EntityAddress destination, ServiceBusMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This fixture must not replay messages.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Factory : IServiceBusClientFactory
    {
        public ServiceBusAdministrationClient AdministrationClient => throw new InvalidOperationException();
        public ServiceBusClient RuntimeClient => throw new InvalidOperationException();
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
