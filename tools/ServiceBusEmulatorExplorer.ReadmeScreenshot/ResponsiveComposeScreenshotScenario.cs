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

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class ResponsiveComposeScreenshotScenario
{
    private static readonly (string Name, int Width, int Height)[] Viewports =
    [
        ("wide", 1700, 1000),
        ("wide-resize", 1695, 1000),
        ("medium", 1500, 1000),
        ("small", 1200, 800),
        ("minimum", 980, 640)
    ];

    public static int Run(string outputDirectory)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
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
                    Width = Viewports[0].Width,
                    Height = Viewports[0].Height,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 0,
                    Top = 0
                };
                window.SourceInitialized += (_, _) => sizeOverride = NativeWindowSizeOverride.Install(window, 1700, 1000);
                var rendered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                window.ContentRendered += (_, _) => rendered.TrySetResult(true);
                window.Show();
                await rendered.Task.WaitAsync(TimeSpan.FromSeconds(30));

                ((ToggleButton)window.FindName("MessageLibraryTab")!).IsChecked = true;
                var view = (MessageLibraryPrototypeView)window.FindName("MessageLibraryPrototype")!;
                Idle(window);
                SelectTemplate(view, "Order created");
                var body = (JsonEditor)view.FindName("EditorText")!;
                body.Text = "{\n  \"orderId\": \"ORD-RESPONSIVE-1042\",\n  \"amount\": 149.90\n}";
                ((TextBox)view.FindName("PropertySubject")!).Text = "ResponsiveSubject";
                ((Expander)view.FindName("ReplyRoutingExpander")!).IsExpanded = true;
                ((TextBox)view.FindName("PropertyReplyTo")!).Text = "orders-replies-responsive";

                SetSize(window, Viewports[0]);
                AssertLayout(view, "wide");
                Capture(window, outputDirectory, "wide-body-and-properties", Viewports[0]);

                Click(view, "WideVariablesAction");
                AssertLayout(view, "wide");
                Require(((FrameworkElement)view.FindName("VariablesEditorSurface")!).IsVisible,
                    "The wide Variables action must open Variables beside the body.");
                Capture(window, outputDirectory, "wide-variables-inspector", Viewports[0]);
                Require(body.TextArea.Focus(), "The JSON body editor should receive focus in the wide layout.");
                SetSize(window, Viewports[1]);
                AssertLayout(view, "wide");
                Require(((FrameworkElement)view.FindName("VariablesEditorSurface")!).IsVisible,
                    "Focusing JSON and resizing within the wide layout must keep the Variables inspector selected.");
                Capture(window, outputDirectory, "wide-json-focused-variables-inspector", Viewports[1]);

                SetSize(window, Viewports[2]);
                AssertLayout(view, "medium");
                Click(view, "EditorBodyTab");
                Capture(window, outputDirectory, "medium-body-tab", Viewports[2]);
                Click(view, "EditorPropertiesTab");
                AssertLayout(view, "medium");
                ((TextBox)view.FindName("PropertySubject")!).Text = "Edited in medium";
                ((Expander)view.FindName("ReplyRoutingExpander")!).IsExpanded = true;
                Capture(window, outputDirectory, "medium-properties-two-columns", Viewports[2]);
                Click(view, "EditorVariablesTab");
                var variables = (DataGrid)view.FindName("VariablesGrid")!;
                variables.SelectedItem = variables.Items.Cast<object>().Single(row => Read(row, "Name") == "CustomerId");
                ((CheckBox)view.FindName("UseVariableDefault")!).IsChecked = true;
                ((TextBox)view.FindName("VariableDefaultValue")!).Text = "C-RESPONSIVE";
                Capture(window, outputDirectory, "medium-variable-default", Viewports[2]);

                SetSize(window, Viewports[3]);
                AssertLayout(view, "small");
                Click(view, "EditorPropertiesTab");
                AssertLayout(view, "small");
                Require(((DataGrid)view.FindName("ApplicationPropertiesGrid")!).ActualWidth
                    <= ((ScrollViewer)view.FindName("PropertiesEditorSurface")!).ActualWidth + 0.5,
                    "The compact property table must fit inside its available surface.");
                Capture(window, outputDirectory, "small-properties-one-column", Viewports[3]);
                CapturePropertyBottom(window, view, outputDirectory, Viewports[3]);
                Click(view, "EditorBodyTab");
                Capture(window, outputDirectory, "small-body-tab", Viewports[3]);

                SetSize(window, Viewports[4]);
                AssertLayout(view, "small");
                Click(view, "EditorPropertiesTab");
                Capture(window, outputDirectory, "minimum-properties-one-column", Viewports[4]);
                CapturePropertyBottom(window, view, outputDirectory, Viewports[4]);
                Require(body.Text.Contains("ORD-RESPONSIVE-1042", StringComparison.Ordinal), "The JSON draft should survive every viewport transition.");
                Require(((TextBox)view.FindName("PropertySubject")!).Text == "Edited in medium", "Property edits should survive every viewport transition.");
                Require(((TextBox)view.FindName("PropertyReplyTo")!).Text == "orders-replies-responsive", "Reply routing should survive every viewport transition.");
                Require(Read(variables.SelectedItem!, "Name") == "CustomerId"
                    && ((TextBox)view.FindName("VariableDefaultValue")!).Text == "C-RESPONSIVE",
                    "Variable selection and default should survive every viewport transition.");
                Require(((TreeViewItem)((TreeView)view.FindName("LibraryTree")!).SelectedItem!).Tag?.ToString() == "template:Order created",
                    "The selected template should remain selected while the editor is resized.");

                var prepare = (Button)view.FindName("ContinueToPrepareButton")!;
                Require(prepare.IsEnabled, "The valid demo template should allow Prepare after the responsive edits.");
                Click(view, "EditorBodyTab");
                Click(view, "ContinueToPrepareButton");
                Require(((FrameworkElement)view.FindName("PreparePane")!).IsVisible, "Compose should navigate to Prepare.");
                Capture(window, outputDirectory, "minimum-prepare", Viewports[4]);
                Click(view, "BackToComposeButton");
                Require(((FrameworkElement)view.FindName("AuthorPane")!).IsVisible, "Back should return to Compose.");
                Require(body.Text.Contains("ORD-RESPONSIVE-1042", StringComparison.Ordinal), "Prepare/back should preserve the JSON draft.");
                Capture(window, outputDirectory, "minimum-compose-after-back", Viewports[4]);

                Console.WriteLine("PASS responsive compose captures: one window resized wide → medium → small → minimum with body, property groups, Variables, and Prepare/back state assertions.");
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

    private static void CapturePropertyBottom(Window window, MessageLibraryPrototypeView view, string directory,
        (string Name, int Width, int Height) viewport)
    {
        var scroll = (ScrollViewer)view.FindName("PropertiesEditorSurface")!;
        scroll.ScrollToBottom();
        Idle(window);
        var destination = (ComboBox)view.FindName("TemplateDestination")!;
        Rect bounds = destination.TransformToAncestor(scroll).TransformBounds(new Rect(destination.RenderSize));
        Require(bounds.Top >= 0 && bounds.Bottom <= scroll.ActualHeight + 1,
            "Destination must be fully visible at the bottom of the compact property form.");
        Capture(window, directory, "properties-bottom", viewport);
        scroll.ScrollToTop();
        Idle(window);
    }

    private static void SetSize(Window window, (string Name, int Width, int Height) viewport)
    {
        window.Width = viewport.Width;
        window.Height = viewport.Height;
        Idle(window);
        var content = (FrameworkElement)window.Content;
        Require(Math.Abs(content.ActualWidth - viewport.Width) < 1 && Math.Abs(content.ActualHeight - viewport.Height) < 1,
            "The rendered window must match the requested viewport dimensions.");
    }

    private static void AssertLayout(MessageLibraryPrototypeView view, string expected)
    {
        var tabs = (FrameworkElement)view.FindName("EditorTabsPanel")!;
        var wideHeader = (FrameworkElement)view.FindName("EditorWideHeader")!;
        var groups = (Grid)view.FindName("PropertyGroupsGrid")!;
        var route = (FrameworkElement)view.FindName("RoutingPropertiesGroup")!;
        double editorWidth = ((FrameworkElement)view.FindName("EditorArea")!).ActualWidth;
        string actual = editorWidth >= 1050 ? "wide" : editorWidth >= 700 ? "medium" : "small";
        Require(actual == expected, $"Expected {expected} layout at {editorWidth:F0} DIP of editor width, got {actual}.");
        Require((tabs.Visibility == Visibility.Visible) == (expected != "wide"), "The tab row visibility should match the responsive mode.");
        Require((wideHeader.Visibility == Visibility.Visible) == (expected == "wide"), "The wide editor headings should match the responsive mode.");
        if (((FrameworkElement)view.FindName("PropertiesEditorSurface")!).IsVisible && expected == "small")
        {
            Require(groups.ColumnDefinitions[1].ActualWidth <= 1 && Grid.GetRow(route) == 1 && Grid.GetColumn(route) == 0,
                "Small properties should stack in one column.");
        }
        else if (((FrameworkElement)view.FindName("PropertiesEditorSurface")!).IsVisible)
        {
            Require(groups.ColumnDefinitions[1].ActualWidth > 0 && Grid.GetRow(route) == 0 && Grid.GetColumn(route) == 1,
                "Wide and medium properties should keep two columns.");
        }
        if (expected == "wide")
            Require(((FrameworkElement)view.FindName("BodyEditorSurface")!).IsVisible
                && (((FrameworkElement)view.FindName("PropertiesEditorSurface")!).IsVisible
                    ^ ((FrameworkElement)view.FindName("VariablesEditorSurface")!).IsVisible),
                "Wide mode should show JSON beside exactly one selected inspector surface.");
    }

    private static void SelectTemplate(MessageLibraryPrototypeView view, string name)
    {
        var tree = (TreeView)view.FindName("LibraryTree")!;
        foreach (TreeViewItem item in TreeItems(tree.Items.OfType<TreeViewItem>()))
            if (Equals(item.Tag, "template:" + name)) { item.IsSelected = true; return; }
        throw new InvalidOperationException($"Template {name} was not present in the screenshot fixture.");
    }

    private static IEnumerable<TreeViewItem> TreeItems(IEnumerable<TreeViewItem> roots)
    {
        foreach (TreeViewItem root in roots)
        {
            yield return root;
            foreach (TreeViewItem child in TreeItems(root.Items.OfType<TreeViewItem>())) yield return child;
        }
    }

    private static void Click(DependencyObject root, string controlName)
    {
        Button button = root is FrameworkElement frameworkRoot
            ? frameworkRoot.FindName(controlName) as Button
                ?? Elements(root).OfType<Button>().Single(control => control.IsVisible
                    && AutomationProperties.GetAutomationId(control) == controlName)
            : throw new InvalidOperationException("The responsive Compose scenario expects a named Workbench root.");
        Require(button.IsVisible && button.IsEnabled, $"Action {controlName} should be visible and enabled.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        if (Window.GetWindow(root) is { } window) Idle(window);
    }

    private static IEnumerable<DependencyObject> Elements(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (DependencyObject child in Elements(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static string? Read(object row, string property) => row.GetType().GetProperty(property)?.GetValue(row)?.ToString();

    private static void Capture(Window window, string directory, string state, (string Name, int Width, int Height) viewport)
    {
        Idle(window);
        string path = Path.Combine(directory, $"{viewport.Name}-{state}.png");
        WpfScreenshot.SaveWindowContent(window, path, viewport.Width, viewport.Height, minimumBytes: 20_000);
    }

    private static void Idle(Window window) => window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
