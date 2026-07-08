using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Services;
using ServiceBusEmulatorExplorer.App.ViewModels;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests.ViewModels;

public sealed class ShellViewModelTests
{
    [Fact]
    public async Task LoadProfilesAsync_applies_first_saved_profile()
    {
        var profile = new ConnectionProfile(
            "Saved",
            "Endpoint=sb://runtime;UseDevelopmentEmulator=true;",
            "Endpoint=sb://admin;UseDevelopmentEmulator=true;");
        var viewModel = CreateViewModel(store: new FakeProfileStore([profile]));

        await viewModel.LoadProfilesAsync(CancellationToken.None);

        Assert.Equal("Saved", viewModel.ProfileName);
        Assert.Equal(profile.RuntimeConnectionString, viewModel.RuntimeConnectionString);
        Assert.Equal(profile.AdministrationConnectionString, viewModel.AdministrationConnectionString);
    }

    [Fact]
    public async Task ConnectCommand_saves_profile_connects_factory_and_enables_disconnect_refresh()
    {
        var store = new FakeProfileStore([]);
        var factory = new FakeClientFactory();
        var entityBrowser = new FakeEntityBrowser([]);
        var viewModel = CreateViewModel(store, factory, entityBrowser);

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsConnected);
        Assert.Equal("Connected to Local emulator", viewModel.ConnectionStatus);
        Assert.True(factory.ConnectCalled);
        Assert.True(entityBrowser.GetEntityTreeCalled);
        Assert.Single(store.SavedProfiles);
        Assert.True(viewModel.DisconnectCommand.CanExecute(null));
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task LoadProfilesAsync_falls_back_to_default_profile_when_store_fails()
    {
        var viewModel = CreateViewModel(store: new ThrowingProfileStore());

        await viewModel.LoadProfilesAsync(CancellationToken.None);

        Assert.Equal(ConnectionProfileDefaults.LocalEmulator.Name, viewModel.ProfileName);
        Assert.Contains("Could not load saved connection profiles.", viewModel.OperationLog[0].Message);
    }

