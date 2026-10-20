namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>
/// The Message Workbench library's folder rules. Folders are paths such as "Orders/Returns", compared ignoring case;
/// library items are tagged "folder:&lt;path&gt;" or "template:&lt;name&gt;".
/// </summary>
public static class LibraryFolders
{
    public const string FolderTag = "folder:";
    public const string TemplateTag = "template:";

    public static string Parent(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";

    public static string Name(string path) => path.Split('/')[^1];

    public static string Join(string parent, string child) => parent.Length == 0 ? child : parent + "/" + child;

    /// <summary>The path of <paramref name="path"/> after renaming its last segment to <paramref name="name"/>.</summary>
    public static string Renamed(string path, string name) => Parent(path) is { Length: > 0 } parent ? $"{parent}/{name}" : name;

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or lies inside it.</summary>
    public static bool IsInside(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where <paramref name="path"/> ends up when the folder <paramref name="from"/> moves to <paramref name="to"/>.</summary>
    public static string Relocated(string path, string from, string to) => IsInside(path, from) ? to + path[from.Length..] : path;

    /// <summary>
    /// Why the library item <paramref name="tag"/> cannot move to <paramref name="destination"/> ("" for the root), or
    /// <c>null</c> when it can.
    /// </summary>
    public static string? MoveProblem(string tag, string destination, IReadOnlyCollection<string> folders, IEnumerable<string> templateNames)
    {
        if (destination.Length > 0 && !folders.Contains(destination, StringComparer.OrdinalIgnoreCase)) return "Choose an existing folder.";
        if (tag.StartsWith(TemplateTag, StringComparison.Ordinal))
            return templateNames.Contains(tag[TemplateTag.Length..]) ? null : "The template is no longer available.";
        if (!tag.StartsWith(FolderTag, StringComparison.Ordinal) || !folders.Contains(tag[FolderTag.Length..], StringComparer.OrdinalIgnoreCase))
            return "The folder is no longer available.";
        string source = tag[FolderTag.Length..];
        if (IsInside(destination, source))
            return "A folder cannot be moved inside itself or its descendants.";
        string replacement = Join(destination, Name(source));
        if (!replacement.Equals(source, StringComparison.OrdinalIgnoreCase) && folders.Contains(replacement, StringComparer.OrdinalIgnoreCase))
            return "A folder with that name already exists here.";
        return null;
    }

    /// <summary>Why the library item <paramref name="tag"/> cannot be renamed to the trimmed <paramref name="name"/>, or <c>null</c> when it can.</summary>
    public static string? RenameProblem(string tag, string name, IEnumerable<string> folders, IEnumerable<string> templateNames)
    {
        bool folder = tag.StartsWith(FolderTag, StringComparison.Ordinal);
        string? problem;
        if (folder)
        {
            string path = tag[FolderTag.Length..];
            problem = folders.Any(other => !other.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                Parent(other).Equals(Parent(path), StringComparison.OrdinalIgnoreCase) && Name(other).Equals(name, StringComparison.OrdinalIgnoreCase))
                ? "A folder with that name already exists here." : null;
            if (name.Contains('/') || name.Contains('\\')) problem = "Use a folder name without path separators.";
        }
        else
        {
            string oldName = tag[TemplateTag.Length..];
            problem = templateNames.Any(other => !other.Equals(oldName, StringComparison.OrdinalIgnoreCase) && other.Equals(name, StringComparison.OrdinalIgnoreCase))
                ? "A template with that name already exists here." : null;
        }
        return name.Length == 0 || problem is not null ? problem ?? "Enter a name." : null;
    }

    /// <summary>Whether <paramref name="name"/> can be a new folder in <paramref name="parent"/>: unique there and without path separators.</summary>
    public static bool IsNewFolderName(string parent, string name, IEnumerable<string> folders) =>
        !name.Contains('/') && !name.Contains('\\') && !folders.Any(folder =>
            Parent(folder).Equals(parent, StringComparison.OrdinalIgnoreCase) && Name(folder).Equals(name, StringComparison.OrdinalIgnoreCase));
}
