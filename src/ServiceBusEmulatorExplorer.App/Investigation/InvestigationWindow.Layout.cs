using System.Windows;
using System.Windows.Controls;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private void MessageGrid_SizeChanged(object sender, SizeChangedEventArgs e) => FitMessageIdentityColumns();

    private bool fittingMessageColumns;

    private void FitMessageIdentityColumns()
    {
        if (MessageGrid is null || StateColumn is null || fittingMessageColumns || MessageGrid.ActualWidth <= 0) return;
        fittingMessageColumns = true;
        try
        {
            var columns = MessageGrid.Columns.Where(column => column.Visibility == Visibility.Visible &&
                column != MessageGrid.Columns[0] && column != StateColumn).OrderBy(column => column.DisplayIndex).ToArray();
            if (columns.Length == 0) return;
            double available = MessageGrid.ActualWidth - 8 - MessageGrid.Columns[0].ActualWidth - StateColumn.ActualWidth;
            double responsiveCap = Math.Clamp(available / columns.Length, 120, 200);
            foreach (var column in columns)
            {
                column.MinWidth = Math.Min(column.MinWidth, responsiveCap);
                column.MaxWidth = responsiveCap;
                column.Width = DataGridLength.Auto;
            }
        }
        finally { fittingMessageColumns = false; }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (initializingWindow) resizedDuringInitialize = true;
        if (ready) UpdateLayoutMode();
    }
    private void UpdateLayoutMode()
    {
        var compact = ActualWidth < 1200;
        var shortWindow = compact && ActualHeight < 720;
        UpdateMessageColumns();
        LogPanel.Height = shortWindow ? 35 : compact ? 95 : 150;
        ActiveTab.Padding = DeadLetterTab.Padding = shortWindow ? new Thickness(12, 4, 12, 4) : new Thickness(12, 8, 12, 8);
        RefreshButton.Padding = shortWindow ? new Thickness(8, 4, 8, 4) : new Thickness(12, 7, 12, 7);
        ListHeading.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        InspectorMessageId.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        CorrelationMetadata.Padding = shortWindow ? new Thickness(6, 4, 6, 4) : new Thickness(10, 9, 10, 9);
        BodyEditor.Padding = compact ? new Thickness(12, 8, 128, 8) : new Thickness(12, 16, 128, 16);
        BodyViewer.Padding = new Thickness(BodyViewer.Padding.Left, BodyViewer.Padding.Top, 128, BodyViewer.Padding.Bottom);
        UpdateInspectorCopy();
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

    private void InspectorToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (InspectorTabChoices is null || InspectorActionButtons is null) return;
        var available = new Size(double.PositiveInfinity, double.PositiveInfinity);
        InspectorTabChoices.Measure(available);
        InspectorActionButtons.Measure(available);
        bool wrapActions = InspectorTabs.ActualWidth <
            InspectorTabChoices.DesiredSize.Width + InspectorActionButtons.DesiredSize.Width;
        Grid.SetRow(InspectorActionButtons, wrapActions ? 1 : 0);
        Grid.SetColumn(InspectorActionButtons, wrapActions ? 0 : 1);
        Grid.SetColumnSpan(InspectorActionButtons, wrapActions ? 2 : 1);
    }

    private void UpdateWorkspaceColumns()
    {
        if (WorkspaceGrid is null || MessageLibraryPrototype is null) return;
        if (ReplayHistoryButton is not null)
        {
            ReplayHistoryButton.Visibility = MessageLibraryPrototype.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            ReplayHistoryLabel.Visibility = ActualWidth < 1200 ? Visibility.Collapsed : Visibility.Visible;
        }
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
