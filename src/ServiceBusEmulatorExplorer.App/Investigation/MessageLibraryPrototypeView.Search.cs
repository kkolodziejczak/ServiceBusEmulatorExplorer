using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeView
{
    private const int MaximumLibrarySuggestionsPerGroup = 5;

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

        var eligibleTemplates = templates.Where(template =>
            namespaceQuery.Length == 0 || TemplateAssociations(template).Any(path =>
                path.Contains(namespaceQuery, StringComparison.OrdinalIgnoreCase))).ToArray();
        var items = eligibleTemplates
            .Where(template => template.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                template.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                template.Folder.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(MaximumLibrarySuggestionsPerGroup)
            .Select(template => new LibrarySuggestion(
                "TEMPLATES", template.Name, template.Folder.Length == 0 ? "Root" : template.Folder,
                template.Name, false))
            .Concat(folders.Concat(createdFolders)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => path.Split('/').Any(segment => segment.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Where(path => createdFolders.Contains(path) || eligibleTemplates.Any(template =>
                    template.Folder.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                    template.Folder.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(MaximumLibrarySuggestionsPerGroup)
                .Select(path => new LibrarySuggestion("FOLDERS", FolderName(path), path, path, true)))
            .ToList();

        var view = new ListCollectionView(items);
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
                LibrarySuggestionsList.SelectedIndex = LibrarySuggestionsList.SelectedIndex < 0
                    ? e.Key == Key.Down ? 0 : count - 1
                    : (LibrarySuggestionsList.SelectedIndex + (e.Key == Key.Down ? 1 : count - 1)) % count;
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

    private sealed record LibrarySuggestion(string Group, string Title, string Detail, string Path, bool IsFolder)
    {
        public string TypeLabel => IsFolder ? "Folder" : "Template";
        public string AccessibleName => IsFolder
            ? $"Folder, {Title}, {Detail}"
            : $"Template, {Title}, folder {Detail}";
        public string Icon => IsFolder
            ? "M1,4 H7 L9,6 H17 V16 H1 Z"
            : "M3,1 H13 L17,5 V19 H3 Z M6,8 H14 M6,11 H14 M6,14 H12";
    }
}