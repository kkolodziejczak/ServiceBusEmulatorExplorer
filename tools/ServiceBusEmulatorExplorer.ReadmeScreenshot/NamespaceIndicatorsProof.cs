using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ShapePath = System.Windows.Shapes.Path;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class NamespaceIndicatorsProof
{
    private const string TopicName = "order-processing-with-long-topic-name";
    private const string QueueName = "checkout-commands-with-long-name";
    private const string InheritedSubscriptionName = "regional-fulfillment-subscription-with-long-name";
    private const string ExcludedSubscriptionName = "legacy-export-subscription";
    private static readonly ProofBrowser browser = new();

    public static async Task<InvestigationWorkspace> CreateWorkspaceAsync()
    {
        var workspace = new InvestigationWorkspace(new ScenarioWorkspacePreferencesStore(RetailScreenshotData.CreatePreferences()),
            new BrokerConnectionWorkflow(() => new ScenarioClientFactory(), _ => browser,
                _ => new ScenarioMessageService(RetailScreenshotData.CreateMessages())));
        await workspace.InitializeAsync();
        await workspace.ConnectAsync();
        await workspace.Browse.SelectAsync(workspace.Browse.AllEntities().Single(node => node.Kind == "Topic" && node.Name == "order-events"), false);
        workspace.Browse.FocusedMessage = workspace.Browse.Messages.First();
        return workspace;
    }

    private sealed class ProofBrowser : IInvestigationEntityBrowser
    {
        public EntityDiscoverySnapshot Snapshot { get; set; } = RetailScreenshotData.CreateSnapshot();
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot);
    }

    public static async Task RunAsync(InvestigationWindow window, InvestigationWorkspace workspace, string outputDirectory)
    {
        string output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        TreeView tree = Require<TreeView>(window, "NamespaceTree");
        SelectAutoRefreshInterval(window, index: 3);
        await SettleAsync(window);
        EntityNode initialTopic = workspace.Browse.SelectedEntity
            ?? throw new InvalidOperationException("The screenshot workspace has no selected topic for the initial search proof.");
        TreeViewItem initialTopicRow = ItemForEntity(tree, initialTopic);
        AssertRefresh(initialTopicRow, "Running", "Active view");
        if (workspace.Browse.FocusedMessage?.CorrelationId is not { Length: > 0 })
            throw new InvalidOperationException("The screenshot workspace has no focused message with a correlation ID for the routed related-message search.");
        Button findRelated = Require<Button>(window, "FindRelatedButton");
        findRelated.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, findRelated));
        await WaitUntilAsync(() => workspace.Search.IsActive);
        await SettleAsync(window);
        if (initialTopic.RefreshIndicator != "None"
            || Descendants<ShapePath>(tree).Any(path => path.Name == "RefreshIndicatorIcon" && path.IsVisible))
            throw new InvalidOperationException("Search retained a running current-view refresh marker.");
        Require<Button>(window, "ClearSearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntilAsync(() => !workspace.Search.IsActive);
        await SettleAsync(window);
        AssertRefresh(ItemForEntity(tree, initialTopic), "Running", "Active view");

        browser.Snapshot = CreateSnapshot(largeCounts: false);
        workspace.Browse.ApplySnapshot(browser.Snapshot);
        await SettleAsync(window);

        EntityNode topic = FindEntity(workspace, EntityKind.Topic, TopicName);
        EntityNode inherited = FindEntity(workspace, EntityKind.Subscription, InheritedSubscriptionName);
        EntityNode excluded = FindEntity(workspace, EntityKind.Subscription, ExcludedSubscriptionName);
        EntityNode queue = FindEntity(workspace, EntityKind.Queue, QueueName);
        var rules = new WatchPreference[]
        {
            new(WatchScopeResolver.TopicScopeKey(TopicName), Active: true, DeadLetter: true),
            new(WatchScopeResolver.SubscriptionScopeKey(TopicName, ExcludedSubscriptionName), Active: false, DeadLetter: null),
            new(WatchScopeResolver.QueueScopeKey(QueueName), Active: true, DeadLetter: false)
        };
        await workspace.UpdateWatchRulesAsync(workspace.Preferences.SelectedProfileId, rules);
        SelectAutoRefreshInterval(window, index: 0);
        await SettleAsync(window);
        SelectAutoRefreshInterval(window, index: 3);
        await SettleAsync(window);

        TreeViewItem topicRow = ItemForEntity(tree, topic);
        topicRow.IsExpanded = true;
        await SettleAsync(window);
        TreeViewItem inheritedRow = ItemFor(topicRow, inherited);
        TreeViewItem excludedRow = ItemFor(topicRow, excluded);
        TreeViewItem queueRow = ItemFor(ItemFor(tree, workspace.Browse.Roots[0]), queue);

        AssertWatch(inheritedRow, active: true, deadLetter: true, "inherited subscription");
        AssertWatch(excludedRow, active: false, deadLetter: true, "subscription override");
        AssertWatch(queueRow, active: true, deadLetter: false, "queue");
        AssertWatch(ItemForEntity(tree, topic), active: false, deadLetter: false, "topic aggregate");

        await SelectAsync(window, workspace, inheritedRow, inherited);
        SelectAutoRefreshInterval(window, index: 3);
        await SettleAsync(window);
        AssertRefresh(inheritedRow, "Running", "Background");
        AssertRefresh(ItemForEntity(tree, topic), "Running", "Background");
        AssertWatch(ItemForEntity(tree, topic), active: false, deadLetter: false, "topic aggregate counts remain unmarked");
        AssertNoIndicatorOverlap(inheritedRow, "running subscription");

        foreach ((double width, double height, string suffix) in new[]
        {
            (1500d, 1000d, "1500"),
            (1100d, 800d, "1100"),
            (980d, 640d, "980")
        })
        {
            window.Width = width;
            window.Height = height;
            await SettleAsync(window);
            AssertNoIndicatorOverlap(inheritedRow, $"running subscription at {suffix}px");
            AssertNoIndicatorOverlap(queueRow, $"watched queue at {suffix}px");
            WpfScreenshot.SaveWindowContent(window,
                Path.Combine(output, $"namespace-indicators-running-{suffix}.png"), (int)width, (int)height);
        }

        window.Width = 1500;
        window.Height = 1000;
        await SettleAsync(window);
        WpfScreenshot.SaveWindowContent(window, Path.Combine(output, "namespace-indicators-normal-counts.png"), 1500, 1000);

        Button pause = Require<Button>(window, "PauseButton");
        pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, pause));
        await SettleAsync(window);
        AssertRefresh(inheritedRow, "Paused", "paused");
        AssertRefresh(ItemForEntity(tree, topic), "Paused", "paused");
        AssertRefresh(queueRow, "Paused", "paused");
        AssertWatch(inheritedRow, active: true, deadLetter: true, "paused Watch remains enabled");
        foreach ((double width, double height, string suffix) in new[]
        {
            (1500d, 1000d, "1500"),
            (1100d, 800d, "1100"),
            (980d, 640d, "980")
        })
        {
            window.Width = width;
            window.Height = height;
            await SettleAsync(window);
            AssertNoIndicatorOverlap(inheritedRow, $"paused subscription at {suffix}px");
            WpfScreenshot.SaveWindowContent(window,
                Path.Combine(output, $"namespace-indicators-paused-{suffix}.png"), (int)width, (int)height);
        }

        pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, pause));
        await SelectAsync(window, workspace, inheritedRow, inherited);
        Require<ToggleButton>(window, "DeadLetterTab").IsChecked = true;
        await WaitUntilAsync(() => workspace.Browse.IsDeadLetter);
        await SettleAsync(window);
        AssertRefresh(inheritedRow, "Running", "Background");
        AssertRefreshDetail(inheritedRow, "Dead letter");

        TreeViewItem topicAgain = ItemForEntity(tree, topic);
        await SelectAsync(window, workspace, topicAgain, topic);
        AssertRefresh(topicAgain, "Running", "Background");
        AssertWatch(topicAgain, active: false, deadLetter: false, "topic aggregate remains unwatched");
        AssertWatch(ItemFor(topicAgain, inherited), active: true, deadLetter: true, "inherited child Watch");

        SelectAutoRefreshInterval(window, index: 0);
        await SettleAsync(window);
        AssertRefresh(topicAgain, "None", "");
        AssertRefresh(inheritedRow, "None", "");
        AssertRefresh(queueRow, "None", "");
        AssertWatch(ItemFor(topicAgain, inherited), active: true, deadLetter: true, "Watch with auto-refresh off");
        SelectAutoRefreshInterval(window, index: 3);
        await SettleAsync(window);
        AssertRefresh(topicAgain, "Running", "Background");

        Require<ToggleButton>(window, "ActiveTab").IsChecked = true;
        await WaitUntilAsync(() => !workspace.Browse.IsDeadLetter);
        await SettleAsync(window);
        AssertRefresh(topicAgain, "Running", "Background");

        browser.Snapshot = CreateSnapshot(largeCounts: true);
        workspace.Browse.ApplySnapshot(browser.Snapshot);
        await SettleAsync(window);
        topicRow = ItemForEntity(tree, FindEntity(workspace, EntityKind.Topic, TopicName));
        topicRow.IsExpanded = true;
        await SettleAsync(window);
        inheritedRow = ItemFor(topicRow, FindEntity(workspace, EntityKind.Subscription, InheritedSubscriptionName));
        queueRow = ItemFor(ItemFor(tree, workspace.Browse.Roots[0]), FindEntity(workspace, EntityKind.Queue, QueueName));
        Require<ToggleButton>(window, "ActiveTab").IsChecked = true;
        await WaitUntilAsync(() => !workspace.Browse.IsDeadLetter);
        await SelectAsync(window, workspace, inheritedRow, FindEntity(workspace, EntityKind.Subscription, InheritedSubscriptionName));
        await SettleAsync(window);
        AssertWatch(inheritedRow, active: true, deadLetter: true, "large-count subscription");
        AssertRefresh(inheritedRow, "Running", "Background");
        AssertNoIndicatorOverlap(inheritedRow, "large count stress subscription");
        AssertNoIndicatorOverlap(queueRow, "large count stress watched queue");
        WpfScreenshot.SaveWindowContent(window, Path.Combine(output, "namespace-indicators-large-counts.png"), 980, 640);

        await workspace.DisconnectAsync();
        await SettleAsync(window);
        if (Descendants<ShapePath>(tree).Any(path => path.Name == "RefreshIndicatorIcon" && path.IsVisible))
            throw new InvalidOperationException("An auto-refresh icon remained visible in the namespace tree after disconnect.");
        Console.WriteLine("Namespace indicator proof passed: inherited and overridden Watch, watched background refresh on queue/subscription/topic, active/DLQ selection, auto-refresh off/paused/search/disconnect, and aligned rows at 1500, 1100, and 980px.");
    }

    private static EntityDiscoverySnapshot CreateSnapshot(bool largeCounts)
    {
        long active = largeCounts ? 123456789012345 : 18;
        long dlq = largeCounts ? 7654321098765 : 2;
        long scheduled = largeCounts ? 9876543210987 : 1;
        var entities = new[]
        {
            Observation(EntityKind.Queue, QueueName, null, active, dlq, scheduled),
            Observation(EntityKind.Topic, TopicName, null, active, dlq, scheduled),
            Observation(EntityKind.Subscription, InheritedSubscriptionName, TopicName, active, dlq, scheduled),
            Observation(EntityKind.Subscription, ExcludedSubscriptionName, TopicName, active, dlq, scheduled)
        };
        return new EntityDiscoverySnapshot(entities, RetailScreenshotData.ScenarioTime, true, []);
    }

    private static EntityObservation Observation(EntityKind kind, string name, string? topicName, long active, long dlq, long scheduled)
    {
        var metadata = new EntityMetadata(topicName is null ? name : $"{topicName}/Subscriptions/{name}", "Active",
            null, null, null, null, null, null, null);
        return new EntityObservation(new DiscoveredEntity(kind, name, topicName, metadata), new EntityCountObservation(
            new CountObservation(active, CountAvailability.Known), new CountObservation(dlq, CountAvailability.Known),
            new CountObservation(scheduled, CountAvailability.Known)));
    }

    private static async Task SelectAsync(InvestigationWindow window, InvestigationWorkspace workspace, TreeViewItem row, EntityNode expected)
    {
        row.IsSelected = true;
        await WaitUntilAsync(() => window.FindName("NamespaceTree") is TreeView tree
            && ReferenceEquals(tree.SelectedItem, expected));
        await WaitUntilAsync(() => workspace.Browse.SelectedEntity == expected && !workspace.Browse.IsBusy);
        await SettleAsync(window);
    }

    private static void SelectAutoRefreshInterval(InvestigationWindow window, int index)
    {
        ComboBox interval = Require<ComboBox>(window, "AutoInterval");
        interval.SelectedIndex = index;
    }

    private static void AssertWatch(TreeViewItem row, bool active, bool deadLetter, string context)
    {
        TextBlock activeCount = BoundCount(row, "ActiveCount");
        TextBlock dlqCount = BoundCount(row, "DlqCount");
        if (HasUnderline(activeCount) != active || HasUnderline(dlqCount) != deadLetter)
            throw new InvalidOperationException($"The {context} row has incorrect Watch underlines (active={HasUnderline(activeCount)}, DLQ={HasUnderline(dlqCount)}).");
    }

    private static void AssertRefresh(TreeViewItem row, string state, string detail)
    {
        ShapePath icon = RefreshIcon(row);
        bool visible = state != "None";
        if (icon.Visibility != (visible ? Visibility.Visible : Visibility.Collapsed))
            throw new InvalidOperationException($"The refresh icon visibility does not represent {state}.");
        if (!visible) return;
        string tooltip = icon.ToolTip as string ?? "";
        string automation = AutomationProperties.GetName(icon);
        if (!tooltip.Contains(detail, StringComparison.OrdinalIgnoreCase)
            || !automation.Contains(detail, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(tooltip))
            throw new InvalidOperationException($"The {state} refresh icon is missing its view detail in tooltip or automation text: '{tooltip}' / '{automation}'.");
    }

    private static void AssertRefreshDetail(TreeViewItem row, string view)
    {
        string detail = RefreshIcon(row).ToolTip as string ?? "";
        if (!detail.Contains(view, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The refresh tooltip does not identify the {view} view: '{detail}'.");
    }

    private static void AssertNoIndicatorOverlap(TreeViewItem row, string context, bool expectVisible = true)
    {
        ShapePath icon = RefreshIcon(row);
        if (icon.IsVisible != expectVisible)
            throw new InvalidOperationException($"The {context} refresh marker visibility is {icon.IsVisible}, expected {expectVisible}.");
        TextBlock name = Descendants<TextBlock>(row).Single(text =>
            BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path == nameof(EntityNode.Name)
            && ReferenceEquals(text.DataContext, row.DataContext));
        FrameworkElement active = BoundCount(row, "ActiveCount");
        FrameworkElement dlq = BoundCount(row, "DlqCount");
        Rect? iconBounds = expectVisible ? Bounds(icon, row) : null;
        Rect nameBounds = Bounds(name, row);
        Rect activeBounds = Bounds(active, row);
        Rect dlqBounds = Bounds(dlq, row);
        if (nameBounds.IntersectsWith(activeBounds) || nameBounds.IntersectsWith(dlqBounds)
            || activeBounds.IntersectsWith(dlqBounds)
            || expectVisible && (iconBounds!.Value.Width <= 0 || iconBounds.Value.Height <= 0
                || iconBounds.Value.Left < nameBounds.Left || iconBounds.Value.IntersectsWith(nameBounds)
                || iconBounds.Value.Right > activeBounds.Left + 1
                || iconBounds.Value.IntersectsWith(activeBounds) || iconBounds.Value.IntersectsWith(dlqBounds)))
            throw new InvalidOperationException($"The refresh marker overlaps the name or right count columns for {context}: icon={iconBounds}, name={nameBounds}, active={activeBounds}, DLQ={dlqBounds}.");
    }

    private static Rect Bounds(FrameworkElement element, FrameworkElement ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(new Point(), element.RenderSize));

    private static bool HasUnderline(TextBlock text) => text.TextDecorations?.Any(decoration => decoration.Location == TextDecorationLocation.Underline) == true;

    private static TextBlock BoundCount(TreeViewItem row, string name) => Descendants<TextBlock>(row)
        .SingleOrDefault(text => text.Name == name && ReferenceEquals(text.DataContext, row.DataContext))
        ?? throw new InvalidOperationException($"The rendered namespace row has no '{name}' count cell.");

    private static ShapePath RefreshIcon(TreeViewItem row) => Descendants<ShapePath>(row)
        .SingleOrDefault(path => path.Name == "RefreshIndicatorIcon" && ReferenceEquals(path.DataContext, row.DataContext))
        ?? throw new InvalidOperationException("The rendered namespace row has no refresh indicator glyph.");

    private static EntityNode FindEntity(InvestigationWorkspace workspace, EntityKind kind, string name) => workspace.Browse.AllEntities()
        .Single(node => node.Address?.Kind == kind && node.Name == name);

    private static TreeViewItem ItemFor(ItemsControl parent, EntityNode node)
    {
        parent.UpdateLayout();
        return parent.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem
            ?? throw new InvalidOperationException($"The namespace row for '{node.Name}' was not realized. Parent items: {string.Join(", ", parent.Items.Cast<EntityNode>().Select(item => item.Name))}; expanded: {(parent as TreeViewItem)?.IsExpanded}.");
    }

    private static TreeViewItem ItemForEntity(TreeView tree, EntityNode node)
    {
        EntityNode group = tree.Items.Cast<EntityNode>().Single(root => root.IsGroup
            && root.Name == (node.Kind == nameof(EntityKind.Queue) ? "Queues" : "Topics"));
        var groupRow = ItemFor(tree, group);
        groupRow.IsExpanded = true;
        groupRow.UpdateLayout();
        return ItemFor(groupRow, node);
    }

    private static async Task SettleAsync(FrameworkElement root)
    {
        root.UpdateLayout();
        await root.Dispatcher.InvokeAsync(root.UpdateLayout, DispatcherPriority.ContextIdle);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 80; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        if (!condition()) throw new TimeoutException("A routed namespace indicator action did not reach its expected state.");
    }

    private static T Require<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"The rendered window is missing '{name}'.");

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
