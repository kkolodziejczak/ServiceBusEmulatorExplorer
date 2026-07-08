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
    private readonly IServiceBusEntityBrowser _entityBrowser;
    private readonly IClock _clock;
    private string _profileName = ConnectionProfileDefaults.LocalEmulator.Name;
    private string _runtimeConnectionString = ConnectionProfileDefaults.LocalEmulator.RuntimeConnectionString;
    private string _administrationConnectionString = ConnectionProfileDefaults.LocalEmulator.AdministrationConnectionString;
    private string _connectionStatus = "Disconnected";
    private string _namespaceFilter = "";
    private string _entityBrowserStatus = "Connect to load queues, topics, and subscriptions.";
    private string? _entityBrowserError;
    private string _selectedEntityTitle = "No entity selected";
    private string _selectedEntityKind = "Select a queue, topic, or subscription from the namespace tree.";
    private string _selectedEntityPath = "";
    private string _selectedEntityCounts = "";
    private string _selectedEntityMetadata = "";
    private bool _isConnected;
    private bool _isBusy;
    private bool _isRefreshing;
    private IReadOnlyList<ServiceBusEntityNode> _loadedEntities = [];
    private CancellationTokenSource? _refreshCancellation;

    public ShellViewModel(
        IConnectionProfileStore profileStore,
        IServiceBusClientFactory clientFactory,
        IServiceBusEntityBrowser entityBrowser,
        IClock clock)
    {
        _profileStore = profileStore;
        _clientFactory = clientFactory;
        _entityBrowser = entityBrowser;
        _clock = clock;
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, CanDisconnect);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
        CancelRefreshCommand = new RelayCommand(CancelRefresh, CanCancelRefresh);
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

    public string NamespaceFilter
    {
        get => _namespaceFilter;
        set
        {
            if (SetProperty(ref _namespaceFilter, value))
            {
                ApplyEntityTree();
            }
        }
    }

    public string EntityBrowserStatus
    {
        get => _entityBrowserStatus;
        private set => SetProperty(ref _entityBrowserStatus, value);
    }

    public string? EntityBrowserError
    {
        get => _entityBrowserError;
        private set => SetProperty(ref _entityBrowserError, value);
    }

    public string SelectedEntityTitle
    {
        get => _selectedEntityTitle;
        private set => SetProperty(ref _selectedEntityTitle, value);
    }

    public string SelectedEntityKind
    {
        get => _selectedEntityKind;
        private set => SetProperty(ref _selectedEntityKind, value);
    }

    public string SelectedEntityPath
    {
        get => _selectedEntityPath;
        private set => SetProperty(ref _selectedEntityPath, value);
    }

    public string SelectedEntityCounts
    {
        get => _selectedEntityCounts;
        private set => SetProperty(ref _selectedEntityCounts, value);
    }

    public string SelectedEntityMetadata
    {
        get => _selectedEntityMetadata;
        private set => SetProperty(ref _selectedEntityMetadata, value);
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

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public ObservableCollection<EntityTreeNodeViewModel> EntityTree { get; } = [];

    public ObservableCollection<OperationLogEntry> OperationLog { get; } = [];

    public IAsyncRelayCommand ConnectCommand { get; }

    public IAsyncRelayCommand DisconnectCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand CancelRefreshCommand { get; }

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

        if (IsConnected)
        {
            await RefreshAsync();
        }
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
            CancelRefresh();
            await _clientFactory.DisposeAsync();
            IsConnected = false;
            ConnectionStatus = "Disconnected";
            EntityBrowserStatus = "Connect to load queues, topics, and subscriptions.";
            EntityBrowserError = null;
            _loadedEntities = [];
            EntityTree.Clear();
            SelectEntity(null);
            AddLog("Disconnected.");
        });
    }

    private async Task RefreshAsync()
    {
        if (!IsConnected || IsRefreshing)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _refreshCancellation = timeout;

        try
        {
            IsBusy = true;
            IsRefreshing = true;
            EntityBrowserError = null;
            EntityBrowserStatus = "Loading namespace entities...";

            IReadOnlyList<ServiceBusEntityNode> entities = await _entityBrowser.GetEntityTreeAsync(timeout.Token);
            _loadedEntities = entities;
            ApplyEntityTree();
            EntityBrowserStatus = $"Loaded {entities.Count} entities.";
            AddLog($"Loaded {entities.Count} namespace entities.");
        }
        catch (OperationCanceledException)
        {
            EntityBrowserStatus = "Refresh canceled.";
            EntityBrowserError = "Namespace refresh was canceled before it completed.";
            AddLog("Refresh canceled.");
        }
        catch (Exception ex)
        {
            EntityBrowserStatus = "Refresh failed.";
            EntityBrowserError = ex.Message;
            AddLog($"Refresh failed: {ex.Message}");
        }
        finally
        {
            _refreshCancellation = null;
            IsRefreshing = false;
            IsBusy = false;
        }
    }

    private void ApplyEntityTree()
    {
        EntityTree.Clear();
        foreach (EntityTreeNodeViewModel node in EntityTreeNodeViewModel.CreateTree(_loadedEntities, NamespaceFilter))
        {
            EntityTree.Add(node);
        }

        SelectEntity(null);
    }

    public void SelectEntity(EntityTreeNodeViewModel? node)
    {
        ServiceBusEntityNode? entity = node?.Entity;
        if (entity is null)
        {
            SelectedEntityTitle = "No entity selected";
            SelectedEntityKind = "Select a queue, topic, or subscription from the namespace tree.";
            SelectedEntityPath = "";
            SelectedEntityCounts = "";
            SelectedEntityMetadata = "";
            return;
        }

        SelectedEntityTitle = entity.Name;
        SelectedEntityKind = entity.Kind.ToString();
        SelectedEntityPath = entity.Metadata.Path;
        SelectedEntityCounts = CreateCountsText(entity);
        SelectedEntityMetadata = CreateMetadataText(entity);
    }

    private static string CreateCountsText(ServiceBusEntityNode entity)
    {
        return $"Active {entity.Counts.ActiveMessageCount} | DLQ {entity.Counts.DeadLetterMessageCount} | Scheduled {entity.Counts.ScheduledMessageCount} | Total {entity.Counts.TotalMessageCount}";
    }

    private static string CreateMetadataText(ServiceBusEntityNode entity)
    {
        List<string> parts =
        [
            $"Status {entity.Metadata.Status}",
            $"Created {FormatUtc(entity.Metadata.CreatedAtUtc)}",
            $"Updated {FormatUtc(entity.Metadata.UpdatedAtUtc)}",
            $"Delivery {CreateDeliverySettingsText(entity.Metadata)}"
        ];

        if (entity.Metadata.LockDuration is not null)
        {
            parts.Add($"Lock {entity.Metadata.LockDuration}");
        }

        if (entity.Metadata.MaxDeliveryCount is not null)
        {
            parts.Add($"Max deliveries {entity.Metadata.MaxDeliveryCount}");
        }

        return string.Join(" | ", parts);
    }

    private static string FormatUtc(DateTimeOffset? value)
    {
        return value is null
            ? "unknown"
            : value.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'");
    }

    private static string CreateDeliverySettingsText(EntityMetadata metadata)
    {
        List<string> parts = [];

        AddSetting(parts, "Max deliveries", metadata.MaxDeliveryCount);
        AddSetting(parts, "TTL", metadata.DefaultMessageTimeToLive);
        AddSetting(parts, "Sessions", metadata.RequiresSession);
        AddSetting(parts, "Duplicate detection", metadata.RequiresDuplicateDetection);

        return parts.Count == 0 ? "none" : string.Join("; ", parts);
    }

    private static void AddSetting<T>(List<string> parts, string name, T? value)
    {
        if (value is not null)
        {
            parts.Add($"{name} {value}");
        }
    }

    private void CancelRefresh()
    {
        _refreshCancellation?.Cancel();
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

    private bool CanCancelRefresh()
    {
        return IsRefreshing;
    }

    private void NotifyCommandStateChanged()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        CancelRefreshCommand.NotifyCanExecuteChanged();
    }

    private void AddLog(string message)
    {
        OperationLog.Insert(0, new OperationLogEntry(_clock.UtcNow, message));
    }
}
