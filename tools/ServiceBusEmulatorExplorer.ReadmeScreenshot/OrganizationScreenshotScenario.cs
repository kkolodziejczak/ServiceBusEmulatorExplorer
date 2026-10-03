using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class OrganizationScreenshotScenario
{
    private static readonly (string Name, int Width, int Height)[] Viewports =
    [
        ("wide", 1642, 958),
        ("desktop", 1500, 1000),
        ("compact", 1100, 800),
        ("minimum", 980, 640)
    ];

    private const string LongFolder = "Returns with a deliberately long folder name for truncation";
    private const string LongTemplate = "Order copy with a deliberately long template name for truncation";

    public static int Run(string outputDirectory)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int result = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                foreach (var viewport in Viewports)
                    await CaptureViewport(outputDirectory, viewport.Name, viewport.Width, viewport.Height);
                Console.WriteLine("PASS organization captures: Save as to Root/folder, subtree move, destination availability, and 1,000-message review at all four viewports.");
                result = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { app.Shutdown(result); }
        };
        app.Run();
        return result;
    }

    private static async Task CaptureViewport(string root, string name, int width, int height)
    {
        string directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        InvestigationWorkspace workspace = await RetailScreenshotScenario.CreateWorkspaceAsync();
        NativeWindowSizeOverride? sizeOverride = null;
        var window = new InvestigationWindow(workspace)
        {
            Width = width, Height = height, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0
        };
        window.SourceInitialized += (_, _) => sizeOverride = NativeWindowSizeOverride.Install(window, width, height);
        var rendered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.ContentRendered += (_, _) => rendered.TrySetResult(true);
        window.Show();
        try
        {
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            window.Width = width;
            window.Height = height;
            await IdleAsync(window);
            ((ToggleButton)window.FindName("MessageLibraryTab")!).IsChecked = true;
            var view = (MessageLibraryPrototypeView)window.FindName("MessageLibraryPrototype")!;
            Idle(window);

            CreateLongFolder(window, view, LongFolder);
            CaptureActiveMessage(window, workspace, view, directory, width, height);
            SaveAs(window, view, LongTemplate, $"Orders/{LongFolder}", Path.Combine(directory, "save-as-folder-dialog.png"));
            AssertTemplateLocation(view, LongTemplate, $"Orders/{LongFolder}");
            AssertLongTreeLabel(view, $"folder:Orders/{LongFolder}", LongFolder);
            AssertLongTreeLabel(view, $"template:{LongTemplate}", LongTemplate);
            Capture(window, directory, "save-as-folder", width, height);

            SaveAs(window, view, "Root copied template", "", Path.Combine(directory, "save-as-root-dialog.png"));
            Require(FindItem(view, "template:Root copied template").Parent is ItemsControl rootItems && ReferenceEquals(rootItems, view.FindName("LibraryTree")),
                "Saving as to Root must place the copy at the tree root.");
            RequireFullyInside((FrameworkElement)FindItem(view, "template:Root copied template").Header, (TreeView)view.FindName("LibraryTree")!, "New root template row content");
            Capture(window, directory, "save-as-root", width, height);

            MoveFolder(window, view, $"folder:Orders/{LongFolder}", "Inventory", Path.Combine(directory, "move-folder-dialog.png"));
            TreeViewItem movedFolder = FindItem(view, $"folder:Inventory/{LongFolder}");
            Require(movedFolder.IsExpanded, "The moved folder should remain expanded after relocation.");
            Require(movedFolder.Items.OfType<TreeViewItem>().Any(item => Equals(item.Tag, $"template:{LongTemplate}")),
                "Moving a folder must preserve its saved template subtree.");
            AssertLongTreeLabel(view, $"folder:Inventory/{LongFolder}", LongFolder);
            Capture(window, directory, "moved-subtree", width, height);

            DestinationStates(window, view, directory, width, height);
            ReviewLargeBatch(window, view, directory, width, height);
        }
        finally
        {
            window.Close();
            sizeOverride?.Dispose();
            await workspace.DisposeAsync();
        }
        Console.WriteLine($"PASS organization {name} {width}x{height}: routed Save as, move, discovery warnings and large-batch review asserted.");
    }

    private static void CaptureActiveMessage(
        Window window, InvestigationWorkspace workspace, MessageLibraryPrototypeView view,
        string directory, int width, int height)
    {
        const string capturedName = "Captured active order message";
        string originalMessageId = workspace.Inspector.Current?.Message.MessageId
            ?? throw new InvalidOperationException("The synthetic Investigation workspace must have a selected source message.");
        ((ToggleButton)window.FindName("InvestigationWorkspaceTab")!).IsChecked = true;
        Idle(window);
        var create = (Button)window.FindName("SaveInspectedTemplateButton")!;
        Require(create.IsVisible && create.IsEnabled, "A selected Active message must expose Create template.");

        Exception? failure = null;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            MessageLibraryPrototypeDialog? dialog = null;
            try
            {
                dialog = FindOwnedDialog(window) as MessageLibraryPrototypeDialog
                    ?? throw new InvalidOperationException("Capture action must open the message-library capture dialog.");
                Idle(dialog);
                Require(((Expander)dialog.FindName("CaptureDetails")!).IsExpanded == false,
                    "Capture details must be collapsed when the dialog opens.");
                SelectCaptureFolder(dialog, $"Orders/{LongFolder}");
                ((TextBox)dialog.FindName("CaptureName")!).Text = capturedName;
                AssertDialogActionVisible(dialog, "PrototypeOpenDraft");
                CaptureDialog(dialog, Path.Combine(directory, "capture-default.png"));

                var details = (Expander)dialog.FindName("CaptureDetails")!;
                details.IsExpanded = true;
                Idle(dialog);
                Require(((RadioButton)dialog.FindName("CaptureOriginal")!).IsChecked == true,
                    "Capture details must default to the body as received.");
                Require(dialog.CaptureTopic == "order-events" && dialog.CaptureDestinationKind == EntityKind.Topic,
                    "The captured subscription message must retain its parent topic as a typed destination.");
                CaptureDialog(dialog, Path.Combine(directory, "capture-expanded.png"));
                var scroll = (ScrollViewer)dialog.FindName("DialogScroll")!;
                scroll.ScrollToBottom();
                Idle(dialog);
                if (scroll.ScrollableHeight > 0)
                {
                    Require(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1,
                        "The expanded capture details must reach the lower scroll endpoint.");
                }
                RequireFullyInside(FindAutomationButton(dialog, "PrototypeOpenDraft"), dialog,
                    "Create template action in expanded capture dialog");
                CaptureDialog(dialog, Path.Combine(directory, "capture-expanded-bottom.png"));

                var createTemplate = FindAutomationButton(dialog, "PrototypeOpenDraft");
                createTemplate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, createTemplate));
            }
            catch (Exception error) { failure = error; dialog?.Close(); }
        }));
        create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, create));
        if (failure is not null) throw new InvalidOperationException("Could not complete capture through the Investigation inspector.", failure);
        Idle(window);

        Require(workspace.Browse.Messages.Any(row => row.MessageId == originalMessageId),
            "Opening a template must leave the source Active message in the Investigation message list.");
        Require(((TextBlock)view.FindName("AuthorTitle")!).Text == capturedName,
            "The captured message must open as the selected Workbench draft.");
        AssertTemplateLocation(view, capturedName, $"Orders/{LongFolder}");
        Capture(window, directory, "capture-draft", width, height);
    }

    private static void SelectCaptureFolder(Window dialog, string folder)
    {
        var picker = (ComboBox)dialog.FindName("CaptureCollection")!;
        picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(item =>
            string.Equals(item.Tag?.ToString(), folder, StringComparison.OrdinalIgnoreCase));
        Require(picker.SelectedItem is ComboBoxItem selected
            && selected.Tag?.ToString() == folder,
            "Capture must save the new draft to the chosen local folder.");
    }

    private static void SaveAs(Window owner, MessageLibraryPrototypeView view, string name, string folder, string dialogPath)
    {
        Exception? failure = null;
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Window? dialog = null;
            try
            {
                dialog = FindOwnedDialog(owner);
                Idle(dialog);
                var nameInput = (TextBox)dialog.FindName("NameInput")!;
                var folderInput = (ComboBox)dialog.FindName("FolderInput")!;
                nameInput.Text = name;
                folderInput.SelectedItem = FolderChoice(folderInput, folder);
                AssertDialogActionVisible(dialog, "LibraryNameSave");
                CaptureDialog(dialog, dialogPath);
                ((Button)dialog.FindName("ConfirmButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception error) { failure = error; dialog?.Close(); }
        }));
        Click(view, "LibrarySaveAs");
        if (failure is not null) throw new InvalidOperationException("Could not complete the routed Save as flow.", failure);
        Idle(owner);
        Require(FindItem(view, $"template:{name}") is not null, $"The saved copy '{name}' must be visible in the library tree.");
    }

    private static object FolderChoice(ComboBox picker, string path) => picker.Items.Cast<object>().Single(choice =>
        string.Equals(choice.GetType().GetProperty("Path")?.GetValue(choice)?.ToString(), path, StringComparison.OrdinalIgnoreCase));

    private static void AssertTemplateLocation(MessageLibraryPrototypeView view, string name, string folder)
    {
        TreeViewItem template = FindItem(view, $"template:{name}");
        Require(template.Parent is TreeViewItem parent && parent.Tag?.ToString() == $"folder:{folder}",
            $"Template '{name}' must be saved in '{folder}'.");
        Require(((TextBlock)view.FindName("AuthorTitle")!).Text == name,
            "Save as must make the new copy the visible editor template.");
    }

    private static void MoveFolder(Window owner, MessageLibraryPrototypeView view, string tag, string destination, string dialogPath)
    {
        TreeViewItem item = FindItem(view, tag);
        if (item.ContextMenu is null) throw new InvalidOperationException("Folder context actions must be available.");
        ContextMenu contextMenu = item.ContextMenu;
        contextMenu.IsOpen = true;
        Idle(owner);
        var move = contextMenu.Items.OfType<MenuItem>().Single(menu =>
            AutomationProperties.GetAutomationId(menu) == "LibraryMoveTo");
        Exception? failure = null;
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Window? dialog = null;
            try
            {
                dialog = FindOwnedDialog(owner);
                Idle(dialog);
                var picker = (ComboBox)dialog.FindName("FolderInput")!;
                picker.SelectedItem = FolderChoice(picker, destination);
                Require(((FrameworkElement)dialog.FindName("NameRow")!).Visibility == Visibility.Collapsed,
                    "Move to must keep the template/folder name unchanged.");
                AssertDialogActionVisible(dialog, "LibraryNameSave");
                CaptureDialog(dialog, dialogPath);
                ((Button)dialog.FindName("ConfirmButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception error) { failure = error; dialog?.Close(); }
        }));
        move.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, move));
        contextMenu.IsOpen = false;
        if (failure is not null) throw new InvalidOperationException("Could not complete the routed Move to flow.", failure);
        Idle(owner);
    }

    private static void DestinationStates(Window window, MessageLibraryPrototypeView view, string directory, int width, int height)
    {
        Select(view, "template:Stock reserved");
        view.SetDestinationDiscovery("organization-profile", 1, null, "sb://localhost:5300/");
        Idle(window);
        RequireWarning(view, "not verified");
        RequireTreeWarning(view, "template:Stock reserved", true);
        Capture(window, directory, "destination-unverified", width, height);

        view.SetDestinationDiscovery("organization-profile", 2,
            Snapshot(complete: false, new WorkbenchDestination("order-events", EntityKind.Topic)), "sb://localhost:5300/");
        Idle(window);
        RequireWarning(view, "not verified");
        Capture(window, directory, "destination-partial", width, height);

        view.SetDestinationDiscovery("organization-profile", 3, Snapshot(complete: true), "sb://localhost:5300/");
        Idle(window);
        RequireWarning(view, "unavailable");
        RequireTreeWarning(view, "template:Stock reserved", true);
        Capture(window, directory, "destination-missing", width, height);

        view.SetDestinationDiscovery("organization-profile", 4,
            Snapshot(complete: true, new WorkbenchDestination("inventory-events", EntityKind.Topic)), "sb://localhost:5300/");
        Idle(window);
        Require(Warning(view).Visibility == Visibility.Collapsed,
            "A positively discovered saved topic must clear the warning.");
        RequireTreeWarning(view, "template:Stock reserved", false);
        Capture(window, directory, "destination-available", width, height);
    }

    private static void ReviewLargeBatch(Window window, MessageLibraryPrototypeView view, string directory, int width, int height)
    {
        Click(view, "LibraryContinueToPrepare");
        Idle(window);
        ((RadioButton)view.FindName("SingleMode")!).IsChecked = true;
        Idle(window);
        var reviewButton = (Button)view.FindName("ReviewButton")!;
        Require(reviewButton.IsEnabled, "A validated template with an available destination must reach Review.");
        reviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, reviewButton));
        Idle(window);
        var review = (MessageLibraryPrototypeReviewSurface)((ContentControl)view.FindName("ReviewHost")!).Content;
        review.Configure("Local emulator", "inventory-events", 1000);
        review.SetTargetDetails(EntityKind.Topic, "localhost", () => true);
        review.ShowReview();
        Idle(window);

        Require(((TextBlock)review.FindName("ReviewMessageCount")!).Text == "1000 valid messages",
            "Large review must summarize count without rendering individual Message IDs.");
        Require(review.FindName("ReviewMessageIds") is null,
            "The review UI must not contain a list of every prepared Message ID.");
        foreach (string name in new[] { "ReviewProfile", "ReviewEndpoint", "ReviewTarget" })
            Require(review.FindName(name) is TextBlock, $"Review target field {name} must be read-only text.");
        Require(!Elements(review).OfType<TextBox>().Any(textBox =>
                textBox.Name is "ReviewProfile" or "ReviewEndpoint" or "ReviewTarget"),
            "Review target summary must not look like an editable input.");
        Require(!Elements(review).OfType<TextBlock>().Any(text => text.Text.Contains("00000000-0000-0000-0000-", StringComparison.Ordinal)),
            "Individual generated Message IDs must not appear in the review summary.");
        var send = (Button)review.FindName("ConfirmDispatch")!;
        RequireFullyInside(send, window, "Review send action");
        Capture(window, directory, "review-1000", width, height);
        var scroll = (ScrollViewer)review.FindName("ReviewScroll")!;
        var count = (TextBlock)review.FindName("ReviewMessageCount")!;
        count.BringIntoView();
        Idle(window);
        RequireFullyInside(count, review, "Review summary count after scrolling");
        Capture(window, directory, "review-1000-summary", width, height);
        scroll.ScrollToBottom();
        Idle(window);
        RequireFullyInside(send, window, "Review send action after scrolling");
        Capture(window, directory, "review-1000-bottom", width, height);
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

    private static void RequireWarning(MessageLibraryPrototypeView view, string text)
    {
        TextBlock warning = Warning(view);
        Require(warning.Visibility == Visibility.Visible && warning.Text.Contains(text, StringComparison.OrdinalIgnoreCase),
            $"Destination warning should include '{text}', actual: '{warning.Text}'.");
    }

    private static void RequireTreeWarning(MessageLibraryPrototypeView view, string tag, bool expected)
    {
        TreeViewItem item = FindItem(view, tag);
        Grid header = item.Header as Grid
            ?? throw new InvalidOperationException("Destination warning must be attached to the template tree header.");
        bool exists = Elements(header).OfType<FrameworkElement>().Any(element =>
            AutomationProperties.GetAutomationId(element) == "LibraryDestinationWarning");
        Require(exists == expected, $"Tree warning for {tag} should be {(expected ? "visible" : "cleared")}.");
    }

    private static TextBlock Warning(MessageLibraryPrototypeView view) =>
        (TextBlock)view.FindName("DestinationWarning")!;

    private static void AssertDialogActionVisible(Window dialog, string automationId)
    {
        Idle(dialog);
        var action = Elements(dialog).OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == automationId);
        RequireFullyInside(action, dialog, "Location dialog primary action");
    }

    private static void RequireFullyInside(FrameworkElement element, FrameworkElement ancestor, string description)
    {
        Rect bounds = element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
        Require(element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0
            && bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= ancestor.ActualWidth && bounds.Bottom <= ancestor.ActualHeight,
            $"{description} must be visible and in bounds: {bounds}, ancestor={ancestor.ActualWidth}x{ancestor.ActualHeight}.");
    }

    private static void AssertLongTreeLabel(MessageLibraryPrototypeView view, string tag, string fullName)
    {
        TreeViewItem item = FindItem(view, tag);
        Require(AutomationProperties.GetName(item) == fullName,
            "The full long name must remain in the accessible tree item name.");
        Grid header = item.Header as Grid
            ?? throw new InvalidOperationException("The tree item should render a grid header.");
        var label = Elements(header).OfType<TextBlock>().Single(text => text.Text == fullName);
        Require(label.TextTrimming == TextTrimming.CharacterEllipsis && label.ActualWidth > 0,
            "Long folder/template labels must trim inside the tree pane without widening it.");
    }

    private static void CreateLongFolder(Window owner, MessageLibraryPrototypeView view, string name)
    {
        Select(view, "folder:Orders");
        CompleteNamePrompt(owner, view, "LibraryAddFolder", name);
        TreeViewItem folder = FindItem(view, $"folder:Orders/{name}");
        Require(folder.ToolTip?.ToString() == $"Orders/{name}", "The full nested folder path must remain available as help text.");
    }

    private static void CompleteNamePrompt(Window owner, MessageLibraryPrototypeView view, string actionId, string name)
    {
        Exception? failure = null;
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Window? dialog = null;
            try
            {
                dialog = FindOwnedDialog(owner);
                Idle(dialog);
                Elements(dialog).OfType<TextBox>().Single().Text = name;
                var save = Elements(dialog).OfType<Button>().Single(button => button.Content?.ToString() == "Save");
                RequireFullyInside(save, dialog, "Add folder save action");
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, save));
            }
            catch (Exception error) { failure = error; dialog?.Close(); }
        }));
        Click(view, actionId);
        if (failure is not null) throw new InvalidOperationException($"Could not complete {actionId}.", failure);
        Idle(owner);
    }

    private static Button FindAutomationButton(FrameworkElement root, string automationId) =>
        Elements(root).OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == automationId && button.IsVisible);

    private static Window FindOwnedDialog(Window owner) => owner.OwnedWindows.OfType<Window>().Single();

    private static void CaptureDialog(Window dialog, string path)
    {
        var content = (FrameworkElement)dialog.Content;
        Require(content.ActualWidth > 0 && content.ActualHeight > 0, "A location dialog must be arranged before screenshot capture.");
        WpfScreenshot.SaveWindowContent(dialog, path,
            (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), minimumBytes: 1000);
    }

    private static void Select(MessageLibraryPrototypeView view, string tag)
    {
        TreeViewItem item = FindItem(view, tag);
        item.IsSelected = true;
        item.IsExpanded = true;
        Idle(Window.GetWindow(view)!);
    }

    private static TreeViewItem FindItem(MessageLibraryPrototypeView view, string tag) =>
        TreeItems((ItemsControl)view.FindName("LibraryTree")!).Single(item => Equals(item.Tag, tag));

    private static IEnumerable<TreeViewItem> TreeItems(ItemsControl parent)
    {
        foreach (TreeViewItem item in parent.Items)
        {
            yield return item;
            foreach (TreeViewItem child in TreeItems(item)) yield return child;
        }
    }

    private static void Click(DependencyObject root, string automationId)
    {
        Button action = Elements(root).OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == automationId);
        Require(action.IsEnabled, $"Action {automationId} must be enabled.");
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, action));
        if (Window.GetWindow(root) is { } window) Idle(window);
    }

    private static IEnumerable<DependencyObject> Elements(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Elements(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void Capture(Window window, string directory, string state, int width, int height)
    {
        Idle(window);
        WpfScreenshot.SaveWindowContent(window, Path.Combine(directory, $"{state}.png"), width, height);
    }

    private static void Idle(Window window) => window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
    private static Task IdleAsync(Window window) => window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle).Task;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
