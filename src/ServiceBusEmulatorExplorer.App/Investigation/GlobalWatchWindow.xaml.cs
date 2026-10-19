using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>
/// The WPF view of <see cref="GlobalWatchViewModel"/>. It builds the inclusion tree from the view model's items,
/// keeps keyboard focus on the same entity when the tree is rebuilt, and fits long names to the visible width.
/// </summary>
public partial class GlobalWatchWindow : Window
{
    private readonly GlobalWatchViewModel viewModel;
    private readonly Dictionary<string, CheckBox> inclusionChoices = [];
    private bool showingRules;
    private string? toggledKey;

    public GlobalWatchWindow(IEnumerable<EntityNode> roots, WatchRuleEditor rules, Func<WatchRuleEditor, Task<bool>> onChanged)
    {
        viewModel = new GlobalWatchViewModel(roots, rules, onChanged);
        DataContext = viewModel;
        InitializeComponent();
        InclusionTree.SizeChanged += (_, _) => ConstrainLabels();
        ShowGlobalChoices();
        RenderInclusions();
        viewModel.PropertyChanged += ViewModelChanged;
    }

    public void RefreshRules(IEnumerable<EntityNode> updatedRoots, WatchRuleEditor updatedRules) =>
        viewModel.Refresh(updatedRoots, updatedRules);

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GlobalWatchViewModel.GlobalActive) or nameof(GlobalWatchViewModel.GlobalDeadLetter)) ShowGlobalChoices();
        else if (e.PropertyName == nameof(GlobalWatchViewModel.Items)) RenderInclusions();
    }

    private void ShowGlobalChoices()
    {
        showingRules = true;
        GlobalActiveChoice.IsChecked = viewModel.GlobalActive;
        GlobalDlqChoice.IsChecked = viewModel.GlobalDeadLetter;
        showingRules = false;
    }

    private async void GlobalChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (showingRules) return;
        await viewModel.SetGlobalAsync(ReferenceEquals(sender, GlobalDlqChoice), ((CheckBox)sender).IsChecked == true);
    }

    private void RenderInclusions()
    {
        string? focusedKey = toggledKey ?? inclusionChoices.FirstOrDefault(pair => pair.Value.IsKeyboardFocusWithin).Key;
        toggledKey = null;
        InclusionTree.Items.Clear();
        inclusionChoices.Clear();
        foreach (var item in viewModel.Items) InclusionTree.Items.Add(CreateItem(item));
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(ConstrainLabels));
        if (focusedKey is not null) RestoreChoiceFocus(focusedKey);
    }

    private void RestoreChoiceFocus(string key) => Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (IsVisible && inclusionChoices.TryGetValue(key, out var replacement)) replacement.Focus();
        }));

    private void ConstrainLabels()
    {
        foreach (var choice in inclusionChoices.Values)
        {
            if (!choice.IsVisible || !choice.IsDescendantOf(InclusionTree)) continue;
            double left = choice.TransformToAncestor(InclusionTree).Transform(new Point()).X;
            choice.MaxWidth = Math.Max(0, InclusionTree.ActualWidth - left - SystemParameters.VerticalScrollBarWidth - choice.Margin.Right);
        }
    }

    private TreeViewItem CreateItem(WatchEntityItem entity)
    {
        EntityNode node = entity.Node;
        var label = new Grid();
        label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        label.Children.Add(new System.Windows.Shapes.Path { DataContext = node, Style = (Style)FindResource("EntityIcon") });
        var name = new TextBlock { Text = node.Name, TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = entity.IsEmphasized ? FontWeights.SemiBold : FontWeights.Normal };
        Grid.SetColumn(name, 1);
        label.Children.Add(name);
        var choice = new IncludeCheckBox { Content = label, IsThreeState = true,
            IsChecked = entity.IsIncluded, Tag = node,
            Margin = new Thickness(0, 5, 5, 5), ToolTip = node.Path, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(choice, "Watch " + node.Name);
        AutomationProperties.SetAutomationId(choice, "Include:" + node.Path);
        inclusionChoices[entity.Key] = choice;
        choice.Checked += Inclusion_Changed;
        choice.Unchecked += Inclusion_Changed;
        var item = new TreeViewItem { Header = choice, IsExpanded = true, Tag = node.Path, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        foreach (var child in entity.Children) item.Items.Add(CreateItem(child));
        return item;
    }

    private async void Inclusion_Changed(object sender, RoutedEventArgs e)
    {
        var choice = (CheckBox)sender;
        var node = (EntityNode)choice.Tag;
        if (choice.IsKeyboardFocusWithin) toggledKey = WatchRuleEditor.Key(node);
        await viewModel.SetIncludedAsync(node, choice.IsChecked == true);
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}

// Partial parent selections become fully selected on the next click or UIA Toggle.
internal sealed class IncludeCheckBox : CheckBox
{
    protected override void OnToggle() => IsChecked = IsChecked != true;
}
