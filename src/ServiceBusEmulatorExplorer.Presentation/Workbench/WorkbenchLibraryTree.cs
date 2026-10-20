namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>An entry of the library tree: a folder with its children, or a template.</summary>
public abstract record LibraryTreeNode;

/// <summary>A folder in the library tree; its subfolders come first, then its templates.</summary>
public sealed record LibraryFolderNode(string Path, IReadOnlyList<LibraryTreeNode> Children) : LibraryTreeNode
{
    public string Name => LibraryFolders.Name(Path);
}

public sealed record LibraryTemplateNode(PrototypeTemplate Template) : LibraryTreeNode;

/// <summary>
/// What the Message Workbench library tree shows. Without a search every folder and template is shown. A library or
/// namespace search keeps the templates that match and the folders that hold them or that the user created; the open
/// new draft and the template just revealed stay visible.
/// </summary>
public static class WorkbenchLibraryTree
{
    /// <param name="search">The trimmed library search: template names, descriptions and folder segments, ignoring case.</param>
    /// <param name="namespaceQuery">The namespace search; templates must have a destination containing it.</param>
    /// <param name="alwaysShown">Templates shown whatever the search.</param>
    public static IReadOnlyList<LibraryTreeNode> Build(WorkbenchLibrary library, string search, string namespaceQuery,
        Func<PrototypeTemplate, bool> alwaysShown)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(alwaysShown);
        var matchingFolders = search.Length == 0 ? [] : library.Folders.Where(path =>
            path.Split('/').Any(segment => segment.Contains(search, StringComparison.OrdinalIgnoreCase))).ToArray();
        var visible = library.Templates.Where(template =>
            (namespaceQuery.Length == 0 || template.Topic.Length > 0 && template.Topic.Contains(namespaceQuery, StringComparison.OrdinalIgnoreCase)) &&
            (search.Length == 0 || template.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             template.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             matchingFolders.Any(path => LibraryFolders.IsInside(template.Folder, path))) ||
            alwaysShown(template)).ToList();
        bool unfiltered = namespaceQuery.Length == 0 && search.Length == 0;

        bool IsShown(string path) => unfiltered ||
            visible.Any(template => LibraryFolders.IsInside(template.Folder, path)) ||
            library.CreatedFolders.Any(created => LibraryFolders.IsInside(created, path));

        LibraryFolderNode Folder(string path) => new(path, [
            .. library.Folders.Where(candidate =>
                    candidate.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase) &&
                    candidate.Count(ch => ch == '/') == path.Count(ch => ch == '/') + 1 && IsShown(candidate))
                .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
                .Select(Folder),
            .. visible.Where(template => string.Equals(template.Folder, path, StringComparison.OrdinalIgnoreCase))
                .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
                .Select(template => new LibraryTemplateNode(template))]);

        return [
            .. library.Folders.Where(path => !path.Contains('/', StringComparison.Ordinal) && IsShown(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(Folder),
            .. visible.Where(template => string.IsNullOrEmpty(template.Folder)).Select(template => new LibraryTemplateNode(template))];
    }
}
