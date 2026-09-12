using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationWorkspaceTests
{
    [Fact]
    public async Task DisconnectDuringInitialRead_DoesNotMarkWorkspaceConnectedAfterReadCompletes()
    {
        var profile = Profile("profile-a", "A");
        var factory = new FakeFactory();
        var browser = new FakeBrowser(Snapshot(Queue("orders")));
        var messages = new FakeMessages { BlockSecondPeek = true };
        await using var workspace = CreateWorkspace(profile, factory, browser, messages);
        await workspace.InitializeAsync();

        Task connect = workspace.ConnectAsync();
        await messages.InitialReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await workspace.DisconnectAsync();
        messages.ReleaseInitialRead();
        await connect;

        Assert.False(workspace.IsConnected);
        Assert.False(workspace.Preferences.WasConnected);
        Assert.Equal("Disconnected.", workspace.Status);
    }

    [Fact]
    public async Task SwitchProfileDuringConnection_IgnoresStaleSessionWhenOriginalConnectCompletes()
    {
        var profileA = Profile("profile-a", "A");
        var profileB = Profile("profile-b", "B");
        var factory = new FakeFactory { BlockConnect = true };
        var browser = new FakeBrowser(Snapshot(Queue("orders")));
        var messages = new FakeMessages();
        await using var workspace = CreateWorkspace(profileA, factory, browser, messages, profileB);
        await workspace.InitializeAsync();

        Task connect = workspace.ConnectAsync();
        await factory.ConnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(await workspace.SwitchProfileAsync(profileB));
        factory.ReleaseConnect();
        await connect;

        Assert.Equal(profileB.Id, workspace.Preferences.SelectedProfileId);
        Assert.False(workspace.IsConnected);
        Assert.False(workspace.Preferences.WasConnected);
        Assert.Equal(1, factory.DisposeCount);
    }

    [Fact]
    public async Task SwitchProfileWhileWarningApprovalIsPending_DoesNotConnectTheOldProfileAfterApproval()
    {
        var profileA = Profile("profile-a", "A") with { WarningMessage = "Use the test namespace?" };
        var profileB = Profile("profile-b", "B");
        var factory = new FakeFactory();
        var browser = new FakeBrowser(Snapshot(Queue("orders")));
        var messages = new FakeMessages();
        await using var workspace = CreateWorkspace(profileA, factory, browser, messages, profileB);
        var approvalRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.ConfirmWarning = profile =>
        {
            approvalRequested.TrySetResult(true);
            return approval.Task;
        };
        await workspace.InitializeAsync();

        Task connect = workspace.ConnectAsync();
        await approvalRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(await workspace.SwitchProfileAsync(profileB));
        approval.SetResult(true);
        await connect;

        Assert.Equal(profileB.Id, workspace.Preferences.SelectedProfileId);
        Assert.False(workspace.IsConnected);
        Assert.Equal(0, factory.ConnectCount);
    }

    [Fact]
    public async Task UpdateWindowBoundsAsync_PersistsFiniteBoundsAndIgnoresInvalidValues()
    {
        var profile = Profile("profile-a", "A");
        var factory = new FakeFactory();
        var browser = new FakeBrowser(Snapshot(Queue("orders")));
        var messages = new FakeMessages();
        await using var workspace = CreateWorkspace(profile, factory, browser, messages);
        await workspace.InitializeAsync();

        await workspace.UpdateWindowBoundsAsync(1200, 800);
        await workspace.UpdateWindowBoundsAsync(double.NaN, 800);
        await workspace.UpdateWindowBoundsAsync(1200, double.PositiveInfinity);
        await workspace.UpdateWindowBoundsAsync(979, 640);

        Assert.Equal(1200, workspace.Preferences.WindowWidth);
        Assert.Equal(800, workspace.Preferences.WindowHeight);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotentAndSavesWasConnectedBeforeDisconnecting()
    {
        var profile = Profile("profile-a", "A");
        var preferences = new WorkspacePreferences
        {
            Profiles = [profile],
            SelectedProfileId = profile.Id
        };
        var store = new FakeStore(preferences);
        var factory = new FakeFactory();
        var workflow = new BrokerConnectionWorkflow(
            () => factory,
            _ => new FakeBrowser(Snapshot()),
            _ => new FakeMessages());
        var workspace = new InvestigationWorkspace(store, workflow);

        await workspace.InitializeAsync();
        await workspace.ConnectAsync();
        Assert.True(workspace.IsConnected);

        await workspace.DisposeAsync();
        await workspace.DisposeAsync();

        Assert.Equal(2, store.Saves.Count);
        Assert.True(store.Saves[^1].WasConnected);
    }

    private static InvestigationWorkspace CreateWorkspace(
        InvestigationProfile selected,
        FakeFactory factory,
        FakeBrowser browser,
        FakeMessages messages,
        params InvestigationProfile[] additionalProfiles)
    {
        var profiles = new[] { selected }.Concat(additionalProfiles).ToArray();
        var preferences = new WorkspacePreferences
        {
            Profiles = profiles,
            SelectedProfileId = selected.Id,
            WasConnected = false
        };
        var store = new FakeStore(preferences);
        var workflow = new BrokerConnectionWorkflow(
            () => factory,
            _ => browser,
            _ => messages);
        return new InvestigationWorkspace(store, workflow);
    }

    private static InvestigationProfile Profile(string id, string name) =>
        new(id, new ConnectionProfile(name, $"runtime-{id}", $"admin-{id}"));

    private static EntityDiscoverySnapshot Snapshot(params ServiceBusEntityNode[] entities) =>
        new(
            entities.Select(entity => new EntityObservation(
                entity,
                new EntityCountObservation(
                    new(0, CountAvailability.Known),
                    new(0, CountAvailability.Known),
                    new(0, CountAvailability.Known)))).ToArray(),
            DateTimeOffset.UtcNow,
            IsComplete: true,
            Issues: []);

    private static ServiceBusEntityNode Queue(string name) =>
        new(
            EntityKind.Queue,
            name,
            TopicName: null,
            new EntityRuntimeCounts(0, 0, 0, 0),
            new EntityMetadata(name, "Active", null, null, null, null, null, null, null));

    private sealed class FakeStore(WorkspacePreferences initial) : IWorkspacePreferencesStore
    {
        public List<WorkspacePreferences> Saves { get; } = [];

        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreferencesLoadResult(initial));

        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
        {
            Saves.Add(preferences);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFactory : IServiceBusClientFactory
    {
        public bool BlockConnect { get; init; }
        public TaskCompletionSource<bool> ConnectStarted { get; } = NewSignal();
        private TaskCompletionSource<bool> ConnectRelease { get; } = NewSignal();
        public int ConnectCount { get; private set; }
        public int DisposeCount { get; private set; }

        public Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient AdministrationClient => null!;
        public Azure.Messaging.ServiceBus.ServiceBusClient RuntimeClient => null!;

        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            ConnectCount++;
            ConnectStarted.TrySetResult(true);
            return BlockConnect ? WaitForReleaseAsync() : Task.CompletedTask;
        }

        public void ReleaseConnect() => ConnectRelease.TrySetResult(true);

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        private async Task WaitForReleaseAsync() => await ConnectRelease.Task;
    }

    private sealed class FakeBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        private int peekCount;
        private TaskCompletionSource<IReadOnlyList<ExplorerMessage>> InitialReadRelease { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockSecondPeek { get; init; }
        public TaskCompletionSource<bool> InitialReadStarted { get; } = NewSignal();

        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address,
            MessageBucket bucket,
            int take,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            if (BlockSecondPeek && Interlocked.Increment(ref peekCount) == 2)
            {
                InitialReadStarted.TrySetResult(true);
                return WaitForInitialReadAsync();
            }

            return Task.FromResult<IReadOnlyList<ExplorerMessage>>([]);
        }

        public void ReleaseInitialRead() => InitialReadRelease.TrySetResult([]);

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        private async Task<IReadOnlyList<ExplorerMessage>> WaitForInitialReadAsync() =>
            await InitialReadRelease.Task;
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
