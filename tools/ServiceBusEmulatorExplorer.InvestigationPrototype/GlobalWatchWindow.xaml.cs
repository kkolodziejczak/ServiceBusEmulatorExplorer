using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class GlobalWatchWindow : Window
{
    private readonly EntityNode[] roots;
    private readonly WatchRules rules;
    private readonly Action onChanged;
    private bool ready;
    private readonly Dictionary<string, CheckBox> inclusionChoices = [];

    public GlobalWatchWindow(IEnumerable<EntityNode> roots, WatchRules rules, Action onChanged)
    {
        this.roots = roots.ToArray();
        this.rules = rules;
        this.onChanged = onChanged;
        InitializeComponent();
        GlobalActiveChoice.IsChecked = rules.GlobalActive;
        GlobalDlqChoice.IsChecked = rules.GlobalDeadLetter;
        ready = true;
        RenderInclusions();
    }

    public void RefreshRules()
    {
        ready = false;
        GlobalActiveChoice.IsChecked = rules.GlobalActive;
        GlobalDlqChoice.IsChecked = rules.GlobalDeadLetter;
        ready = true;
        RenderInclusions();
    }

    private void GlobalChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        rules.SetGlobal(ReferenceEquals(sender, GlobalDlqChoice), ((CheckBox)sender).IsChecked == true);
        onChanged();
        RenderInclusions();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (ready) RenderInclusions();
    }

    private void RenderInclusions()
    {
        InclusionTree.Items.Clear();
        inclusionChoices.Clear();
        var query = InclusionSearch.Text.Trim();
        foreach (var root in roots)
        {
            var item = CreateItem(root, query, false);
            if (item is not null) InclusionTree.Items.Add(item);
        }
        NoEntitiesMatch.Visibility = InclusionTree.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private TreeViewItem? CreateItem(EntityNode node, string query, bool ancestorMatches)
    {
        var matches = ancestorMatches || node.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
            || node.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
        var children = node.Children.Select(child => CreateItem(child, query, matches)).Where(child => child is not null).ToArray();
        if (!matches && children.Length == 0) return null;
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new System.Windows.Shapes.Path { DataContext = node, Style = (Style)FindResource("EntityIcon") });
        label.Children.Add(new TextBlock { Text = node.Name, TextWrapping = TextWrapping.Wrap,
            FontWeight = node.IsGroup || node.Kind == "Topic" ? FontWeights.SemiBold : FontWeights.Normal });
        var choice = new IncludeCheckBox { Content = label, IsThreeState = true,
            IsChecked = rules.GetIncludedState(node.Path, roots), Tag = node.Path,
            Margin = new Thickness(0, 5, 5, 5), ToolTip = node.Path };
        AutomationProperties.SetName(choice, "Watch " + node.Name);
        AutomationProperties.SetAutomationId(choice, "Include:" + node.Path);
        inclusionChoices[node.Path] = choice;
        choice.Checked += Inclusion_Changed;
        choice.Unchecked += Inclusion_Changed;
        var item = new TreeViewItem { Header = choice, IsExpanded = true, Tag = node.Path };
        foreach (var child in children) item.Items.Add(child);
        return item;
    }

    private void Inclusion_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        var choice = (CheckBox)sender;
        var restoreFocus = choice.IsKeyboardFocusWithin;
        rules.SetIncluded((string)choice.Tag, choice.IsChecked == true, roots);
        onChanged();
        RenderInclusions();
        if (restoreFocus && inclusionChoices.TryGetValue((string)choice.Tag, out var replacement)) replacement.Focus();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}

// Partial parent selections become fully selected on the next click or UIA Toggle.
internal sealed class IncludeCheckBox : CheckBox
{
    protected override void OnToggle() => IsChecked = IsChecked != true;
}
