using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// The message list's columns: which are shown and in what order, and the column chooser's choices. A change is shown
/// at once and saved; until the save ends the unsaved choice is shown, and a failed save puts back the saved one.
/// While the list shows messages from several sources it uses a fixed layout that cannot be changed.
/// </summary>
public sealed class MessageColumnsViewModel : ObservableObject
{
    private readonly InvestigationWorkspace workspace;
    private IReadOnlyList<string>? pendingColumns;
    private IReadOnlyList<string>? pendingOrder;
    private int saveRevision;

    public MessageColumnsViewModel(InvestigationWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
    }

    /// <summary>The chooser's choices: shown columns first in their order, then the others by section and label.</summary>
    public ObservableCollection<MessageColumnOption> Options { get; } = [];

    /// <summary>The list shows its fixed layout with the source column; the chooser and reordering are not offered.</summary>
    public bool IsFixedLayout => workspace.Surface.ShowsSource;

    /// <summary>The shown columns: the unsaved choice while it is saved, otherwise the saved one.</summary>
    public IReadOnlyList<string> Selected => pendingColumns ?? MessageColumnCatalog.Normalize(workspace.Preferences.MessageColumns);

    /// <summary>The order of every column the list knows.</summary>
    public IReadOnlyList<string> Order => pendingOrder ?? MessageColumnCatalog.NormalizeOrder(workspace.Preferences.MessageColumnOrder, Selected);

    /// <summary>
    /// Brings the choices up to date with the messages listed and returns what the list shows. Choices for columns no
    /// longer found are only removed while the chooser is closed, so it does not change under the pointer.
    /// </summary>
    /// <param name="chooserOpen">The column chooser is open.</param>
    /// <param name="narrow">The window is too narrow to show the Enqueued column by default.</param>
    public MessageColumnLayout Refresh(bool chooserOpen, bool narrow)
    {
        var selected = Selected;
        bool fixedLayout = IsFixedLayout;
        var order = Order;
        var available = MessageColumnCatalog.Discover(workspace.Surface.Messages, order);
        if (!chooserOpen)
            foreach (var stale in Options.Where(option => !available.Any(item => item.Id == option.Id)).ToArray())
                Options.Remove(stale);
        foreach (var definition in available)
            if (!Options.Any(option => option.Id == definition.Id)) Options.Add(new(definition));
        foreach (var option in Options)
        {
            option.IsSelected = selected.Contains(option.Id);
            option.CanToggle = !option.IsSelected || selected.Count > 1;
        }
        ArrangeOptions(order, selected);
        bool defaultEnqueued = fixedLayout || workspace.Preferences.MessageColumns is null && pendingColumns is null;
        return new(fixedLayout, order,
            available.Where(definition => definition.Section != "Default columns").ToArray(),
            ShowsEvent: fixedLayout || selected.Contains(MessageColumnCatalog.Event),
            ShowsCorrelation: !fixedLayout && selected.Contains(MessageColumnCatalog.Correlation),
            ShowsEnqueued: defaultEnqueued ? !narrow : selected.Contains(MessageColumnCatalog.Enqueued),
            Shown: fixedLayout ? [] : selected);
    }

    /// <summary>Shows or hides a column. Hiding the last shown column is refused and the choice is put back.</summary>
    public Task ToggleAsync(MessageColumnOption option, bool selected)
    {
        ArgumentNullException.ThrowIfNull(option);
        option.IsSelected = selected;
        var chosen = Options.Where(choice => choice.IsSelected).Select(choice => choice.Id).ToArray();
        if (chosen.Length == 0)
        {
            OnPropertyChanged(nameof(Selected));
            return Task.CompletedTask;
        }
        return SaveAsync(chosen, MessageColumnCatalog.NormalizeOrder(pendingOrder ?? workspace.Preferences.MessageColumnOrder, chosen));
    }

    public Task ResetAsync() => SaveAsync(MessageColumnCatalog.Defaults, MessageColumnCatalog.Defaults);

    /// <summary>Moves a shown column one place up or down; returns <c>false</c> when it cannot move that way.</summary>
    public async Task<bool> MoveAsync(string id, bool up)
    {
        ArgumentNullException.ThrowIfNull(id);
        var selected = Selected;
        var order = Order;
        var visible = order.Where(selected.Contains).ToArray();
        int index = Array.IndexOf(visible, id);
        int next = index + (up ? -1 : 1);
        if (index < 0 || next < 0 || next >= visible.Length) return false;
        (visible[index], visible[next]) = (visible[next], visible[index]);
        await SaveAsync(selected, MessageColumnCatalog.ReorderVisible(order, visible));
        return true;
    }

    /// <summary>Saves the order the user dragged the columns into, given as the list's columns from left to right.</summary>
    public Task ReorderAsync(IEnumerable<string> columnsLeftToRight)
    {
        ArgumentNullException.ThrowIfNull(columnsLeftToRight);
        var selected = Selected;
        var visible = columnsLeftToRight.Where(selected.Contains).ToArray();
        return SaveAsync(selected, MessageColumnCatalog.ReorderVisible(Order, visible));
    }

    private async Task SaveAsync(IReadOnlyList<string> selected, IReadOnlyList<string> order)
    {
        int revision = ++saveRevision;
        pendingColumns = selected;
        pendingOrder = order;
        OnPropertyChanged(nameof(Selected));
        try { await workspace.UpdateMessageColumnsAsync(selected, order); }
        catch (Exception)
        {
            workspace.Log("Column choices could not be saved. Restored the last saved selection and order; try again.", true);
        }
        finally
        {
            if (revision == saveRevision)
            {
                pendingColumns = null;
                pendingOrder = null;
                OnPropertyChanged(nameof(Selected));
            }
        }
    }

    private void ArrangeOptions(IReadOnlyList<string> order, IReadOnlyList<string> selected)
    {
        var visible = order.Where(selected.Contains).ToArray();
        var choices = visible.Concat(Options.Where(option => !option.IsSelected)
            .OrderBy(option => option.Section == "Default columns" ? 0 : option.Section == "System properties" ? 1 : 2)
            .ThenBy(option => option.Label, StringComparer.Ordinal)
            .Select(option => option.Id)).ToArray();
        var arranged = choices.Select(id => Options.Single(item => item.Id == id)).ToArray();
        // Reset the grouped view after membership changes; incremental Move notifications can
        // otherwise target the old group after a checkbox changes Visible columns membership.
        if (!Options.SequenceEqual(arranged))
        {
            Options.Clear();
            foreach (var option in arranged) Options.Add(option);
        }
        foreach (var option in arranged)
        {
            int position = Array.IndexOf(visible, option.Id);
            option.CanMoveUp = position > 0;
            option.CanMoveDown = position >= 0 && position < visible.Length - 1;
        }
    }
}

/// <summary>
/// What the message list shows: the fixed layout or the chosen one, the column order, the property columns the
/// messages have, whether the Event, Correlation and Enqueued columns show, and which property columns show.
/// </summary>
public sealed record MessageColumnLayout(bool IsFixed, IReadOnlyList<string> Order, IReadOnlyList<MessageColumnDefinition> PropertyColumns,
    bool ShowsEvent, bool ShowsCorrelation, bool ShowsEnqueued, IReadOnlyList<string> Shown);
