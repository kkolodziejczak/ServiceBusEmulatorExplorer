using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class DestinationScreenshotScenario
{
    private const string LongTopic = "retail-order-events-for-regional-fulfilment-and-audit-trail";
    private const string LongQueue = "retail-order-replies-for-regional-fulfilment-and-customer-notifications";

    public static int Run(string outputDirectory)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int result = 1;
        app.Startup += async (_, _) =>
        {
            Window? window = null;
            try
            {
                foreach ((int width, int height) in new[] { (1500, 1000), (1100, 800), (980, 640) })
                {
                    await using var workspace = await RetailScreenshotScenario.CreateWorkspaceAsync();
                    NativeWindowSizeOverride? sizeOverride = null;
                    window = new InvestigationWindow(workspace)
                    {
                        Width = width, Height = height, ShowActivated = false, ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                        WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0
                    };
                    window.SourceInitialized += (_, _) => sizeOverride = NativeWindowSizeOverride.Install(window, width, height);
                    window.Width = width;
                    window.Height = height;
                    window.Show();
                    await Idle(window);
                    window.Width = width;
                    window.Height = height;
                    await Idle(window);
                    window.UpdateLayout();
                    Require(((FrameworkElement)window.Content).ActualWidth == width && ((FrameworkElement)window.Content).ActualHeight == height, "Requested viewport dimensions must match the rendered surface.");
                    ((ToggleButton)window.FindName("MessageLibraryTab")!).IsChecked = true;
                    var view = (MessageLibraryPrototypeView)window.FindName("MessageLibraryPrototype")!;
                    var libraryTree = Get<TreeView>(view, "LibraryTree");
                    var ordersFolder = libraryTree.Items.OfType<TreeViewItem>().Single(item => Equals(item.Tag, "folder:Orders"));
                    ordersFolder.Items.OfType<TreeViewItem>().Single(item => Equals(item.Tag, "template:Order created")).IsSelected = true;
                    Click(view, "EditorPropertiesTab");
                    await Idle(window);
                    var selector = Get<ComboBox>(view, "TemplateDestination");
                    view.SetDestinationDiscovery("destination-proof", 1, Snapshot(true,
                        new WorkbenchDestination("order-events", EntityKind.Topic),
                        new WorkbenchDestination("order-replies", EntityKind.Queue),
                        new WorkbenchDestination(LongTopic, EntityKind.Topic),
                        new WorkbenchDestination(LongQueue, EntityKind.Queue)));
                    await Idle(window);
                    selector.BringIntoView();
                    await Idle(window);
                    RequireInside(selector, window, "Selected destination");
                    Capture(window, outputDirectory, $"destination-{width}-topic-selected");
                    SelectDestination(selector, LongTopic, EntityKind.Topic);
                    await Idle(window);
                    selector.BringIntoView();
                    await Idle(window);
                    RequireInside(selector, window, "Selected long topic");
                    Capture(window, outputDirectory, $"destination-{width}-long-topic-selected");
                    CapturePopup(window, selector, outputDirectory, $"destination-{width}-long-topic-popup");
                    SelectDestination(selector, LongQueue, EntityKind.Queue);
                    await Idle(window);
                    selector.BringIntoView();
                    await Idle(window);
                    RequireInside(selector, window, "Selected long queue");
                    Capture(window, outputDirectory, $"destination-{width}-long-queue-selected");
                    CapturePopup(window, selector, outputDirectory, $"destination-{width}-long-queue-popup");

                    view.SetDestinationDiscovery("destination-proof", 2, Snapshot(true));
                    await Idle(window);
                    AssertComposeWarningState(window, view, "unavailable");
                    var bodyBeforeWarningAction = Get<JsonEditor>(view, "EditorText").Text;
                    var subjectBeforeWarningAction = Get<TextBox>(view, "PropertySubject").Text;
                    Capture(window, outputDirectory, $"destination-{width}-compose-missing");
                    Click(view, "ComposeDestinationAction");
                    await Idle(window);
                    AssertDestinationActionNavigation(view, bodyBeforeWarningAction, subjectBeforeWarningAction);
                    Capture(window, outputDirectory, $"destination-{width}-compose-missing-action-focused");

                    view.SetDestinationDiscovery("destination-proof", 3, Snapshot(true,
                        new WorkbenchDestination("order-events", EntityKind.Topic),
                        new WorkbenchDestination("order-replies", EntityKind.Queue),
                        new WorkbenchDestination(LongTopic, EntityKind.Topic),
                        new WorkbenchDestination(LongQueue, EntityKind.Queue)));
                    await Idle(window);
                    selector = Get<ComboBox>(view, "TemplateDestination");
                    SelectDestination(selector, "order-events", EntityKind.Topic);
                    Click(view, "ContinueToPrepareButton");
                    await Idle(window);
                    WaitFor(window, () => Get<Button>(view, "ReviewButton").IsEnabled);
                    AssertWarningState(view, Visibility.Collapsed, "", reviewEnabled: true);
                    AssertPrepareLayout(window, view);
                    Capture(window, outputDirectory, $"destination-{width}-prepare-available");

                    view.SetDestinationDiscovery("destination-proof", 4, Snapshot(true));
                    await Idle(window);
                    AssertWarningState(view, Visibility.Visible, "unavailable", reviewEnabled: false);
                    AssertPrepareLayout(window, view);
                    Capture(window, outputDirectory, $"destination-{width}-prepare-missing");
                    Click(view, "PrepareDestinationAction");
                    await Idle(window);
                    AssertDestinationActionNavigation(view, bodyBeforeWarningAction, subjectBeforeWarningAction);
                    Capture(window, outputDirectory, $"destination-{width}-prepare-missing-action-return");

                    view.SetDestinationDiscovery("destination-proof", 5, Snapshot(true,
                        new WorkbenchDestination("order-events", EntityKind.Topic),
                        new WorkbenchDestination("order-replies", EntityKind.Queue)));
                    await Idle(window);
                    selector = Get<ComboBox>(view, "TemplateDestination");
                    SelectDestination(selector, "order-events", EntityKind.Topic);
                    Click(view, "ContinueToPrepareButton");
                    await Idle(window);
                    WaitFor(window, () => Get<Button>(view, "ReviewButton").IsEnabled);
                    view.SetDestinationDiscovery("destination-proof", 6, null);
                    await Idle(window);
                    AssertWarningState(view, Visibility.Visible, "not verified", reviewEnabled: false);
                    AssertPrepareLayout(window, view);
                    Capture(window, outputDirectory, $"destination-{width}-prepare-unverified");
                    Click(view, "PrepareDestinationAction");
                    await Idle(window);
                    Require(((Button)window.FindName("ConnectionButton")!).IsKeyboardFocusWithin,
                        "An unverified destination must direct focus to the connection control.");

                    Click(view, "BackToComposeButton");
                    await Idle(window);
                    view.SetDestinationDiscovery("destination-proof", 7, Snapshot(true,
                        new WorkbenchDestination("order-events", EntityKind.Topic),
                        new WorkbenchDestination("order-replies", EntityKind.Queue)));
                    await Idle(window);
                    SelectDestination(Get<ComboBox>(view, "TemplateDestination"), "order-events", EntityKind.Topic);
                    Click(view, "ContinueToPrepareButton");
                    await Idle(window);
                    WaitFor(window, () => Get<Button>(view, "ReviewButton").IsEnabled);
                    var review = Get<Button>(view, "ReviewButton");
                    Require(review.IsEnabled, "A positively discovered destination must enable Review.");
                    Click(view, "ReviewButton");
                    await Idle(window);
                    Require(Get<ContentControl>(view, "ReviewHost").Visibility == Visibility.Visible,
                        "The available destination must route to Review.");
                    var reviewSurface = Get<ContentControl>(view, "ReviewHost").Content as MessageLibraryPrototypeReviewSurface
                        ?? throw new InvalidOperationException("The embedded Review surface was not found.");
                    var schedule = ReviewElement<RadioButton>(reviewSurface, "ReviewSchedule");
                    schedule.IsChecked = true;
                    await Idle(window);
                    var scheduleInputs = ReviewElement<FrameworkElement>(reviewSurface, "ScheduleInputs");
                    Require(scheduleInputs.Visibility == Visibility.Visible, "The embedded schedule inputs must appear when Schedule is selected.");
                    foreach (string inputName in new[] { "ScheduleDateInput", "ScheduleTimeInput", "ScheduleUtc", "ScheduleLocal", "ResolvedSchedule" })
                    {
                        var input = ReviewElement<FrameworkElement>(reviewSurface, inputName);
                        input.BringIntoView();
                        await Idle(window);
                        RequireInside(input, window, $"Embedded Review schedule {inputName} at {width}px");
                    }
                    Capture(window, outputDirectory, $"destination-{width}-review-schedule");
                    var summaryCard = (FrameworkElement)reviewSurface.FindName("ReviewSummaryCard");
                    var reviewScroll = (ScrollViewer)reviewSurface.FindName("ReviewScroll");
                    reviewScroll.ScrollToVerticalOffset(reviewScroll.VerticalOffset + summaryCard.TransformToAncestor(reviewScroll).Transform(new Point()).Y);
                    await Idle(window);
                    Capture(window, outputDirectory, $"destination-{width}-review-schedule-summary-top");
                    reviewScroll.ScrollToEnd();
                    await Idle(window);
                    var summaryBottom = summaryCard.TransformToAncestor(reviewScroll).Transform(new Point(0, summaryCard.ActualHeight));
                    Require(summaryBottom.Y > 0 && summaryBottom.Y <= reviewScroll.ActualHeight, "The end of the Review summary must be reachable inside its scroll viewport.");
                    Capture(window, outputDirectory, $"destination-{width}-review-schedule-summary");
                    view.SetDestinationDiscovery("destination-proof", 8, Snapshot(true));
                    await Idle(window);
                    Require(Get<Grid>(view, "PreparePane").Visibility == Visibility.Visible,
                        "A destination that becomes missing in Review must return to Prepare.");
                    AssertWarningState(view, Visibility.Visible, "unavailable", reviewEnabled: false);
                    AssertPrepareLayout(window, view);
                    Capture(window, outputDirectory, $"destination-{width}-review-return-missing");

                    Click(view, "BackToComposeButton");
                    await Idle(window);
                    selector = Get<ComboBox>(view, "TemplateDestination");
                    selector.SelectedIndex = 0;
                    await Idle(window);
                    AssertComposeWarningState(window, view, "No destination selected");
                    Capture(window, outputDirectory, $"destination-{width}-compose-unset");
                    window.Close();
                    sizeOverride?.Dispose();
                    window = null;
                }
                result = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { window?.Close(); app.Shutdown(result); }
        };
        app.Run();
        return result;
    }

    private static EntityDiscoverySnapshot Snapshot(bool complete, params WorkbenchDestination[] destinations)
        => new(destinations.Select(destination => new EntityObservation(
                new DiscoveredEntity(destination.Kind, destination.Name, null,
                    new EntityMetadata(destination.Name, "Active", null, null, null, null, null, null, null)),
                new EntityCountObservation(
                    new(null, CountAvailability.NotSupported),
                    new(null, CountAvailability.NotSupported),
                    new(null, CountAvailability.NotSupported))))
            .ToArray(), DateTimeOffset.UtcNow, complete, []);

    private static void SelectDestination(ComboBox selector, string name, EntityKind kind)
    {
        selector.SelectedItem = selector.Items.OfType<ComboBoxItem>().Single(item =>
            item.DataContext is WorkbenchDestination destination && destination.Name == name && destination.Kind == kind);
        var selected = (ComboBoxItem)selector.SelectedItem;
        Require(Equals(selected.Tag, name) && selected.DataContext is WorkbenchDestination { } destination && destination.Kind == kind,
            "Selection must preserve destination name in Tag and typed destination in DataContext.");
        Require(AutomationProperties.GetName(selected) == $"{name}, {kind}",
            "Destination choices must expose both the entity name and kind to accessibility clients.");
    }

    private static void CapturePopup(Window window, ComboBox selector, string directory, string name)
    {
        selector.IsDropDownOpen = true;
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var popup = (Popup)selector.Template.FindName("PART_Popup", selector);
        var surface = (FrameworkElement)(popup.Child
            ?? throw new InvalidOperationException("The destination selector popup did not open."));
        surface.UpdateLayout();
        Require(surface.ActualWidth > 0 && surface.ActualHeight > 0,
            "The destination selector popup must have rendered bounds.");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),
            (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(directory, name + "-popup.png"))) encoder.Save(file);
        selector.IsDropDownOpen = false;
        window.UpdateLayout();
    }

    private static void AssertWarningState(MessageLibraryPrototypeView view, Visibility visibility, string expectedText, bool reviewEnabled)
    {
        var warning = Get<Border>(view, "PrepareDestinationWarning");
        string text = Get<TextBlock>(view, "PrepareDestinationWarningText").Text;
        Require(warning.Visibility == visibility, $"Prepare warning visibility should be {visibility}.");
        Require(expectedText.Length == 0 || text.Contains(expectedText, StringComparison.OrdinalIgnoreCase),
            $"Prepare warning should contain '{expectedText}', actual: '{text}'.");
        Require(Get<Button>(view, "ReviewButton").IsEnabled == reviewEnabled,
            $"Review should be {(reviewEnabled ? "enabled" : "disabled")} for this destination state.");
        Require(Get<FrameworkElement>(view, "PreviewSurface").Visibility == Visibility.Visible,
            "The prepared message preview must remain visible while destination status changes.");
        Require(Get<Button>(view, "PrepareDestinationAction").IsEnabled,
            "The Prepare warning action must remain available so the destination can be repaired.");
    }

    private static void AssertComposeWarningState(Window window, MessageLibraryPrototypeView view, string expectedText)
    {
        var warning = Get<Border>(view, "ComposeDestinationWarning");
        Require(warning.Visibility == Visibility.Visible, "The Compose warning frame must be visible for an invalid destination.");
        Require(Get<TextBlock>(view, "DestinationWarning").Text.Contains(expectedText, StringComparison.OrdinalIgnoreCase),
            $"The Compose warning should contain '{expectedText}'.");
        Require(Get<Button>(view, "ComposeDestinationAction").IsEnabled,
            "The Compose warning action must remain available so the destination can be repaired.");
        Require(!Get<Button>(view, "ContinueToPrepareButton").IsEnabled && !Get<Button>(view, "PrepareStepButton").IsEnabled,
            "An invalid destination must block both ways to enter Prepare.");
        RequireInside(warning, window, "Compose destination warning frame");
        RequireInside(Get<Button>(view, "ComposeDestinationAction"), window, "Compose destination action");
    }

    private static void AssertDestinationActionNavigation(MessageLibraryPrototypeView view, string body, string subject)
    {
        Require(Get<Grid>(view, "AuthorPane").Visibility == Visibility.Visible
            && Get<Grid>(view, "PreparePane").Visibility == Visibility.Collapsed,
            "Choosing a destination from the warning must return to Compose.");
        Require(Get<JsonEditor>(view, "EditorText").Text == body && Get<TextBox>(view, "PropertySubject").Text == subject,
            "Choosing a destination must preserve the current message draft and properties.");
        Require(Get<TreeView>(view, "LibraryTree").SelectedItem is TreeViewItem { IsSelected: true }, "Warning navigation must preserve visible library selection.");
        var selector = Get<ComboBox>(view, "TemplateDestination");
        Require(selector.IsKeyboardFocusWithin,
            "Choosing a destination from the warning must focus the destination selector.");
    }

    private static T ReviewElement<T>(MessageLibraryPrototypeReviewSurface surface, string name) where T : FrameworkElement =>
        surface.NamedElements.TryGetValue(name, out var element) && element is T typed
            ? typed : throw new InvalidOperationException($"Could not find embedded Review element {name}.");

    private static void AssertPrepareLayout(Window window, MessageLibraryPrototypeView view)
    {
        if (Get<Border>(view, "PrepareDestinationWarning").Visibility == Visibility.Visible)
        {
            RequireInside(Get<Border>(view, "PrepareDestinationWarning"), window, "Prepare destination warning");
            RequireInside(Get<Button>(view, "PrepareDestinationAction"), window, "Prepare destination action");
        }
        RequireInside(Get<FrameworkElement>(view, "PreviewSurface"), window, "Message preview");
        RequireInside(Get<Button>(view, "ReviewButton"), window, "Prepare footer Review action");
        Require(Get<TextBlock>(view, "PrepareDestinationWarningText").TextWrapping == TextWrapping.Wrap,
            "Prepare warning text must wrap at compact widths.");
    }

    private static void RequireInside(FrameworkElement element, FrameworkElement ancestor, string name)
    {
        Rect bounds = element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
        Require(element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0
            && bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= ancestor.ActualWidth && bounds.Bottom <= ancestor.ActualHeight,
            $"{name} must remain visible and in bounds: {bounds}, ancestor={ancestor.ActualWidth}x{ancestor.ActualHeight}.");
    }

    private static T Get<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"Could not find {name}.");

    private static void Click(FrameworkElement root, string name)
    {
        var button = Get<Button>(root, name);
        Require(button.IsEnabled, $"The {name} action must be enabled.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        Window.GetWindow(root)?.UpdateLayout();
    }

    private static Task Idle(Window window) => window.Dispatcher.InvokeAsync(
        () => window.UpdateLayout(), DispatcherPriority.ContextIdle).Task;

    private static void WaitFor(Window window, Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Destination screenshot scenario did not reach its expected state.");
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(10);
        }
    }

    private static void Capture(Window window, string directory, string name)
    {
        window.UpdateLayout();
        WpfScreenshot.SaveWindowContent(window, Path.Combine(directory, name + ".png"),
            (int)window.ActualWidth, (int)window.ActualHeight);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