    [Fact]
    public async Task ConnectCommand_logs_validation_errors_without_connecting()
    {
        var factory = new FakeClientFactory();
        var viewModel = CreateViewModel(clientFactory: factory);
        viewModel.RuntimeConnectionString = "";

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsConnected);
        Assert.False(factory.ConnectCalled);
        Assert.Contains("Runtime connection string is required.", viewModel.OperationLog[0].Message);
    }

    [Fact]
    public void OperationLogEntry_formats_timestamp_as_utc_zulu()
    {
        var timestamp = new DateTimeOffset(2026, 7, 8, 12, 34, 56, 789, TimeSpan.Zero);
        var entry = new OperationLogEntry(timestamp, "Connected.");

        Assert.Equal("2026-07-08T12:34:56.789Z Connected.", entry.DisplayText);
    }

    [Fact]
    public async Task RefreshCommand_groups_entities_and_selection_updates_detail_header()
    {
        var queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 3, deadLetter: 1);
        var topic = CreateEntity(EntityKind.Topic, "events", topicName: null, active: 0, deadLetter: 0);
        var subscription = CreateEntity(EntityKind.Subscription, "billing", "events", active: 5, deadLetter: 2);
        var viewModel = CreateViewModel(entityBrowser: new FakeEntityBrowser([subscription, queue, topic]));

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("Loaded 3 entities.", viewModel.EntityBrowserStatus);
        Assert.Equal("Queues", viewModel.EntityTree[0].DisplayName);
        Assert.Equal("orders", viewModel.EntityTree[0].Children[0].DisplayName);
        Assert.Equal("Topics", viewModel.EntityTree[1].DisplayName);
        Assert.Equal("events", viewModel.EntityTree[1].Children[0].DisplayName);
        Assert.Equal("billing", viewModel.EntityTree[1].Children[0].Children[0].DisplayName);

        viewModel.SelectEntity(viewModel.EntityTree[1].Children[0].Children[0]);

        Assert.Equal("billing", viewModel.SelectedEntityTitle);
        Assert.Equal("Subscription", viewModel.SelectedEntityKind);
        Assert.Equal("events/subscriptions/billing", viewModel.SelectedEntityPath);
        Assert.Equal("Active 5 | DLQ 2 | Scheduled 0 | Total 7", viewModel.SelectedEntityCounts);
        Assert.Contains("Status Active", viewModel.SelectedEntityMetadata);
        Assert.Contains("Created 2026-07-08T10:00:00.000Z", viewModel.SelectedEntityMetadata);
        Assert.Contains("Max deliveries 10", viewModel.SelectedEntityMetadata);
        Assert.Contains("Delivery Max deliveries 10", viewModel.SelectedEntityMetadata);
        Assert.Contains("TTL 14.00:00:00", viewModel.SelectedEntityMetadata);
    }

    [Fact]
    public async Task NamespaceFilter_filters_loaded_entities_and_keeps_matching_subscription_parent()
    {
        var queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 3, deadLetter: 1);
        var topic = CreateEntity(EntityKind.Topic, "events", topicName: null, active: 0, deadLetter: 0);
        var subscription = CreateEntity(EntityKind.Subscription, "billing", "events", active: 5, deadLetter: 2);
        var viewModel = CreateViewModel(entityBrowser: new FakeEntityBrowser([queue, topic, subscription]));

        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.NamespaceFilter = "bill";

        Assert.Empty(viewModel.EntityTree[0].Children);
        Assert.Equal("events", viewModel.EntityTree[1].Children[0].DisplayName);
        Assert.Equal("billing", viewModel.EntityTree[1].Children[0].Children[0].DisplayName);
    }

    [Fact]
    public async Task RefreshCommand_shows_visible_error_when_administration_service_fails()
    {
        var viewModel = CreateViewModel(entityBrowser: new ThrowingEntityBrowser());

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsConnected);
        Assert.Equal("Refresh failed.", viewModel.EntityBrowserStatus);
        Assert.Equal("Administration endpoint unavailable.", viewModel.EntityBrowserError);
        Assert.Contains("Refresh failed: Administration endpoint unavailable.", viewModel.OperationLog[0].Message);
    }

    private static ShellViewModel CreateViewModel(
        IConnectionProfileStore? store = null,
        IServiceBusClientFactory? clientFactory = null,
        IServiceBusEntityBrowser? entityBrowser = null)
    {
        return new ShellViewModel(
            store ?? new FakeProfileStore([ConnectionProfileDefaults.LocalEmulator]),
            clientFactory ?? new FakeClientFactory(),
            entityBrowser ?? new FakeEntityBrowser([]),
            new FixedClock());
    }

    private static ServiceBusEntityNode CreateEntity(
        EntityKind kind,
        string name,
        string? topicName,
        long active,
        long deadLetter)
    {
        return EntityTreeBuilder.CreateNode(new EntityTreeSource(
            kind,
            name,
            topicName,
            active,
            deadLetter,
            ScheduledMessageCount: 0,
            Status: "Active",
            new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 8, 11, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(1),
            MaxDeliveryCount: 10,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            RequiresSession: false,
            RequiresDuplicateDetection: false));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeProfileStore(IReadOnlyList<ConnectionProfile> profiles) : IConnectionProfileStore
    {
        public IReadOnlyList<ConnectionProfile> SavedProfiles { get; private set; } = [];

        public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(profiles);
        }

        public Task SaveAsync(IReadOnlyList<ConnectionProfile> savedProfiles, CancellationToken cancellationToken)
        {
            SavedProfiles = savedProfiles;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingProfileStore : IConnectionProfileStore
    {
        public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Bad profile JSON.");
        }

        public Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClientFactory : IServiceBusClientFactory
    {
        public bool ConnectCalled { get; private set; }

        public ServiceBusAdministrationClient AdministrationClient =>
            throw new NotSupportedException("The shell tests do not use a live administration client.");

        public ServiceBusClient RuntimeClient =>
            throw new NotSupportedException("The shell tests do not use a live runtime client.");

        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            ConnectCalled = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeEntityBrowser(IReadOnlyList<ServiceBusEntityNode> entities) : IServiceBusEntityBrowser
    {
        public bool GetEntityTreeCalled { get; private set; }

        public Task<IReadOnlyList<ServiceBusEntityNode>> GetEntityTreeAsync(CancellationToken cancellationToken)
        {
            GetEntityTreeCalled = true;
            return Task.FromResult(entities);
        }
    }

    private sealed class ThrowingEntityBrowser : IServiceBusEntityBrowser
    {
        public Task<IReadOnlyList<ServiceBusEntityNode>> GetEntityTreeAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Administration endpoint unavailable.");
        }
    }
}
