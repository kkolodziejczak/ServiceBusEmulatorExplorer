using System.Text.Json;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>A message template in the Workbench library: its body, destination, description and folder.</summary>
public sealed record PrototypeTemplate(string Name, string Topic, string Description, string Body,
    string Folder = "Orders", IReadOnlyList<string>? Associations = null, string? FileName = null, EntityKind DestinationKind = EntityKind.Topic);

/// <summary>The message properties and variables a template was last saved with.</summary>
public sealed record PrototypeAuthorSettings(string Subject, int ContentType, string CorrelationId,
    string SessionId, bool CustomMessageId, string CustomMessageIdValue, bool SpecifyTtl,
    string TtlMinutes, string ReplyTo, string ReplySessionId, string PartitionKey,
    IReadOnlyList<PrototypePropertySetting> ApplicationProperties,
    IReadOnlyList<PrototypeVariableSetting> Variables);

public sealed record PrototypePropertySetting(string Name, string Type, string Value);

public sealed record PrototypeVariableSetting(string Name, string Type, string Source, bool HasDefault, string DefaultValue);

/// <summary>
/// The Message Workbench's template library for the session: the templates with the settings each was saved with, the
/// folders, and the folders the user created, which stay visible while a search would hide them. Template names are
/// matched exactly; folder paths ignoring case.
/// </summary>
public sealed class WorkbenchLibrary
{
    private readonly List<PrototypeTemplate> templates;
    private readonly List<string> folders;
    private readonly HashSet<string> createdFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PrototypeAuthorSettings> savedSettings = [];

    public WorkbenchLibrary(IEnumerable<PrototypeTemplate> templates, IEnumerable<string> folders)
    {
        this.templates = [.. templates];
        this.folders = [.. folders];
    }

    /// <summary>The sample library the prototype opens with: order and inventory templates in their two folders.</summary>
    public static WorkbenchLibrary CreateSample() => new(
    [
        new("Order created", "order-events", "Emitted when a new order is created in the retail system.", "{\n  \"eventId\": \"$(EventId)\",\n  \"customerId\": \"$(CustomerId)\",\n  \"amount\": \"$(Amount)\",\n  \"occurredAt\": \"$(OccurredAt)\"\n}"),
        new("Order updated", "order-events", "Emitted when the amount on an order changes.", "{\n  \"eventId\": \"$(EventId)\",\n  \"customerId\": \"$(CustomerId)\",\n  \"amount\": \"$(Amount)\",\n  \"state\": \"Updated\"\n}"),
        new("Order dispatched", "order-events", "Emitted when an order leaves the warehouse.", "{\n  \"eventId\": \"$(EventId)\",\n  \"customerId\": \"$(CustomerId)\",\n  \"state\": \"Dispatched\"\n}"),
        new("Stock reserved", "inventory-events", "Emitted when stock is reserved.", "{\n  \"eventId\": \"$(EventId)\",\n  \"sku\": \"$(CustomerId)\",\n  \"quantity\": \"$(Amount)\"\n}", "Inventory"),
        new("Order reply", "order-replies", "Sample queue reply.", "{\n  \"customerId\": \"$(CustomerId)\",\n  \"accepted\": true\n}", DestinationKind: EntityKind.Queue),
        new("Unassociated sample", "", "Local draft without a discovered destination.", "{\n  \"customerId\": \"$(CustomerId)\",\n  \"amount\": \"$(Amount)\"\n}")
    ], ["Orders", "Inventory"]);

    public IReadOnlyList<PrototypeTemplate> Templates => templates;
    public IReadOnlyList<string> Folders => folders;
    public IReadOnlySet<string> CreatedFolders => createdFolders;
    public IEnumerable<string> TemplateNames => templates.Select(template => template.Name);

    public PrototypeTemplate? Find(string name) => templates.FirstOrDefault(template => template.Name == name);

    /// <summary>The settings <paramref name="name"/> was last saved with.</summary>
    public PrototypeAuthorSettings SettingsOf(string name) => savedSettings[name];

