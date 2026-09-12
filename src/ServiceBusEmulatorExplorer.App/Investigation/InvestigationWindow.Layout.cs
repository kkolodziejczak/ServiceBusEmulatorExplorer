using System.Windows;
using System.Windows.Controls;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) { if (ready) UpdateLayoutMode(); }
    private void UpdateLayoutMode()
    {
        var nextCompact = ActualWidth < 1200;
        var compact = nextCompact;
        var shortWindow = compact && ActualHeight < 720;
        EnqueuedColumn.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        LogPanel.Height = shortWindow ? 35 : compact ? 95 : 150;
        ActiveTab.Padding = DeadLetterTab.Padding = shortWindow ? new Thickness(12, 4, 12, 4) : new Thickness(12, 8, 12, 8);
        RefreshButton.Padding = shortWindow ? new Thickness(8, 4, 8, 4) : new Thickness(12, 7, 12, 7);
        ListHeading.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        InspectorMessageId.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        CorrelationMetadata.Padding = shortWindow ? new Thickness(6, 4, 6, 4) : new Thickness(10, 9, 10, 9);
        BodyEditor.Padding = shortWindow ? new Thickness(12, 5, 12, 5) : new Thickness(14, 18, 14, 18);
        ReplayActions.Padding = shortWindow ? new Thickness(8, 5, 8, 5) : new Thickness(10);
        LoadMoreButton.Padding = shortWindow ? new Thickness(8, 4, 8, 4) : new Thickness(12, 7, 12, 7);
        EmptySearchIcon.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        EmptyResults.Margin = shortWindow ? new Thickness(12) : new Thickness(25);
        ListHeading.Margin = compact ? new Thickness(15, 9, 15, 8) : new Thickness(19, 18, 15, 12);
        InspectorHeading.Margin = compact ? new Thickness(15, 8, 15, 5) : new Thickness(18, 14, 15, 10);
        InspectorTitle.FontSize = compact ? 18 : 24;
        MessageGrid.RowHeight = compact ? 50 : 54;
        MessageGrid.ColumnHeaderHeight = compact ? 30 : double.NaN;
        ListToolbar.Padding = compact ? new Thickness(12, 4, 12, 4) : new Thickness(12, 9, 12, 9);
        ListFooter.Padding = compact ? new Thickness(15, 3, 15, 3) : new Thickness(15, 10, 15, 10);
        ContentGrid.ColumnDefinitions[0].MinWidth = compact ? 0 : 370;
        ContentGrid.ColumnDefinitions[2].MinWidth = compact ? 0 : 380;
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(compact ? 1 : 1.4, GridUnitType.Star);
        ContentGrid.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 5);
        ContentGrid.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ContentGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        ContentGrid.RowDefinitions[1].Height = new GridLength(compact ? 5 : 0);
        ContentGrid.RowDefinitions[2].Height = compact ? new GridLength(1.25, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(InspectorPane, compact ? 0 : 2);
        Grid.SetRow(InspectorPane, compact ? 2 : 0);
        Grid.SetColumn(InspectorSplitter, compact ? 0 : 1);
        Grid.SetRow(InspectorSplitter, compact ? 1 : 0);
        InspectorSplitter.Width = compact ? double.NaN : 5;
        InspectorSplitter.Height = compact ? 5 : double.NaN;
        InspectorSplitter.ResizeDirection = compact ? GridResizeDirection.Rows : GridResizeDirection.Columns;

    }
}
