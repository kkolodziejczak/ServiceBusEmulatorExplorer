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
        string warning = DestinationWarningText(currentAssociation, status);
        if (DestinationWarning is not null)
        {
            DestinationWarning.Text = warning.Length == 0 ? "" : "\u26A0 " + warning;
            DestinationWarning.Visibility = warning.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        if (PrepareDestinationWarning is not null)
        {
            PrepareDestinationWarningText.Text = status switch
            {
                WorkbenchDestinationStatus.Missing => $"{currentDestinationKind} \"{currentAssociation}\" is unavailable.\nGo back to the template and select an available destination to continue.",
                WorkbenchDestinationStatus.Unverified => $"{currentDestinationKind} \"{currentAssociation}\" could not be verified.\nConnect and refresh entity discovery to continue.",
                WorkbenchDestinationStatus.Unset => "No destination selected.\nGo back to the template and choose a queue or topic to continue.",
                _ => ""
            };
            PrepareDestinationWarning.Visibility = status == WorkbenchDestinationStatus.Available ? Visibility.Collapsed : Visibility.Visible;
        }
        if (ReviewButton is not null) ReviewButton.IsEnabled = previewReady && selectedDestination is not null;
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
