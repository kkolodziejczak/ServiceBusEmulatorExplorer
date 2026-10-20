namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// Closing the investigation window: whether it goes to the notification area instead, confirming that unsaved
/// message drafts may be discarded, and remembering the window size before the workspace shuts down.
/// </summary>
public sealed class WindowCloseViewModel
{
    private readonly InvestigationWorkspace workspace;

    public WindowCloseViewModel(InvestigationWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
    }

    /// <summary>
    /// The window hides in the notification area instead of closing, unless the user chose Exit, there is no tray icon,
    /// or the preference to close to the tray is off.
    /// </summary>
    public bool HidesToTray(bool exitRequested, bool trayAvailable) =>
        !exitRequested && trayAvailable && workspace.Preferences.CloseToTray;

    /// <summary>Asks before discarding unsaved message drafts; returns <c>false</c> when the window should stay open.</summary>
    public async Task<bool> ConfirmCloseAsync() => !workspace.Inspector.HasDrafts || await workspace.ConfirmDiscard();

    /// <summary>Saves the window size for the next start and shuts the workspace down.</summary>
    public async Task ShutDownAsync(double width, double height)
    {
        await workspace.UpdateWindowBoundsAsync(width, height);
        await workspace.DisposeAsync();
    }
}
