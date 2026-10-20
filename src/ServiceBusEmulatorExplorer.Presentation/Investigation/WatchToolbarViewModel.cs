using CommunityToolkit.Mvvm.ComponentModel;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// The Watch toolbar: whether the selected entity is watched, the watched-entity count, the Watch popup for one entity,
/// and saving watch rules one change at a time.
/// </summary>
public sealed class WatchToolbarViewModel : ObservableObject
{
    private readonly InvestigationWorkspace workspace;
    private EntityNode? popupEntity;
    private bool isSaving;

    public WatchToolbarViewModel(InvestigationWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
    }

    /// <summary>
    /// <c>true</c> while watch rules are being saved; the popup cannot change them until it ends. Announced at the start
    /// and end of every save, also when saves overlap.
    /// </summary>
    public bool IsSaving
    {
        get => isSaving;
        private set
        {
            isSaving = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The entity the Watch popup edits; <c>null</c> when no queue, topic or subscription is selected.</summary>
    public EntityNode? PopupEntity => popupEntity;

    public string PopupTitle => popupEntity is null ? "Select a queue, topic or subscription to watch" : $"Watch {popupEntity.Name}";

    /// <summary>The saved watch rules of the selected profile, over the entities the workspace knows.</summary>
    public WatchRuleEditor CreateRules() => new(workspace.Browse.Roots,
        workspace.Preferences.Watches.TryGetValue(workspace.SelectedProfile.Id, out var rules) ? rules : []);

    /// <summary>What the toolbar and the open popup show for the given rules.</summary>
    public WatchToolbarState StateFor(WatchRuleEditor rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        bool watching = !workspace.Search.IsActive && workspace.Browse.SelectedEntity is { } selected
            && (rules.IsWatched(selected, false) || rules.IsWatched(selected, true));
        int count = rules.Targets.Select(target => target.Address).Distinct().Count();
        bool active = popupEntity is not null && rules.IsWatched(popupEntity, false);
        bool deadLetter = popupEntity is not null && rules.IsWatched(popupEntity, true);
        return new(watching, workspace.IsConnected && !IsSaving, !IsSaving, count, workspace.IsConnected && !IsSaving,
            active, deadLetter, popupEntity is not null && !IsSaving, !IsSaving && (active || deadLetter));
    }

    /// <summary>Points the Watch popup at the selected entity, or at nothing while searching or when a group is selected.</summary>
    public void OpenPopup()
    {
        popupEntity = workspace.Search.IsActive || workspace.Browse.SelectedEntity?.IsGroup != false
            ? null : workspace.Browse.SelectedEntity;
        OnPropertyChanged(nameof(PopupEntity));
        OnPropertyChanged(nameof(PopupTitle));
    }

    public void ClosePopup()
    {
        popupEntity = null;
        OnPropertyChanged(nameof(PopupEntity));
    }

    /// <summary>Turns watching of the popup entity's Active or dead-letter messages on or off.</summary>
    public async Task SetPopupScopeAsync(bool deadLetter, bool enabled)
    {
        if (IsSaving || popupEntity is null || !workspace.IsConnected) return;
        var rules = CreateRules();
        rules.SetScope(popupEntity, deadLetter, enabled);
        await SaveAsync(workspace.SelectedProfile.Id, rules);
    }

    /// <summary>
    /// Stops watching the popup entity. Returns <c>true</c> when the change was attempted, also when its save failed,
    /// and <c>false</c> when there is no popup entity or a save is still running.
    /// </summary>
    public async Task<bool> StopWatchingAsync()
    {
        if (IsSaving || popupEntity is null) return false;
        var rules = CreateRules();
        rules.SetScope(popupEntity, false, false);
        rules.SetScope(popupEntity, true, false);
        await SaveAsync(workspace.SelectedProfile.Id, rules);
        return true;
    }

    /// <summary>Turns watching of one entity and bucket on or off, as the watched-entities overview does.</summary>
    public Task SetScopeAsync(EntityNode node, bool deadLetter, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rules = CreateRules();
        rules.SetScope(node, deadLetter, enabled);
        return SaveAsync(workspace.SelectedProfile.Id, rules);
    }

    /// <summary>Saves edited rules for a profile; returns <c>false</c> when they were not saved.</summary>
    public async Task<bool> SaveAsync(string profileId, WatchRuleEditor rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        IsSaving = true;
        try
        {
            await workspace.UpdateWatchRulesAsync(profileId, rules.Rules);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception)
        {
            workspace.Log("Watch preferences could not be saved. Previous rules remain active.", true);
            return false;
        }
        finally { IsSaving = false; }
    }
}

/// <summary>
/// The Watch toolbar for one set of rules: whether the selected entity is watched, which buttons can be used, how many
/// entities are watched, and the popup entity's choices.
/// </summary>
public sealed record WatchToolbarState(bool IsWatching, bool CanWatch, bool CanOpenGlobalWatch, int WatchedCount,
    bool CanOpenOverview, bool PopupWatchesActive, bool PopupWatchesDeadLetter, bool CanChoose, bool CanStopWatching);
