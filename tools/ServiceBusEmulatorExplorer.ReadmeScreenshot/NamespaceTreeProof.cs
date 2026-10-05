using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class NamespaceTreeProof
{
    private const string TopicName = "orders-with-a-very-long-topic-name-for-tree-layout-proof";
    private const string SubscriptionName = "regional-fulfillment-subscription-with-a-very-long-name-that-must-truncate";
    private const long LargeActiveCount = 123456789012345;
    private const long LargeScheduledCount = 9876543210987;
    private const long LargeDeadLetterCount = 7654321098765;

    public static async Task RunAsync(
        InvestigationWindow window,
        InvestigationWorkspace workspace,
        string outputPath)
    {
        workspace.Browse.ApplySnapshot(CreateSnapshot());
        var dispatcher = window.Dispatcher;
        await SettleAsync(window, dispatcher);

        TreeView tree = Require<TreeView>(window, "NamespaceTree");
        Border legend = Require<Border>(window, "NamespaceCountLegend");
        AssertLegend(legend);

        TreeViewItem queues = Container(tree, 0);
        TreeViewItem topics = Container(tree, 1);
        queues.IsExpanded = true;
        topics.IsExpanded = true;
        await SettleAsync(window, dispatcher);
        TreeViewItem topic = Container(topics, 0);
        topic.IsExpanded = true;
        await SettleAsync(window, dispatcher);
        await AssertUnavailableCount(window, topic);
        TreeViewItem subscription = Container(topic, 0);
        AssertEntityHeader(subscription, SubscriptionName);

        await AssertTooltipAndAutomation(window, subscription, "Messages", LargeActiveCount, outputPath);
        await AssertTooltipAndAutomation(window, subscription, "Scheduled", LargeScheduledCount, outputPath);
        await AssertTooltipAndAutomation(window, subscription, "DLQ", LargeDeadLetterCount, outputPath);
        AssertCountColumnsDoNotOverlap(subscription);

        foreach ((double width, double height, string suffix) in new[]
        {
            (1500d, 1000d, "1500"),
            (1100d, 800d, "1100"),
            (980d, 640d, "980")
        })
        {
            window.Width = width;
            window.Height = height;
            await SettleAsync(window, dispatcher);
            Grid workspaceGrid = Require<Grid>(window, "WorkspaceGrid");
            if (width == 980)
            {
                workspaceGrid.ColumnDefinitions[0].Width = new GridLength(240);
                await SettleAsync(window, dispatcher);
                AssertGroupLabel(queues);
                AssertGroupLabel(topics);
                if (NameWidth(subscription) < 30)
                    throw new InvalidOperationException("The long subscription name has less than 30 DIPs at the minimum namespace pane width.");
            }
            if (workspaceGrid.ColumnDefinitions[0].ActualWidth < 240)
                throw new InvalidOperationException($"Namespace pane fell below its 240-pixel minimum at {suffix}px.");
            AssertLegend(legend);
            AssertCountColumnsDoNotOverlap(subscription);
            WpfScreenshot.SaveWindowContent(window, OutputFor(outputPath, suffix), (int)width, (int)height);
        }

        topic.IsExpanded = false;
        await SettleAsync(window, dispatcher);
        if (subscription.IsVisible)
            throw new InvalidOperationException("Collapsing a topic left its subscription row visible.");
        topic.IsExpanded = true;
        await SettleAsync(window, dispatcher);
        if (!subscription.IsVisible)
            throw new InvalidOperationException("Expanding a topic did not restore its subscription row.");

        TextBox search = Require<TextBox>(window, "SearchBox");
        TextBlock empty = Require<TextBlock>(window, "NamespaceEmpty");
        search.Text = "no-entity-matches-this-proof";
        await SettleAsync(window, dispatcher);
        if (empty.Visibility != Visibility.Visible || tree.Items.Cast<EntityNode>().Any(node => node.IsVisible))
            throw new InvalidOperationException("A namespace search with no matches did not show the empty state.");
        search.Clear();
        await SettleAsync(window, dispatcher);
        if (empty.Visibility != Visibility.Collapsed || !topic.IsVisible || !subscription.IsVisible)
            throw new InvalidOperationException("Clearing namespace search did not restore the topic and subscription rows.");

        workspace.Browse.ApplySnapshot(CreateSmallCountSnapshot());
        await SettleAsync(window, dispatcher);
        TreeViewItem smallTopics = Container(tree, 1);
        TreeViewItem smallTopic = Container(smallTopics, 0);
        smallTopic.IsExpanded = true;
        await SettleAsync(window, dispatcher);
        window.Width = 1500;
        window.Height = 1000;
        await SettleAsync(window, dispatcher);
        WpfScreenshot.SaveWindowContent(window, OutputFor(outputPath, "1500-small-counts"), 1500, 1000);
        Console.WriteLine("Namespace tree proof passed: legend, count semantics, tooltip and automation detail, long names, search, expansion, and supported widths.");
    }

    private static EntityDiscoverySnapshot CreateSnapshot()
    {
        var entities = new List<EntityObservation>
        {
            Observation(EntityKind.Queue, "queue-with-long-name-for-proof", null, 5432109876543, 2345678901234, 3456789012345),
            Observation(EntityKind.Topic, TopicName, null, LargeActiveCount, null, LargeScheduledCount,
                deadLetterAvailability: CountAvailability.Unavailable,
                deadLetterDetail: "DLQ count unavailable. The broker did not report this value for the topic."),
            Observation(EntityKind.Subscription, SubscriptionName, TopicName,
                LargeActiveCount, LargeDeadLetterCount, LargeScheduledCount,
                activeDetail: "Observed by the connected broker.",
                scheduledDetail: "Reported by the connected broker.",
                deadLetterDetail: "Reported by the connected broker.")
        };
        return new EntityDiscoverySnapshot(entities, RetailScreenshotData.ScenarioTime, true, []);
    }

    private static EntityDiscoverySnapshot CreateSmallCountSnapshot()
    {
        var entities = new List<EntityObservation>
        {
            Observation(EntityKind.Topic, TopicName, null, 24, 2, 3),
            Observation(EntityKind.Subscription, SubscriptionName, TopicName, 4, 0, 1)
        };
        return new EntityDiscoverySnapshot(entities, RetailScreenshotData.ScenarioTime, true, []);
    }

    private static EntityObservation Observation(
        EntityKind kind,
        string name,
        string? topicName,
        long active,
        long? deadLetter,
        long scheduled,
        CountAvailability deadLetterAvailability = CountAvailability.Known,
        string? activeDetail = null,
        string? scheduledDetail = null,
        string? deadLetterDetail = null)
    {
        var metadata = new EntityMetadata(
            topicName is null ? name : $"{topicName}/Subscriptions/{name}", "Active", null, null,
            null, null, null, null, null);
        return new EntityObservation(
            new DiscoveredEntity(kind, name, topicName, metadata),
            new EntityCountObservation(
                new CountObservation(active, CountAvailability.Known, activeDetail),
                new CountObservation(deadLetter, deadLetterAvailability, deadLetterDetail),
                new CountObservation(scheduled, CountAvailability.Known, scheduledDetail)));
    }

    private static void AssertLegend(Border legend)
    {
        if (!legend.IsVisible || legend.ActualHeight <= 0)
            throw new InvalidOperationException("The namespace count legend is not visible.");
        string[] labels = Descendants<TextBlock>(legend).Select(text => text.Text).ToArray();
        foreach (string required in new[] { "Messages", "Scheduled", "DLQ" })
            if (!labels.Contains(required, StringComparer.Ordinal))
                throw new InvalidOperationException($"The namespace count legend is missing the '{required}' label.");
        int swatches = Descendants<Border>(legend).Count(border => border.Width >= 8 && border.Height >= 8
            && Math.Abs(border.Width - border.Height) < 0.1 && border.Background is SolidColorBrush brush && brush.Color.A > 0);
        if (swatches < 3)
            throw new InvalidOperationException("The namespace count legend does not render a colored swatch for every count category.");
    }

    private static void AssertEntityHeader(TreeViewItem item, string expectedName)
    {
        TextBlock name = Descendants<TextBlock>(item).SingleOrDefault(text =>
            BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path == nameof(EntityNode.Name))
            ?? throw new InvalidOperationException("The namespace entity name was not found in the realized tree row.");
        if (name.Text != expectedName || name.TextTrimming != TextTrimming.CharacterEllipsis || name.ActualWidth < 30)
            throw new InvalidOperationException("The long namespace name is missing, not ellipsis-trimmed, or not rendered.");
    }

    private static double NameWidth(TreeViewItem item) => Descendants<TextBlock>(item).Single(text =>
        ReferenceEquals(text.DataContext, item.DataContext)
        && BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path == nameof(EntityNode.Name)).ActualWidth;

    private static void AssertGroupLabel(TreeViewItem item)
    {
        TextBlock label = Descendants<TextBlock>(item).SingleOrDefault(text =>
            ReferenceEquals(text.DataContext, item.DataContext)
            && BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path == nameof(EntityNode.Name))
            ?? throw new InvalidOperationException("A namespace folder label was not rendered.");
        var text = new FormattedText(label.Text, CultureInfo.CurrentCulture, label.FlowDirection,
            new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch),
            label.FontSize, label.Foreground, VisualTreeHelper.GetDpi(label).PixelsPerDip);
        if (label.Text != ((EntityNode)item.DataContext).Name || label.ActualWidth < text.WidthIncludingTrailingWhitespace)
            throw new InvalidOperationException($"The namespace folder label '{label.Text}' was trimmed at the minimum pane width.");
    }

    private static async Task AssertTooltipAndAutomation(
        InvestigationWindow window,
        TreeViewItem item,
        string category,
        long count,
        string outputPath)
    {
        string formatted = count.ToString("N0", CultureInfo.CurrentCulture);
        TextBlock countText = Descendants<TextBlock>(item).SingleOrDefault(text =>
            ReferenceEquals(text.DataContext, item.DataContext)
            && AutomationProperties.GetName(text).Contains(category, StringComparison.Ordinal)
            && AutomationProperties.GetName(text).Contains(formatted, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"The full {category} count is not exposed through the row's automation name.");
        if (!AutomationProperties.GetName(countText).Contains(category, StringComparison.Ordinal)
            || !AutomationProperties.GetName(countText).Contains(formatted, StringComparison.Ordinal))
            throw new InvalidOperationException($"The {category} automation name lost its count or category.");

        if (countText.ToolTip is not ToolTip tooltip)
            throw new InvalidOperationException($"The {category} count has no templated tooltip.");
        tooltip.PlacementTarget = countText;
        tooltip.IsOpen = true;
        await SettleAsync(window, window.Dispatcher);
        if (tooltip.Content is not string tooltipText)
        {
            tooltip.IsOpen = false;
            throw new InvalidOperationException($"The {category} tooltip content binding did not resolve after opening.");
        }
        if (!tooltipText.Contains(category, StringComparison.Ordinal)
            || !tooltipText.Contains(formatted, StringComparison.Ordinal))
        {
            tooltip.IsOpen = false;
            throw new InvalidOperationException($"The {category} tooltip does not preserve its complete formatted count.");
        }
        if (!tooltipText.Contains('\n') && !tooltipText.Contains("\r\n", StringComparison.Ordinal))
        {
            tooltip.IsOpen = false;
            throw new InvalidOperationException($"The {category} tooltip does not contain its expected multiline label and explanation.");
        }
        TextBlock? tooltipContent = Descendants<TextBlock>(tooltip).FirstOrDefault(text => text.Text == tooltipText);
        if (tooltipContent is null || tooltipContent.TextWrapping != TextWrapping.Wrap || tooltipContent.TextAlignment != TextAlignment.Left
            || tooltipContent.MaxWidth is <= 0 or > 340
            || tooltipContent.ActualHeight <= tooltipContent.FontSize * 1.3)
        {
            tooltip.IsOpen = false;
            throw new InvalidOperationException($"The {category} tooltip is not bounded, wrapping, and multiline.");
        }
        if (category == "Messages") SaveTooltip(tooltip, OutputFor(outputPath, "tooltip"));
        tooltip.IsOpen = false;
    }

    private static async Task AssertUnavailableCount(InvestigationWindow window, TreeViewItem topic)
    {
        TextBlock dlq = Descendants<TextBlock>(topic).SingleOrDefault(text => ReferenceEquals(text.DataContext, topic.DataContext) &&
            AutomationProperties.GetName(text).StartsWith("DLQ:", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The topic DLQ count cell was not rendered.");
        if (dlq.Text != "—")
            throw new InvalidOperationException($"An unavailable DLQ count rendered as '{dlq.Text}' instead of an em dash.");
        if (dlq.ToolTip is not ToolTip tooltip)
            throw new InvalidOperationException("The unavailable DLQ value lost its category or explanation in the tooltip.");
        tooltip.PlacementTarget = dlq;
        tooltip.IsOpen = true;
        await SettleAsync(window, window.Dispatcher);
        bool detailed = tooltip.Content is string detail
            && detail.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            && detail.Contains("DLQ", StringComparison.Ordinal);
        tooltip.IsOpen = false;
        if (!detailed)
            throw new InvalidOperationException("The unavailable DLQ value lost its category or explanation in the tooltip.");
    }

    private static void AssertCountColumnsDoNotOverlap(TreeViewItem item)
    {
        TextBlock[] counts = Descendants<TextBlock>(item).Where(text => ReferenceEquals(text.DataContext, item.DataContext) &&
            new[] { "Messages", "Scheduled", "DLQ" }.Any(category =>
                AutomationProperties.GetName(text).StartsWith(category + ":", StringComparison.Ordinal))).ToArray();
        if (counts.Length != 3)
            throw new InvalidOperationException("The tree row did not render exactly three category count cells.");
        Rect[] bounds = counts.Select(text => text.TransformToAncestor(item).TransformBounds(new Rect(new Point(), text.RenderSize))).ToArray();
        for (int index = 0; index < bounds.Length; index++)
        {
            if (bounds[index].Width <= 0 || bounds[index].Left < -1 || bounds[index].Right > item.ActualWidth + 1)
                throw new InvalidOperationException("A namespace count cell is clipped outside its tree row.");
            for (int other = index + 1; other < bounds.Length; other++)
                if (bounds[index].IntersectsWith(bounds[other]))
                    throw new InvalidOperationException($"Namespace count cells overlap: '{AutomationProperties.GetName(counts[index])}' at {bounds[index]} and '{AutomationProperties.GetName(counts[other])}' at {bounds[other]}.");
        }
    }

    private static TreeViewItem Container(ItemsControl parent, int index)
    {
        parent.UpdateLayout();
        return parent.ItemContainerGenerator.ContainerFromIndex(index) as TreeViewItem
            ?? throw new InvalidOperationException($"The namespace tree item at index {index} was not realized.");
    }

    private static async Task SettleAsync(FrameworkElement root, Dispatcher dispatcher)
    {
        root.UpdateLayout();
        await dispatcher.InvokeAsync(root.UpdateLayout, DispatcherPriority.ContextIdle);
    }

    private static T Require<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"The rendered window is missing '{name}'.");

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static string OutputFor(string outputPath, string suffix)
    {
        string full = Path.GetFullPath(outputPath);
        if (Directory.Exists(full) || string.IsNullOrEmpty(Path.GetExtension(full)))
            return Path.Combine(full, $"namespace-tree-{suffix}.png");
        string directory = Path.GetDirectoryName(full) ?? Environment.CurrentDirectory;
        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(full)}-{suffix}.png");
    }

    private static void SaveTooltip(ToolTip tooltip, string outputPath)
    {
        int width = (int)Math.Ceiling(tooltip.ActualWidth);
        int height = (int)Math.Ceiling(tooltip.ActualHeight);
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("The opened count tooltip has no renderable size.");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(tooltip);
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(outputPath);
        encoder.Save(stream);
        Console.WriteLine($"Captured {width}x{height} count tooltip at {outputPath}");
    }
}
