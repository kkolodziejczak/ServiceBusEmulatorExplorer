using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationWatchRenderTests
{
    [Theory]
    [InlineData(540, 640)]
    [InlineData(460, 520)]
    [Trait("TestCategory", "UiRender")]
    public void Watch_dialog_supports_independent_buckets_mixed_inclusion_search_and_failed_save(int width, int height)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Render(width, height); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Watch rendering exceeded its 20-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Render(int width, int height)
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var queues = new EntityNode("Queues", "QueueGroup");
        queues.Children.Add(Entity(EntityKind.Queue, "checkout-requests"));
        var topics = new EntityNode("Topics", "TopicGroup");
        var topic = Entity(EntityKind.Topic, "order-events");
        var billing = Entity(EntityKind.Subscription, "billing", topic.Name);
        string longName = "analytics-with-an-unusually-long-subscription-name-for-fulfilment-and-audit";
        var analytics = Entity(EntityKind.Subscription, longName, topic.Name);
        topic.Children.Add(billing);
        topic.Children.Add(analytics);
        topics.Children.Add(topic);
        EntityNode[] roots = [queues, topics];
        var initial = new WatchRuleEditor(roots, []);
        initial.SetIncluded(billing, false);
        WatchRuleEditor? saved = null;
        TaskCompletionSource<bool>? pending = null;
        int saves = 0;
        var window = new GlobalWatchWindow(roots, initial, async changed =>
        {
            saves++;
            bool accepted = pending is null || await pending.Task;
            if (accepted) saved = new WatchRuleEditor(roots, changed.Rules);
            return accepted;
        }) { Width = width, Height = height };
        Exception? modalFailure = null;
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        watchdog.Tick += (_, _) =>
        {
            modalFailure ??= new TimeoutException("Watch dialog interaction timed out.");
            window.Close();
        };
        try
        {
            window.Loaded += async (_, _) =>
            {
                try
                {
                    await Idle(dispatcher);
                    var active = (CheckBox)window.FindName("GlobalActiveChoice");
                    var dlq = (CheckBox)window.FindName("GlobalDlqChoice");
                    var search = (TextBox)window.FindName("InclusionSearch");
                    var tree = (TreeView)window.FindName("InclusionTree");
                    var empty = (TextBlock)window.FindName("NoEntitiesMatch");
                    var done = (Button)window.FindName("DoneButton");
                    AssertBounds(window, active, dlq, search, tree, done);
                    Assert.Equal("Search watched entities", AutomationProperties.GetName(search));
                    Assert.Null(Choice(window, topic.Path).IsChecked);
                    Assert.Null(Choice(window, topics.Path).IsChecked);
                    Capture(window, $"watch-{width}x{height}");
                    Assert.True(active.Focus());
                    Assert.True(active.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                    Assert.True(dlq.IsKeyboardFocused, "Forward keyboard traversal should reach the next bucket choice.");

                    Toggle(active);
                    await Idle(dispatcher);
                    Assert.True(active.IsChecked);
                    Assert.False(dlq.IsChecked);
                    Assert.NotNull(saved);
                    Assert.True(saved.GlobalActive);
                    Assert.False(saved.GlobalDeadLetter);
                    Toggle(dlq);
                    await Idle(dispatcher);
                    Assert.True(active.IsChecked);
                    Assert.True(dlq.IsChecked);
                    Assert.True(saved!.GlobalDeadLetter);

                    Toggle(Choice(window, topic.Path));
                    await Idle(dispatcher);
                    Assert.True(Choice(window, topic.Path).IsChecked);
                    Assert.True(Choice(window, billing.Path).IsChecked);
                    Assert.True(Choice(window, analytics.Path).IsChecked);
                    Assert.True(saved!.IsWatched(billing, false));
                    Assert.True(saved.IsWatched(billing, true));

                    SetText(search, "not-an-existing-entity");
                    await Idle(dispatcher);
                    Assert.Empty(tree.Items.Cast<object>());
                    Assert.True(empty.IsVisible);
                    AssertBounds(window, empty, done);
                    SetText(search, longName);
                    await Idle(dispatcher);
                    Assert.False(empty.IsVisible);
                    CheckBox longChoice = Choice(window, analytics.Path);
                    TextBlock longLabel = Descendants<TextBlock>(longChoice).Single(text => text.Text == longName);
                    AssertBounds(window, longChoice, longLabel);
                    Assert.Equal(analytics.Path, longChoice.ToolTip);
                    Capture(window, $"watch-{width}x{height}-long-name");
                    SetText(search, "");
                    await Idle(dispatcher);

                    pending = new TaskCompletionSource<bool>();
                    int beforeFailure = saves;
                    Toggle(active);
                    await Idle(dispatcher);
                    Assert.False(active.IsEnabled);
                    Assert.False(dlq.IsEnabled);
                    Assert.False(tree.IsEnabled);
                    Assert.False(done.IsEnabled);
                    pending.SetResult(false);
                    await Idle(dispatcher);
                    // Dispatch the continuation before examining the restored state.
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.True(active.IsEnabled);
                    Assert.True(dlq.IsEnabled);
                    Assert.True(tree.IsEnabled);
                    Assert.True(done.IsEnabled);
                    Assert.True(active.IsChecked);
                    Assert.True(dlq.IsChecked);
                    Assert.Equal(beforeFailure + 1, saves);
                    Assert.True(saved!.GlobalActive);

                    // Background workspace notifications must leave an unchanged
                    // editor's navigation position and collapsed branches intact.
                    CheckBox focusedTopics = Choice(window, topics.Path);
                    TreeViewItem topicsItem = tree.Items.Cast<TreeViewItem>()
                        .Single(item => Equals(item.Tag, topics.Path));
                    topicsItem.IsExpanded = false;
                    Assert.True(focusedTopics.Focus());
                    int beforeEquivalentRefresh = saves;
                    window.RefreshRules(roots, new WatchRuleEditor(roots, saved.Rules));
                    await Idle(dispatcher);
                    Assert.Same(focusedTopics, Choice(window, topics.Path));
                    Assert.True(focusedTopics.IsKeyboardFocused);
                    Assert.False(topicsItem.IsExpanded);
                    Assert.Equal(beforeEquivalentRefresh, saves);
                    topicsItem.IsExpanded = true;
                    await Idle(dispatcher);

                    // A scope change made in the workspace while this dialog is open must
                    // survive the next dialog edit, including newly discovered entities.
                    pending = null;
                    var external = new WatchRuleEditor(roots, saved.Rules);
                    external.SetScope(billing, true, false);
                    var discoveredQueue = Entity(EntityKind.Queue, "newly-discovered-queue");
                    queues.Children.Add(discoveredQueue);
                    int beforeRefresh = saves;
                    window.RefreshRules(roots, external);
                    await Idle(dispatcher);
                    Assert.Equal(beforeRefresh, saves);
                    Assert.True(Choice(window, topics.Path).IsKeyboardFocused);
                    Assert.True(Choice(window, discoveredQueue.Path).IsChecked);
                    Toggle(active);
                    await Idle(dispatcher);
                    Assert.False(active.IsChecked);
                    Assert.True(dlq.IsChecked);
                    Assert.False(saved!.IsWatched(billing, true));
                    Assert.True(saved.IsWatched(analytics, true));
                    Assert.True(saved.IsWatched(discoveredQueue, true));
                    Assert.Equal(beforeRefresh + 1, saves);
                    var peer = new ButtonAutomationPeer(done);
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
                }
                catch (Exception exception)
                {
                    modalFailure = exception;
                    window.Close();
                }
            };
            watchdog.Start();
            window.ShowDialog();
            if (modalFailure is not null) ExceptionDispatchInfo.Capture(modalFailure).Throw();
            Assert.False(window.IsVisible);
        }
        finally
        {
            watchdog.Stop();
            pending?.TrySetResult(false);
            if (window.IsVisible) window.Close();
            dispatcher.InvokeShutdown();
        }
    }

    private static async Task Idle(Dispatcher dispatcher) =>
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

    private static CheckBox Choice(Window window, string path) => Descendants<CheckBox>(window)
        .Single(choice => AutomationProperties.GetAutomationId(choice) == "Include:" + path);

    private static void Toggle(CheckBox choice) =>
        ((IToggleProvider)new CheckBoxAutomationPeer(choice).GetPattern(PatternInterface.Toggle)!).Toggle();

    private static void SetText(TextBox field, string value) =>
        ((IValueProvider)new TextBoxAutomationPeer(field).GetPattern(PatternInterface.Value)!).SetValue(value);

    private static EntityNode Entity(EntityKind kind, string name, string? topic = null) => new(name, kind.ToString(),
        new EntityObservation(new DiscoveredEntity(kind, name, topic,
            new EntityMetadata(name, "Active", null, null, null, null, null, null, null)),
            new EntityCountObservation(new(0, CountAvailability.Known), new(0, CountAvailability.Known),
                new(0, CountAvailability.Known))));

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void AssertBounds(Window window, params FrameworkElement[] elements)
    {
        window.UpdateLayout();
        foreach (FrameworkElement element in elements)
        {
            Assert.True(element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0);
            Rect bounds = element.TransformToAncestor(window).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= window.ActualWidth + 1 && bounds.Bottom <= window.ActualHeight + 1,
                $"{element.GetType().Name} {element.Name} is clipped outside Watch: {bounds}.");
        }
    }

    private static void Capture(Window window, string name)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_INVESTIGATION_UI"), "true", StringComparison.OrdinalIgnoreCase)) return;
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git"))) root = root.Parent;
        Assert.NotNull(root);
        string directory = Path.Combine(root.FullName, "artifacts", "investigation-ui");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }
}
