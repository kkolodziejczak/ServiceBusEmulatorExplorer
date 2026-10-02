using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchDestinationTests
{
    [Fact]
    public void Destination_status_distinguishes_complete_partial_unverified_and_wrong_entity_kind_discovery()
    {
        var topic = new WorkbenchDestination("Orders", EntityKind.Topic);
        var sameNamedQueue = new WorkbenchDestination("Orders", EntityKind.Queue);

        Assert.Equal(WorkbenchDestinationStatus.Unset, WorkbenchDestinationAvailability.Evaluate(null, Snapshot(true)));
        Assert.Equal(WorkbenchDestinationStatus.Unverified, WorkbenchDestinationAvailability.Evaluate(topic, null));
        Assert.Equal(WorkbenchDestinationStatus.Available, WorkbenchDestinationAvailability.Evaluate(
            topic, Snapshot(true, new WorkbenchDestination("orders", EntityKind.Topic))));
        Assert.Equal(WorkbenchDestinationStatus.Available, WorkbenchDestinationAvailability.Evaluate(
            topic, Snapshot(false, new WorkbenchDestination("orders", EntityKind.Topic))));
        Assert.Equal(WorkbenchDestinationStatus.Missing, WorkbenchDestinationAvailability.Evaluate(
            sameNamedQueue, Snapshot(true, new WorkbenchDestination("Orders", EntityKind.Topic))));
        Assert.Equal(WorkbenchDestinationStatus.Unverified, WorkbenchDestinationAvailability.Evaluate(
            sameNamedQueue, Snapshot(false, new WorkbenchDestination("Orders", EntityKind.Topic))));
        Assert.Equal(WorkbenchDestinationStatus.Unverified, WorkbenchDestinationAvailability.Evaluate(
            sameNamedQueue, Snapshot(false)));
    }

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Saved_queue_destination_is_matched_by_kind_and_warnings_track_partial_and_complete_discovery()
        => Run((window, view) =>
        {
            view.OpenCapturedDraft("Captured queue", "{}", "orders", "Archive", destinationKind: EntityKind.Queue);
            view.SetDestinationDiscovery("profile-a", 5, Snapshot(true, new WorkbenchDestination("orders", EntityKind.Topic)), "sb://orders.test/");
            WaitFor(window.Dispatcher, () => Warning(view).Visibility == Visibility.Visible);

            Assert.Contains("unavailable", Warning(view).Text, StringComparison.OrdinalIgnoreCase);
            TreeViewItem captured = TreeItems(Get<TreeView>(view, "LibraryTree")).Single(item =>
                Equals(item.Tag, "template:Captured queue"));
            Assert.Contains(Descendants(Assert.IsType<Grid>(captured.Header)), item =>
                item is FrameworkElement element
                && AutomationProperties.GetAutomationId(element) == "LibraryDestinationWarning");
            Assert.Equal("orders", Get<ComboBox>(view, "TemplateDestination").SelectedValue);

            view.SetDestinationDiscovery("profile-a", 6, Snapshot(false, new WorkbenchDestination("orders", EntityKind.Topic)), "sb://orders.test/");
            WaitFor(window.Dispatcher, () => Warning(view).Visibility == Visibility.Visible
                && Warning(view).Text.Contains("not verified", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("unavailable", Warning(view).Text, StringComparison.OrdinalIgnoreCase);

            view.SetDestinationDiscovery("profile-a", 7, Snapshot(true,
                new WorkbenchDestination("orders", EntityKind.Topic), new WorkbenchDestination("orders", EntityKind.Queue)), "sb://orders.test/");
            WaitFor(window.Dispatcher, () => Warning(view).Visibility == Visibility.Collapsed
                && Get<Button>(view, "ReviewButton").IsEnabled);
            Assert.Equal("orders", Get<ComboBox>(view, "TemplateDestination").SelectedValue);
            TreeViewItem available = TreeItems(Get<TreeView>(view, "LibraryTree")).Single(item =>
                Equals(item.Tag, "template:Captured queue"));
            Assert.DoesNotContain(Descendants(Assert.IsType<Grid>(available.Header)), item =>
                item is FrameworkElement element
                && AutomationProperties.GetAutomationId(element) == "LibraryDestinationWarning");
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Missing_destination_after_review_returns_to_prepare_and_blocks_send_while_preserving_saved_name()
        => Run((window, view) =>
        {
            view.SetDestinationDiscovery("profile-a", 1, Snapshot(true, new WorkbenchDestination("order-events", EntityKind.Topic)));
            WaitFor(window.Dispatcher, () => Get<Button>(view, "ReviewButton").IsEnabled);
            Get<Button>(view, "ContinueToPrepareButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitFor(window.Dispatcher, () => Get<Button>(view, "ReviewButton").IsEnabled);
            Get<Button>(view, "ReviewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, Get<ContentControl>(view, "ReviewHost").Visibility);

            view.SetDestinationDiscovery("profile-a", 2, Snapshot(true));
            WaitFor(window.Dispatcher, () => Get<Grid>(view, "PreparePane").Visibility == Visibility.Visible
                && Warning(view).Visibility == Visibility.Visible);

            Assert.Contains("unavailable", Warning(view).Text, StringComparison.OrdinalIgnoreCase);
            Assert.False(Get<Button>(view, "ReviewButton").IsEnabled);
            Assert.Equal("order-events", Get<ComboBox>(view, "TemplateDestination").SelectedValue);
            Assert.Equal(Visibility.Visible, Get<Border>(view, "PrepareDestinationWarning").Visibility);
            Assert.Contains("Topic \"order-events\" is unavailable", Get<TextBlock>(view, "PrepareDestinationWarningText").Text,
                StringComparison.Ordinal);
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Profile_switch_and_stale_or_failed_discovery_never_mark_a_destination_missing()
        => Run((window, view) =>
        {
            view.SetDestinationDiscovery("profile-a", 8, Snapshot(true, new WorkbenchDestination("order-events", EntityKind.Topic)));
            WaitFor(window.Dispatcher, () => Warning(view).Visibility == Visibility.Collapsed);

            view.SetDestinationDiscovery("profile-b", 9, null);
            WaitFor(window.Dispatcher, () => Warning(view).Visibility == Visibility.Visible);
            Assert.Contains("not verified", Warning(view).Text, StringComparison.OrdinalIgnoreCase);

            view.SetDestinationDiscovery("profile-a", 8, Snapshot(true));
            WaitFor(window.Dispatcher, () => Warning(view).Visibility == Visibility.Visible);
            Assert.Contains("not verified", Warning(view).Text, StringComparison.OrdinalIgnoreCase);

            view.SetDestinationDiscovery("profile-b", 10, Snapshot(false));
            WaitFor(window.Dispatcher, () => Warning(view).Visibility == Visibility.Visible);
            Assert.Contains("not verified", Warning(view).Text, StringComparison.OrdinalIgnoreCase);

            view.SetDestinationDiscovery("profile-b", 11, Snapshot(true));
            WaitFor(window.Dispatcher, () => Warning(view).Text.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
            Assert.False(Get<Button>(view, "ReviewButton").IsEnabled);
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Warning_actions_reveal_destination_preserve_draft_and_gate_both_prepare_routes()
        => Run((window, view) =>
        {
            TreeItems(Get<TreeView>(view, "LibraryTree")).Single(item => Equals(item.Tag, "template:Order created")).IsSelected = true;
            var subject = Get<TextBox>(view, "PropertySubject");
            subject.Text = "Keep this unsaved subject";
            view.SetDestinationDiscovery("profile-a", 1, Snapshot(true));
            Assert.False(Get<Button>(view, "ContinueToPrepareButton").IsEnabled);
            Assert.False(Get<Button>(view, "PrepareStepButton").IsEnabled);
            foreach (string route in new[] { "ContinueToPrepareButton", "PrepareStepButton" })
            {
                Get<Button>(view, route).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, Get<Grid>(view, "AuthorPane").Visibility);
            }
            Get<Button>(view, "ComposeDestinationAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal(Visibility.Visible, Get<ScrollViewer>(view, "PropertiesEditorSurface").Visibility);
            Assert.Equal("Keep this unsaved subject", subject.Text);
            Assert.Equal(Visibility.Visible, Get<Border>(view, "ComposeDestinationWarning").Visibility);
            var propertiesSurface = Get<ScrollViewer>(view, "PropertiesEditorSurface");
            var destination = Get<ComboBox>(view, "TemplateDestination");
            Rect destinationBounds = destination.TransformToAncestor(propertiesSurface).TransformBounds(new Rect(destination.RenderSize));
            Assert.True(destinationBounds.Top >= -0.5 && destinationBounds.Bottom <= propertiesSurface.ViewportHeight + 0.5,
                $"The destination remedy should bring the destination field into the visible viewport: field={destinationBounds}, viewport={propertiesSurface.ViewportHeight}.");
            Assert.True(destination.IsKeyboardFocusWithin, "The destination remedy should focus the destination field.");

            view.SetDestinationDiscovery("profile-a", 2, Snapshot(true,
                new WorkbenchDestination("order-events", EntityKind.Topic)));
            Assert.True(Get<Button>(view, "ContinueToPrepareButton").IsEnabled);
            Assert.True(Get<Button>(view, "PrepareStepButton").IsEnabled);
            Assert.Equal(Visibility.Collapsed, Get<Border>(view, "ComposeDestinationWarning").Visibility);
            Get<Button>(view, "ContinueToPrepareButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, Get<Grid>(view, "PreparePane").Visibility);
            view.SetDestinationDiscovery("profile-a", 3, Snapshot(true));
            Assert.Equal(Visibility.Visible, Get<Border>(view, "PrepareDestinationWarning").Visibility);
            Assert.Equal(Warning(view).Text, Get<TextBlock>(view, "PrepareDestinationWarningText").Text);
            Get<Button>(view, "PrepareDestinationAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal(Visibility.Visible, Get<Grid>(view, "AuthorPane").Visibility);
            Assert.Equal(Visibility.Visible, Get<ScrollViewer>(view, "PropertiesEditorSurface").Visibility);
            Assert.Equal("Keep this unsaved subject", subject.Text);
            Assert.Equal("order-events", Get<ComboBox>(view, "TemplateDestination").SelectedValue);

            bool connectionRequested = false;
            view.ConnectionRepairRequested += () => connectionRequested = true;
            view.SetDestinationDiscovery("profile-a", 4, null);
            Assert.Equal("Check connection", Get<Button>(view, "ComposeDestinationAction").Content);
            Get<Button>(view, "ComposeDestinationAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(connectionRequested);
            Assert.Equal("Keep this unsaved subject", subject.Text);
            // Unset templates remain editable and saveable, but cannot advance.
            Get<ComboBox>(view, "TemplateDestination").SelectedIndex = 0;
            Assert.Contains("No destination selected", Warning(view).Text);
            Assert.Equal("template:Order created", Assert.IsType<TreeViewItem>(Get<TreeView>(view, "LibraryTree").SelectedItem).Tag);
            Assert.False(Get<Button>(view, "ContinueToPrepareButton").IsEnabled);
        });
    private static EntityDiscoverySnapshot Snapshot(bool complete, params WorkbenchDestination[] destinations)
        => new(destinations.Select(destination => new EntityObservation(
                new DiscoveredEntity(destination.Kind, destination.Name, null,
                    new EntityMetadata(destination.Name, "Active", null, null, null, null, null, null, null)),
                new EntityCountObservation(
                    new(null, CountAvailability.NotSupported),
                    new(null, CountAvailability.NotSupported),
                    new(null, CountAvailability.NotSupported))))
            .ToArray(), DateTimeOffset.UtcNow, complete, []);

    private static TextBlock Warning(MessageLibraryPrototypeView view) => Get<TextBlock>(view, "DestinationWarning");

    private static T Get<T>(FrameworkElement root, string name) where T : class =>
        Assert.IsAssignableFrom<T>(root.FindName(name));

    private static IEnumerable<TreeViewItem> TreeItems(TreeView tree)
    {
        foreach (TreeViewItem root in tree.Items.OfType<TreeViewItem>())
        {
            yield return root;
            foreach (TreeViewItem descendant in TreeItems(root)) yield return descendant;
        }
    }

    private static IEnumerable<TreeViewItem> TreeItems(TreeViewItem parent)
    {
        foreach (TreeViewItem child in parent.Items.OfType<TreeViewItem>())
        {
            yield return child;
            foreach (TreeViewItem descendant in TreeItems(child)) yield return descendant;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void WaitFor(Dispatcher dispatcher, Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Destination availability UI did not update.");
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static void Run(Action<Window, MessageLibraryPrototypeView> proof)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var view = new MessageLibraryPrototypeView();
                window = new Window { Content = view, Width = 1337, Height = 850,
                    ShowActivated = false, ShowInTaskbar = false };
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                proof(window, view);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Destination availability proof exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
