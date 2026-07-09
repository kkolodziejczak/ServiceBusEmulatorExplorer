using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly IServiceBusAdministrationService _administrationService;
    private readonly IEntityManagementWorkflow _entityManagementWorkflow;
    private readonly ITopicSubscriptionRefreshWorkflow _topicSubscriptionRefreshWorkflow;
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
    private string _selectedEntityHeading = "No entity selected";
    private string _selectedEntityStatus = "";
    private string _selectedEntityCreatedAtUtc = "";
    private string _selectedEntityLockDuration = "";
    private string _selectedEntityDefaultTtl = "";
    private string _selectedEntityMaxDeliveryCount = "";
    private string _selectedEntityActiveCount = "0";
    private string _selectedEntityDeadLetterCount = "0";
    private string _selectedEntityScheduledCount = "0";
    private string _selectedEntityTotalCount = "0";
    private string _selectedEntityDeleteLabel = "Delete Entity";
    private bool _isConnected;
    private bool _isBusy;
    private bool _isRefreshing;
    private IReadOnlyList<ServiceBusEntityNode> _loadedEntities = [];
    private ServiceBusEntityNode? _selectedEntity;
    private CancellationTokenSource? _refreshCancellation;

    public ShellViewModel(
        IConnectionProfileStore profileStore,
        IServiceBusClientFactory clientFactory,
        IServiceBusAdministrationService administrationService,
        IServiceBusMessageService messageService,
        IDeadLetterReplayService deadLetterReplayService,
        IEntityManagementWorkflow entityManagementWorkflow,
        ITopicSubscriptionRefreshWorkflow topicSubscriptionRefreshWorkflow,
        IMessageDialogService messageDialogService,
        IClock clock)
    {
        _profileStore = profileStore;
        _clientFactory = clientFactory;
        _administrationService = administrationService;
        _entityManagementWorkflow = entityManagementWorkflow;
        _topicSubscriptionRefreshWorkflow = topicSubscriptionRefreshWorkflow;
        _clock = clock;
        MessageInspection = new MessageInspectionViewModel(messageService, deadLetterReplayService, messageDialogService, AddLog);
        MessageInspection.PropertyChanged += MessageInspection_PropertyChanged;
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, CanDisconnect);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
        CancelRefreshCommand = new RelayCommand(CancelRefresh, CanCancelRefresh);
        RefreshSelectedTopicSubscriptionsCommand = new AsyncRelayCommand(RefreshSelectedTopicSubscriptionsAsync, CanRefreshSelectedTopicSubscriptions);
        CreateQueueCommand = new AsyncRelayCommand(CreateQueueAsync, CanManageEntities);
        CreateTopicCommand = new AsyncRelayCommand(CreateTopicAsync, CanManageEntities);
        CreateSubscriptionCommand = new AsyncRelayCommand(CreateSubscriptionAsync, CanManageEntities);
        UpdateSelectedEntityCommand = new AsyncRelayCommand(UpdateSelectedEntityAsync, CanUpdateOrDeleteSelectedEntity);
        DeleteSelectedEntityCommand = new AsyncRelayCommand(DeleteSelectedEntityAsync, CanUpdateOrDeleteSelectedEntity);
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

    public string SelectedEntityHeading
    {
        get => _selectedEntityHeading;
        private set => SetProperty(ref _selectedEntityHeading, value);
    }

    public string SelectedEntityStatus
    {
        get => _selectedEntityStatus;
        private set => SetProperty(ref _selectedEntityStatus, value);
    }

    public string SelectedEntityCreatedAtUtc
    {
        get => _selectedEntityCreatedAtUtc;
        private set => SetProperty(ref _selectedEntityCreatedAtUtc, value);
    }

    public string SelectedEntityLockDuration
    {
        get => _selectedEntityLockDuration;
        private set => SetProperty(ref _selectedEntityLockDuration, value);
    }

    public string SelectedEntityDefaultTtl
    {
        get => _selectedEntityDefaultTtl;
        private set => SetProperty(ref _selectedEntityDefaultTtl, value);
    }

    public string SelectedEntityMaxDeliveryCount
    {
        get => _selectedEntityMaxDeliveryCount;
        private set => SetProperty(ref _selectedEntityMaxDeliveryCount, value);
    }

    public string SelectedEntityActiveCount
    {
        get => _selectedEntityActiveCount;
        private set => SetProperty(ref _selectedEntityActiveCount, value);
    }

    public string SelectedEntityDeadLetterCount
    {
        get => _selectedEntityDeadLetterCount;
        private set => SetProperty(ref _selectedEntityDeadLetterCount, value);
    }

    public string SelectedEntityScheduledCount
    {
        get => _selectedEntityScheduledCount;
        private set => SetProperty(ref _selectedEntityScheduledCount, value);
    }

    public string SelectedEntityTotalCount
    {
        get => _selectedEntityTotalCount;
        private set => SetProperty(ref _selectedEntityTotalCount, value);
    }

    public string SelectedEntityDeleteLabel
    {
        get => _selectedEntityDeleteLabel;
        private set => SetProperty(ref _selectedEntityDeleteLabel, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                MessageInspection.IsConnected = value;
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
                MessageInspection.IsShellBusy = value;
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

    public MessageInspectionViewModel MessageInspection { get; }

    public string ConnectControlContent => IsConnected ? "✓ Connected" : "Connect";

    public string ConnectControlAutomationName => IsConnected ? "Connected" : "Connect";

    public string ConnectCommandToolTip => IsConnected
        ? "Connected to the selected emulator profile. Use Disconnect to end this session."
        : CreateToolTip("Connect to the selected emulator profile.", GetShellBusyReason());

    public string DisconnectCommandToolTip => CreateToolTip(
        "Disconnect from the current emulator profile.",
        !IsConnected ? "Connect to an emulator first." : GetShellBusyReason());

    public string RefreshCommandToolTip => CreateToolTip(
        "Refresh the namespace tree, selected metadata, counts, and visible message page.",
        !IsConnected ? "Connect to an emulator first." : GetShellBusyReason());

    public bool CanShowCancelRefreshCommand => IsRefreshing;

    public string CancelRefreshCommandToolTip => "Cancel the active refresh operation.";

    public string CreateQueueCommandToolTip => CreateToolTip(
        "Create a new queue.",
        !IsConnected ? "Connect to an emulator first." : GetShellBusyReason());

    public string CreateTopicCommandToolTip => CreateToolTip(
        "Create a new topic.",
        !IsConnected ? "Connect to an emulator first." : GetShellBusyReason());

    public string CreateSubscriptionCommandToolTip => CreateToolTip(
        "Create a new subscription.",
        !IsConnected ? "Connect to an emulator first." : GetShellBusyReason());

    public bool CanShowRefreshSubscriptionsCommand => _selectedEntity is { Kind: EntityKind.Topic };

    public string RefreshSelectedTopicSubscriptionsCommandToolTip => CreateRefreshSelectedTopicSubscriptionsToolTip();

    public bool CanShowSelectedEntityCommands => _selectedEntity is not null;

    public string UpdateSelectedEntityCommandToolTip => CreateToolTip(
        "Update metadata for the selected queue, topic, or subscription.",
        CreateSelectedEntityUnavailableReason());

    public string DeleteSelectedEntityCommandToolTip => CreateToolTip(
        "Delete the selected queue, topic, or subscription after explicit confirmation.",
        CreateSelectedEntityUnavailableReason());

    public IAsyncRelayCommand ConnectCommand { get; }

    public IAsyncRelayCommand DisconnectCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand CancelRefreshCommand { get; }

    public IAsyncRelayCommand RefreshSelectedTopicSubscriptionsCommand { get; }

    public IAsyncRelayCommand CreateQueueCommand { get; }

    public IAsyncRelayCommand CreateTopicCommand { get; }

    public IAsyncRelayCommand CreateSubscriptionCommand { get; }

    public IAsyncRelayCommand UpdateSelectedEntityCommand { get; }

    public IAsyncRelayCommand DeleteSelectedEntityCommand { get; }

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
        bool connected = await RunShellOperationAsync("Connect failed", async cancellationToken =>
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

        if (connected && IsConnected)
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

    private async Task<bool> RunShellOperationAsync(
        string failurePrefix,
        Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return false;
        }

        try
        {
            IsBusy = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await operation(timeout.Token);
            return true;
        }
        catch (Exception ex)
        {
            ConnectionStatus = IsConnected ? ConnectionStatus : "Disconnected";
            AddLog($"{failurePrefix}: {ex.Message}");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
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

            IReadOnlyList<ServiceBusEntityNode> entities = await _administrationService.GetEntityTreeAsync(timeout.Token);
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

    private async Task RefreshSelectedTopicSubscriptionsAsync()
    {
        ServiceBusEntityNode? selectedTopic = _selectedEntity;
        if (selectedTopic is not { Kind: EntityKind.Topic } || !IsConnected || IsRefreshing)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _refreshCancellation = timeout;

        try
        {
            IsBusy = true;
            IsRefreshing = true;
            EntityBrowserError = null;
            EntityBrowserStatus = $"Refreshing subscriptions for {selectedTopic.Name}...";

            TopicSubscriptionRefreshResult result = await _topicSubscriptionRefreshWorkflow.RefreshAsync(selectedTopic.Name, timeout.Token);
            _loadedEntities = result.Entities;
            ApplyEntityTree(selectedTopic);

            foreach (RefreshedSubscriptionMessages refreshedSubscription in result.RefreshedSubscriptions)
            {
                MessageInspection.CacheMessages(
                    refreshedSubscription.Subscription,
                    refreshedSubscription.ActiveMessages,
                    refreshedSubscription.DeadLetterMessages);
                AddLog($"Refreshed {refreshedSubscription.Subscription.Metadata.Path}: cached {refreshedSubscription.ActiveMessages.Count} active and {refreshedSubscription.DeadLetterMessages.Count} DLQ message(s).");
            }

            foreach (TopicSubscriptionRefreshFailure failure in result.Failures)
            {
                AddLog($"Refresh {failure.Subscription.Metadata.Path} failed: {failure.ErrorMessage}");
            }

            EntityBrowserStatus = result.Failures.Count == 0
                ? $"Refreshed {result.RefreshedSubscriptions.Count} subscription(s) for {selectedTopic.Name}."
                : $"Refreshed {result.RefreshedSubscriptions.Count} subscription(s) for {selectedTopic.Name}; {result.Failures.Count} failed.";
            AddLog(EntityBrowserStatus);
        }
        catch (OperationCanceledException)
        {
            EntityBrowserStatus = "Subscription refresh canceled.";
            EntityBrowserError = "Topic subscription refresh was canceled before it completed.";
            AddLog("Subscription refresh canceled.");
        }
        catch (Exception ex)
        {
            EntityBrowserStatus = "Subscription refresh failed.";
            EntityBrowserError = ex.Message;
            AddLog($"Subscription refresh failed: {ex.Message}");
        }
        finally
        {
            _refreshCancellation = null;
            IsRefreshing = false;
            IsBusy = false;
        }
    }

    private void ApplyEntityTree(ServiceBusEntityNode? entityToSelect = null)
    {
        EntityTree.Clear();
        foreach (EntityTreeNodeViewModel node in EntityTreeNodeViewModel.CreateTree(_loadedEntities, NamespaceFilter))
        {
            EntityTree.Add(node);
        }

        SelectEntity(entityToSelect is null ? null : FindEntityNode(entityToSelect));
    }

    private EntityTreeNodeViewModel? FindEntityNode(ServiceBusEntityNode entity)
    {
        foreach (EntityTreeNodeViewModel root in EntityTree)
        {
            EntityTreeNodeViewModel? match = FindEntityNode(root, entity);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static EntityTreeNodeViewModel? FindEntityNode(
        EntityTreeNodeViewModel current,
        ServiceBusEntityNode entity)
    {
        if (current.Entity is not null && IsSameEntity(current.Entity, entity))
        {
            return current;
        }

        foreach (EntityTreeNodeViewModel child in current.Children)
        {
            EntityTreeNodeViewModel? match = FindEntityNode(child, entity);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static bool IsSameEntity(ServiceBusEntityNode first, ServiceBusEntityNode second)
    {
        return first.Kind == second.Kind
            && string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(first.TopicName, second.TopicName, StringComparison.OrdinalIgnoreCase);
    }

    public void SelectEntity(EntityTreeNodeViewModel? node)
    {
        ServiceBusEntityNode? entity = node?.Entity;
        _selectedEntity = entity;
        MessageInspection.SelectEntity(entity);
        if (entity is null)
        {
            SelectedEntityTitle = "No entity selected";
            SelectedEntityKind = "Select a queue, topic, or subscription from the namespace tree.";
            SelectedEntityPath = "";
            SelectedEntityCounts = "";
            SelectedEntityMetadata = "";
            SelectedEntityHeading = "No entity selected";
            SelectedEntityStatus = "";
            SelectedEntityCreatedAtUtc = "";
            SelectedEntityLockDuration = "";
            SelectedEntityDefaultTtl = "";
            SelectedEntityMaxDeliveryCount = "";
            SelectedEntityActiveCount = "0";
            SelectedEntityDeadLetterCount = "0";
            SelectedEntityScheduledCount = "0";
            SelectedEntityTotalCount = "0";
            SelectedEntityDeleteLabel = "Delete Entity";
            NotifyCommandStateChanged();
            return;
        }

        SelectedEntityTitle = entity.Name;
        SelectedEntityKind = entity.Kind.ToString();
        SelectedEntityPath = entity.Metadata.Path;
        SelectedEntityCounts = CreateCountsText(entity);
        SelectedEntityMetadata = CreateMetadataText(entity);
        SelectedEntityHeading = $"{entity.Kind}: {entity.Name}";
        SelectedEntityStatus = entity.Metadata.Status;
        SelectedEntityCreatedAtUtc = FormatUtc(entity.Metadata.CreatedAtUtc);
        SelectedEntityLockDuration = FormatNullableValue(entity.Metadata.LockDuration);
        SelectedEntityDefaultTtl = FormatNullableValue(entity.Metadata.DefaultMessageTimeToLive);
        SelectedEntityMaxDeliveryCount = FormatNullableValue(entity.Metadata.MaxDeliveryCount);
        SelectedEntityActiveCount = entity.Counts.ActiveMessageCount.ToString();
        SelectedEntityDeadLetterCount = entity.Counts.DeadLetterMessageCount.ToString();
        SelectedEntityScheduledCount = entity.Counts.ScheduledMessageCount.ToString();
        SelectedEntityTotalCount = entity.Counts.TotalMessageCount.ToString();
        SelectedEntityDeleteLabel = $"Delete {entity.Kind}";
        NotifyCommandStateChanged();
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

    private static string FormatNullableValue<T>(T? value)
    {
        return value?.ToString() ?? "n/a";
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

    private void MessageInspection_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MessageInspectionViewModel.IsBusy))
        {
            NotifyCommandStateChanged();
        }
    }

    private void CancelRefresh()
    {
        _refreshCancellation?.Cancel();
    }

    private async Task CreateQueueAsync()
    {
        await RunEntityManagementOperationAsync(
            "Create queue failed",
            _entityManagementWorkflow.CreateQueueAsync);
    }

    private async Task CreateTopicAsync()
    {
        await RunEntityManagementOperationAsync(
            "Create topic failed",
            _entityManagementWorkflow.CreateTopicAsync);
    }

    private async Task CreateSubscriptionAsync()
    {
        await RunEntityManagementOperationAsync(
            "Create subscription failed",
            _entityManagementWorkflow.CreateSubscriptionAsync);
    }

    private async Task UpdateSelectedEntityAsync()
    {
        ServiceBusEntityNode? entity = _selectedEntity;
        if (entity is null)
        {
            return;
        }

        await RunEntityManagementOperationAsync(
            "Update entity failed",
            cancellationToken => _entityManagementWorkflow.UpdateAsync(entity, cancellationToken));
    }

    private async Task DeleteSelectedEntityAsync()
    {
        ServiceBusEntityNode? entity = _selectedEntity;
        if (entity is null)
        {
            return;
        }

        await RunEntityManagementOperationAsync(
            "Delete entity failed",
            cancellationToken => _entityManagementWorkflow.DeleteAsync(entity, cancellationToken));
    }

    private async Task RunEntityManagementOperationAsync(
        string failurePrefix,
        Func<CancellationToken, Task<EntityManagementOperationResult>> operation)
    {
        if (IsBusy)
        {
            return;
        }

        EntityManagementOperationResult result = EntityManagementOperationResult.NoChange;

        try
        {
            IsBusy = true;
            result = await operation(CancellationToken.None);
            if (result.LogMessage is not null)
            {
                AddLog(result.LogMessage);
            }
        }
        catch (Exception ex)
        {
            AddLog($"{failurePrefix}: {ex.Message}");
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (result.Changed && IsConnected)
        {
            await RefreshAsync();
        }
    }

    private bool CanConnect()
    {
        return !IsConnected && !IsBusy && !MessageInspection.IsBusy;
    }

    private bool CanDisconnect()
    {
        return IsConnected && !IsBusy && !MessageInspection.IsBusy;
    }

    private bool CanRefresh()
    {
        return IsConnected && !IsBusy && !MessageInspection.IsBusy;
    }

    private bool CanCancelRefresh()
    {
        return IsRefreshing;
    }

    private bool CanRefreshSelectedTopicSubscriptions()
    {
        return IsConnected && !IsBusy && !MessageInspection.IsBusy && _selectedEntity is { Kind: EntityKind.Topic };
    }

    private bool CanManageEntities()
    {
        return IsConnected && !IsBusy && !MessageInspection.IsBusy;
    }

    private bool CanUpdateOrDeleteSelectedEntity()
    {
        return IsConnected && !IsBusy && !MessageInspection.IsBusy && _selectedEntity is not null;
    }

    private void NotifyCommandStateChanged()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        CancelRefreshCommand.NotifyCanExecuteChanged();
        RefreshSelectedTopicSubscriptionsCommand.NotifyCanExecuteChanged();
        CreateQueueCommand.NotifyCanExecuteChanged();
        CreateTopicCommand.NotifyCanExecuteChanged();
        CreateSubscriptionCommand.NotifyCanExecuteChanged();
        UpdateSelectedEntityCommand.NotifyCanExecuteChanged();
        DeleteSelectedEntityCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ConnectControlContent));
        OnPropertyChanged(nameof(ConnectControlAutomationName));
        OnPropertyChanged(nameof(ConnectCommandToolTip));
        OnPropertyChanged(nameof(DisconnectCommandToolTip));
        OnPropertyChanged(nameof(RefreshCommandToolTip));
        OnPropertyChanged(nameof(CanShowCancelRefreshCommand));
        OnPropertyChanged(nameof(CancelRefreshCommandToolTip));
        OnPropertyChanged(nameof(CreateQueueCommandToolTip));
        OnPropertyChanged(nameof(CreateTopicCommandToolTip));
        OnPropertyChanged(nameof(CreateSubscriptionCommandToolTip));
        OnPropertyChanged(nameof(CanShowRefreshSubscriptionsCommand));
        OnPropertyChanged(nameof(RefreshSelectedTopicSubscriptionsCommandToolTip));
        OnPropertyChanged(nameof(CanShowSelectedEntityCommands));
        OnPropertyChanged(nameof(UpdateSelectedEntityCommandToolTip));
        OnPropertyChanged(nameof(DeleteSelectedEntityCommandToolTip));
    }

    private void AddLog(string message)
    {
        OperationLog.Insert(0, new OperationLogEntry(_clock.UtcNow, message));
    }

    private string CreateRefreshSelectedTopicSubscriptionsToolTip()
    {
        const string purpose = "Refresh child subscription counts and cache first active/DLQ pages without showing a combined topic grid.";
        if (_selectedEntity is not { Kind: EntityKind.Topic })
        {
            return CreateToolTip(purpose, "Select a topic first.");
        }

        return CreateToolTip(purpose, !IsConnected ? "Connect to an emulator first." : GetShellBusyReason());
    }

    private string? CreateSelectedEntityUnavailableReason()
    {
        if (_selectedEntity is null)
        {
            return "Select a queue, topic, or subscription first.";
        }

        if (!IsConnected)
        {
            return "Connect to an emulator first.";
        }

        return GetShellBusyReason();
    }

    private string? GetShellBusyReason()
    {
        if (IsBusy)
        {
            return "Wait for the current shell operation to finish.";
        }

        return MessageInspection.IsBusy ? "Wait for the current message operation to finish." : null;
    }

    private static string CreateToolTip(string purpose, string? unavailableReason)
    {
        return unavailableReason is null ? purpose : $"{purpose} Unavailable: {unavailableReason}";
    }
}
