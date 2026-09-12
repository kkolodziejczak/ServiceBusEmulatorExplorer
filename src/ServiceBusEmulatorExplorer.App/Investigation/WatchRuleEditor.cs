using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed class WatchRuleEditor
{
    private readonly EntityNode[] roots;
    private readonly List<WatchPreference> rules;
    public IReadOnlyList<WatchPreference> Rules => rules.ToArray();
    public bool GlobalActive => Field(WatchScopeResolver.ConnectionScopeKey, false) == true;
    public bool GlobalDeadLetter => Field(WatchScopeResolver.ConnectionScopeKey, true) == true;
    public IReadOnlyList<WatchTarget> Targets => new WatchScopeResolver().Resolve(rules,
        new(roots.SelectMany(Flatten).Where(node => node.Observation is not null).Select(node => node.Observation!).ToArray(),
            DateTimeOffset.UtcNow, true, []));

    public WatchRuleEditor(IEnumerable<EntityNode> roots, IReadOnlyList<WatchPreference> rules)
    {
        this.roots = roots.ToArray();
        this.rules = rules.ToList();
    }

    private bool? Field(string key, bool deadLetter) => rules.Where(rule => rule.ScopeKey == key)
        .Select(rule => deadLetter ? rule.DeadLetter : rule.Active).LastOrDefault(value => value is not null);

    public void SetGlobal(bool deadLetter, bool enabled)
    {
        Rewrite(rule => rule.ScopeKey == WatchScopeResolver.ConnectionScopeKey
            || enabled && (deadLetter ? rule.DeadLetter : rule.Active) == false
            ? Bucket(rule, deadLetter, null) : rule);
        rules.Add(Bucket(new(WatchScopeResolver.ConnectionScopeKey, null, null), deadLetter, enabled));
    }

    private void Rewrite(Func<WatchPreference, WatchPreference> change)
    {
        for (int index = 0; index < rules.Count; index++) rules[index] = change(rules[index]);
        rules.RemoveAll(rule => rule.Active is null && rule.DeadLetter is null && rule.Included is null);
    }

    private static WatchPreference Bucket(WatchPreference rule, bool deadLetter, bool? value) =>
        deadLetter ? rule with { DeadLetter = value } : rule with { Active = value };

    public void SetScope(EntityNode node, bool deadLetter, bool enabled)
    {
        string key = Key(node);
        Rewrite(rule => IsWithin(rule.ScopeKey, key) ? Bucket(rule, deadLetter, null) : rule);
        rules.Add(Bucket(new(key, null, null), deadLetter, enabled));
    }

    public static string Key(EntityNode node) => node.IsGroup
        ? node.Kind == "QueueGroup" || node.Name == "Queues" ? WatchScopeResolver.QueueCategoryScopeKey : WatchScopeResolver.TopicCategoryScopeKey
        : node.Kind switch
        {
            "Queue" => WatchScopeResolver.QueueScopeKey(node.Name),
            "Topic" => WatchScopeResolver.TopicScopeKey(node.Name),
            "Subscription" => WatchScopeResolver.SubscriptionScopeKey(node.Address!.TopicName!, node.Name),
            _ => throw new ArgumentException("Unknown Watch entity.", nameof(node))
        };

    private static bool IsWithin(string candidate, string branch) => candidate == branch
        || branch == WatchScopeResolver.ConnectionScopeKey
        || branch == WatchScopeResolver.QueueCategoryScopeKey && candidate.StartsWith("queue:", StringComparison.Ordinal)
        || branch == WatchScopeResolver.TopicCategoryScopeKey && (candidate.StartsWith("topic:", StringComparison.Ordinal) || candidate.StartsWith("subscription:", StringComparison.Ordinal))
        || branch.StartsWith("topic:", StringComparison.Ordinal)
            && candidate.StartsWith("subscription:", StringComparison.Ordinal)
            && candidate.LastIndexOf('/') is var separator && separator >= "subscription:".Length
            && string.Equals(candidate["subscription:".Length..separator], branch[6..], StringComparison.Ordinal);

    public void SetIncluded(EntityNode node, bool included)
    {
        string key = Key(node);
        Rewrite(rule => !IsWithin(rule.ScopeKey, key) ? rule : rule with
        {
            Included = null,
            Active = included && rule.Active == false ? null : rule.Active,
            DeadLetter = included && rule.DeadLetter == false ? null : rule.DeadLetter
        });
        rules.Add(new(key, null, null, included));
    }

    public bool? GetIncludedState(EntityNode node)
    {
        bool[] states = Flatten(node).Where(child => child.Children.Count == 0).Select(IsIncluded).Distinct().ToArray();
        return states.Length == 1 ? states[0] : null;
    }

    private bool IsIncluded(EntityNode node)
    {
        var ancestors = new[] { WatchScopeResolver.ConnectionScopeKey }.Concat(roots.SelectMany(Flatten)
            .Where(parent => Flatten(parent).Contains(node)).Select(Key));
        bool included = true;
        foreach (string key in ancestors)
            included = rules.Where(rule => rule.ScopeKey == key).Select(rule => rule.Included)
                .LastOrDefault(value => value is not null) ?? included;
        return included;
    }

    public bool IsWatched(EntityNode node, bool deadLetter)
    {
        var addresses = Flatten(node).Where(child => child.Address is not null).Select(child => child.Address).ToHashSet();
        return Targets.Any(target => addresses.Contains(target.Address)
            && target.Bucket == (deadLetter ? MessageBucket.DeadLetter : MessageBucket.Active));
    }

    public static IEnumerable<EntityNode> Flatten(EntityNode node) => new[] { node }.Concat(node.Children.SelectMany(Flatten));
}
