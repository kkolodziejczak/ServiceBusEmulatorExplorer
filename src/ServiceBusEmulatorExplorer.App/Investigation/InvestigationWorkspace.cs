using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.App.Services;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed class InvestigationWorkspace : ObservableObject, IAsyncDisposable
{
    private readonly IWorkspacePreferencesStore store;
    private readonly BrokerConnectionWorkflow connections;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly object disposeSync = new();
    private BrokerSession? session;
    private CancellationTokenSource? connectCancellation;
    private long generation;
    private int disposeStarted;
    private Task? disposeTask;
    private bool connecting;
    private bool initialized;
    private string? readinessWarning;
    private WorkspacePreferences preferences = new();
    public MessageBrowseWorkflow Browse { get; } = new();
    public MessageSearchWorkflow Search { get; } = new();
    public InvestigationSurface Surface { get; }
    public DeliveryInspector Inspector { get; } = new();
    public ObservableCollection<ActivityEntry> Activity { get; } = [];
    public Func<InvestigationProfile, Task<bool>> ConfirmWarning { get; set; } = _ => Task.FromResult(false);
    public Func<Task<bool>> ConfirmDiscard { get; set; } = () => Task.FromResult(false);
    public WorkspacePreferences Preferences => preferences;
    public InvestigationProfile SelectedProfile => preferences.Profiles.First(profile => profile.Id == preferences.SelectedProfileId);
    public bool IsConnected => session is not null;
    public bool IsConnecting => connecting;
    public string HealthText => connecting ? "Connecting…" : !IsConnected ? "Disconnected" : readinessWarning is null ? "Connected" : "Warning";
    public string HealthDetail => readinessWarning ?? (IsConnected ? "Administration and runtime peek access verified." : "Connect to inspect messages.");
    public string Status => Activity.LastOrDefault()?.Message ?? "Choose a connection to begin.";

    public InvestigationWorkspace(IWorkspacePreferencesStore store, BrokerConnectionWorkflow connections)
    {
        this.store = store;
        this.connections = connections;
        Surface = new(Browse, Search);
        Surface.PropertyChanged += SurfaceChanged;
    }

    private void SurfaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        Inspector.Select(Surface.FocusedMessage?.Delivery);
    }

    public async Task InitializeAsync()
    {
        if (Volatile.Read(ref disposeStarted) != 0) return;
        var result = await store.LoadAsync(CancellationToken.None);
        await lifecycleGate.WaitAsync();
        try
        {
            if (Volatile.Read(ref disposeStarted) != 0) return;
            preferences = result.Preferences;
            Browse.SetPreferences(preferences);
            Search.SetPreferences(preferences);
            initialized = true;
            NotifyConnection();
            if (result.Warning is not null) Log(result.Warning, true);
        }
        finally { lifecycleGate.Release(); }
        if (preferences.WasConnected) await ConnectAsync();
    }

    public async Task ConnectAsync()
    {
        if (Volatile.Read(ref disposeStarted) != 0 || connecting || IsConnected) return;
        var profile = SelectedProfile;
        if (!await ApproveWarningAsync(profile)) return;
        if (Volatile.Read(ref disposeStarted) != 0 || !string.Equals(profile.Id, preferences.SelectedProfileId, StringComparison.Ordinal)) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        connectCancellation = cancellation;
        var attempt = ++generation;
        connecting = true;
        NotifyConnection();
        try
        {
            var connected = await connections.ConnectAsync(profile.Connection, cancellation.Token);
            if (attempt != generation)
            {
                await connected.DisposeAsync();
                return;
            }
            session = connected;
            readinessWarning = connected.ReadinessWarning;
            Browse.SetSession(session, generation);
            Search.SetSession(session, generation);
            Log(readinessWarning ?? $"Connected to {profile.Connection.Name}.", readinessWarning is not null);
            foreach (var issue in connected.Snapshot.Issues) Log(issue, true);
            var initial = Browse.AllEntities().FirstOrDefault(node => node.Path == preferences.SelectedEntityPath)
                ?? Browse.AllEntities().FirstOrDefault(node => node.Kind != "Topic");
            if (initial is not null) await RunReadAsync(() => Browse.SelectAsync(initial, preferences.DeadLetter));
            await lifecycleGate.WaitAsync();
            try
            {
                if (Volatile.Read(ref disposeStarted) != 0 || attempt != generation || !ReferenceEquals(session, connected)) return;
                preferences = preferences with { WasConnected = true };
                await SaveCurrentAsync();
            }
            finally { lifecycleGate.Release(); }
        }
        catch (OperationCanceledException)
        {
            if (attempt == generation) Log("Connection attempt canceled.");
        }
        catch (Exception exception)
        {
            if (attempt == generation) ReportFailure(exception);
        }
        finally
        {
            if (attempt == generation)
            {
                connecting = false;
                connectCancellation = null;
                NotifyConnection();
            }
        }
    }

    private async Task<bool> ApproveWarningAsync(InvestigationProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.WarningMessage) || await ConfirmWarning(profile)) return true;
        Log("Connection canceled at the profile warning.");
        return false;
    }

    public async Task<bool> SwitchProfileAsync(InvestigationProfile profile)
    {
        if (profile.Id == SelectedProfile.Id) return true;
        if (Inspector.HasDrafts && !await ConfirmDiscard()) return false;
        if (!await ApproveWarningAsync(profile)) return false;
        await DisconnectCoreAsync();
        preferences = preferences with { SelectedProfileId = profile.Id, WasConnected = false, SelectedEntityPath = "", DeadLetter = false };
        readinessWarning = null;
        Browse.SetPreferences(preferences);
        Search.SetPreferences(preferences);
        NotifyConnection();
        await SaveCurrentAsync();
        if (preferences.AutoConnectOnSwitch) await ConnectApprovedAsync(profile);
        return true;
    }

    private async Task ConnectApprovedAsync(InvestigationProfile profile)
    {
        // The switch already obtained this profile warning's approval. Reuse it for this one attempt.
        var previous = ConfirmWarning;
        ConfirmWarning = candidate => candidate == profile ? Task.FromResult(true) : previous(candidate);
        try { await ConnectAsync(); }
        finally { ConfirmWarning = previous; }
    }

    public async Task DisconnectAsync()
    {
        if (Inspector.HasDrafts && !await ConfirmDiscard()) return;
        await DisconnectCoreAsync();
        preferences = preferences with { WasConnected = false };
        Log("Disconnected.");
        NotifyConnection();
        await SaveCurrentAsync();
    }

    private async Task DisconnectCoreAsync()
    {
        ++generation;
        connectCancellation?.Cancel();
        connectCancellation = null;
        connecting = false;
        Browse.SetSession(null, generation);
        Search.SetSession(null, generation);
        Inspector.Clear();
        var previous = session;
        session = null;
        if (previous is not null) await previous.DisposeAsync();
    }

    public async Task ApplyPreferencesAsync(WorkspacePreferences updated)
    {
        var active = updated.Profiles.First(profile => profile.Id == SelectedProfile.Id);
        bool credentialsChanged = active.Connection with { Name = SelectedProfile.Connection.Name } != SelectedProfile.Connection;
        if (credentialsChanged && (IsConnected || connecting))
        {
            if (Inspector.HasDrafts && !await ConfirmDiscard()) throw new OperationCanceledException("Save canceled.");
            await DisconnectCoreAsync();
            Log("Connection details changed. Reconnect to use the saved profile.");
        }
        var replacement = updated with { SelectedProfileId = preferences.SelectedProfileId, WasConnected = IsConnected };
        await saveGate.WaitAsync();
        try { await store.SaveAsync(replacement, CancellationToken.None); }
        finally { saveGate.Release(); }
        preferences = replacement;
        Browse.SetPreferences(preferences);
        Search.SetPreferences(preferences);
        NotifyConnection();
    }

    public async Task UpdateDisplayPreferencesAsync(bool? logExpanded = null, TimestampDisplay? timestampDisplay = null,
        int? autoRefreshSeconds = null)
    {
        preferences = preferences with
        {
            LogExpanded = logExpanded ?? preferences.LogExpanded,
            TimestampDisplay = timestampDisplay ?? preferences.TimestampDisplay,
            AutoRefreshSeconds = autoRefreshSeconds ?? preferences.AutoRefreshSeconds
        };
        Browse.SetPreferences(preferences);
        Search.SetPreferences(preferences);
        OnPropertyChanged(nameof(Preferences));
        await SaveCurrentAsync();
    }

    public async Task UpdateWindowBoundsAsync(double width, double height)
    {
        if (!double.IsFinite(width) || width < 980 || !double.IsFinite(height) || height < 640) return;
        preferences = preferences with { WindowWidth = width, WindowHeight = height };
        await SaveCurrentAsync();
    }

    public async Task RunReadAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            preferences = preferences with
            {
                SelectedEntityPath = Browse.SelectedEntity?.Path ?? "",
                DeadLetter = Browse.IsDeadLetter
            };
            await SaveCurrentAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ReportFailure(exception); }
    }

    private void ReportFailure(Exception exception)
    {
        var failure = OperationFailureFormatter.Format(exception, SelectedProfile.Connection.AuthenticationMode);
        readinessWarning = failure.UserMessage;
        Log($"{failure.UserMessage} {failure.Detail}", true);
        NotifyConnection();
    }

    public void Log(string message, bool warning = false)
    {
        Activity.Add(new(DateTimeOffset.UtcNow, message, warning));
        while (Activity.Count > 100) Activity.RemoveAt(0);
        OnPropertyChanged(nameof(Status));
    }

    private async Task SaveCurrentAsync()
    {
        // Shutdown can precede the initial load; never replace saved profiles with startup defaults.
        if (!initialized) return;
        await saveGate.WaitAsync();
        try { await store.SaveAsync(preferences, CancellationToken.None); }
        catch (Exception) { Log("Preferences could not be saved. Session changes remain available.", true); }
        finally { saveGate.Release(); }
    }

    private void NotifyConnection()
    {
        OnPropertyChanged(nameof(Preferences));
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(HealthText));
        OnPropertyChanged(nameof(HealthDetail));
    }

    public ValueTask DisposeAsync()
    {
        lock (disposeSync)
        {
            disposeTask ??= DisposeCoreAsync();
            return new ValueTask(disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Volatile.Write(ref disposeStarted, 1);
        connectCancellation?.Cancel();
        Browse.Cancel();
        Search.Stop();
        await lifecycleGate.WaitAsync();
        try
        {
            await SaveCurrentAsync();
            await DisconnectCoreAsync();
        }
        finally { lifecycleGate.Release(); }
        Surface.PropertyChanged -= SurfaceChanged;
        Surface.Dispose();
    }
}
