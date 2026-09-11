using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class PrototypeWindow
{
    private bool scanQueued;
    private string? pendingSearchFocusKey;

    private void ChangeScope(Action change)
    {
        synchronizingSelection = true;
        try { change(); }
        finally { synchronizingSelection = false; }
        SynchronizeSelection();
        RenderInspector();
        UpdateSearchSurface();
    }

    private void QueueScan()
    {
        if (scanQueued || !Workspace.IsSearching) return;
        scanQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            scanQueued = false;
            if (!Workspace.IsSearching) return;
            ChangeScope(Workspace.ScanNext);
            QueueScan();
        }), DispatcherPriority.Background);
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        pendingSearchFocusKey = null;
        SearchBox.Text = "";
        Workspace.SetSearch("");
        SuggestionsPopup.IsOpen = false;
        ChangeScope(Workspace.ClearCorrelationSearch);
        ConfigureTimer();
        SearchBox.Focus();
        SuggestionsPopup.IsOpen = false;
    }

    private void StopSearch_Click(object sender, RoutedEventArgs e) => Workspace.StopCorrelationSearch();
    private void CopyCorrelation_Click(object sender, RoutedEventArgs e) => CopyCorrelation(Workspace.FocusedMessage?.CorrelationId);
    private void CopyRowCorrelation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MessageRow row }) CopyCorrelation(row.CorrelationId);
        e.Handled = true;
    }

    private void CopyCorrelation(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        try { Clipboard.SetText(value); AddLog("Copied correlation ID for log lookup."); }
        catch (System.Runtime.InteropServices.COMException) { AddLog("Clipboard is busy. Try Copy again."); }
    }

    private void FindRelated_Click(object sender, RoutedEventArgs e)
    {
        if (Workspace.FocusedMessage is not { CorrelationId.Length: > 0 } row) return;
        SearchBox.Text = SearchLiteral(row.CorrelationId);
        BeginGlobalSearch(false);
    }

    private void UpdateSearchSurface()
    {
        if (SearchSummary is null) return;
        if (pendingSearchFocusKey is { } key)
        {
            var match = Workspace.Messages.FirstOrDefault(row => row.Key == key);
            if (match is not null)
            {
                pendingSearchFocusKey = null;
                Workspace.FocusedMessage = match;
                SynchronizeSelection();
                RenderInspector();
                MessageGrid.ScrollIntoView(match);
            }
            else if (!Workspace.IsSearching) pendingSearchFocusKey = null;
        }
        var searching = Workspace.IsCorrelationSearch;
        SourceColumn.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        NamespaceEmpty.Visibility = Workspace.Roots.Any(node => node.IsVisible) ? Visibility.Collapsed : Visibility.Visible;
        NamespaceEmpty.Text = searching
            ? Workspace.IsSearching ? "Searching for matching entities…" : "No matching entities found. Clear the search to show all entities."
            : "No matching entities. Clear the search to show all entities.";
        NamespaceTree.ToolTip = searching ? "Counts show matching messages found so far. Clear search to restore entity totals." : null;
        ListBreadcrumb.Text = searching ? "Search / Related messages" : Workspace.EntityPath;
        SearchSummary.Text = $"{Workspace.CorrelationQuery} · {Workspace.Messages.Count} matches{(Workspace.SearchComplete ? "" : " so far")}";
        SearchSummary.Visibility = searching && !compact ? Visibility.Visible : Visibility.Collapsed;
        BrowseTabs.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        SearchStatusPanel.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        SearchStatusPanel.Background = Workspace.SearchComplete ? System.Windows.Media.Brushes.Transparent : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 244, 255));
        SearchStatusPanel.BorderThickness = new Thickness(Workspace.SearchComplete ? 0 : 1);
        SearchStatusPanel.Padding = Workspace.SearchComplete ? new Thickness(0, 3, 0, 8) : new Thickness(10, 8, 10, 8);
        ListToolbar.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        LoadMoreButton.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        StopSearchButton.Visibility = Workspace.IsSearching ? Visibility.Visible : Visibility.Collapsed;
        UpdateClearSearch();
        if (!Workspace.IsConnected) SuggestionsPopup.IsOpen = false;
        EmptyMessage.Text = !searching ? "No messages in this view" : Workspace.SearchComplete ? "No matching messages" : "No matches found so far";
        EmptyDescription.Text = !searching ? "Choose another entity to browse messages." : Workspace.SearchComplete
            ? $"No messages with {(Workspace.SearchByMessageId ? "message ID" : "correlation ID")} {Workspace.CorrelationQuery} were found in this search.\nCheck the ID or clear the search to browse messages."
            : Workspace.IsSearching ? "The search is still running." : "Search incomplete. Search again or clear the search to browse messages.";
        EmptyClearButton.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        if (Workspace.SearchQueryError.Length > 0)
        {
            EmptyMessage.Text = "Invalid search";
            EmptyDescription.Text = Workspace.SearchQueryError + "\nUse OR between IDs and * to match any characters.";
        }
        DlqReason.Visibility = Workspace.FocusedMessage?.IsDeadLetter == true && !compact ? Visibility.Visible : Visibility.Collapsed;
    }
}
