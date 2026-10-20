using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// The browse scope above the message list (the selected queue, or the topic and subscription) and which namespace
/// entity the tree should show as selected, also while a search replaces the tree.
/// </summary>
public sealed class NamespaceScopeViewModel
{
    private readonly InvestigationWorkspace workspace;

    public NamespaceScopeViewModel(InvestigationWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
    }

    /// <summary>The entity last selected in the namespace tree.</summary>
    public EntityAddress? RememberedSelection { get; private set; }

    /// <summary>The entity the tree should select: while searching the one last selected, otherwise the browsed one.</summary>
    public EntityAddress? SelectionToRestore => workspace.Search.IsActive
        ? RememberedSelection ?? workspace.Browse.SelectedEntity?.Address
        : workspace.Browse.SelectedEntity?.Address;

    /// <summary>The user selected an entity in the namespace tree.</summary>
    public void Remember(EntityAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        RememberedSelection = address;
    }

    /// <summary>The tree selected <paramref name="address"/> again; outside a search it becomes the remembered entity.</summary>
    public void Restored(EntityAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!workspace.Search.IsActive) RememberedSelection = address;
    }

    /// <summary>
    /// The scope header for the browsed entity. While searching, in the Message Library or without a browsed entity the
    /// list shows its breadcrumb instead.
    /// </summary>
    public ScopeHeaderState HeaderFor(bool libraryVisible)
    {
        EntityNode? scope = workspace.Browse.SelectedEntity;
        if (workspace.Search.IsActive || libraryVisible || scope?.Address is not { } address)
            return new(false, workspace.Surface.EntityPath, null, null, false, false);
        if (scope.Kind == nameof(EntityKind.Subscription))
        {
            EntityNode? topic = workspace.Browse.AllEntities().FirstOrDefault(node =>
                node.Kind == nameof(EntityKind.Topic) && string.Equals(node.Name, address.TopicName, StringComparison.OrdinalIgnoreCase));
            return new(true, $"Topic: {address.TopicName}; Subscription: {scope.Name}", topic, scope, false, true);
        }
        if (scope.Kind == nameof(EntityKind.Topic))
            return new(true, $"Topic: {scope.Name}; All subscriptions", scope, null, true, false);
        return new(true, $"Queue: {scope.Name}", null, scope, false, false);
    }

    /// <summary>The nodes from a root down to the entity at <paramref name="address"/>, or <c>null</c> when it is not listed.</summary>
    public static IReadOnlyList<EntityNode>? PathTo(IEnumerable<EntityNode> roots, EntityAddress address)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(address);
        var path = new List<EntityNode>();
        return FindPath(roots, address, path) ? path : null;
    }

    private static bool FindPath(IEnumerable<EntityNode> nodes, EntityAddress address, List<EntityNode> path)
    {
        foreach (EntityNode node in nodes)
        {
            path.Add(node);
            if (node.Address == address || FindPath(node.Children, address, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }
}

/// <summary>
/// The scope header: whether it shows (otherwise the breadcrumb does), its accessible name, the topic line, the entity
/// line, whether the topic covers all subscriptions, and whether the entity line is indented under its topic.
/// </summary>
public sealed record ScopeHeaderState(bool IsShown, string AutomationName, EntityNode? Topic, EntityNode? Entity,
    bool ShowsAllSubscriptions, bool IndentsEntity);
