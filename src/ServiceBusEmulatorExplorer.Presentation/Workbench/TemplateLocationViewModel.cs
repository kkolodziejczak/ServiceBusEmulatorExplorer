namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>A folder the user can pick: its path ("" for the root) and how the list shows it.</summary>
public sealed record FolderChoice(string Path, string Label);

/// <summary>
/// Where a template is saved or moved to: the folder choices (the root first, then every folder by path) and, for a
/// save, the template's name. The caller checks the destination; this rejects an empty name first.
/// </summary>
public sealed class TemplateLocationViewModel
{
    private readonly Func<string, string, string?> validate;

    /// <param name="validate">Why the trimmed name cannot go to the folder path, or <c>null</c> when it can.</param>
    public TemplateLocationViewModel(IEnumerable<string> folders, string selectedFolder, Func<string, string, string?> validate)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(validate);
        this.validate = validate;
        Choices = [new FolderChoice("", "Root"), .. folders
            .Where(path => path.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new FolderChoice(path, path.Replace("/", " / ")))];
        InitialChoice = Choices.FirstOrDefault(choice => choice.Path.Equals(selectedFolder, StringComparison.OrdinalIgnoreCase)) ?? Choices[0];
    }

    public IReadOnlyList<FolderChoice> Choices { get; }

    /// <summary>The folder chosen when the dialog opens: the given one, or the root when the library does not have it.</summary>
    public FolderChoice InitialChoice { get; }

    /// <summary>Why the location cannot be confirmed, or <c>null</c> when it can.</summary>
    public string? Problem(string name, FolderChoice? folder)
    {
        string trimmed = name.Trim();
        return trimmed.Length == 0 ? "Enter a name." : validate(trimmed, folder?.Path ?? "");
    }
}
