using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

/// <summary>One receiving source and bucket selected for Watch.</summary>
public sealed record WatchTarget(EntityAddress Address, MessageBucket Bucket);

/// <summary>
/// Resolves persisted Watch rules against the current discovery snapshot.
/// Rules inherit from the connection to a category, topic, and receiving leaf.
/// Untagged legacy path keys are ignored; callers should use the key builders below.
/// </summary>
public sealed class WatchScopeResolver
{
    /// <summary>The persisted key used for the connection-wide Watch rule.</summary>
    public const string ConnectionScopeKey = "connection:*";

    public const string QueueCategoryScopeKey = "category:queues";
    public const string TopicCategoryScopeKey = "category:topics";

    public static string CategoryScopeKey(EntityKind kind) => kind switch
    {
        EntityKind.Queue => QueueCategoryScopeKey,
        EntityKind.Subscription => TopicCategoryScopeKey,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Watch categories apply to receiving entities.")
    };

    public static string QueueScopeKey(string queueName) => Tagged("queue", queueName);

    public static string TopicScopeKey(string topicName) => Tagged("topic", topicName);

    public static string SubscriptionScopeKey(string topicName, string subscriptionName)
        => $"subscription:{RequireName(topicName)}/{RequireName(subscriptionName)}";

    public IReadOnlyList<WatchTarget> Resolve(
        IReadOnlyList<WatchPreference> preferences,
        EntityDiscoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(snapshot);

        var rules = preferences
            .Where(rule => rule is not null && !string.IsNullOrWhiteSpace(rule.ScopeKey))
            .ToArray();
        var targets = new List<WatchTarget>();
        var seen = new HashSet<WatchTarget>();

        foreach (EntityObservation observation in snapshot.Entities)
        {
            EntityAddress? address = CreateReceivingAddress(observation.Entity);
            if (address is null)
            {
                continue;
            }

            string categoryKey = CategoryScopeKey(address.Kind);
            string leafKey = address.Kind == EntityKind.Queue
                ? QueueScopeKey(address.Name)
                : SubscriptionScopeKey(address.TopicName!, address.Name);
            string? topicKey = address.TopicName is null ? null : TopicScopeKey(address.TopicName);
            if (!IsIncluded(rules, categoryKey, topicKey, leafKey)) continue;

            AddTargetIfWatched(address, MessageBucket.Active, rules, categoryKey, topicKey, leafKey, targets, seen);
            AddTargetIfWatched(address, MessageBucket.DeadLetter, rules, categoryKey, topicKey, leafKey, targets, seen);
        }

        return targets;
    }

    private static bool IsIncluded(IReadOnlyList<WatchPreference> rules,
        string categoryKey, string? topicKey, string leafKey)
    {
        bool included = true;
        foreach (string? key in new[] { ConnectionScopeKey, categoryKey, topicKey, leafKey })
        {
            if (key is null) continue;
            for (int index = rules.Count - 1; index >= 0; index--)
            {
                if (string.Equals(rules[index].ScopeKey, key, StringComparison.Ordinal)
                    && rules[index].Included is bool value)
                {
                    included = value;
                    break;
                }
            }
        }
        return included;
    }

    private static void AddTargetIfWatched(
        EntityAddress address,
        MessageBucket bucket,
        IReadOnlyList<WatchPreference> rules,
        string categoryKey,
        string? topicKey,
        string leafKey,
        ICollection<WatchTarget> targets,
        ISet<WatchTarget> seen)
    {
        bool enabled = ResolveBucket(rules, bucket, categoryKey, topicKey, leafKey);
        if (enabled && seen.Add(new WatchTarget(address, bucket)))
        {
            targets.Add(new WatchTarget(address, bucket));
        }
    }

    private static bool ResolveBucket(
        IReadOnlyList<WatchPreference> rules,
        MessageBucket bucket,
        string categoryKey,
        string? topicKey,
        string leafKey)
    {
        bool? value = FindBucketValue(rules, bucket, [ConnectionScopeKey]);
        value = FindBucketValue(rules, bucket, [categoryKey]) ?? value;
        if (topicKey is not null)
        {
            value = FindBucketValue(rules, bucket, [topicKey]) ?? value;
        }

        value = FindBucketValue(rules, bucket, [leafKey]) ?? value;
        return value == true;
    }

    private static bool? FindBucketValue(
        IReadOnlyList<WatchPreference> rules,
        MessageBucket bucket,
        IReadOnlyList<string> scopeKeys)
    {
        for (int index = rules.Count - 1; index >= 0; index--)
        {
            WatchPreference rule = rules[index];
            if (!scopeKeys.Contains(rule.ScopeKey, StringComparer.Ordinal))
            {
                continue;
            }

            bool? value = bucket == MessageBucket.Active ? rule.Active : rule.DeadLetter;
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static EntityAddress? CreateReceivingAddress(DiscoveredEntity entity)
    {
        return entity.Kind switch
        {
            EntityKind.Queue when !string.IsNullOrWhiteSpace(entity.Name)
                => new EntityAddress(EntityKind.Queue, entity.Name),
            EntityKind.Subscription when !string.IsNullOrWhiteSpace(entity.Name)
                && !string.IsNullOrWhiteSpace(entity.TopicName)
                => new EntityAddress(EntityKind.Subscription, entity.Name, entity.TopicName),
            _ => null
        };
    }

    private static string Tagged(string tag, string value)
    {
        return $"{tag}:{RequireName(value)}";
    }

    private static string RequireName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
