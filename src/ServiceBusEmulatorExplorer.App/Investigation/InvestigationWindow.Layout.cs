using System.Windows;
using System.Windows.Controls;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (initializingWindow) resizedDuringInitialize = true;
        if (ready) UpdateLayoutMode();
    }
    private void UpdateLayoutMode()
    {
        var compact = ActualWidth < 1200;
        var shortWindow = compact && ActualHeight < 720;
        EnqueuedColumn.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        LogPanel.Height = shortWindow ? 35 : compact ? 95 : 150;
        ActiveTab.Padding = DeadLetterTab.Padding = shortWindow ? new Thickness(12, 4, 12, 4) : new Thickness(12, 8, 12, 8);
        RefreshButton.Padding = shortWindow ? new Thickness(8, 4, 8, 4) : new Thickness(12, 7, 12, 7);
        ListHeading.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        InspectorMessageId.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        CorrelationMetadata.Padding = shortWindow ? new Thickness(6, 4, 6, 4) : new Thickness(10, 9, 10, 9);
        BodyEditor.Padding = compact ? new Thickness(12, 5, 12, 5) : new Thickness(14, 18, 14, 18);
        ReplayActions.Padding = shortWindow ? new Thickness(8, 5, 8, 5) : new Thickness(10);
        LoadMoreButton.Padding = shortWindow ? new Thickness(8, 4, 8, 4) : new Thickness(12, 7, 12, 7);
        EmptySearchIcon.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        EmptyResults.Margin = compact ? new Thickness(4) : new Thickness(25);
        EmptyMessage.FontSize = compact ? 16 : 20;
        EmptyMessage.Margin = compact ? new Thickness(0, 0, 0, 4) : new Thickness(0, 8, 0, 8);
        EmptyClearButton.Margin = compact ? new Thickness(0, 5, 0, 0) : new Thickness(0, 16, 0, 0);
        EmptyClearButton.Padding = compact ? new Thickness(8, 3, 8, 3) : new Thickness(12, 7, 12, 7);
        ListHeading.Margin = compact ? new Thickness(15, 9, 15, 8) : new Thickness(19, 18, 15, 12);
        InspectorHeading.Margin = compact ? new Thickness(15, 8, 15, 5) : new Thickness(18, 14, 15, 10);
        InspectorTitle.FontSize = compact ? 18 : 24;
        MessageGrid.RowHeight = compact ? 50 : 54;
        MessageGrid.ColumnHeaderHeight = compact ? 30 : double.NaN;
        ListToolbar.Padding = compact ? new Thickness(12, 4, 12, 4) : new Thickness(12, 9, 12, 9);
        ListFooter.Padding = compact ? new Thickness(15, 3, 15, 3) : new Thickness(15, 10, 15, 10);
        ContentGrid.ColumnDefinitions[0].MinWidth = compact ? 320 : 370;
        ContentGrid.ColumnDefinitions[2].MinWidth = compact ? 340 : 380;
        DockPanel.SetDock(FindRelatedButton, compact ? Dock.Bottom : Dock.Right);
        UpdateWorkspaceColumns();
        UpdateSearchSurface();
    }

    private void UpdateWorkspaceColumns()
    {
        if (WorkspaceGrid is null || MessageLibraryPrototype is null) return;
        bool compactLibrary = MessageLibraryPrototype.Visibility == Visibility.Visible && ActualWidth < 1200;
        WorkspaceGrid.ColumnDefinitions[0].MinWidth = 240;
        double namespaceWidth = MessageLibraryPrototype.Visibility == Visibility.Visible
            ? (compactLibrary ? 250 : 305) : (ActualWidth < 1200 ? 240 : 305);
        WorkspaceGrid.ColumnDefinitions[0].MaxWidth = MessageLibraryPrototype.Visibility == Visibility.Visible
            ? 420 : Math.Min(420,
            Math.Max(240, WorkspaceGrid.ActualWidth - ContentGrid.ColumnDefinitions[0].MinWidth
                - ContentGrid.ColumnDefinitions[2].MinWidth - 10));
        WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(namespaceWidth);
        NamespaceTree.FontSize = compactLibrary ? 11 : 14;
    }
}
