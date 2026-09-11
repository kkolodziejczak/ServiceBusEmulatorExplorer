using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public sealed record SearchSuggestion(string Group, string Title, string Detail, string Kind, EntityNode? Entity = null, MessageRow? Message = null)
{
    public string Badge => Entity?.Kind ?? (Message is null ? "" : Message.IsDeadLetter ? "DLQ" : "Active");
    public string IconBrush => Entity?.Kind == "Subscription" ? "#7351B5" : Message?.IsDeadLetter == true ? "#A65B00" : "#0078F8";
    public string Icon => Entity?.Kind switch
    {
        "Topic" => "M7,1 H13 V6 H7 Z M10,6 V10 M3,10 H17 M3,10 V14 M17,10 V14 M0,14 H6 V19 H0 Z M14,14 H20 V19 H14 Z",
        "Subscription" => "M10,0 V12 M6,8 L10,12 14,8 M3,10 L0,19 H20 L17,10 M0,15 H6 L8,18 H12 L14,15 H20",
        "Queue" => "M2,2 H18 V6 H2 Z M2,9 H18 V13 H2 Z M2,16 H18 V20 H2 Z",
        _ => Kind.StartsWith("search-") ? "M10,5 A5,5 0 1 1 0,5 A5,5 0 1 1 10,5 M9,9 L14,14" : "M2,0 H10 L15,5 V18 H2 Z M10,0 V5 H15 M5,9 H12 M5,13 H12"
    };
}

public partial class PrototypeWindow
{
    private readonly Dictionary<string, MessageRow> loadedSearchMessages = new(StringComparer.Ordinal);

    private void SearchFocused(object sender, KeyboardFocusChangedEventArgs e) => UpdateSuggestions();

    private void UpdateSuggestions()
    {
        if (!IsLoaded) return;
        foreach (var row in Workspace.Messages) loadedSearchMessages[row.Key] = row;
        var query = SearchBox.Text.Trim();
        var items = new List<SearchSuggestion>();
        if (query.Length > 0 && Workspace.IsConnected)
        {
            items.AddRange(Workspace.Roots.SelectMany(PrototypeData.Flatten)
                .Where(node => !node.IsGroup && node.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(5)
                .Select(node => new SearchSuggestion("ENTITIES", node.Path, "", "entity", node)));
            items.AddRange(Workspace.Suggestions(query).Take(3).Select(id => new SearchSuggestion("CORRELATION IDS", id, "Search all entities", "correlation")));
            items.AddRange(loadedSearchMessages.Values.Where(row => row.MessageId.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(3)
                .Select(row => new SearchSuggestion("MESSAGES · LOADED ONLY", row.MessageId, row.Source, "message", Message: row)));
            items.Add(new("SEARCH ALL ENTITIES", "Search all messages by correlation ID", query, "search-correlation"));
            items.Add(new("SEARCH ALL ENTITIES", "Search all messages by message ID", query, "search-message"));
        }
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
            if (SuggestionsPopup.IsOpen && SuggestionsList.SelectedItem is SearchSuggestion) ChooseSuggestion();
            else { UpdateSuggestions(); if (SuggestionsList.Items.Count > 0) SuggestionsList.SelectedIndex = 0; }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { SuggestionsPopup.IsOpen = false; e.Handled = true; }
    }

    private void SuggestionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ChooseSuggestion(); e.Handled = true; }
        else if (e.Key == Key.Escape) { SearchBox.Focus(); SuggestionsPopup.IsOpen = false; e.Handled = true; }
    }

    private void SuggestionPicked(object sender, MouseButtonEventArgs e) => ChooseSuggestion();

    private void ChooseSuggestion()
    {
        if (!Workspace.IsConnected || SuggestionsList.SelectedItem is not SearchSuggestion suggestion) return;
        switch (suggestion.Kind)
        {
            case "entity":
                SearchBox.Text = suggestion.Entity!.Path;
                ChangeScope(() => Workspace.SelectEntity(suggestion.Entity));
                SelectInitialEntity();
                ConfigureTimer();
                AddLog($"Opened {suggestion.Entity.Path}.");
                break;
            case "message":
                SearchBox.Clear();
                ChangeScope(() => Workspace.OpenMessage(suggestion.Message!));
                SelectInitialEntity();
                MessageGrid.ScrollIntoView(Workspace.FocusedMessage);
                ConfigureTimer();
                AddLog($"Opened {suggestion.Message!.MessageId}.");
                break;
            case "correlation":
                SearchBox.Text = SearchLiteral(suggestion.Title);
                BeginGlobalSearch(false);
                break;
            default:
                BeginGlobalSearch(suggestion.Kind == "search-message");
                break;
        }
        SuggestionsPopup.IsOpen = false;
    }

    private void BeginGlobalSearch(bool byMessageId)
    {
        if (!Workspace.IsConnected || string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        pendingSearchFocusKey = null;
        Workspace.SetSearch("");
        NamespaceEmpty.Visibility = Visibility.Collapsed;
        SuggestionsPopup.IsOpen = false;
        refreshTimer.Stop();
        ChangeScope(() => { if (byMessageId) Workspace.StartMessageIdSearch(SearchBox.Text); else Workspace.StartCorrelationSearch(SearchBox.Text); });
        FindScrollViewer(MessageGrid)?.ScrollToTop();
        AddLog($"Searching all entities by {(byMessageId ? "message" : "correlation")} ID: {SearchBox.Text.Trim()}.");
        QueueScan();
    }

    private void UpdateClearSearch() => ClearSearchButton.Visibility = Workspace.IsCorrelationSearch || SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    private static string SearchLiteral(string value) => value.StartsWith("correlation:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("message:", StringComparison.OrdinalIgnoreCase)
        || value.Equals("OR", StringComparison.OrdinalIgnoreCase) || value.Any(character => char.IsWhiteSpace(character) || character is '*' or '"')
        ? MessageSearchQuery.QuoteLiteral(value) : value;
}
