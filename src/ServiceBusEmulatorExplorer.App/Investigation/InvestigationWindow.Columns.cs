using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private readonly ObservableCollection<MessageColumnOption> columnOptions = [];
    private readonly Dictionary<string, DataGridTextColumn> propertyColumns = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? pendingColumns;
    private IReadOnlyList<string>? pendingColumnOrder;
    private int columnSaveRevision;
    private ICollectionView? columnChoicesView;

    private void UpdateMessageColumns()
    {
        if (ColumnsButton is null) return;
        var selected = pendingColumns ?? MessageColumnCatalog.Normalize(workspace.Preferences.MessageColumns);
        bool fixedLayout = workspace.Surface.ShowsSource;
        MessageGrid.CanUserReorderColumns = !fixedLayout;
        ColumnsButton.Visibility = fixedLayout ? Visibility.Collapsed : Visibility.Visible;
        if (fixedLayout) ColumnsPopup.IsOpen = false;
        EventColumn.SortMemberPath = MessageColumnCatalog.Event;
        CorrelationColumn.SortMemberPath = MessageColumnCatalog.Correlation;
        EnqueuedColumn.SortMemberPath = MessageColumnCatalog.Enqueued;
        SourceColumn.Visibility = fixedLayout ? Visibility.Visible : Visibility.Collapsed;
        EventColumn.Visibility = fixedLayout || selected.Contains(MessageColumnCatalog.Event) ? Visibility.Visible : Visibility.Collapsed;
        CorrelationColumn.Visibility = !fixedLayout && selected.Contains(MessageColumnCatalog.Correlation) ? Visibility.Visible : Visibility.Collapsed;
        EnqueuedColumn.Visibility = (fixedLayout || workspace.Preferences.MessageColumns is null && pendingColumns is null)
            ? (ActualWidth < 1200 ? Visibility.Collapsed : Visibility.Visible)
            : selected.Contains(MessageColumnCatalog.Enqueued) ? Visibility.Visible : Visibility.Collapsed;
        var order = pendingColumnOrder ?? MessageColumnCatalog.NormalizeOrder(workspace.Preferences.MessageColumnOrder, selected);
        var available = MessageColumnCatalog.Discover(workspace.Surface.Messages, order);
        if (!ColumnsPopup.IsOpen)
            foreach (var stale in columnOptions.Where(option => !available.Any(item => item.Id == option.Id)).ToArray())
                columnOptions.Remove(stale);
        foreach (var definition in available)
        {
            if (!columnOptions.Any(option => option.Id == definition.Id)) columnOptions.Add(new(definition));
            if (definition.Section == "Default columns" || propertyColumns.ContainsKey(definition.Id)) continue;
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
        foreach (var option in columnOptions)
        {
            option.IsSelected = selected.Contains(option.Id);
            option.CanToggle = !option.IsSelected || selected.Count > 1;
        }
        UpdateColumnChoiceOrder(order, selected);
        foreach (var (id, column) in propertyColumns)
            column.Visibility = !fixedLayout && selected.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
        ApplyColumnOrder(order, fixedLayout);
        FitMessageIdentityColumns();
    }

    private void Columns_Click(object sender, RoutedEventArgs e)
    {
        UpdateMessageColumns();
        if (columnChoicesView is null)
        {
            columnChoicesView = new ListCollectionView(columnOptions);
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
        option.IsSelected = checkBox.IsChecked == true;
        var selected = columnOptions.Where(choice => choice.IsSelected).Select(choice => choice.Id).ToArray();
        if (selected.Length == 0) { UpdateMessageColumns(); return; }
        await SaveMessageColumnsAsync(selected, MessageColumnCatalog.NormalizeOrder(pendingColumnOrder ?? workspace.Preferences.MessageColumnOrder, selected));
    }

    private async Task SaveMessageColumnsAsync(IReadOnlyList<string> selected, IReadOnlyList<string> order)
    {
        int revision = ++columnSaveRevision;
        pendingColumns = selected;
        pendingColumnOrder = order;
        UpdateMessageColumns();
        try { await workspace.UpdateMessageColumnsAsync(selected, order); }
        catch (Exception)
        {
            workspace.Log("Column choices could not be saved. Restored the last saved selection and order; try again.", true);
        }
        finally
        {
            if (revision == columnSaveRevision)
            {
                pendingColumns = null;
                pendingColumnOrder = null;
                UpdateMessageColumns();
            }
        }
    }

    private async void ColumnsReset_Click(object sender, RoutedEventArgs e) => await SaveMessageColumnsAsync(MessageColumnCatalog.Defaults, MessageColumnCatalog.Defaults);
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
