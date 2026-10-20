namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// The message list's toolbar: the Active and dead-letter tabs, checking every message, loading more or refreshing,
/// and automatic refresh (its interval, pausing it, and what each tick refreshes).
/// </summary>
public sealed class MessageListToolbarViewModel
{
    private static readonly int[] IntervalSeconds = [0, 5, 10, 30];
    private readonly InvestigationWorkspace workspace;

    public MessageListToolbarViewModel(InvestigationWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
    }

    /// <summary>
    /// The check-all box: checked when every message is checked, unchecked when none is, and <c>null</c> in between.
    /// </summary>
    public bool? AllChecked
    {
        get
        {
            var surface = workspace.Surface;
            return surface.SelectedCount == 0 ? false : surface.SelectedCount == surface.Messages.Count ? true : null;
        }
    }

    public bool CanCheckAll => workspace.Surface.Messages.Count > 0;

    /// <summary>Checks every message unless all are checked; then unchecks them all.</summary>
    public void ToggleAll() => workspace.Surface.SetAllChecked(workspace.Surface.SelectedCount < workspace.Surface.Messages.Count);

    /// <summary>
    /// Browses the selected entity's Active or dead-letter messages. Returns <c>false</c> while searching or without a
    /// browsed entity; the tabs should then show the current bucket again.
    /// </summary>
    public async Task<bool> SelectBucketAsync(bool deadLetter)
    {
        if (workspace.Search.IsActive || workspace.Browse.SelectedEntity is not { } node) return false;
        await workspace.RunReadAsync(() => workspace.Browse.SelectAsync(node, deadLetter));
        return true;
    }

    /// <summary>Loads more browsed messages, or continues the search.</summary>
    public Task LoadMoreAsync() => workspace.RunReadAsync(workspace.Search.IsActive ? workspace.Search.ContinueAsync : workspace.Browse.LoadMoreAsync);

    /// <summary>Reads the browsed messages again, or runs the search again.</summary>
    public Task RefreshAsync() => workspace.RunReadAsync(workspace.Search.IsActive ? workspace.Search.RefreshAsync : workspace.Browse.RefreshAsync);

    /// <summary>The position of the saved interval among the choices Off, 5, 10 and 30 seconds, or -1.</summary>
    public int IntervalIndex => Array.IndexOf(IntervalSeconds, workspace.Preferences.AutoRefreshSeconds);

    public Task SetIntervalAsync(int index) => workspace.UpdateDisplayPreferencesAsync(autoRefreshSeconds: IntervalSeconds[index]);

    public bool IsPaused { get; private set; }

    /// <summary>The pause button's accessible name.</summary>
    public string PauseLabel => IsPaused ? "Resume automatic refresh" : "Pause automatic refresh";

    public void TogglePause() => IsPaused = !IsPaused;

    /// <summary>
    /// How often to refresh automatically, or <c>null</c> when it should stop: paused, off, disconnected, or while a
    /// search in the investigation has no watched entities to keep up to date.
    /// </summary>
    /// <param name="libraryVisible">The Message Library is shown instead of the investigation.</param>
    public TimeSpan? RefreshInterval(bool libraryVisible)
    {
        bool investigationSearch = !libraryVisible && workspace.Search.IsActive;
        if (IsPaused || (investigationSearch && !workspace.Browse.HasWatchedTargets) || !workspace.IsConnected
            || workspace.Preferences.AutoRefreshSeconds == 0) return null;
        return TimeSpan.FromSeconds(workspace.Preferences.AutoRefreshSeconds);
    }

    /// <summary>Stops a background refresh that is running; used when automatic refresh stops.</summary>
    public void CancelBackgroundRefresh() => workspace.Browse.CancelBackgroundRefresh();

    /// <summary>
    /// One automatic refresh: watched entities in the background (and the browsed one when it is on screen), otherwise
    /// the browsed messages unless the investigation shows a search or a read is already running.
    /// </summary>
    public async Task TickAsync(bool libraryVisible)
    {
        if (workspace.Browse.HasWatchedTargets)
        {
            bool refreshSelected = !libraryVisible && !workspace.Search.IsActive;
            await workspace.RunReadAsync(() => workspace.Browse.RefreshBackgroundAsync(refreshSelected));
            return;
        }
        if (workspace.Browse.IsBusy) return;
        if (libraryVisible || !workspace.Search.IsActive)
            await workspace.RunReadAsync(workspace.Browse.RefreshAsync);
    }
}
