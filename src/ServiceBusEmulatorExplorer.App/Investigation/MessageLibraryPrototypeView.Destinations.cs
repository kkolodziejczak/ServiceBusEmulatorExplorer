using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeView
{
    private readonly WorkbenchDestinationsViewModel destinations = new();
    public event Action? ConnectionRepairRequested;
    private EntityKind currentDestinationKind = EntityKind.Topic;

    private WorkbenchDestination CurrentDestination => new(currentAssociation, currentDestinationKind);

    public void SetDestinationDiscovery(string profileId, long generation, EntityDiscoverySnapshot? snapshot, string endpoint = "")
    {
        if (destinations.ShowDiscovery(profileId, generation, snapshot, endpoint, CurrentDestination) is not { } change) return;
        UpdateDestinationControls();
        RefreshLibraryTree();
        if (change.ProfileChanged || change.CurrentStatusChanged)
        {
            if (change.ProfileChanged) { lastRun = null; ViewRunResultsButton.Visibility = Visibility.Collapsed; }
            InvalidatePreview();
        }
    }

    // Offline prototype mode can be entered after the window has already published disconnected
    // discovery, so it must restore the sample destinations rather than rely on startup order.
    public void UseSampleDestinations()
    {
        if (destinations.UseSamples(CurrentDestination) is not { } change) return;
        UpdateDestinationControls();
        RefreshLibraryTree();
        if (change.CurrentStatusChanged) InvalidatePreview();
    }

    private WorkbenchDestinationStatus DestinationStatus(string name, EntityKind kind) => destinations.StatusOf(new(name, kind));

    private void UpdateDestinationControls()
    {
        if (TemplateDestination is null) return;
        WorkbenchDestinationStatus status = DestinationStatus(currentAssociation, currentDestinationKind);
        selectedDestination = status == WorkbenchDestinationStatus.Available ? currentAssociation : null;
        var choices = destinations.ChoicesFor(CurrentDestination);
        loadingDestinationSelection = true;
        try
        {
            TemplateDestination.Items.Clear();
            TemplateDestination.Items.Add(new ComboBoxItem { Content = "Choose destination", Tag = "" });
            foreach (var item in choices)
                {
                var option = new ComboBoxItem { Content = item, ContentTemplate = (DataTemplate)FindResource("DestinationChoiceTemplate"), Tag = item.Name, DataContext = item };
                System.Windows.Automation.AutomationProperties.SetName(option, $"{item.Name}, {item.Kind}");
                TemplateDestination.Items.Add(option);
            }
            TemplateDestination.SelectedItem = TemplateDestination.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                item.DataContext is WorkbenchDestination choice && choice.Name.Equals(currentAssociation, StringComparison.OrdinalIgnoreCase) && choice.Kind == currentDestinationKind)
                ?? TemplateDestination.Items[0];
        }
        finally { loadingDestinationSelection = false; }
        string warning = WorkbenchDestinationsViewModel.NoticeFor(CurrentDestination, status);
        var visibility = status == WorkbenchDestinationStatus.Available ? Visibility.Collapsed : Visibility.Visible;
        DestinationWarning.Text = PrepareDestinationWarningText.Text = warning;
        DestinationWarning.Visibility = visibility;
        ComposeDestinationWarning.Visibility = PrepareDestinationWarning.Visibility = visibility;
        ContinueToPrepareButton.IsEnabled = PrepareStepButton.IsEnabled = status == WorkbenchDestinationStatus.Available;
        foreach (var action in new[] { ComposeDestinationAction, PrepareDestinationAction })
        {
            bool needsConnection = status == WorkbenchDestinationStatus.Unverified;
            action.Content = WorkbenchDestinationsViewModel.ActionFor(status);
            System.Windows.Automation.AutomationProperties.SetName(action, (string)action.Content);
            action.Visibility = needsConnection && ConnectionRepairRequested is null ? Visibility.Collapsed : Visibility.Visible;
        }
        if (ReviewButton is not null) ReviewButton.IsEnabled = previewReady && selectedDestination is not null;
    }

    private void ChooseDestination_Click(object sender, RoutedEventArgs e)
    {
        if (DestinationStatus(currentAssociation, currentDestinationKind) == WorkbenchDestinationStatus.Unverified)
        {
            ConnectionRepairRequested?.Invoke();
            return;
        }
        ShowWizardStage(WizardStage.Compose);
        EditorProperties_Click(sender, e);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            TemplateDestination.BringIntoView();
            TemplateDestination.Focus();
        }));
    }

    private void DestinationNotice_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var grid = (Grid)sender;
        var text = (TextBlock)grid.Children[1];
        var action = (Button)grid.Children[2];
        bool compact = e.NewSize.Width < 520;
        Grid.SetColumnSpan(text, compact ? 2 : 1);
        Grid.SetRow(action, compact ? 1 : 0);
        Grid.SetColumn(action, compact ? 1 : 2);
        Grid.SetColumnSpan(action, compact ? 2 : 1);
        action.HorizontalAlignment = HorizontalAlignment.Right;
        action.Margin = compact ? new Thickness(0, 8, 0, 0) : new Thickness(12, 0, 0, 0);
    }
    private void AddDestinationWarning(TreeViewItem item, PrototypeTemplate template)
    {
        string warning = WorkbenchDestinationsViewModel.LibraryWarningFor(template.Topic, DestinationStatus(template.Topic, template.DestinationKind));
        if (warning.Length == 0 || item.Header is not Grid header) return;
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new TextBlock { Text = "\u26A0", FontSize = 18, Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("WarningHealthBrush"), ToolTip = warning };
        System.Windows.Automation.AutomationProperties.SetName(icon, warning);
        System.Windows.Automation.AutomationProperties.SetAutomationId(icon, "LibraryDestinationWarning");
        Grid.SetColumn(icon, 2);
        header.Children.Add(icon);
        item.ToolTip = template.Name + "\n" + warning;
        System.Windows.Automation.AutomationProperties.SetHelpText(item, warning);
    }
}
