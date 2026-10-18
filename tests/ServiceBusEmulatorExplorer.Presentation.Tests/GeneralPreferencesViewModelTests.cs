using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Settings;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class GeneralPreferencesViewModelTests
{
    private static readonly WorkspacePreferences Start = new()
    {
        CloseToTray = true,
        NotificationsEnabled = false,
        AutoConnectOnSwitch = true,
        DateFormat = DateDisplayFormat.Iso,
        QueuePageSize = 25,
        TopicPageSize = 100,
        SubscriptionPageSize = 200,
        SearchTimeBudgetSeconds = 60,
        SearchDeliveryBudget = 50000
    };

    [Fact]
    public void Opening_shows_the_saved_values_and_saves_nothing()
    {
        var saves = new SaveScript();
        var general = new GeneralPreferencesViewModel(new SettingsPersistence(Start, saves.SaveAsync), trayAvailable: true);

        Assert.Equal((true, false, true, DateDisplayFormat.Iso), (general.CloseToTray, general.NotificationsEnabled, general.AutoConnectOnSwitch, general.DateFormat));
        Assert.Equal((25, 100, 200, 60, 50000), (general.QueuePageSize, general.TopicPageSize, general.SubscriptionPageSize,
            general.SearchTimeBudgetSeconds, general.SearchDeliveryBudget));
        Assert.True(general.IsEditable);
        Assert.Equal(string.Empty, general.Status);
        Assert.Empty(saves.Requested);
    }

    [Fact]
    public void Saved_values_outside_the_choices_show_the_default_choice_without_saving_it()
    {
        var saves = new SaveScript();
        var odd = Start with { QueuePageSize = 37, SearchTimeBudgetSeconds = 5, SearchDeliveryBudget = 7 };

        var general = new GeneralPreferencesViewModel(new SettingsPersistence(odd, saves.SaveAsync), trayAvailable: true);

        Assert.Equal((50, 30, 10000), (general.QueuePageSize, general.SearchTimeBudgetSeconds, general.SearchDeliveryBudget));
        Assert.Empty(saves.Requested);
    }

    public static TheoryData<Action<GeneralPreferencesViewModel>, Func<WorkspacePreferences, WorkspacePreferences>> Changes => new()
    {
        { general => general.CloseToTray = false, start => start with { CloseToTray = false } },
        { general => general.NotificationsEnabled = true, start => start with { NotificationsEnabled = true } },
        { general => general.AutoConnectOnSwitch = false, start => start with { AutoConnectOnSwitch = false } },
        { general => general.DateFormat = DateDisplayFormat.DayFirst, start => start with { DateFormat = DateDisplayFormat.DayFirst } },
        { general => general.QueuePageSize = 200, start => start with { QueuePageSize = 200 } },
        { general => general.TopicPageSize = 25, start => start with { TopicPageSize = 25 } },
        { general => general.SubscriptionPageSize = 50, start => start with { SubscriptionPageSize = 50 } },
        { general => general.SearchTimeBudgetSeconds = 10, start => start with { SearchTimeBudgetSeconds = 10 } },
        { general => general.SearchDeliveryBudget = 1000, start => start with { SearchDeliveryBudget = 1000 } },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task Each_change_saves_once_with_only_that_preference_changed(
        Action<GeneralPreferencesViewModel> change, Func<WorkspacePreferences, WorkspacePreferences> expected)
    {
        var saves = new SaveScript();
        var persistence = new SettingsPersistence(Start, saves.SaveAsync);
        var general = new GeneralPreferencesViewModel(persistence, trayAvailable: true);

        change(general);
        Assert.False(general.IsEditable);
        Assert.Equal("Saving…", general.Status);
        await saves.CompleteNext();

        Assert.Equal(expected(Start), Assert.Single(saves.Requested));
        Assert.Equal(expected(Start), persistence.Saved);
        Assert.True(general.IsEditable);
        Assert.Equal("General preferences saved.", general.Status);
    }

    [Fact]
    public async Task A_failed_save_puts_back_every_saved_value_without_saving_again()
    {
        var saves = new SaveScript();
        var persistence = new SettingsPersistence(Start, saves.SaveAsync);
        var general = new GeneralPreferencesViewModel(persistence, trayAvailable: true);

        general.QueuePageSize = 200;
        await saves.FailNext("disk full");

        Assert.Equal(25, general.QueuePageSize);
        Assert.Same(Start, persistence.Saved);
        Assert.Single(saves.Requested);
        Assert.True(general.IsEditable);
        Assert.Equal("Could not save preferences: disk full", general.Status);
    }

    [Fact]
    public async Task A_retry_after_a_failure_saves()
    {
        var saves = new SaveScript();
        var persistence = new SettingsPersistence(Start, saves.SaveAsync);
        var general = new GeneralPreferencesViewModel(persistence, trayAvailable: true);

        general.CloseToTray = false;
        await saves.FailNext("locked");
        Assert.True(general.CloseToTray);
        general.CloseToTray = false;
        await saves.CompleteNext();

        Assert.False(persistence.Saved.CloseToTray);
        Assert.Equal("General preferences saved.", general.Status);
    }

    [Fact]
    public void Choosing_the_value_that_is_already_saved_saves_nothing()
    {
        var saves = new SaveScript();
        var general = new GeneralPreferencesViewModel(new SettingsPersistence(Start with { QueuePageSize = 37 }, saves.SaveAsync), trayAvailable: true);

        general.QueuePageSize = 37;

        Assert.Empty(saves.Requested);
    }

    [Fact]
    public async Task A_change_while_a_save_is_running_is_not_saved_separately()
    {
        var saves = new SaveScript();
        var general = new GeneralPreferencesViewModel(new SettingsPersistence(Start, saves.SaveAsync), trayAvailable: true);

        general.QueuePageSize = 200;
        general.TopicPageSize = 25;
        await saves.CompleteNext();

        Assert.Single(saves.Requested);
    }

    [Fact]
    public async Task Without_a_tray_its_switch_stays_unavailable_and_says_why()
    {
        var saves = new SaveScript();
        var general = new GeneralPreferencesViewModel(new SettingsPersistence(Start, saves.SaveAsync), trayAvailable: false);

        Assert.False(general.CanChangeCloseToTray);
        Assert.Equal("System tray is unavailable in this session.", general.CloseToTrayDescription);
        Assert.True(general.CloseToTray);

        general.NotificationsEnabled = true;
        await saves.CompleteNext();
        Assert.False(general.CanChangeCloseToTray);
    }

    [Fact]
    public void With_a_tray_its_switch_is_available_except_while_saving()
    {
        var saves = new SaveScript();
        var general = new GeneralPreferencesViewModel(new SettingsPersistence(Start, saves.SaveAsync), trayAvailable: true);

        Assert.True(general.CanChangeCloseToTray);
        Assert.Equal("Keep watches running when you close the window.", general.CloseToTrayDescription);
        general.NotificationsEnabled = true;
        Assert.False(general.CanChangeCloseToTray);
    }

    [Fact]
    public void Refusing_to_close_explains_why()
    {
        var general = new GeneralPreferencesViewModel(new SettingsPersistence(Start, new SaveScript().SaveAsync), trayAvailable: true);

        general.ShowCloseBlocked();

        Assert.Equal("Saving preferences. Wait for the save to finish before closing.", general.Status);
    }

    /// <summary>Records each save and lets the test finish or fail it.</summary>
    private sealed class SaveScript
    {
        private readonly Queue<TaskCompletionSource> pending = new();

        public List<WorkspacePreferences> Requested { get; } = [];

        public Task SaveAsync(WorkspacePreferences preferences)
        {
            Requested.Add(preferences);
            var save = new TaskCompletionSource();
            pending.Enqueue(save);
            return save.Task;
        }

        public Task CompleteNext()
        {
            pending.Dequeue().SetResult();
            return Settle();
        }

        public Task FailNext(string message)
        {
            pending.Dequeue().SetException(new InvalidOperationException(message));
            return Settle();
        }

        // Continuations of the view model's awaits run inline here; one yield lets any that were queued finish.
        private static async Task Settle() => await Task.Yield();
    }
}
