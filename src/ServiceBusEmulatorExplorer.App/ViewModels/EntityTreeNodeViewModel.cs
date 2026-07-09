using System.Collections.ObjectModel;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.ViewModels;

public sealed class EntityTreeNodeViewModel
{
    private EntityTreeNodeViewModel(
        string displayName,
        string countsSummary,
        string iconText,
        string iconBrush,
        ServiceBusEntityNode? entity,
        IEnumerable<EntityTreeNodeViewModel>? children = null)
    {
        DisplayName = displayName;
        CountsSummary = countsSummary;
        IconText = iconText;
        IconBrush = iconBrush;
        Entity = entity;
        Children = new ObservableCollection<EntityTreeNodeViewModel>(children ?? []);
    }

    public string DisplayName { get; }

    public string CountsSummary { get; }

    public string IconText { get; }

    public string IconBrush { get; }

    public ServiceBusEntityNode? Entity { get; }

    public ObservableCollection<EntityTreeNodeViewModel> Children { get; }

    public static IReadOnlyList<EntityTreeNodeViewModel> CreateTree(
        IEnumerable<ServiceBusEntityNode> entities,
        string filter = "")
    {
        var entityList = entities.ToList();
        var displayedEntities = FilterEntities(entityList, filter);
        var queues = CreateQueueNodes(displayedEntities);
        var topics = CreateTopicNodes(displayedEntities);

        return
        [
            new(
                "sb://localhost/",
                "",
                "N",
                "#2563EB",
                entity: null,
                [
                    new("Queues", "", "G", "#64748B", entity: null, queues),
                    new("Topics", "", "G", "#64748B", entity: null, topics)
                ])
        ];
    }

    private static IReadOnlyList<ServiceBusEntityNode> FilterEntities(
        IReadOnlyList<ServiceBusEntityNode> entities,
        string filter)
    {
        string normalizedFilter = filter.Trim();
        if (normalizedFilter.Length == 0)
        {
            return entities;
        }

        var matches = entities
            .Where(entity => EntityMatchesFilter(entity, normalizedFilter))
            .ToHashSet();

        AddParentsForMatchingSubscriptions(entities, matches);
        AddSubscriptionsForMatchingTopics(entities, matches);

        return EntityTreeBuilder.SortForNavigation(matches);
    }

    private static bool EntityMatchesFilter(ServiceBusEntityNode entity, string filter)
    {
        return ContainsFilter(entity.Name, filter)
            || ContainsFilter(entity.TopicName, filter)
            || ContainsFilter(entity.Metadata.Path, filter);
    }

    private static bool ContainsFilter(string? value, string filter)
    {
        return value?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void AddParentsForMatchingSubscriptions(
        IReadOnlyList<ServiceBusEntityNode> entities,
        HashSet<ServiceBusEntityNode> matches)
    {
        foreach (ServiceBusEntityNode subscription in matches.Where(entity => entity.Kind == EntityKind.Subscription).ToList())
        {
            ServiceBusEntityNode? topic = entities.FirstOrDefault(entity =>
                entity.Kind == EntityKind.Topic
                && string.Equals(entity.Name, subscription.TopicName, StringComparison.OrdinalIgnoreCase));

            if (topic is not null)
            {
                matches.Add(topic);
            }
        }
    }

    private static void AddSubscriptionsForMatchingTopics(
        IReadOnlyList<ServiceBusEntityNode> entities,
        HashSet<ServiceBusEntityNode> matches)
    {
        foreach (ServiceBusEntityNode topic in matches.Where(entity => entity.Kind == EntityKind.Topic).ToList())
        {
            foreach (ServiceBusEntityNode subscription in entities.Where(entity =>
                entity.Kind == EntityKind.Subscription
                && string.Equals(entity.TopicName, topic.Name, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(subscription);
            }
        }
    }

    private static IReadOnlyList<EntityTreeNodeViewModel> CreateQueueNodes(IReadOnlyList<ServiceBusEntityNode> entities)
    {
        return entities
            .Where(entity => entity.Kind == EntityKind.Queue)
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entity => CreateEntityNode(entity))
            .ToList();
    }

    private static IReadOnlyList<EntityTreeNodeViewModel> CreateTopicNodes(IReadOnlyList<ServiceBusEntityNode> entities)
    {
        return entities
            .Where(entity => entity.Kind == EntityKind.Topic)
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .Select(topic => CreateTopicNode(topic, entities))
            .ToList();
    }

    private static EntityTreeNodeViewModel CreateTopicNode(
        ServiceBusEntityNode topic,
        IReadOnlyList<ServiceBusEntityNode> entities)
    {
        IReadOnlyList<EntityTreeNodeViewModel> subscriptions = entities
            .Where(entity => entity.Kind == EntityKind.Subscription && string.Equals(entity.TopicName, topic.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entity => CreateEntityNode(entity))
            .ToList();

        return CreateEntityNode(topic, subscriptions);
    }

    private static EntityTreeNodeViewModel CreateEntityNode(
        ServiceBusEntityNode entity,
        IEnumerable<EntityTreeNodeViewModel>? children = null)
    {
        return new EntityTreeNodeViewModel(
            entity.Name,
            CreateCountsSummary(entity),
            CreateIconText(entity.Kind),
            CreateIconBrush(entity.Kind),
            entity,
            children);
    }

    private static string CreateCountsSummary(ServiceBusEntityNode entity)
    {
        return $"{entity.Counts.ActiveMessageCount} / {entity.Counts.DeadLetterMessageCount}";
    }

    private static string CreateIconText(EntityKind kind)
    {
        return kind switch
        {
            EntityKind.Queue => "Q",
            EntityKind.Topic => "T",
            EntityKind.Subscription => "S",
            _ => "E"
        };
    }

    private static string CreateIconBrush(EntityKind kind)
    {
        return kind switch
        {
            EntityKind.Queue => "#107C10",
            EntityKind.Topic => "#C2410C",
            EntityKind.Subscription => "#6D28D9",
            _ => "#64748B"
        };
    }
}
