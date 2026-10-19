using System.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Settings;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class SettingsPersistenceTests
{
    [Fact]
    public async Task Saved_changes_only_once_the_save_succeeds()
    {
        var held = new TaskCompletionSource();
        var start = new WorkspacePreferences { QueuePageSize = 25 };
        var persistence = new SettingsPersistence(start, _ => held.Task);

        Task saving = persistence.SaveAsync(current => current with { QueuePageSize = 200 });
        Assert.Same(start, persistence.Saved);
        Assert.True(persistence.IsSaving);

        held.SetResult();
        await saving;

        Assert.Equal(200, persistence.Saved.QueuePageSize);
        Assert.False(persistence.IsSaving);
    }

    [Fact]
    public async Task A_failed_save_keeps_the_previous_preferences_rethrows_and_lets_the_next_save_run()
    {
        var start = new WorkspacePreferences { QueuePageSize = 25 };
        bool fail = true;
        var persistence = new SettingsPersistence(start, _ => fail ? Task.FromException(new InvalidOperationException("disk full")) : Task.CompletedTask);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => persistence.SaveAsync(current => current with { QueuePageSize = 200 }));
        Assert.Equal("disk full", failure.Message);
        Assert.Same(start, persistence.Saved);
        Assert.False(persistence.IsSaving);

        fail = false;
        await persistence.SaveAsync(current => current with { QueuePageSize = 100 });
        Assert.Equal(100, persistence.Saved.QueuePageSize);
    }

    [Fact]
    public async Task A_save_that_throws_before_awaiting_still_releases_the_gate()
    {
        bool fail = true;
        var persistence = new SettingsPersistence(new WorkspacePreferences(), _ => fail ? throw new InvalidOperationException("sync") : Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(() => persistence.SaveAsync(current => current));
        fail = false;
        await persistence.SaveAsync(current => current with { TopicPageSize = 25 }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(25, persistence.Saved.TopicPageSize);
    }

    [Fact]
    public async Task Saves_run_one_at_a_time_and_each_builds_on_the_previous_result()
    {
        var calls = new List<(WorkspacePreferences Preferences, TaskCompletionSource Done)>();
        var persistence = new SettingsPersistence(new WorkspacePreferences { QueuePageSize = 25, TopicPageSize = 25 }, preferences =>
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            calls.Add((preferences, done));
            return done.Task;
        });

        Task first = persistence.SaveAsync(current => current with { QueuePageSize = 200 });
        Task second = persistence.SaveAsync(current => current with { TopicPageSize = 100 });
        Assert.Single(calls);

        calls[0].Done.SetResult();
        await first;
        await WaitUntil(() => calls.Count == 2);
        Assert.Equal(200, calls[1].Preferences.QueuePageSize);
        Assert.Equal(100, calls[1].Preferences.TopicPageSize);
        Assert.True(persistence.IsSaving);

        calls[1].Done.SetResult();
        await second;
        Assert.False(persistence.IsSaving);
        Assert.Equal((200, 100), (persistence.Saved.QueuePageSize, persistence.Saved.TopicPageSize));
    }

    [Fact]
    public async Task Is_saving_is_announced_once_when_saving_starts_and_once_when_everything_queued_has_finished()
    {
        var held = new TaskCompletionSource();
        var persistence = new SettingsPersistence(new WorkspacePreferences(), _ => held.Task);
        var announced = new List<bool>();
        ((INotifyPropertyChanged)persistence).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsPersistence.IsSaving)) announced.Add(persistence.IsSaving);
        };

        Task first = persistence.SaveAsync(current => current);
        Task second = persistence.SaveAsync(current => current);
        held.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal([true, false], announced);
    }

    [Fact]
    public async Task An_update_that_throws_saves_nothing_and_releases_the_gate()
    {
        int saves = 0;
        var persistence = new SettingsPersistence(new WorkspacePreferences(), _ => { saves++; return Task.CompletedTask; });

        await Assert.ThrowsAsync<InvalidOperationException>(() => persistence.SaveAsync(_ => throw new InvalidOperationException("bad update")));
        await persistence.SaveAsync(current => current).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, saves);
        Assert.False(persistence.IsSaving);
    }

    [Fact]
    public async Task Waiting_for_idle_includes_saves_queued_while_waiting()
    {
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        var saves = new Queue<Task>([first.Task, second.Task]);
        var persistence = new SettingsPersistence(new WorkspacePreferences(), _ => saves.Dequeue());
        Assert.True(await persistence.WhenIdleAsync());

        Task saving = persistence.SaveAsync(current => current with { QueuePageSize = 100 });
        Task<bool> idle = persistence.WhenIdleAsync();
        Task queued = persistence.SaveAsync(current => current with { TopicPageSize = 100 });
        first.SetResult();
        await saving;
        Assert.False(idle.IsCompleted);

        second.SetResult();
        await queued;
        Assert.True(await idle);
        Assert.False(persistence.IsSaving);
    }

    [Fact]
    public async Task Waiting_for_idle_reports_a_save_that_failed_meanwhile()
    {
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        var saves = new Queue<Task>([first.Task, second.Task]);
        var persistence = new SettingsPersistence(new WorkspacePreferences(), _ => saves.Dequeue());

        Task saving = persistence.SaveAsync(current => current with { QueuePageSize = 100 });
        Task<bool> idle = persistence.WhenIdleAsync();
        Task failing = persistence.SaveAsync(current => current with { TopicPageSize = 100 });
        first.SetResult();
        second.SetException(new InvalidOperationException("disk full"));
        await saving;
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing);

        Assert.False(await idle);
        Assert.True(await persistence.WhenIdleAsync());
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The queued save did not start.");
            await Task.Delay(10);
        }
    }
}