    /// <summary>Gives every template <paramref name="settings"/> as its saved settings.</summary>
    public void SaveSettingsForAll(PrototypeAuthorSettings settings)
    {
        foreach (var template in templates) savedSettings[template.Name] = settings;
    }

    /// <summary><paramref name="requested"/>, or with the first free number appended when a template already has the name in any case.</summary>
    public string UniqueName(string requested)
    {
        string candidate = requested;
        int suffix = 2;
        while (templates.Any(value => value.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = $"{requested} {suffix++}";
        return candidate;
    }

    public void Add(PrototypeTemplate template, PrototypeAuthorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(template);
        templates.Add(template);
        savedSettings[template.Name] = settings;
    }

    /// <summary>Removes a new draft that the user discarded, with its settings.</summary>
    public void Remove(string name)
    {
        templates.RemoveAll(template => template.Name == name);
        savedSettings.Remove(name);
    }

    /// <summary>Saves the body, destination and settings of <paramref name="name"/>; returns <c>false</c> when it no longer exists.</summary>
    public bool Save(string name, string body, string destination, EntityKind destinationKind, PrototypeAuthorSettings settings)
    {
        int index = templates.FindIndex(value => value.Name == name);
        if (index < 0) return false;
        templates[index] = templates[index] with { Body = body, Topic = destination,
            Associations = destination.Length == 0 ? [] : [destination], DestinationKind = destinationKind };
        savedSettings[name] = settings;
        return true;
    }

    /// <summary>
    /// Whether the open template differs from what was saved: its body, destination or settings. A template that no
    /// longer exists has nothing unsaved.
    /// </summary>
    public bool HasUnsavedChanges(string name, string body, string destination, EntityKind destinationKind, PrototypeAuthorSettings settings) =>
        Find(name) is { } template &&
        (template.Body != body || template.Topic != destination || template.DestinationKind != destinationKind ||
            JsonSerializer.Serialize(savedSettings[name]) != JsonSerializer.Serialize(settings));

    /// <summary>Renames a template and moves its saved settings; returns <c>false</c> when it no longer exists.</summary>
    public bool RenameTemplate(string oldName, string newName)
    {
        int index = templates.FindIndex(template => template.Name == oldName);
        if (index < 0) return false;
        templates[index] = templates[index] with { Name = newName };
        if (savedSettings.Remove(oldName, out var settings)) savedSettings[newName] = settings;
        return true;
    }

    public void MoveTemplate(string name, string folder)
    {
        int index = templates.FindIndex(template => template.Name == name);
        templates[index] = templates[index] with { Folder = folder };
    }

    /// <summary>Adds <paramref name="path"/> and each of its parents that the library does not have yet.</summary>
    public void AddFolderAndParents(string path)
    {
        int separator = path.LastIndexOf('/');
        if (separator > 0) AddFolderAndParents(path[..separator]);
        if (!folders.Contains(path, StringComparer.OrdinalIgnoreCase)) folders.Add(path);
    }

    /// <summary>Remembers a folder the user created, so a search does not hide it while it is empty.</summary>
    public void MarkCreated(string path) => createdFolders.Add(path);

    /// <summary>Moves the folder <paramref name="path"/> and everything inside it to <paramref name="replacement"/>.</summary>
    public void RelocateFolder(string path, string replacement)
    {
        foreach (string entry in createdFolders.Where(entry => LibraryFolders.IsInside(entry, path)).ToArray())
        {
            createdFolders.Remove(entry);
            createdFolders.Add(LibraryFolders.Relocated(entry, path, replacement));
        }
        for (int index = 0; index < folders.Count; index++)
            folders[index] = LibraryFolders.Relocated(folders[index], path, replacement);
        for (int index = 0; index < templates.Count; index++)
            templates[index] = templates[index] with { Folder = LibraryFolders.Relocated(templates[index].Folder, path, replacement) };
    }
}
