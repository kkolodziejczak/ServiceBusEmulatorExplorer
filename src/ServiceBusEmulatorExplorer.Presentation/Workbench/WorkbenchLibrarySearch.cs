namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>A template as the library search sees it: its name, description, folder path and destination.</summary>
public sealed record LibraryEntry(string Name, string Description, string Folder, string Destination);

/// <summary>A template or folder the library search suggests, grouped under TEMPLATES or FOLDERS.</summary>
public sealed record LibrarySuggestion(string Group, string Title, string Detail, string Path, bool IsFolder)
{
    public string TypeLabel => IsFolder ? "Folder" : "Template";
    public string AccessibleName => IsFolder
        ? $"Folder, {Title}, {Detail}"
        : $"Template, {Title}, folder {Detail}";
    public string Icon => IsFolder
        ? "M1,4 H7 L9,6 H17 V16 H1 Z"
        : "M3,1 H13 L17,5 V19 H3 Z M6,8 H14 M6,11 H14 M6,14 H12";
}

/// <summary>
/// The Message Workbench library search: templates whose name, description or folder match, and folders with a
/// matching segment, limited to templates whose destination matches the namespace search and the folders that hold
/// them or that the user created.
/// </summary>
public static class WorkbenchLibrarySearch
{
    /// <summary>The most suggestions shown in each group.</summary>
    public const int MaximumPerGroup = 5;

    /// <param name="query">The trimmed, non-empty library search text.</param>
    /// <param name="namespaceQuery">The namespace search text; templates must have a destination containing it.</param>
    /// <param name="folders">The library's folders, then <paramref name="createdFolders"/>, the folders the user created.</param>
    public static IReadOnlyList<LibrarySuggestion> Suggest(string query, string namespaceQuery, IEnumerable<LibraryEntry> templates,
        IEnumerable<string> folders, IReadOnlySet<string> createdFolders)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(createdFolders);
        var eligibleTemplates = templates.Where(template =>
            namespaceQuery.Length == 0 ||
            template.Destination.Length > 0 && template.Destination.Contains(namespaceQuery, StringComparison.OrdinalIgnoreCase)).ToArray();
        return eligibleTemplates
            .Where(template => template.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                template.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                template.Folder.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(MaximumPerGroup)
            .Select(template => new LibrarySuggestion(
                "TEMPLATES", template.Name, template.Folder.Length == 0 ? "Root" : template.Folder, template.Name, false))
            .Concat(folders.Concat(createdFolders)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => path.Split('/').Any(segment => segment.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Where(path => createdFolders.Contains(path) || eligibleTemplates.Any(template =>
                    template.Folder.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                    template.Folder.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(MaximumPerGroup)
                .Select(path => new LibrarySuggestion("FOLDERS", path.Split('/')[^1], path, path, true)))
            .ToList();
    }

    /// <summary>
    /// The suggestion the arrow keys move to: the first or last when none is selected, otherwise the next or previous,
    /// wrapping around.
    /// </summary>
    public static int NextIndex(int selected, int count, bool down) => selected < 0
        ? down ? 0 : count - 1
        : (selected + (down ? 1 : count - 1)) % count;
}
