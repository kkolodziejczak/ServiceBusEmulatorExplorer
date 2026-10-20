using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// Marks each namespace entity with its Watch indicators and its refresh indicator: running or paused while it is
/// watched or is the entity on screen and automatic refresh is on, with a tooltip saying what refreshes.
/// </summary>
public static class NamespaceIndicators
{
    public static void Apply(IEnumerable<EntityNode> nodes, IEnumerable<WatchTarget> targets, NamespaceRefresh refresh)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(refresh);
        var watched = targets.Select(target => (target.Address, target.Bucket)).ToHashSet();
        bool showRefresh = refresh.Connected && refresh.AutoRefreshSeconds > 0;
        string bucket = refresh.DeadLetterView ? "Dead letter" : "Active";
        foreach (var node in nodes)
        {
            bool watchable = node.Address is { Kind: EntityKind.Queue or EntityKind.Subscription };
            bool active = watchable && watched.Contains((node.Address!, MessageBucket.Active));
            bool deadLetter = watchable && watched.Contains((node.Address!, MessageBucket.DeadLetter));
            bool topicActive = node.Kind == nameof(EntityKind.Topic) && watched.Any(target => target.Address.TopicName == node.Name && target.Bucket == MessageBucket.Active);
            bool topicDlq = node.Kind == nameof(EntityKind.Topic) && watched.Any(target => target.Address.TopicName == node.Name && target.Bucket == MessageBucket.DeadLetter);
            bool background = active || deadLetter || topicActive || topicDlq;
            bool current = node.Address is not null && node.Address == refresh.Current;
            bool refreshing = showRefresh && (background || current);
            string indicator = refreshing ? refresh.Paused ? "Paused" : "Running" : "None";
            string scope = background
                ? $"Background: {((active || topicActive) && (deadLetter || topicDlq) ? "Active and Dead letter" : active || topicActive ? "Active" : "Dead letter")}" + (node.Kind == nameof(EntityKind.Topic) ? " watched subscriptions." : ".")
                : $"Current {bucket} view.";
            string detail = !refreshing ? "" :
                $"{(refresh.Paused ? "Auto-refresh paused" : "Refresh")}: Every {refresh.AutoRefreshSeconds} seconds\n{scope} Watch remains independent.";
            node.SetIndicators(active, deadLetter, refresh.Connected, indicator, detail);
        }
    }
}

/// <summary>
/// What the refresh indicators depend on: the connection, the automatic refresh interval (0 when off), whether it is
/// paused, whether the dead-letter view is browsed, and the entity on screen (<c>null</c> while searching or in the
/// Message Library).
/// </summary>
public sealed record NamespaceRefresh(bool Connected, int AutoRefreshSeconds, bool Paused, bool DeadLetterView, EntityAddress? Current);
