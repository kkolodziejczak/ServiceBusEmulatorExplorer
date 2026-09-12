using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class GlobalWatchWindow : Window
{
    private EntityNode[] roots;
    private WatchRuleEditor rules;
    private readonly Func<WatchRuleEditor, Task<bool>> onChanged;
    private bool ready;
    private IReadOnlyList<WatchPreference> savedRules;
    private string[] renderedEntities = [];
    private readonly Dictionary<string, CheckBox> inclusionChoices = [];

    public GlobalWatchWindow(IEnumerable<EntityNode> roots, WatchRuleEditor rules, Func<WatchRuleEditor, Task<bool>> onChanged)
    {
        this.roots = roots.ToArray();
        this.rules = rules;
        savedRules = rules.Rules;
        this.onChanged = onChanged;
        InitializeComponent();
        InclusionTree.SizeChanged += (_, _) => ConstrainLabels();
        GlobalActiveChoice.IsChecked = rules.GlobalActive;
        GlobalDlqChoice.IsChecked = rules.GlobalDeadLetter;
        ready = true;
        RenderInclusions();
    }

    private async Task SaveAsync()
    {
        ready = false;
        GlobalActiveChoice.IsEnabled = GlobalDlqChoice.IsEnabled = InclusionTree.IsEnabled = DoneButton.IsEnabled = false;
        try
        {
            if (await onChanged(rules)) savedRules = rules.Rules;
            else rules = new WatchRuleEditor(roots, savedRules);
        }
        finally
        {
            GlobalActiveChoice.IsEnabled = GlobalDlqChoice.IsEnabled = InclusionTree.IsEnabled = DoneButton.IsEnabled = true;
            RefreshRules();
        }
    }

    public void RefreshRules()
    {
        ready = false;
        GlobalActiveChoice.IsChecked = rules.GlobalActive;
        GlobalDlqChoice.IsChecked = rules.GlobalDeadLetter;
        ready = true;
        RenderInclusions();
    }

    public void RefreshRules(IEnumerable<EntityNode> updatedRoots, WatchRuleEditor updatedRules)
    {
        if (!ready) return;
        var replacementRoots = updatedRoots.ToArray();
        var entities = replacementRoots.SelectMany(WatchRuleEditor.Flatten).Select(WatchRuleEditor.Key).ToArray();
        if (renderedEntities.SequenceEqual(entities) && rules.Rules.SequenceEqual(updatedRules.Rules)) return;
        var focusedKey = inclusionChoices.FirstOrDefault(pair => pair.Value.IsKeyboardFocusWithin).Key;
        roots = replacementRoots;
        rules = updatedRules;
        savedRules = rules.Rules;
        RefreshRules();
        if (focusedKey is not null) RestoreChoiceFocus(focusedKey);
    }

    private void RestoreChoiceFocus(string key) => Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (IsVisible && inclusionChoices.TryGetValue(key, out var replacement)) replacement.Focus();
        }));

    private async void GlobalChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        rules.SetGlobal(ReferenceEquals(sender, GlobalDlqChoice), ((CheckBox)sender).IsChecked == true);
        await SaveAsync();
        RenderInclusions();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (ready) RenderInclusions();
    }

    private void RenderInclusions()
    {
        renderedEntities = roots.SelectMany(WatchRuleEditor.Flatten).Select(WatchRuleEditor.Key).ToArray();
        InclusionTree.Items.Clear();
        inclusionChoices.Clear();
        var query = InclusionSearch.Text.Trim();
        foreach (var root in roots)
        {
            var item = CreateItem(root, query, false);
            if (item is not null) InclusionTree.Items.Add(item);
        }
        NoEntitiesMatch.Visibility = InclusionTree.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(ConstrainLabels));
    }

    private void ConstrainLabels()
    {
        foreach (var choice in inclusionChoices.Values)
        {
            if (!choice.IsVisible || !choice.IsDescendantOf(InclusionTree)) continue;
            double left = choice.TransformToAncestor(InclusionTree).Transform(new Point()).X;
            choice.MaxWidth = Math.Max(0, InclusionTree.ActualWidth - left - SystemParameters.VerticalScrollBarWidth - choice.Margin.Right);
        }
    }

    private TreeViewItem? CreateItem(EntityNode node, string query, bool ancestorMatches)
    {
        var matches = ancestorMatches || node.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
            || node.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
        var children = node.Children.Select(child => CreateItem(child, query, matches)).Where(child => child is not null).ToArray();
        if (!matches && children.Length == 0) return null;
        var label = new Grid();
        label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        label.Children.Add(new System.Windows.Shapes.Path { DataContext = node, Style = (Style)FindResource("EntityIcon") });
        var name = new TextBlock { Text = node.Name, TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = node.IsGroup || node.Kind == "Topic" ? FontWeights.SemiBold : FontWeights.Normal };
        Grid.SetColumn(name, 1);
        label.Children.Add(name);
        var choice = new IncludeCheckBox { Content = label, IsThreeState = true,
            IsChecked = rules.GetIncludedState(node), Tag = node,
            Margin = new Thickness(0, 5, 5, 5), ToolTip = node.Path, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(choice, "Watch " + node.Name);
        AutomationProperties.SetAutomationId(choice, "Include:" + node.Path);
        inclusionChoices[WatchRuleEditor.Key(node)] = choice;
        choice.Checked += Inclusion_Changed;
        choice.Unchecked += Inclusion_Changed;
        var item = new TreeViewItem { Header = choice, IsExpanded = true, Tag = node.Path, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        foreach (var child in children) item.Items.Add(child);
        return item;
    }

    private async void Inclusion_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        var choice = (CheckBox)sender;
        var restoreFocus = choice.IsKeyboardFocusWithin;
        rules.SetIncluded((EntityNode)choice.Tag, choice.IsChecked == true);
        await SaveAsync();
        RenderInclusions();
        if (restoreFocus) RestoreChoiceFocus(WatchRuleEditor.Key((EntityNode)choice.Tag));
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}

// Partial parent selections become fully selected on the next click or UIA Toggle.
internal sealed class IncludeCheckBox : CheckBox
{
    protected override void OnToggle() => IsChecked = IsChecked != true;
}

