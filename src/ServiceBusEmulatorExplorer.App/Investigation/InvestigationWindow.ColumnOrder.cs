using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private bool applyingColumnOrder;

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
        await columns.ReorderAsync(MessageGrid.Columns.OrderBy(column => column.DisplayIndex).Select(column => column.SortMemberPath));
    }

    private async void ColumnMove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MessageColumnOption option } button) return;
        if (!await columns.MoveAsync(option.Id, up: button.Tag as string == "Up")) return;
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
