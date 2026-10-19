using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// The Global watch window: the connection-wide Active and dead-letter choices and the searchable tree of entities
/// to include. Every change is saved at once; a save that fails puts back the last saved rules. While a save runs
/// the window is not editable and changes made in the workspace are not applied.
/// </summary>
public sealed class GlobalWatchViewModel : ObservableObject
{
    private readonly Func<WatchRuleEditor, Task<bool>> saveAsync;
    private EntityNode[] roots;
    private WatchRuleEditor rules;
    private IReadOnlyList<WatchPreference> savedRules;
    private string[] entityKeys = [];
    private IReadOnlyList<WatchEntityItem> items = [];
    private string searchText = string.Empty;
    private bool isEditable = true;

    /// <param name="saveAsync">Saves the edited rules; returns <c>false</c> when they were not saved.</param>
    public GlobalWatchViewModel(IEnumerable<EntityNode> roots, WatchRuleEditor rules, Func<WatchRuleEditor, Task<bool>> saveAsync)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(saveAsync);
        this.roots = roots.ToArray();
        this.rules = rules;
        this.saveAsync = saveAsync;
        savedRules = rules.Rules;
        ShowItems();
    }

    public bool GlobalActive => rules.GlobalActive;

    public bool GlobalDeadLetter => rules.GlobalDeadLetter;

    /// <summary>Filters the tree by entity name or path; a matching entity keeps its whole branch.</summary>
    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value ?? string.Empty) && IsEditable) ShowItems();
        }
    }

    /// <summary>The entities shown for <see cref="SearchText"/>, with their included state.</summary>
    public IReadOnlyList<WatchEntityItem> Items
    {
        get => items;
        private set
        {
            if (SetProperty(ref items, value)) OnPropertyChanged(nameof(HasNoMatches));
        }
    }

    public bool HasNoMatches => Items.Count == 0;

    /// <summary><c>false</c> while a change is being saved.</summary>
    public bool IsEditable
    {
        get => isEditable;
        private set => SetProperty(ref isEditable, value);
    }

    public async Task SetGlobalAsync(bool deadLetter, bool enabled)
    {
        if (!IsEditable) return;
        rules.SetGlobal(deadLetter, enabled);
        await SaveAsync();
    }

    public async Task SetIncludedAsync(EntityNode node, bool included)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!IsEditable) return;
        rules.SetIncluded(node, included);
        await SaveAsync();
    }

    /// <summary>
    /// Applies entities and rules changed in the workspace. Nothing happens while a save runs or when neither the
    /// entities nor the rules changed, so the shown tree, its expansion and focus stay as they are.
    /// </summary>
    public void Refresh(IEnumerable<EntityNode> updatedRoots, WatchRuleEditor updatedRules)
    {
        ArgumentNullException.ThrowIfNull(updatedRoots);
        ArgumentNullException.ThrowIfNull(updatedRules);
        if (!IsEditable) return;
        EntityNode[] replacementRoots = updatedRoots.ToArray();
        if (entityKeys.SequenceEqual(KeysOf(replacementRoots)) && rules.Rules.SequenceEqual(updatedRules.Rules)) return;
        roots = replacementRoots;
        rules = updatedRules;
        savedRules = rules.Rules;
        ShowRules();
    }

    private async Task SaveAsync()
    {
        IsEditable = false;
        try
        {
            if (await saveAsync(rules)) savedRules = rules.Rules;
            else rules = new WatchRuleEditor(roots, savedRules);
        }
        finally
        {
            IsEditable = true;
            ShowRules();
        }
    }

    private void ShowRules()
    {
        OnPropertyChanged(nameof(GlobalActive));
        OnPropertyChanged(nameof(GlobalDeadLetter));
        ShowItems();
    }

    private void ShowItems()
    {
        entityKeys = KeysOf(roots);
        string query = SearchText.Trim();
        Items = roots.Select(root => Item(root, query, false)).OfType<WatchEntityItem>().ToArray();
    }

    private static string[] KeysOf(IEnumerable<EntityNode> nodes) =>
        nodes.SelectMany(WatchRuleEditor.Flatten).Select(WatchRuleEditor.Key).ToArray();

    private WatchEntityItem? Item(EntityNode node, string query, bool ancestorMatches)
    {
        bool matches = ancestorMatches || node.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
            || node.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
        WatchEntityItem[] children = node.Children.Select(child => Item(child, query, matches)).OfType<WatchEntityItem>().ToArray();
        return !matches && children.Length == 0 ? null : new WatchEntityItem(node, rules.GetIncludedState(node), children);
    }
}

/// <summary>An entity shown in Global watch: included, excluded, or <c>null</c> when only part of its branch is included.</summary>
public sealed record WatchEntityItem(EntityNode Node, bool? IsIncluded, IReadOnlyList<WatchEntityItem> Children)
{
    public string Key => WatchRuleEditor.Key(Node);

    /// <summary>Groups and topics are shown in bold.</summary>
    public bool IsEmphasized => Node.IsGroup || Node.Kind == "Topic";
}
