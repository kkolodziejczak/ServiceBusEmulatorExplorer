using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class WorkbenchTreeScreenshotScenario
{
    public static int Run(string outputDirectory, string mode)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        (int width, int height) = mode switch
        {
            "--workbench-tree-minimum" => (980, 640),
            "--workbench-tree-compact" => (1100, 800),
            "--workbench-tree-1500" => (1500, 1000),
            _ => (1642, 958)
        };
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int result = 1;
        app.Startup += async (_, _) =>
        {
            InvestigationWorkspace? workspace = null;
            InvestigationWindow? window = null;
            NativeWindowSizeOverride? sizeOverride = null;
            try
            {
                workspace = await RetailScreenshotScenario.CreateWorkspaceAsync();
                window = new InvestigationWindow(workspace)
                {
                    Width = width, Height = height, WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0
                };
                window.SourceInitialized += (_, _) => sizeOverride = NativeWindowSizeOverride.Install(window, width, height);
                var rendered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                window.ContentRendered += (_, _) => rendered.TrySetResult(true);
                window.Show();
                await rendered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                window.Width = width;
                window.Height = height;
                await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                ((ToggleButton)window.FindName("MessageLibraryTab")!).IsChecked = true;
                var view = (MessageLibraryPrototypeView)window.FindName("MessageLibraryPrototype")!;
                Select(view, "Orders");
                CompleteNamePrompt(window, view, "LibraryAddFolder", "Returns");
                Require(Item(view, "Returns").Parent is TreeViewItem parent && Name(parent) == "Orders", "New folder must be nested under Orders.");
                Click(view, "LibraryNew");
                Require(Item(view, "Untitled message").Parent is TreeViewItem folder && Name(folder) == "Returns", "New template must be inside Returns.");
                ((JsonEditor)view.FindName("EditorText")!).Text = "{\n  \"orderId\": \"ORD-1001\",\n  \"amount\": 49.95\n}";
                ((Button)view.FindName("EditorPropertiesTab")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var destination = (ComboBox)view.FindName("TemplateDestination")!;
                destination.SelectedItem = destination.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "order-events"));
                Click(view, "LibrarySave");
                Click(view, "LibraryRename");
                var rename = Elements(view).OfType<TextBox>().Single(element => AutomationProperties.GetAutomationId(element) == "LibraryRenameInput" && element.IsVisible);
                rename.Text = "Refund approved";
                Capture(window, outputDirectory, "rename", width, height);
                rename.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(rename)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
                Idle(window);
                Require(((TextBlock)view.FindName("AuthorTitle")!).Text == "Refund approved", "Inline rename must update the editor title.");
                Require(Item(view, "Refund approved") is not null, "Inline rename must update the tree.");
                Select(view, "Returns");
                Capture(window, outputDirectory, "properties", width, height);
                var propertyScroll = Elements(view).OfType<ScrollViewer>()
                    .Single(scroll => scroll.Content is StackPanel panel && panel.Children.OfType<Grid>().Any(grid => grid.Children.Contains(destination)));
                propertyScroll.ScrollToBottom();
                Capture(window, outputDirectory, "properties-bottom", width, height);
                propertyScroll.ScrollToTop();
                ((Button)view.FindName("EditorBodyTab")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Capture(window, outputDirectory, "compose", width, height);

                ((Button)view.FindName("ContinueToPrepareButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((RadioButton)view.FindName("SingleMode")!).IsChecked = true;
                Idle(window);
                var review = (Button)view.FindName("ReviewButton")!;
                Require(review.IsEnabled, "The newly saved JSON template with one destination must be ready for review.");
                review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Capture(window, outputDirectory, "review", width, height);
                var reviewScroll = Elements(view).OfType<ScrollViewer>().Single(scroll => scroll.Name == "ReviewScroll" && scroll.IsVisible);
                reviewScroll.ScrollToBottom();
                Capture(window, outputDirectory, "review-bottom", width, height);
                var notice = Elements(view).OfType<TextBlock>().Single(text => text.Name == "ReviewNotice" && text.IsVisible);
                Require(reviewScroll.TranslatePoint(new Point(0, reviewScroll.ActualHeight), window).Y <=
                    notice.TranslatePoint(new Point(0, 0), window).Y, "Review notice must stay below the scroll viewport.");
                Console.WriteLine($"PASS {width}x{height}: nested folder, new template, JSON edit, single destination, save, inline rename, review reached through real controls.");
                result = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally
            {
                window?.Close();
                sizeOverride?.Dispose();
                if (workspace is not null) await workspace.DisposeAsync();
                app.Shutdown(result);
            }
        };
        app.Run();
        return result;
    }

    private static void Select(MessageLibraryPrototypeView view, string name)
    {
        TreeViewItem item = Item(view, name);
        item.IsSelected = true;
        item.IsExpanded = true;
        Idle(Window.GetWindow(view));
    }

    private static TreeViewItem Item(MessageLibraryPrototypeView view, string name) =>
        TreeItems((ItemsControl)view.FindName("LibraryTree")!).Single(item => Name(item) == name);

    internal static IEnumerable<TreeViewItem> TreeItems(ItemsControl parent)
    {
        foreach (TreeViewItem item in parent.Items)
        {
            yield return item;
            foreach (TreeViewItem child in TreeItems(item)) yield return child;
        }
    }

    private static string Name(DependencyObject item) => AutomationProperties.GetName(item);

    private static void CompleteNamePrompt(Window owner, MessageLibraryPrototypeView view, string action, string value)
    {
        Exception? failure = null;
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Window? dialog = null;
            try
            {
                dialog = owner.OwnedWindows.OfType<Window>().Single();
                Elements(dialog).OfType<TextBox>().Single().Text = value;
                Elements(dialog).OfType<Button>().Single(button => Equals(button.Content, "Save"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception error) { failure = error; dialog?.Close(); }
        }));
        Click(view, action);
        if (failure is not null) throw new InvalidOperationException($"Could not complete {action}.", failure);
        Idle(owner);
    }

    private static void Click(DependencyObject view, string id) => Elements(view).OfType<Button>()
        .Single(button => AutomationProperties.GetAutomationId(button) == id)
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
