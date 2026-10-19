using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// The modeless Replay history list (Replay 32, 45 and 47). Rows are rebuilt from the workspace whenever its
/// preferences, connection or profile change, keeping the selected attempt. One action runs at a time; searching,
/// checking status and deleting an original are done by the caller.
/// </summary>
public sealed class ReplayHistoryViewModel : ObservableObject, IDisposable
{
    private const string WorkingNotice = "Local history. Observations are last checked states, not proof of API processing.";
    private const string ObservedNotice = "Last observed state; not proof of API processing.";
    private const string EmptyNotice = "No replay attempts have been saved for this connection. Replay history is local to this profile.";
    private const string FailedNotice = "Action could not complete. Check the connection, then retry the action.";
    private const string NotRemovedNotice = "History entry was not removed. Check status and retry; if saving failed, check storage access.";

    private readonly IReplayHistorySource source;
    private readonly Func<IReadOnlyList<string>, Task> find;
    private readonly Func<ReplayAttempt, Task> findOriginal;
    private readonly Func<ReplayAttempt, Task> deleteOriginal;
    private readonly Func<Task> checkStatus;
    private readonly Action<string> copyText;
    private readonly Action<Action> onUiThread;
    private IReadOnlyList<ReplayHistoryRow> rows = [];
    private ReplayHistoryRow? selectedRow;
    private string summary = string.Empty;
    private string notice = WorkingNotice;
    private string profileColorHex = string.Empty;
    private bool isIdle = true;
    private string? actionNotice;

