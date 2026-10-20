using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeView
{
    private void WorkbenchSearchRegion_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) CloseLibrarySearchSuggestions();
    }

    internal void CloseLibrarySearchSuggestions() => LibrarySuggestionsPopup.IsOpen = false;

    private void TemplateSearch_Focused(object sender, KeyboardFocusChangedEventArgs e) => UpdateLibrarySuggestions();

    private void UpdateLibrarySuggestions()
    {
        if (TemplateSearch is null || LibrarySuggestionsPopup is null || !IsVisible) return;

        string query = TemplateSearch.Text.Trim();
        if (query.Length == 0)
        {
            LibrarySuggestionsPopup.IsOpen = false;
            return;
        }

        var items = WorkbenchLibrarySearch.Suggest(query, namespaceQuery,
            templates.Select(template => new LibraryEntry(template.Name, template.Description, template.Folder, template.Topic)),
            folders, createdFolders);

        var view = new ListCollectionView(items.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LibrarySuggestion.Group)));
        LibrarySuggestionsList.ItemsSource = view;
        LibrarySuggestionsList.SelectedIndex = -1;
        LibrarySuggestionsPopup.IsOpen = TemplateSearch.IsKeyboardFocusWithin && items.Count > 0;
    }

    private void TemplateSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up)
        {
            if (!LibrarySuggestionsPopup.IsOpen) UpdateLibrarySuggestions();
            int count = LibrarySuggestionsList.Items.Count;
            if (count > 0)
            {
                LibrarySuggestionsList.SelectedIndex = WorkbenchLibrarySearch.NextIndex(LibrarySuggestionsList.SelectedIndex, count, e.Key == Key.Down);
                LibrarySuggestionsList.ScrollIntoView(LibrarySuggestionsList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (LibrarySuggestionsPopup.IsOpen && LibrarySuggestionsList.SelectedItem is LibrarySuggestion)
                ChooseLibrarySuggestion();
            else
            {
                UpdateLibrarySuggestions();
                if (LibrarySuggestionsList.Items.Count > 0) LibrarySuggestionsList.SelectedIndex = 0;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            LibrarySuggestionsPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void LibrarySuggestions_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ChooseLibrarySuggestion();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TemplateSearch.Focus();
            LibrarySuggestionsPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void LibrarySuggestions_Picked(object sender, MouseButtonEventArgs e) => ChooseLibrarySuggestion();

    private void ChooseLibrarySuggestion()
    {
        if (LibrarySuggestionsList.SelectedItem is not LibrarySuggestion suggestion) return;
        LibrarySuggestionsPopup.IsOpen = false;
        if (suggestion.IsFolder)
        {
            selectedLibraryTag = $"folder:{suggestion.Path}";
            selectedFolder = suggestion.Path;
            ExpandFolderPath(suggestion.Path);
            RefreshLibraryTree();
            RevealLibrarySuggestionItem(selectedLibraryTag);
            return;
        }

        selectedLibraryTag = $"template:{suggestion.Path}";
        OpenTemplateFromTree(suggestion.Path);
        if (selectedTemplateName == suggestion.Path) RevealLibrarySuggestionItem(selectedLibraryTag);
    }

    private void RevealLibrarySuggestionItem(string tag) =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (FindTreeItem(tag) is not { } item) return;
            item.IsSelected = true;
            item.BringIntoView();
        }));
}