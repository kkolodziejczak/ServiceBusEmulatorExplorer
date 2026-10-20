using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private readonly Dictionary<string, DataGridTextColumn> propertyColumns = new(StringComparer.Ordinal);
    private ICollectionView? columnChoicesView;

    private void UpdateMessageColumns()
    {
        if (ColumnsButton is null) return;
        bool fixedLayout = columns.IsFixedLayout;
        MessageGrid.CanUserReorderColumns = !fixedLayout;
        ColumnsButton.Visibility = fixedLayout ? Visibility.Collapsed : Visibility.Visible;
        if (fixedLayout) ColumnsPopup.IsOpen = false;
        EventColumn.SortMemberPath = MessageColumnCatalog.Event;
        CorrelationColumn.SortMemberPath = MessageColumnCatalog.Correlation;
        EnqueuedColumn.SortMemberPath = MessageColumnCatalog.Enqueued;
        var layout = columns.Refresh(ColumnsPopup.IsOpen, narrow: ActualWidth < 1200);
        SourceColumn.Visibility = layout.IsFixed ? Visibility.Visible : Visibility.Collapsed;
        EventColumn.Visibility = layout.ShowsEvent ? Visibility.Visible : Visibility.Collapsed;
        CorrelationColumn.Visibility = layout.ShowsCorrelation ? Visibility.Visible : Visibility.Collapsed;
        EnqueuedColumn.Visibility = layout.ShowsEnqueued ? Visibility.Visible : Visibility.Collapsed;
        foreach (var definition in layout.PropertyColumns)
        {
            if (propertyColumns.ContainsKey(definition.Id)) continue;
            var textStyle = new Style(typeof(TextBlock), (Style)FindResource("CompactDataGridText"));
            textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            textStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("Text") { RelativeSource = RelativeSource.Self }));
            var valueBinding = new MultiBinding { Converter = new MessagePropertyConverter(definition.Id) };
            valueBinding.Bindings.Add(new Binding("Delivery"));
            valueBinding.Bindings.Add(new Binding(nameof(InvestigationWorkspace.Preferences)) { Source = workspace });
            var column = new DataGridTextColumn
            {
                Header = definition.Label, SortMemberPath = definition.Id, Width = 160, MinWidth = 100,
                Binding = valueBinding,
                ElementStyle = textStyle
            };
            propertyColumns.Add(definition.Id, column);
            MessageGrid.Columns.Add(column);
        }
        columnChoicesView?.Refresh();
        foreach (var (id, column) in propertyColumns)
            column.Visibility = layout.Shown.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
        ApplyColumnOrder(layout.Order, layout.IsFixed);
        FitMessageIdentityColumns();
    }

    private void ColumnsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MessageColumnsViewModel.Selected)) UpdateMessageColumns();
    }

    private void Columns_Click(object sender, RoutedEventArgs e)
    {
        UpdateMessageColumns();
        if (columnChoicesView is null)
        {
            columnChoicesView = new ListCollectionView(columns.Options);
            columnChoicesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MessageColumnOption.Section)));
            columnChoicesView.Filter = item => item is MessageColumnOption option &&
                (string.IsNullOrWhiteSpace(ColumnsSearchBox.Text) || option.Label.Contains(ColumnsSearchBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase));
            ColumnsChoices.ItemsSource = columnChoicesView;
        }
        ColumnsSearchBox.Clear();
        ColumnsPopup.IsOpen = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ColumnsSearchBox.Focus()));
    }

    private void ColumnsSearch_Changed(object sender, TextChangedEventArgs e)
    {
        columnChoicesView?.Refresh();
        if (ColumnsEmpty is not null) ColumnsEmpty.Visibility = columnChoicesView?.IsEmpty == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ColumnChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: MessageColumnOption option } checkBox) return;
        await columns.ToggleAsync(option, checkBox.IsChecked == true);
    }

    private async void ColumnsReset_Click(object sender, RoutedEventArgs e) => await columns.ResetAsync();
    private void ColumnsDone_Click(object sender, RoutedEventArgs e) => ColumnsPopup.IsOpen = false;
    private void Columns_Closed(object? sender, EventArgs e) { if (ColumnsButton.IsVisible) ColumnsButton.Focus(); }
    private void Columns_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        ColumnsPopup.IsOpen = false;
        e.Handled = true;
    }

    private void MessageColumns_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Column.SortMemberPath)) return;
        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in MessageGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
        if (CollectionViewSource.GetDefaultView(MessageGrid.ItemsSource) is ListCollectionView view)
        {
            view.CustomSort = new MessagePropertyComparer(e.Column.SortMemberPath, direction);
            view.LiveSortingProperties.Clear();
            view.LiveSortingProperties.Add(nameof(MessageRow.Delivery));
            view.IsLiveSorting = true;
        }
    }

    private sealed class MessagePropertyConverter(string id) : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values is [MessageDelivery delivery, WorkspacePreferences preferences] ? MessageColumnCatalog.Format(MessageColumnCatalog.Value(delivery.Message, id), preferences) : "—";
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class MessagePropertyComparer(string id, ListSortDirection direction) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            object? Value(object? row) => row is MessageRow message
                ? id == MessageColumnCatalog.Event ? message.EventName : MessageColumnCatalog.Value(message.Delivery.Message, id) : null;
            int result = MessageColumnCatalog.Compare(Value(x), Value(y));
            return direction == ListSortDirection.Descending ? -Math.Sign(result) : result;
        }
    }
}
