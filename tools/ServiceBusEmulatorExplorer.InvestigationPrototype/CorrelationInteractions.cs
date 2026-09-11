using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class PrototypeWindow
{
    private bool scanQueued;

    private void CorrelationChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateSuggestions();
        UpdateSearchAction();
    }

    private void CorrelationFocused(object sender, KeyboardFocusChangedEventArgs e) => UpdateSuggestions();

    private void UpdateSuggestions()
    {
        if (SuggestionsList is null) return;
        SuggestionsList.ItemsSource = Workspace.Suggestions(CorrelationBox.Text);
        SuggestionsList.SelectedIndex = -1;
        SuggestionsPopup.IsOpen = Workspace.IsConnected && !string.IsNullOrWhiteSpace(CorrelationBox.Text) && CorrelationBox.IsKeyboardFocusWithin && SuggestionsList.Items.Count > 0;
    }

    private void CorrelationKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up)
        {
            if (!SuggestionsPopup.IsOpen) UpdateSuggestions();
            var count = SuggestionsList.Items.Count;
            if (count > 0)
            {
                SuggestionsList.SelectedIndex = SuggestionsList.SelectedIndex < 0 ? e.Key == Key.Down ? 0 : count - 1
                    : (SuggestionsList.SelectedIndex + (e.Key == Key.Down ? 1 : count - 1)) % count;
                SuggestionsList.ScrollIntoView(SuggestionsList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (SuggestionsPopup.IsOpen && SuggestionsList.SelectedItem is string suggestion) CorrelationBox.Text = suggestion;
            BeginSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { SuggestionsPopup.IsOpen = false; e.Handled = true; }
    }

    private void SuggestionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ChooseSuggestion(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CorrelationBox.Focus(); SuggestionsPopup.IsOpen = false; e.Handled = true; }
    }

    private void SuggestionPicked(object sender, MouseButtonEventArgs e) => ChooseSuggestion();

    private void ChooseSuggestion()
    {
        if (SuggestionsList.SelectedItem is not string suggestion) return;
        CorrelationBox.Text = suggestion;
        BeginSearch();
    }

    private void UpdateSearchAction()
    {
        ClearCorrelationSearchButton.Visibility = Workspace.IsCorrelationSearch || CorrelationBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BeginSearch()
    {
        if (!Workspace.IsConnected || string.IsNullOrWhiteSpace(CorrelationBox.Text)) return;
        SuggestionsPopup.IsOpen = false;
        refreshTimer.Stop();
        ChangeScope(() => Workspace.StartCorrelationSearch(CorrelationBox.Text));
        FindScrollViewer(MessageGrid)?.ScrollToTop();
        QueueScan();
    }

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
        CorrelationBox.Text = "";
        SuggestionsPopup.IsOpen = false;
        ChangeScope(Workspace.ClearCorrelationSearch);
        ConfigureTimer();
        CorrelationBox.Focus();
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
        CorrelationBox.Text = row.CorrelationId;
        BeginSearch();
    }

    private void UpdateSearchSurface()
    {
        if (SearchSummary is null) return;
        var searching = Workspace.IsCorrelationSearch;
        ListBreadcrumb.Text = searching ? "Search / Related messages" : Workspace.EntityPath;
        MessagesHeading.Text = searching ? "Related messages" : "Messages";
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
        UpdateSearchAction();
        CorrelationBox.IsEnabled = Workspace.IsConnected;
        EmptyMessage.Text = !searching ? "No messages in this view" : Workspace.SearchComplete ? "No matching messages" : "No matches found so far";
        EmptyDescription.Text = !searching ? "Choose another entity to browse messages." : Workspace.SearchComplete
            ? $"No messages with correlation ID {Workspace.CorrelationQuery} were found in this search.\nCheck the ID or clear the search to browse messages."
            : Workspace.IsSearching ? "The search is still running." : "Search incomplete. Search again or clear the search to browse messages.";
        EmptyClearButton.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        DlqReason.Visibility = Workspace.FocusedMessage?.IsDeadLetter == true && !compact ? Visibility.Visible : Visibility.Collapsed;
    }
}
