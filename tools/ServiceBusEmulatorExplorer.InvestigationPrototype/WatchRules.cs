namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

// Rules target paths, so global and topic watches also cover newly discovered entities.
public sealed class WatchRules
{
    private readonly Dictionary<(string Path, bool DeadLetter), bool> scopes = [];
    private readonly Dictionary<string, bool> inclusions = new(StringComparer.Ordinal);
    public bool GlobalActive { get; private set; }
    public bool GlobalDeadLetter { get; private set; }

    public void SetGlobal(bool deadLetter, bool enabled)
    {
        if (deadLetter) GlobalDeadLetter = enabled;
        else GlobalActive = enabled;
        if (enabled)
            foreach (var scope in scopes.Where(pair => pair.Key.DeadLetter == deadLetter && !pair.Value).Select(pair => pair.Key).ToArray())
                scopes.Remove(scope);
    }

    // Explicit bucket choices apply within the included entity tree.
    public void SetScope(string path, bool deadLetter, bool enabled) => scopes[(path, deadLetter)] = enabled;

    public void SetIncluded(string path, bool included, IEnumerable<EntityNode> roots)
    {
        var node = roots.SelectMany(PrototypeData.Flatten).FirstOrDefault(node => node.Path == path);
        if (node is null) return;
        // Selecting a branch applies to its whole current subtree and establishes future inheritance.
        foreach (var descendant in PrototypeData.Flatten(node))
        {
            inclusions.Remove(descendant.Path);
            if (included)
                foreach (var scope in scopes.Where(pair => pair.Key.Path == descendant.Path && !pair.Value).Select(pair => pair.Key).ToArray())
                    scopes.Remove(scope);
        }
        inclusions[path] = included;
    }

    public bool IsIncluded(string path, IEnumerable<EntityNode> roots)
    {
        var ancestors = roots.SelectMany(PrototypeData.Flatten)
            .Where(node => PrototypeData.Flatten(node).Any(descendant => descendant.Path == path)).ToArray();
        if (ancestors.Length == 0) return false;
        // Flatten emits ancestors first, so the most specific explicit rule wins.
        return ancestors.Where(node => inclusions.ContainsKey(node.Path)).Select(node => inclusions[node.Path]).LastOrDefault(true);
    }

    public bool? GetIncludedState(string path, IEnumerable<EntityNode> roots)
    {
        var snapshot = roots.ToArray();
        var node = snapshot.SelectMany(PrototypeData.Flatten).FirstOrDefault(node => node.Path == path);
        if (node is null) return false;
        var leaves = PrototypeData.Flatten(node).Where(child => child.Children.Count == 0).ToArray();
        var states = leaves.Select(child => IsIncluded(child.Path, snapshot)).Distinct().ToArray();
        return states.Length == 1 ? states[0] : null;
    }

    public bool IsWatched(string entityPath, bool deadLetter, IEnumerable<EntityNode> roots)
    {
        var snapshot = roots.ToArray();
        var nodes = snapshot.SelectMany(PrototypeData.Flatten).ToArray();
        var entity = nodes.FirstOrDefault(node => node.Path == entityPath && !node.IsGroup);
        if (entity is null || entity.Kind == "Topic") return false;
        var topic = nodes.FirstOrDefault(node => node.Kind == "Topic" && node.Children.Contains(entity));
        if (!IsIncluded(entityPath, snapshot)) return false;
        if (scopes.TryGetValue((entityPath, deadLetter), out var direct)) return direct;
        if (topic is not null && scopes.TryGetValue((topic.Path, deadLetter), out var inherited)) return inherited;
        return deadLetter ? GlobalDeadLetter : GlobalActive;
    }

    public IReadOnlyList<(string Path, bool DeadLetter)> EffectiveLocations(IEnumerable<EntityNode> roots)
    {
        var snapshot = roots.ToArray();
        return snapshot.SelectMany(PrototypeData.Flatten)
            .Where(node => !node.IsGroup && node.Kind is "Queue" or "Subscription")
            .SelectMany(node => new[] { (Path: node.Path, DeadLetter: false), (Path: node.Path, DeadLetter: true) })
            .Where(location => IsWatched(location.Path, location.DeadLetter, snapshot)).Distinct().ToArray();
    }

    public void Clear()
    {
        scopes.Clear();
        inclusions.Clear();
        GlobalActive = false;
        GlobalDeadLetter = false;
    }

    public WatchRulesSnapshot Capture() => new(GlobalActive, GlobalDeadLetter,
        scopes.Select(pair => new WatchScopeRule(pair.Key.Path, pair.Key.DeadLetter, pair.Value)).ToList(),
        inclusions.Select(pair => new WatchInclusionRule(pair.Key, pair.Value)).ToList());

    public void Restore(WatchRulesSnapshot snapshot)
    {
        Clear();
        GlobalActive = snapshot.GlobalActive;
        GlobalDeadLetter = snapshot.GlobalDeadLetter;
        foreach (var rule in snapshot.Scopes ?? [])
            if (rule is not null && !string.IsNullOrWhiteSpace(rule.Path)) scopes[(rule.Path, rule.DeadLetter)] = rule.Enabled;
        foreach (var rule in snapshot.Inclusions ?? [])
            if (rule is not null && !string.IsNullOrWhiteSpace(rule.Path)) inclusions[rule.Path] = rule.Included;
    }
}

public sealed record WatchRulesSnapshot(bool GlobalActive, bool GlobalDeadLetter, List<WatchScopeRule> Scopes, List<WatchInclusionRule> Inclusions);
public sealed record WatchScopeRule(string Path, bool DeadLetter, bool Enabled);
public sealed record WatchInclusionRule(string Path, bool Included);
