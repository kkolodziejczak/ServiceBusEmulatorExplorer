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
        var replay = workspace.ReplayAsync(Delivery(), cancellationToken: cancellation.Token);
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
    }

    private static async Task<InvestigationWorkspace> OpenAsync(Store store, Sender sender)
    {
        var workspace = new InvestigationWorkspace(store,
            new BrokerConnectionWorkflow(() => new Factory(), _ => new Browser(), _ => new Messages()),
            profile =>
            {
                Assert.Equal("runtime", profile.RuntimeConnectionString);
                return sender;
            });
        await workspace.InitializeAsync();
        if (!workspace.IsConnected) await workspace.ConnectAsync();
        Assert.True(workspace.IsConnected);
        return workspace;
    }

    private static MessageDelivery Delivery(EntityAddress? source = null) => new(
        new DeliveryIdentity(1, source ?? new(EntityKind.Queue, "orders"), MessageBucket.DeadLetter, 7),
        new ExplorerMessage("original", 7, "display body", "display body", 2, null, null, 0,
            "application/octet-stream", null, null, null,
            new Dictionary<string, object?>(), new Dictionary<string, object?>())
        { RawBody = new BinaryData(new byte[] { 0, 255, 42 }) });

    private sealed class Store : IWorkspacePreferencesStore
    {
        public WorkspacePreferences Saved { get; private set; } = new()
        {
            Profiles = [new("profile", new ConnectionProfile("Profile", "runtime", "admin"))],
            SelectedProfileId = "profile"
        };
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
