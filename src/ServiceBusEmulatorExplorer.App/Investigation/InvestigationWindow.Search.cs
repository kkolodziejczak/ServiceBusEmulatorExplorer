using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private readonly SearchSuggestions suggestions = new();
    private double browseScrollOffset;
    private long? suggestionGeneration;

    private void UpdateSearchSurface()
    {
        var search = workspace.Search;
        if (!workspace.IsConnected) { suggestions.Clear(); suggestionGeneration = null; }
        var generation = workspace.Surface.Messages.FirstOrDefault()?.Key.ConnectionGeneration;
        if (generation is not null && generation != suggestionGeneration)
        {
            suggestions.Clear();
            suggestionGeneration = generation;
        }
        suggestions.Track(workspace.Surface.Messages);
        SearchStatusPanel.Visibility = search.IsActive ? Visibility.Visible : Visibility.Collapsed;
        SearchStatusText.Text = search.Status;
        bool complete = search.IsComplete;
        SearchStatusPanel.Background = complete ? Brushes.Transparent : (Brush)FindResource("NeutralHoverBrush");
        SearchStatusPanel.BorderThickness = new Thickness(complete ? 0 : 1);
        SearchStatusPanel.Padding = complete ? new Thickness(0, 3, 0, 8) : new Thickness(10, 8, 10, 8);
        StopSearchButton.IsEnabled = search.IsBusy;
        StopSearchButton.Visibility = search.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        BrowseTabs.Visibility = search.IsActive ? Visibility.Collapsed : Visibility.Visible;
        SearchSummary.Visibility = search.IsActive && ActualWidth >= 1200 ? Visibility.Visible : Visibility.Collapsed;
        SearchSummary.Text = $"{search.QueryText} · {search.Messages.Count:N0} matches{(complete ? "" : " so far")}";
        ListToolbar.Visibility = search.IsActive ? Visibility.Collapsed : Visibility.Visible;
        NamespaceTree.ToolTip = search.IsActive && MessageLibraryPrototype.Visibility != Visibility.Visible
            ? "Counts show matching messages found so far. Clear search to restore entity totals."
            : null;
        NamespaceEmpty.Visibility = workspace.Surface.Roots.Any(node => node.IsVisible) ? Visibility.Collapsed : Visibility.Visible;
        if (MessageLibraryPrototype.Visibility == Visibility.Visible) NamespaceEmpty.Visibility = Visibility.Collapsed;
        NamespaceEmpty.Text = search.IsActive ? search.IsBusy ? "Searching for matching entities…"
            : "No matching entities found in the scanned deliveries. Clear the search to show all entities."
            : "No matching entities. Clear the search to show all entities.";
        LoadMoreButton.Content = search.IsActive ? "Continue" : "Load more";
        FindRelatedButton.IsEnabled = workspace.Surface.FocusedMessage is { CorrelationId.Length: > 0 };
        if (search.IsActive)
        {
            EmptyMessage.Text = search.QueryError.Length > 0 ? "Invalid search" : search.IsBusy ? "Searching messages…"
                : complete ? "No matching messages" : "No matches in the scanned deliveries";
            EmptyDescription.Text = search.QueryError.Length > 0 ? search.QueryError + "\nUse OR between IDs and * to match any characters." : search.Status;
            EmptyClearButton.Visibility = Visibility.Visible;
        }
        ClearSearchButton.Visibility = search.IsActive || SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ConfigureRefresh();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (!ready) return;
        if (MessageLibraryPrototype.Visibility == Visibility.Visible)
        {
            workbenchAssociationPaths = null;
            FilterWorkbenchNamespaces(SearchBox.Text);
            ClearSearchButton.Visibility = SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            SuggestionsPopup.IsOpen = false;
            return;
        }
        if (!workspace.Search.IsActive) workspace.Browse.FilterEntities(SearchBox.Text);
        ClearSearchButton.Visibility = workspace.Search.IsActive || SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSuggestions();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        if (MessageLibraryPrototype.Visibility == Visibility.Visible)
        {
            SearchBox.Clear();
            NamespaceTree.Focus();
            SearchBox.Focus();
            return;
        }
        var wasSearch = workspace.Search.IsActive;
        inspectorSynchronizingSelection = true;
        try { workspace.Search.Clear(); }
        finally { inspectorSynchronizingSelection = false; }
        SearchBox.Clear();
        workspace.Browse.FilterEntities("");
        SearchBox.Focus();
        SuggestionsPopup.IsOpen = false;
        if (wasSearch)
        {
            MessageGrid.UpdateLayout();
            FindMessageScrollViewer(MessageGrid)?.ScrollToVerticalOffset(browseScrollOffset);
        }
        SurfaceChanged(this, new System.ComponentModel.PropertyChangedEventArgs(null));
    }

    private void SearchFocused(object sender, KeyboardFocusChangedEventArgs e) => UpdateSuggestions();

    private void UpdateSuggestions()
    {
        if (!ready) return;
        if (MessageLibraryPrototype.Visibility == Visibility.Visible)
        {
            SuggestionsPopup.IsOpen = false;
            return;
        }
        suggestions.Track(workspace.Surface.Messages);
        var items = suggestions.Build(SearchBox.Text, workspace.Browse.AllEntities(), workspace.IsConnected).ToList();
        var view = new ListCollectionView(items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SearchSuggestion.Group)));
        SuggestionsList.ItemsSource = view;
        SuggestionsList.SelectedIndex = -1;
        SuggestionsPopup.IsOpen = SearchBox.IsKeyboardFocusWithin && items.Count > 0;
    }

    private void SearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up)
        {
            if (!SuggestionsPopup.IsOpen) UpdateSuggestions();
            int count = SuggestionsList.Items.Count;
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
            if (SuggestionsPopup.IsOpen && SuggestionsList.SelectedItem is SearchSuggestion) ChooseSuggestion();
            else { UpdateSuggestions(); if (SuggestionsList.Items.Count > 0) SuggestionsList.SelectedIndex = 0; }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { SuggestionsPopup.IsOpen = false; e.Handled = true; }
    }

    private void SuggestionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ChooseSuggestion(); e.Handled = true; }
        else if (e.Key == Key.Escape) { SuggestionsPopup.IsOpen = false; SearchBox.Focus(); e.Handled = true; }
    }

    private void SuggestionPicked(object sender, MouseButtonEventArgs e) => ChooseSuggestion();

    private async void ChooseSuggestion()
    {
        if (SuggestionsList.SelectedItem is not SearchSuggestion suggestion) return;
        SuggestionsPopup.IsOpen = false;
        if (suggestion.Entity is { } entity)
        {
            ClearSearch_Click(this, new RoutedEventArgs());
            SearchBox.Text = entity.Path;
            SuggestionsPopup.IsOpen = false;
            await workspace.RunReadAsync(() => workspace.Browse.SelectAsync(entity, false));
            FindMessageScrollViewer(MessageGrid)?.ScrollToTop();
        }
        else if (suggestion.Message is { } message)
        {
            ClearSearch_Click(this, new RoutedEventArgs());
            await workspace.RunReadAsync(() => workspace.Browse.OpenKnownAsync(message));
            MessageGrid.ScrollIntoView(workspace.Browse.FocusedMessage);
        }
        else
        {
            if (suggestion.Kind == "correlation") SearchBox.Text = MessageSearchQuery.QuoteLiteral(suggestion.Title);
            await BeginGlobalSearchAsync(suggestion.Kind == "search-message");
        }
    }

    private async Task BeginGlobalSearchAsync(bool byMessageId)
    {
        if (!workspace.IsConnected || string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        if (!workspace.Search.IsActive) browseScrollOffset = FindMessageScrollViewer(MessageGrid)?.VerticalOffset ?? 0;
        workspace.Browse.Cancel();
        workspace.Browse.FilterEntities("");
        refreshTimer.Stop();
        SuggestionsPopup.IsOpen = false;
        inspectorSynchronizingSelection = true;
        Task operation;
        try { operation = workspace.Search.StartAsync(SearchBox.Text, byMessageId); }
        finally { inspectorSynchronizingSelection = false; }
        FindMessageScrollViewer(MessageGrid)?.ScrollToTop();
        await workspace.RunReadAsync(() => operation);
    }

    private void StopSearch_Click(object sender, RoutedEventArgs e) => workspace.Search.Stop();

    private async void FindRelated_Click(object sender, RoutedEventArgs e)
    {
        if (workspace.Surface.FocusedMessage is not { CorrelationId.Length: > 0 } row) return;
        SearchBox.Text = MessageSearchQuery.QuoteLiteral(row.CorrelationId);
        await BeginGlobalSearchAsync(false);
    }

    private static ScrollViewer? FindMessageScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindMessageScrollViewer(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
}
