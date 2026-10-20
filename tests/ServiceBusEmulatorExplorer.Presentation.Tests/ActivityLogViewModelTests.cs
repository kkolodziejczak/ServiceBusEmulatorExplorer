using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class ActivityLogViewModelTests
{
    [Fact]
    public void Requires_a_workspace() =>
        Assert.Equal("workspace", Assert.Throws<ArgumentNullException>(() => new ActivityLogViewModel(null!)).ParamName);

    [Fact]
    public async Task Lines_show_their_kind_and_time_in_the_chosen_zone()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var log = new ActivityLogViewModel(fixture.Workspace);
        log.Clear();
        fixture.Workspace.Log("info line");
        fixture.Workspace.Log("watch line", watch: true);
        fixture.Workspace.Log("warning line", warning: true);

        Assert.True(log.UsesUtc);
        var lines = log.Lines;
        Assert.Equal([ActivityLevel.Info, ActivityLevel.Watch, ActivityLevel.Warning], lines.Select(line => line.Level));
        Assert.Equal(["INFO    ", "WATCH   ", "WARN    "], lines.Select(line => line.Label));
        Assert.Equal(["info line", "watch line", "warning line"], lines.Select(line => line.Message));
        var last = fixture.Workspace.Activity[^1].TimestampUtc;
        Assert.Equal(last.ToString("HH:mm:ss") + " UTC", lines[^1].Time);
        Assert.EndsWith(" UTC", lines[^1].Date, StringComparison.Ordinal);
        Assert.Equal(lines[^1].Time, log.LastTime);
        Assert.Equal(lines[^1].Date, log.LastDate);
        Assert.Equal("Enqueued (UTC)", log.EnqueuedHeader);

        await log.SetUsesUtcAsync(false);

        Assert.False(log.UsesUtc);
        Assert.Equal(last.ToLocalTime().ToString("HH:mm:ss") + " Local", log.Lines[^1].Time);
        Assert.EndsWith(" Local", log.LastDate, StringComparison.Ordinal);
        Assert.Equal("Enqueued (Local)", log.EnqueuedHeader);
    }

    [Fact]
    public async Task Clearing_empties_the_log_and_its_last_time()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var log = new ActivityLogViewModel(fixture.Workspace);
        Assert.False(log.IsEmpty);

        log.Clear();

        Assert.True(log.IsEmpty);
        Assert.Empty(log.Lines);
        Assert.Equal("", log.LastTime);
        Assert.Null(log.LastDate);
    }

    [Fact]
    public async Task The_log_expands_and_collapses_and_says_which()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var log = new ActivityLogViewModel(fixture.Workspace);
        bool expanded = log.IsExpanded;

        await log.ToggleAsync();

        Assert.Equal(!expanded, log.IsExpanded);
        Assert.Equal(!expanded, fixture.Store.Saved.LogExpanded);
        Assert.Equal(log.IsExpanded ? "Activity log · Expanded" : "Activity log · Collapsed", log.Heading);
        Assert.Equal(log.IsExpanded ? "Collapse activity log" : "Expand activity log", log.ToggleLabel);
        await log.ToggleAsync();
        Assert.Equal(expanded, log.IsExpanded);
        Assert.Equal(log.IsExpanded ? "Activity log · Expanded" : "Activity log · Collapsed", log.Heading);
    }
}