    /// <param name="onUiThread">Runs an action on the UI thread, at once when already on it.</param>
    public ReplayHistoryViewModel(IReplayHistorySource source, Func<IReadOnlyList<string>, Task> find,
        Func<ReplayAttempt, Task> findOriginal, Func<ReplayAttempt, Task> deleteOriginal, Func<Task> checkStatus,
        Action<string> copyText, Action<Action> onUiThread)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(findOriginal);
        ArgumentNullException.ThrowIfNull(deleteOriginal);
        ArgumentNullException.ThrowIfNull(checkStatus);
        ArgumentNullException.ThrowIfNull(copyText);
        ArgumentNullException.ThrowIfNull(onUiThread);
        this.source = source;
        this.find = find;
        this.findOriginal = findOriginal;
        this.deleteOriginal = deleteOriginal;
        this.checkStatus = checkStatus;
        this.copyText = copyText;
        this.onUiThread = onUiThread;
        FindAllCommand = new RelayCommand(() => _ = RunAsync(FindAllAsync), CanUseBroker);
        CheckStatusCommand = new RelayCommand(() => _ = RunAsync(this.checkStatus), CanUseBroker);
        FindReplayCommand = new RelayCommand<ReplayHistoryRow>(row => _ = FindReplayAsync(row));
        FindOriginalCommand = new RelayCommand<ReplayHistoryRow>(row => _ = FindOriginalAsync(row));
        CopyOriginalIdCommand = new RelayCommand<ReplayHistoryRow>(row => CopyId(row?.OriginalMessageId));
        CopyReplayIdCommand = new RelayCommand<ReplayHistoryRow>(row => CopyId(row?.MessageId));
        RemoveCommand = new RelayCommand<ReplayHistoryRow>(row => _ = RemoveAsync(row));
        DeleteOriginalCommand = new RelayCommand<ReplayHistoryRow>(row => _ = DeleteOriginalAsync(row));
        source.PropertyChanged += SourceChanged;
        Refresh();
    }

    /// <summary>Asks whether to remove an attempt from local history; the argument is the question to show.</summary>
    public Func<string, bool> ConfirmRemove { get; set; } = _ => false;

    public IReadOnlyList<ReplayHistoryRow> Rows
    {
        get => rows;
        private set => SetProperty(ref rows, value);
    }

    public ReplayHistoryRow? SelectedRow
    {
        get => selectedRow;
        set => SetProperty(ref selectedRow, value);
    }

    /// <summary>The profile name and the number of saved attempts.</summary>
    public string Summary
    {
        get => summary;
        private set => SetProperty(ref summary, value);
    }

    public string Notice
    {
        get => notice;
        private set => SetProperty(ref notice, value);
    }

    public string ProfileColorHex
    {
        get => profileColorHex;
        private set => SetProperty(ref profileColorHex, value);
    }

    /// <summary><c>false</c> while an action runs; the list and its actions are unavailable until it ends.</summary>
    public bool IsIdle
    {
        get => isIdle;
        private set => SetProperty(ref isIdle, value);
    }

    public RelayCommand FindAllCommand { get; }

    public RelayCommand CheckStatusCommand { get; }

    public RelayCommand<ReplayHistoryRow> FindReplayCommand { get; }

    public RelayCommand<ReplayHistoryRow> FindOriginalCommand { get; }

    public RelayCommand<ReplayHistoryRow> CopyOriginalIdCommand { get; }

    public RelayCommand<ReplayHistoryRow> CopyReplayIdCommand { get; }

    public RelayCommand<ReplayHistoryRow> RemoveCommand { get; }

    public RelayCommand<ReplayHistoryRow> DeleteOriginalCommand { get; }

    /// <summary>Stops following the workspace; call it when the window closes.</summary>
    public void Dispose() => source.PropertyChanged -= SourceChanged;

    private bool CanUseBroker() => IsIdle && source.IsConnected && Rows.Count > 0;

    private void SourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IReplayHistorySource.Preferences) or nameof(IReplayHistorySource.IsConnected)
            or nameof(IReplayHistorySource.SelectedProfile)) onUiThread(Refresh);
    }

    private void Refresh()
    {
        Guid? selectedId = SelectedRow?.Attempt.AttemptId;
        bool connected = source.IsConnected;
        ReplayHistoryRow[] refreshed = source.CurrentReplayHistory.Select(attempt => new ReplayHistoryRow(attempt,
            source.Preferences, source.CanRemoveReplayHistory(attempt), connected,
            connected && source.CanReviewReplayOriginalCleanup(attempt))).ToArray();
        InvestigationProfile profile = source.SelectedProfile;
        ProfileColorHex = profile.ColorHex;
        Summary = $"{profile.Connection.Name} · {refreshed.Length} attempts";
        Notice = actionNotice ?? (refreshed.Length == 0 ? EmptyNotice : ObservedNotice);
        Rows = refreshed;
        SelectedRow = refreshed.FirstOrDefault(row => row.Attempt.AttemptId == selectedId) ?? refreshed.FirstOrDefault();
        UpdateActions();
    }

    private void UpdateActions()
    {
        FindAllCommand.NotifyCanExecuteChanged();
        CheckStatusCommand.NotifyCanExecuteChanged();
    }

    private Task FindAllAsync() =>
        find(source.CurrentReplayHistory.Select(attempt => attempt.Reservation.MessageId).Distinct(StringComparer.Ordinal).ToArray());

    private async Task RunAsync(Func<Task> action, bool requireConnection = true)
    {
        if (!IsIdle || (requireConnection && !source.IsConnected)) return;
        IsIdle = false;
        actionNotice = null;
        Notice = WorkingNotice;
        UpdateActions();
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            actionNotice = Notice = FailedNotice;
            source.LogWarning(FailedNotice);
        }
        finally
        {
            IsIdle = true;
            onUiThread(Refresh);
        }
    }

    private async Task FindReplayAsync(ReplayHistoryRow? row)
    {
        if (row is not null) await RunAsync(() => find([row.MessageId]));
    }

    private async Task FindOriginalAsync(ReplayHistoryRow? row)
    {
        if (row is not null) await RunAsync(() => findOriginal(row.Attempt));
    }

    private void CopyId(string? id)
    {
        if (id is null) return;
        try
        {
            copyText(id);
            Notice = "Message ID copied.";
        }
        catch (Exception)
        {
            Notice = "Could not copy the message ID. Select it and copy manually.";
        }
    }

    private async Task RemoveAsync(ReplayHistoryRow? row)
    {
        if (row is null || !IsIdle || !source.CanRemoveReplayHistory(row.Attempt)) return;
        await RunAsync(async () =>
        {
            if (!ConfirmRemove(RemoveQuestion(row))) return;
            if (!await source.RemoveReplayHistoryAsync(row.Attempt)) actionNotice = Notice = NotRemovedNotice;
        }, requireConnection: false);
    }

    private static string RemoveQuestion(ReplayHistoryRow row) => "Remove this replay from local history?\n\n" + row.MessageId
        + "\n\nNo Service Bus messages will be deleted. Internal replay safety records remain.";

    private async Task DeleteOriginalAsync(ReplayHistoryRow? row)
    {
        if (row is not null && source.IsConnected && source.CanReviewReplayOriginalCleanup(row.Attempt))
            await RunAsync(() => deleteOriginal(row.Attempt));
    }
}
