using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceBusEmulatorExplorer.App.Services;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private readonly IConnectionProfileStore _profileStore;
    private readonly IServiceBusClientFactory _clientFactory;
    private readonly IClock _clock;
    private string _profileName = ConnectionProfileDefaults.LocalEmulator.Name;
    private string _runtimeConnectionString = ConnectionProfileDefaults.LocalEmulator.RuntimeConnectionString;
    private string _administrationConnectionString = ConnectionProfileDefaults.LocalEmulator.AdministrationConnectionString;
    private string _connectionStatus = "Disconnected";
    private bool _isConnected;
    private bool _isBusy;

    public ShellViewModel(
        IConnectionProfileStore profileStore,
        IServiceBusClientFactory clientFactory,
        IClock clock)
    {
        _profileStore = profileStore;
        _clientFactory = clientFactory;
        _clock = clock;
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, CanDisconnect);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
        AddLog("Shell ready. Direct SDK mode.");
    }

    public string ProfileName
    {
        get => _profileName;
        set => SetProperty(ref _profileName, value);
    }

    public string RuntimeConnectionString
    {
        get => _runtimeConnectionString;
        set => SetProperty(ref _runtimeConnectionString, value);
    }

    public string AdministrationConnectionString
    {
        get => _administrationConnectionString;
        set => SetProperty(ref _administrationConnectionString, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public ObservableCollection<OperationLogEntry> OperationLog { get; } = [];

    public IAsyncRelayCommand ConnectCommand { get; }

    public IAsyncRelayCommand DisconnectCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public async Task LoadProfilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<ConnectionProfile> profiles = await _profileStore.LoadAsync(cancellationToken);
            ApplyProfile(profiles.FirstOrDefault() ?? ConnectionProfileDefaults.LocalEmulator);
        }
        catch (Exception ex)
        {
            ApplyProfile(ConnectionProfileDefaults.LocalEmulator);
            AddLog($"Could not load saved connection profiles. Using local emulator defaults. {ex.Message}");
        }
    }

    private void ApplyProfile(ConnectionProfile profile)
    {
        ProfileName = profile.Name;
        RuntimeConnectionString = profile.RuntimeConnectionString;
        AdministrationConnectionString = profile.AdministrationConnectionString;
    }

    private async Task ConnectAsync()
    {
        await RunShellOperationAsync("Connect failed", async cancellationToken =>
        {
            ConnectionProfile profile = CreateCurrentProfile();
            ValidationResult validation = ConnectionProfileValidator.Validate(profile);
            if (!validation.IsValid)
            {
                ConnectionStatus = "Disconnected";
                AddLog(string.Join(" ", validation.Errors));
                return;
            }

            await _profileStore.SaveAsync([profile], cancellationToken);
            await _clientFactory.ConnectAsync(profile, cancellationToken);
            IsConnected = true;
            ConnectionStatus = $"Connected to {profile.Name}";
            AddLog($"Connected to {profile.Name}.");
        });
    }

    private ConnectionProfile CreateCurrentProfile()
    {
        return new ConnectionProfile(
            ProfileName,
            RuntimeConnectionString,
            AdministrationConnectionString);
    }

    private async Task DisconnectAsync()
    {
        await RunShellOperationAsync("Disconnect failed", async _ =>
        {
            await _clientFactory.DisposeAsync();
            IsConnected = false;
            ConnectionStatus = "Disconnected";
            AddLog("Disconnected.");
        });
    }

    private async Task RefreshAsync()
    {
        await RunShellOperationAsync("Refresh failed", _ =>
        {
            AddLog("Refresh requested. Entity loading starts in Stage 2.");
            return Task.CompletedTask;
        });
    }

    private async Task RunShellOperationAsync(
        string failurePrefix,
        Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await operation(timeout.Token);
        }
        catch (Exception ex)
        {
            ConnectionStatus = IsConnected ? ConnectionStatus : "Disconnected";
            AddLog($"{failurePrefix}: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnect()
    {
        return !IsBusy;
    }

    private bool CanDisconnect()
    {
        return IsConnected && !IsBusy;
    }

    private bool CanRefresh()
    {
        return IsConnected && !IsBusy;
    }

    private void NotifyCommandStateChanged()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private void AddLog(string message)
    {
        OperationLog.Insert(0, new OperationLogEntry(_clock.UtcNow, message));
    }
}
