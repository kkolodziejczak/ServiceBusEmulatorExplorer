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
        var viewModel = CreateViewModel(store, factory);

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsConnected);
        Assert.Equal("Connected to Local emulator", viewModel.ConnectionStatus);
        Assert.True(factory.ConnectCalled);
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

    private static ShellViewModel CreateViewModel(
        IConnectionProfileStore? store = null,
        IServiceBusClientFactory? clientFactory = null)
    {
        return new ShellViewModel(
            store ?? new FakeProfileStore([ConnectionProfileDefaults.LocalEmulator]),
            clientFactory ?? new FakeClientFactory(),
            new FixedClock());
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
}
