using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Settings;

/// <summary>
/// Saves every Settings change one at a time, each applied to the latest saved preferences, and says whether anything
/// is still saving so the window can keep Done, Save profile and closing blocked until it finishes (DEC-053).
/// </summary>
public sealed class SettingsPersistence : ObservableObject
{
    private readonly Func<WorkspacePreferences, Task> saveAsync;
    private readonly SemaphoreSlim gate = new(1, 1);
    private int pending;
    private TaskCompletionSource<bool>? idle;
    private bool failedWhileWaiting;

    public SettingsPersistence(WorkspacePreferences saved, Func<WorkspacePreferences, Task> saveAsync)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(saveAsync);
        Saved = saved;
        this.saveAsync = saveAsync;
    }

    /// <summary>The preferences as last saved successfully.</summary>
    public WorkspacePreferences Saved { get; private set; }

    /// <summary>True from the moment a save is requested until it, and every save queued behind it, has finished.</summary>
    public bool IsSaving => pending > 0;

    /// <summary>
    /// Completes once nothing is saving, including saves queued after this call: true when every save that finished
    /// meanwhile succeeded.
    /// </summary>
    public Task<bool> WhenIdleAsync()
    {
        if (!IsSaving) return Task.FromResult(true);
        if (idle is null)
        {
            idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            failedWhileWaiting = false;
        }

        return idle.Task;
    }

    /// <summary>
    /// Waits for earlier saves, applies <paramref name="update"/> to the preferences they left, and keeps the result only
    /// if saving it succeeds. A failure leaves <see cref="Saved"/> unchanged and is rethrown to the caller.
    /// </summary>
    public async Task SaveAsync(Func<WorkspacePreferences, WorkspacePreferences> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        ChangePending(+1);
        try
        {
            await gate.WaitAsync();
            try
            {
                WorkspacePreferences next = update(Saved);
                await saveAsync(next);
                Saved = next;
            }
            catch
            {
                failedWhileWaiting = true;
                throw;
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            ChangePending(-1);
        }
    }

    private void ChangePending(int change)
    {
        bool wasSaving = IsSaving;
        pending += change;
        if (wasSaving == IsSaving) return;
        OnPropertyChanged(nameof(IsSaving));
        if (IsSaving || idle is null) return;
        TaskCompletionSource<bool> drained = idle;
        idle = null;
        drained.SetResult(!failedWhileWaiting);
    }
}
