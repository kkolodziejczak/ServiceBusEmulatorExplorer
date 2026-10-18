using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Settings;

/// <summary>
/// The General tab of Settings: every change saves at once, one save at a time; while it saves the controls are locked,
/// and a failed save puts back the last saved values and says why (DEC-053).
/// </summary>
public sealed class GeneralPreferencesViewModel : ObservableObject
{
    public const string SavingCloseMessage = "Saving preferences. Wait for the save to finish before closing.";

    private readonly SettingsPersistence persistence;
    private bool closeToTray;
    private bool notificationsEnabled;
    private bool autoConnectOnSwitch;
    private DateDisplayFormat dateFormat;
    private int queuePageSize;
    private int topicPageSize;
    private int subscriptionPageSize;
    private int searchTimeBudgetSeconds;
    private int searchDeliveryBudget;
    private bool isEditable = true;
    private string status = string.Empty;
    private bool savePending;
    private bool restoring;

    public GeneralPreferencesViewModel(SettingsPersistence persistence, bool trayAvailable)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        this.persistence = persistence;
        TrayAvailable = trayAvailable;
        Show(persistence.Saved);
    }

    public IReadOnlyList<int> PageSizeChoices { get; } = [25, 50, 100, 200];

    public IReadOnlyList<int> SearchTimeBudgetChoices { get; } = [10, 30, 60, 120];

    public IReadOnlyList<int> SearchDeliveryBudgetChoices { get; } = [1000, 10000, 50000, 100000];

    public bool TrayAvailable { get; }

    public string CloseToTrayDescription => TrayAvailable
        ? "Keep watches running when you close the window."
        : "System tray is unavailable in this session.";

    public bool CloseToTray
    {
        get => closeToTray;
        set => Change(ref closeToTray, value, current => current with { CloseToTray = CloseToTray });
    }

    public bool NotificationsEnabled
    {
        get => notificationsEnabled;
        set => Change(ref notificationsEnabled, value, current => current with { NotificationsEnabled = NotificationsEnabled });
    }

    public bool AutoConnectOnSwitch
    {
        get => autoConnectOnSwitch;
        set => Change(ref autoConnectOnSwitch, value, current => current with { AutoConnectOnSwitch = AutoConnectOnSwitch });
    }

    public DateDisplayFormat DateFormat
    {
        get => dateFormat;
        set => Change(ref dateFormat, value, current => current with { DateFormat = value });
    }

    public int QueuePageSize
    {
        get => queuePageSize;
        set => ChangeUnlessSaved(ref queuePageSize, value, persistence.Saved.QueuePageSize, current => current with { QueuePageSize = value });
    }

    public int TopicPageSize
    {
        get => topicPageSize;
        set => ChangeUnlessSaved(ref topicPageSize, value, persistence.Saved.TopicPageSize, current => current with { TopicPageSize = value });
    }

    public int SubscriptionPageSize
    {
        get => subscriptionPageSize;
        set => ChangeUnlessSaved(ref subscriptionPageSize, value, persistence.Saved.SubscriptionPageSize, current => current with { SubscriptionPageSize = value });
    }

    public int SearchTimeBudgetSeconds
    {
        get => searchTimeBudgetSeconds;
        set => ChangeUnlessSaved(ref searchTimeBudgetSeconds, value, persistence.Saved.SearchTimeBudgetSeconds, current => current with { SearchTimeBudgetSeconds = value });
    }

    public int SearchDeliveryBudget
    {
        get => searchDeliveryBudget;
        set => ChangeUnlessSaved(ref searchDeliveryBudget, value, persistence.Saved.SearchDeliveryBudget, current => current with { SearchDeliveryBudget = value });
    }

    /// <summary>False while a general save runs, so the controls cannot queue a second one.</summary>
    public bool IsEditable
    {
        get => isEditable;
        private set
        {
            if (SetProperty(ref isEditable, value)) OnPropertyChanged(nameof(CanChangeCloseToTray));
        }
    }

    public bool CanChangeCloseToTray => IsEditable && TrayAvailable;

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    /// <summary>Explains why the window refused to close while something was still saving.</summary>
    public void ShowCloseBlocked() => Status = SavingCloseMessage;

    private void Change<T>(ref T field, T value, Func<WorkspacePreferences, WorkspacePreferences> update,
        [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName) && !restoring) _ = SaveAsync(update);
    }

    private void ChangeUnlessSaved(ref int field, int value, int saved, Func<WorkspacePreferences, WorkspacePreferences> update,
        [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName) && !restoring && value != saved) _ = SaveAsync(update);
    }

    private async Task SaveAsync(Func<WorkspacePreferences, WorkspacePreferences> update)
    {
        if (savePending) return;
        savePending = true;
        IsEditable = false;
        Status = "Saving…";
        try
        {
            await persistence.SaveAsync(update);
            Status = "General preferences saved.";
        }
        catch (Exception exception)
        {
            Show(persistence.Saved);
            Status = $"Could not save preferences: {exception.Message}";
        }
        finally
        {
            savePending = false;
            IsEditable = true;
        }
    }

    /// <summary>Shows saved preferences without saving them again; values outside the choices show the default choice.</summary>
    private void Show(WorkspacePreferences saved)
    {
        restoring = true;
        try
        {
            CloseToTray = saved.CloseToTray;
            NotificationsEnabled = saved.NotificationsEnabled;
            AutoConnectOnSwitch = saved.AutoConnectOnSwitch;
            DateFormat = saved.DateFormat;
            QueuePageSize = OneOf(PageSizeChoices, saved.QueuePageSize, 50);
            TopicPageSize = OneOf(PageSizeChoices, saved.TopicPageSize, 50);
            SubscriptionPageSize = OneOf(PageSizeChoices, saved.SubscriptionPageSize, 50);
            SearchTimeBudgetSeconds = OneOf(SearchTimeBudgetChoices, saved.SearchTimeBudgetSeconds, 30);
            SearchDeliveryBudget = OneOf(SearchDeliveryBudgetChoices, saved.SearchDeliveryBudget, 10000);
        }
        finally
        {
            restoring = false;
        }
    }

    private static int OneOf(IReadOnlyList<int> choices, int value, int fallback) => choices.Contains(value) ? value : fallback;
}
