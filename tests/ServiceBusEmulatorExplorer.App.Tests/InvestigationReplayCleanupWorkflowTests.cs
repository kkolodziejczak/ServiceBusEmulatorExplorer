using System.IO;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationReplayCleanupWorkflowTests
{
    private static readonly ConnectionProfile Profile = new("Profile",
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=test;UseDevelopmentEmulator=true;", "admin");
    private static readonly EntityAddress Source = new(EntityKind.Queue, "orders");
    private static readonly ServiceBusReceivedMessage Received = ServiceBusModelFactory.ServiceBusReceivedMessage(
        body: BinaryData.FromString("original body"), messageId: "original", sequenceNumber: 7,
        enqueuedTime: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
    private static MessageDelivery Delivery() => new(new(1, Source, MessageBucket.DeadLetter, 7), MessageProjection.Create(Received));
    private static ReplayFamilyState Family() => ReplayLineage.Reserve(Delivery(), []).Family;

    [Fact]
    public async Task ConfirmedDelete_PersistsMarkerOnlyForItsProfileAndFamily()
    {
        var original = Family();
        var store = new Store(original);
        var browser = new Browser();
        await using var workspace = await OpenAsync(store, browser, new Messages(), new Receiver());

        var result = await workspace.DeleteAsync([Delivery()]);

        Assert.Equal(DlqDeleteStatus.Confirmed, Assert.Single(result.Deletion.Outcomes).Status);
        Assert.False(result.CleanupPersistenceFailed);
        Assert.Equal(ReplayNamespace.Fingerprint(Profile), Assert.Single(store.Saved.ReplayFamilies["profile"]).CleanupNamespace);
        Assert.Equal(original, Assert.Single(store.Saved.ReplayFamilies["other"]));
    }

    [Theory]
    [InlineData(false, DlqDeleteStatus.Unavailable)]
    [InlineData(true, DlqDeleteStatus.Uncertain)]
    public async Task UnconfirmedDelete_DoesNotAuthorizeCounterCleanup(bool receive, DlqDeleteStatus expected)
    {
        var original = Family();
        var store = new Store(original);
        await using var workspace = await OpenAsync(store, new Browser(), new Messages(),
            new Receiver { ReturnMessage = receive, FailComplete = receive });

        var result = await workspace.DeleteAsync([Delivery()]);

        Assert.Equal(expected, Assert.Single(result.Deletion.Outcomes).Status);
        Assert.Equal(original, Assert.Single(workspace.Preferences.ReplayFamilies["profile"]));
        Assert.Equal(original, Assert.Single(store.Saved.ReplayFamilies["profile"]));
    }

    [Fact]
    public async Task FailedMarkerSave_RetainsConfirmedSessionFactAndReportsPersistenceFailure()
    {
        var store = new Store(Family());
        var browser = new Browser();
        await using var workspace = await OpenAsync(store, browser, new Messages(), new Receiver());
        store.FailSave = true;

        var result = await workspace.DeleteAsync([Delivery()]);

        Assert.True(result.CleanupPersistenceFailed);
        Assert.Equal(DlqDeleteStatus.Confirmed, Assert.Single(result.Deletion.Outcomes).Status);
        Assert.NotNull(Assert.Single(workspace.Preferences.ReplayFamilies["profile"]).CleanupNamespace);
        Assert.Null(Assert.Single(store.Saved.ReplayFamilies["profile"]).CleanupNamespace);
        store.FailSave = false;
        browser.Complete = true;
        Assert.Equal(1, await workspace.CleanupReplayFamiliesAsync());
        Assert.False(store.Saved.ReplayFamilies.ContainsKey("profile"));
        Assert.Single(store.Saved.ReplayFamilies["other"]);
    }

    [Fact]
    public async Task MarkerPersistenceRetry_DoesNotRequireCompleteDiscovery()
    {
        var store = new Store(Family());
        await using var workspace = await OpenAsync(store, new Browser(), new Messages(), new Receiver());
        store.FailSave = true;
        Assert.True((await workspace.DeleteAsync([Delivery()])).CleanupPersistenceFailed);
        Assert.Null(Assert.Single(store.Saved.ReplayFamilies["profile"]).CleanupNamespace);

        store.FailSave = false;
        Assert.Equal(0, await workspace.CleanupReplayFamiliesAsync());

        Assert.Equal(ReplayNamespace.Fingerprint(Profile), Assert.Single(store.Saved.ReplayFamilies["profile"]).CleanupNamespace);
        Assert.Equal(Assert.Single(workspace.Preferences.ReplayFamilies["profile"]), Assert.Single(store.Saved.ReplayFamilies["profile"]));
    }

    [Fact]
    public async Task LowercaseNamespaceMarker_ResumesCleanupOnConnect()
    {
        var store = new Store(Family() with { CleanupNamespace = ReplayNamespace.Fingerprint(Profile).ToLowerInvariant() });
        await using var workspace = await OpenAsync(store, new Browser { Complete = true }, new Messages(), new Receiver());

        Assert.False(store.Saved.ReplayFamilies.ContainsKey("profile"));
    }

    [Fact]
    public async Task LargeDlq_RestartsWithLargerBudgetAndEventuallyRemovesAbsentFamily()
    {
        var store = new Store(Family());
        var browser = new Browser();
        var starts = new List<long>();
        var unrelated = Enumerable.Range(1, 10_001).Select(index => MessageProjection.Create(
            ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("unrelated"),
                messageId: "unrelated", sequenceNumber: index))).ToArray();
        var messages = new Messages
        {
            Page = (_, take, from, _) =>
            {
                starts.Add(from ?? 0);
                return Task.FromResult<IReadOnlyList<ExplorerMessage>>(unrelated.Where(message => message.SequenceNumber >= (from ?? 0)).Take(take).ToArray());
            }
        };
        await using var workspace = await OpenAsync(store, browser, messages, new Receiver());
        await workspace.DeleteAsync([Delivery()]);
        browser.Complete = true;

        Assert.Equal(0, await workspace.CleanupReplayFamiliesAsync());
        Assert.True(store.Saved.ReplayFamilies.ContainsKey("profile"));
        int calls = starts.Count;
        Assert.Equal(100, calls);
        Assert.Equal(1, await workspace.CleanupReplayFamiliesAsync());

        Assert.Equal(0, starts[calls]);
        Assert.Equal(10_002, starts[^1]);
        Assert.False(store.Saved.ReplayFamilies.ContainsKey("profile"));
    }

    [Fact]
    public async Task PresentFamilyAtBudgetBoundary_DoesNotStarveLaterAbsentFamily()
    {
        var firstReceived = ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("first"),
            messageId: "first", sequenceNumber: 10_000);
        var firstDelivery = new MessageDelivery(new(1, Source, MessageBucket.DeadLetter, 10_000), MessageProjection.Create(firstReceived));
        var secondSource = new EntityAddress(EntityKind.Queue, "second");
        var secondDelivery = new MessageDelivery(Delivery().Identity with { Source = secondSource }, Delivery().Message);
        string marker = ReplayNamespace.Fingerprint(Profile);
        var first = ReplayLineage.Reserve(firstDelivery, []).Family with { CleanupNamespace = marker };
        var second = ReplayLineage.Reserve(secondDelivery, []).Family with { CleanupNamespace = marker };
        var store = new Store(first, second);
        var browser = new Browser { AdditionalQueue = "second" };
        var scannedSources = new List<EntityAddress>();
        var page = Enumerable.Range(1, 9_999).Select(index => MessageProjection.Create(
            ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("unrelated"),
                messageId: "unrelated", sequenceNumber: index))).Append(firstDelivery.Message).ToArray();
        var messages = new Messages
        {
            Page = (source, take, from, _) =>
            {
                scannedSources.Add(source);
                return Task.FromResult<IReadOnlyList<ExplorerMessage>>(source == secondSource ? []
                    : page.Where(message => message.SequenceNumber >= (from ?? 0)).Take(take).ToArray());
            }
        };
        await using var workspace = await OpenAsync(store, browser, messages, new Receiver());
        browser.Complete = true;

        Assert.Equal(0, await workspace.CleanupReplayFamiliesAsync());
        int calls = scannedSources.Count;
        Assert.Equal(100, calls);
        Assert.Equal(1, await workspace.CleanupReplayFamiliesAsync());

        Assert.Equal(secondSource, scannedSources[calls]);
        Assert.Equal(first, Assert.Single(store.Saved.ReplayFamilies["profile"]));
    }

    [Fact]
    public async Task PartialDiscoveryAndPeekFailure_RetainMarkerUntilCompleteSuccessfulRetry()
    {
        var store = new Store(Family());
        var browser = new Browser();
        var messages = new Messages();
        await using var workspace = await OpenAsync(store, browser, messages, new Receiver());
        await workspace.DeleteAsync([Delivery()]);

        Assert.Equal(0, await workspace.CleanupReplayFamiliesAsync());
        Assert.NotNull(Assert.Single(store.Saved.ReplayFamilies["profile"]).CleanupNamespace);
        browser.Complete = true;
        messages.Peek = _ => throw new IOException("Peek unavailable");
        Assert.Equal(0, await workspace.CleanupReplayFamiliesAsync());
        Assert.True(store.Saved.ReplayFamilies.ContainsKey("profile"));
        messages.Peek = _ => Task.FromResult<IReadOnlyList<ExplorerMessage>>([]);
        Assert.Equal(1, await workspace.CleanupReplayFamiliesAsync());
        Assert.False(workspace.Preferences.ReplayFamilies.ContainsKey("profile"));
        Assert.False(store.Saved.ReplayFamilies.ContainsKey("profile"));
    }

    [Fact]
    public async Task AbsenceWithFailedSave_DoesNotRemoveCounterFromMemoryOrDisk()
    {
        var store = new Store(Family());
        var browser = new Browser();
        await using var workspace = await OpenAsync(store, browser, new Messages(), new Receiver());
        await workspace.DeleteAsync([Delivery()]);
        var marked = Assert.Single(store.Saved.ReplayFamilies["profile"]);
        browser.Complete = true;
        store.FailSave = true;

        await Assert.ThrowsAsync<IOException>(() => workspace.CleanupReplayFamiliesAsync());

        Assert.Equal(marked, Assert.Single(workspace.Preferences.ReplayFamilies["profile"]));
        Assert.Equal(marked, Assert.Single(store.Saved.ReplayFamilies["profile"]));
        store.FailSave = false;
        Assert.Equal(1, await workspace.CleanupReplayFamiliesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteAbsence_DoesNotClearUnmarkedOrDifferentNamespaceFamily(bool differentNamespace)
    {
        var family = Family() with { CleanupNamespace = differentNamespace ? new string('A', 64) : null };
        var store = new Store(family);
        await using var workspace = await OpenAsync(store, new Browser { Complete = true }, new Messages(), new Receiver());

        Assert.Equal(0, await workspace.CleanupReplayFamiliesAsync());

        Assert.Equal(family, Assert.Single(store.Saved.ReplayFamilies["profile"]));
    }

    [Fact]
    public async Task Reconnection_ResumesPersistedPendingCleanup()
    {
        var family = Family() with { CleanupNamespace = ReplayNamespace.Fingerprint(Profile) };
        var store = new Store(family);
        await using var workspace = await OpenAsync(store, new Browser { Complete = true }, new Messages(), new Receiver());

        Assert.False(workspace.Preferences.ReplayFamilies.ContainsKey("profile"));
        Assert.False(store.Saved.ReplayFamilies.ContainsKey("profile"));
        Assert.Equal(family, Assert.Single(store.Saved.ReplayFamilies["other"]));
    }

    [Fact]
    public async Task DisconnectDuringHeldScan_CannotCommitStaleAbsence()
    {
        var store = new Store(Family());
        var browser = new Browser();
        var messages = new Messages();
        await using var workspace = await OpenAsync(store, browser, messages, new Receiver());
        await workspace.DeleteAsync([Delivery()]);
        browser.Complete = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        messages.Peek = async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [];
        };
        var cleanup = workspace.CleanupReplayFamiliesAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await workspace.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cleanup);

        Assert.NotNull(Assert.Single(store.Saved.ReplayFamilies["profile"]).CleanupNamespace);
        Assert.True(workspace.Preferences.ReplayFamilies.ContainsKey("profile"));
    }

    [Fact]
    public async Task ReplayWaitsForFamilyScan_ThenKeepsAdvancedReservation()
    {
        var store = new Store(Family());
        var browser = new Browser();
        var messages = new Messages();
        await using var workspace = await OpenAsync(store, browser, messages, new Receiver());
        await workspace.DeleteAsync([Delivery()]);
        browser.Complete = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        messages.Peek = async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return [Delivery().Message];
        };
        var cleanup = workspace.CleanupReplayFamiliesAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var replay = workspace.ReplayAsync(Delivery());
            Assert.False(replay.IsCompleted);
            release.TrySetResult();
            Assert.Equal(0, await cleanup.WaitAsync(TimeSpan.FromSeconds(5)));
            var result = await replay.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, result.Reservation.Family.LastAttempt);
            Assert.Equal(result.Reservation.Family, Assert.Single(store.Saved.ReplayFamilies["profile"]));
        }
        finally { release.TrySetResult(); }
    }

    private static async Task<InvestigationWorkspace> OpenAsync(Store store, Browser browser, Messages messages, Receiver receiver)
    {
        var workspace = new InvestigationWorkspace(store,
            new BrokerConnectionWorkflow(() => new Factory(), _ => browser, _ => messages), _ => new Sender(), (_, source) =>
            {
                Assert.Equal(Source, source);
                return receiver;
            });
        await workspace.InitializeAsync();
        if (!workspace.IsConnected) await workspace.ConnectAsync();
        Assert.True(workspace.IsConnected);
        return workspace;
    }

    private sealed class Store : IWorkspacePreferencesStore
    {
        public Store(params ReplayFamilyState[] families) => Saved = new()
        {
            Profiles = [new("profile", Profile), new("other", Profile with { Name = "Other" })],
            SelectedProfileId = "profile",
            ReplayFamilies = new Dictionary<string, IReadOnlyList<ReplayFamilyState>> { ["profile"] = families, ["other"] = [families[0]] }
        };
        public WorkspacePreferences Saved { get; private set; }
        public bool FailSave { get; set; }
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(Saved));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
        {
            if (FailSave) throw new IOException("Storage unavailable");
            Saved = preferences;
            return Task.CompletedTask;
        }
    }

    private sealed class Receiver : IDlqDeleteReceiver
    {
        public bool ReturnMessage { get; init; } = true;
        public bool FailComplete { get; init; }
        private bool received;
        public Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAsync(int take, CancellationToken cancellationToken)
        {
            bool deliver = ReturnMessage && !received;
            received = true;
            return Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(deliver ? [Received] : []);
        }
        public Task CompleteAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken) =>
            FailComplete ? Task.FromException(new IOException("Settlement response lost")) : Task.CompletedTask;
        public Task AbandonAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Browser : IInvestigationEntityBrowser
    {
        public bool Complete { get; set; }
        public string? AdditionalQueue { get; init; }
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(
            new EntityDiscoverySnapshot(AdditionalQueue is null ? [Observe("orders")] : [Observe("orders"), Observe(AdditionalQueue)],
                DateTimeOffset.UtcNow, Complete, []));

        private static EntityObservation Observe(string name) => new(new ServiceBusEntityNode(EntityKind.Queue, name, null,
            new EntityRuntimeCounts(0, 0, 0, 0), new EntityMetadata(name, "Active", null, null, null, null, null, null, null)),
            new EntityCountObservation(new(0, CountAvailability.Known), new(0, CountAvailability.Known), new(0, CountAvailability.Known)));
    }

    private sealed class Messages : IServiceBusMessageService
    {
        public Func<CancellationToken, Task<IReadOnlyList<ExplorerMessage>>> Peek { get; set; } = _ => Task.FromResult<IReadOnlyList<ExplorerMessage>>([]);
        public Func<EntityAddress, int, long?, CancellationToken, Task<IReadOnlyList<ExplorerMessage>>>? Page { get; init; }
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket, int take,
            long? fromSequenceNumber, CancellationToken cancellationToken) => bucket == MessageBucket.DeadLetter
                ? Page is null ? Peek(cancellationToken) : Page(address, take, fromSequenceNumber, cancellationToken)
                : Task.FromResult<IReadOnlyList<ExplorerMessage>>([]);
        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class Sender : IReplayCopySender
    {
        public Task SendAsync(EntityAddress destination, ServiceBusMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
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
