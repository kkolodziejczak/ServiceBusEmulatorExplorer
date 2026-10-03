using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeView
{
    private static readonly WorkbenchDestination[] SampleDestinations =
        [new("order-events", EntityKind.Topic), new("inventory-events", EntityKind.Topic), new("order-replies", EntityKind.Queue)];
    public event Action? ConnectionRepairRequested;
    private bool syntheticDestinations = true;
    private string? destinationProfileId;
    private long destinationGeneration = -1;
    private EntityDiscoverySnapshot? destinationSnapshot;
    private EntityKind currentDestinationKind = EntityKind.Topic;
    private string destinationEndpoint = "localhost";

    public void SetDestinationDiscovery(string profileId, long generation, EntityDiscoverySnapshot? snapshot, string endpoint = "")
    {
        if (generation < destinationGeneration) return;
        if (!syntheticDestinations && destinationProfileId == profileId && generation == destinationGeneration &&
            ReferenceEquals(destinationSnapshot, snapshot) && destinationEndpoint == endpoint) return;
        var previousStatus = DestinationStatus(currentAssociation, currentDestinationKind);
        bool profileChanged = destinationProfileId != profileId || destinationGeneration != generation;
        syntheticDestinations = false;
        destinationProfileId = profileId;
        destinationGeneration = generation;
        destinationSnapshot = snapshot;
        destinationEndpoint = endpoint;
        UpdateDestinationControls();
        RefreshLibraryTree();
        if (profileChanged || previousStatus != DestinationStatus(currentAssociation, currentDestinationKind))
        {
            if (profileChanged) { lastRun = null; ViewRunResultsButton.Visibility = Visibility.Collapsed; }
            InvalidatePreview();
        }
    }

    private WorkbenchDestinationStatus DestinationStatus(string name, EntityKind kind)
    {
        if (name.Length == 0) return WorkbenchDestinationStatus.Unset;
        if (syntheticDestinations) return SampleDestinations.Any(item => item.Kind == kind && item.Name == name)
            ? WorkbenchDestinationStatus.Available : WorkbenchDestinationStatus.Unverified;
        return WorkbenchDestinationAvailability.Evaluate(new(name, kind), destinationSnapshot);
    }

    private static string DestinationWarningText(string name, WorkbenchDestinationStatus status) => status switch
    {
        WorkbenchDestinationStatus.Missing => $"Destination unavailable: {name}. Select an available queue or topic.",
        WorkbenchDestinationStatus.Unverified => $"Destination not verified: {name}. Connect and refresh discovery before sending.",
        _ => ""
    };

    private void UpdateDestinationControls()
    {
        if (TemplateDestination is null) return;
        WorkbenchDestinationStatus status = DestinationStatus(currentAssociation, currentDestinationKind);
        selectedDestination = status == WorkbenchDestinationStatus.Available ? currentAssociation : null;
        IEnumerable<WorkbenchDestination> discovered = syntheticDestinations ? SampleDestinations
            : destinationSnapshot?.Entities.Where(item => item.Entity.Kind is EntityKind.Topic or EntityKind.Queue)
                .Select(item => new WorkbenchDestination(item.Entity.Name, item.Entity.Kind)) ?? [];
        var choices = discovered.Distinct().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (currentAssociation.Length > 0 && !choices.Any(item => item.Name.Equals(currentAssociation, StringComparison.OrdinalIgnoreCase) && item.Kind == currentDestinationKind))
            choices.Insert(0, new(currentAssociation, currentDestinationKind));
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
        string warning = status switch
        {
            WorkbenchDestinationStatus.Missing => $"{currentDestinationKind} \"{currentAssociation}\" is unavailable.\nChoose an available destination to continue.",
            WorkbenchDestinationStatus.Unverified => $"{currentDestinationKind} \"{currentAssociation}\" is not verified.\nCheck the connection and reconnect to refresh entity discovery.",
            WorkbenchDestinationStatus.Unset => "No destination selected.\nChoose a queue or topic to continue.",
            _ => ""
        };
        var visibility = status == WorkbenchDestinationStatus.Available ? Visibility.Collapsed : Visibility.Visible;
        DestinationWarning.Text = PrepareDestinationWarningText.Text = warning;
        DestinationWarning.Visibility = visibility;
        ComposeDestinationWarning.Visibility = PrepareDestinationWarning.Visibility = visibility;
        ContinueToPrepareButton.IsEnabled = PrepareStepButton.IsEnabled = status == WorkbenchDestinationStatus.Available;
        foreach (var action in new[] { ComposeDestinationAction, PrepareDestinationAction })
        {
            bool needsConnection = status == WorkbenchDestinationStatus.Unverified;
            action.Content = needsConnection ? "Check connection" : "Choose destination";
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
        string warning = DestinationWarningText(template.Topic, DestinationStatus(template.Topic, template.DestinationKind));
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
