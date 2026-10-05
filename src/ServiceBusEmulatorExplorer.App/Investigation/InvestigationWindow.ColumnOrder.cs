using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private bool applyingColumnOrder;

    private void UpdateColumnChoiceOrder(IReadOnlyList<string> order, IReadOnlyList<string> selected)
    {
        var visible = order.Where(selected.Contains).ToArray();
        var choices = visible.Concat(columnOptions.Where(option => !option.IsSelected)
            .OrderBy(option => option.Section == "Default columns" ? 0 : option.Section == "System properties" ? 1 : 2)
            .ThenBy(option => option.Label, StringComparer.Ordinal)
            .Select(option => option.Id)).ToArray();
        var arranged = choices.Select(id => columnOptions.Single(item => item.Id == id)).ToArray();
        // Reset the grouped view after membership changes; incremental Move notifications can
        // otherwise target the old group after a checkbox changes Visible columns membership.
        if (!columnOptions.SequenceEqual(arranged))
        {
            columnOptions.Clear();
            foreach (var option in arranged) columnOptions.Add(option);
        }
        foreach (var option in arranged)
        {
            int position = Array.IndexOf(visible, option.Id);
            option.CanMoveUp = position > 0;
            option.CanMoveDown = position >= 0 && position < visible.Length - 1;
        }
        columnChoicesView?.Refresh();
    }

    private void ApplyColumnOrder(IReadOnlyList<string> order, bool fixedLayout)
    {
        applyingColumnOrder = true;
        try
        {
            MessageGrid.Columns[0].DisplayIndex = 0;
            StateColumn.DisplayIndex = 1;
            var data = MessageGrid.Columns.Where(column => column != StateColumn && column != MessageGrid.Columns[0]).ToArray();
            var arranged = fixedLayout
                ? new DataGridColumn[] { EventColumn, SourceColumn, EnqueuedColumn, CorrelationColumn }
                : order.Select(id => data.Single(column => column.SortMemberPath == id)).ToArray();
            int index = 2;
            foreach (var column in arranged.Concat(data.Except(arranged))) column.DisplayIndex = index++;
        }
        finally { applyingColumnOrder = false; }
    }

    private void MessageColumns_Reordering(object sender, DataGridColumnReorderingEventArgs e)
    {
        if (workspace.Surface.ShowsSource || e.Column == StateColumn || e.Column == MessageGrid.Columns[0]) e.Cancel = true;
    }

    private async void MessageColumns_Reordered(object sender, DataGridColumnEventArgs e)
    {
        if (applyingColumnOrder || workspace.Surface.ShowsSource) return;
        var selected = pendingColumns ?? MessageColumnCatalog.Normalize(workspace.Preferences.MessageColumns);
        var order = pendingColumnOrder ?? MessageColumnCatalog.NormalizeOrder(workspace.Preferences.MessageColumnOrder, selected);
        var visible = MessageGrid.Columns.Where(column => selected.Contains(column.SortMemberPath))
            .OrderBy(column => column.DisplayIndex).Select(column => column.SortMemberPath).ToArray();
        await SaveMessageColumnsAsync(selected, MessageColumnCatalog.ReorderVisible(order, visible));
    }

    private async void ColumnMove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MessageColumnOption option } button) return;
        var selected = pendingColumns ?? MessageColumnCatalog.Normalize(workspace.Preferences.MessageColumns);
        var order = pendingColumnOrder ?? MessageColumnCatalog.NormalizeOrder(workspace.Preferences.MessageColumnOrder, selected);
        var visible = order.Where(selected.Contains).ToArray();
        int index = Array.IndexOf(visible, option.Id);
        int next = index + (button.Tag as string == "Up" ? -1 : 1);
        if (index < 0 || next < 0 || next >= visible.Length) return;
        (visible[index], visible[next]) = (visible[next], visible[index]);
        await SaveMessageColumnsAsync(selected, MessageColumnCatalog.ReorderVisible(order, visible));
        // Group refresh rebuilds containers; keep keyboard navigation on the moved choice.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!ColumnsPopup.IsOpen) return;
            var target = FindColumnMoveButton(ColumnsChoices, option.Id, button.Tag);
            if (target is not null) { target.BringIntoView(); target.Focus(); }
            else ColumnsSearchBox.Focus();
        }));
    }

    private static Button? FindColumnMoveButton(DependencyObject parent, string id, object direction)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button { IsEnabled: true, DataContext: MessageColumnOption option } button &&
                option.Id == id && Equals(button.Tag, direction)) return button;
            if (FindColumnMoveButton(child, id, direction) is { } found) return found;
        }
        return null;
    }
}
