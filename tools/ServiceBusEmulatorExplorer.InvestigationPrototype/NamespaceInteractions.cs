using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class PrototypeWindow
{
    private void EntitySearchFocused(object sender, KeyboardFocusChangedEventArgs e) => UpdateEntitySuggestions();

    private void UpdateEntitySuggestions()
    {
        var query = SearchBox.Text.Trim();
        EntitySuggestionsList.ItemsSource = Workspace.Roots.SelectMany(PrototypeData.Flatten)
            .Where(node => !node.IsGroup && query.Length > 0 && node.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        EntitySuggestionsPopup.IsOpen = Workspace.IsConnected && SearchBox.IsKeyboardFocusWithin && EntitySuggestionsList.Items.Count > 0;
    }

    private void EntitySearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up)
        {
            if (!EntitySuggestionsPopup.IsOpen) UpdateEntitySuggestions();
            var count = EntitySuggestionsList.Items.Count;
            if (count > 0)
            {
                EntitySuggestionsList.SelectedIndex = EntitySuggestionsList.SelectedIndex < 0 ? e.Key == Key.Down ? 0 : count - 1
                    : (EntitySuggestionsList.SelectedIndex + (e.Key == Key.Down ? 1 : count - 1)) % count;
                EntitySuggestionsList.ScrollIntoView(EntitySuggestionsList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && EntitySuggestionsPopup.IsOpen) { ChooseEntitySuggestion(); e.Handled = true; }
        else if (e.Key == Key.Escape) { EntitySuggestionsPopup.IsOpen = false; e.Handled = true; }
    }

    private void EntitySuggestionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ChooseEntitySuggestion(); e.Handled = true; }
        else if (e.Key == Key.Escape) { SearchBox.Focus(); EntitySuggestionsPopup.IsOpen = false; e.Handled = true; }
    }

    private void EntitySuggestionPicked(object sender, MouseButtonEventArgs e) => ChooseEntitySuggestion();

    private void ChooseEntitySuggestion()
    {
        if (EntitySuggestionsList.SelectedItem is not EntityNode node || !Workspace.IsConnected) return;
        SearchBox.Text = node.Path;
        EntitySuggestionsPopup.IsOpen = false;
        ChangeScope(() => Workspace.SelectEntity(node));
        SelectInitialEntity();
        FindScrollViewer(MessageGrid)?.ScrollToTop();
        ConfigureTimer();
    }

    private void ClearEntitySearch(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
        EntitySuggestionsPopup.IsOpen = false;
        SelectInitialEntity();
    }
}
