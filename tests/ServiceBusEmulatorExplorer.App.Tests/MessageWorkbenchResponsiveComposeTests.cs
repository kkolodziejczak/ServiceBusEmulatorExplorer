using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.ReadmeScreenshot;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchResponsiveComposeTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void One_compose_draft_adapts_across_all_layouts_and_keeps_its_editing_context()
        => RunOnSta((dispatcher, view, window) =>
        {
            SelectTemplate(view, "Order created");
            var body = Get<JsonEditor>(view, "EditorText");
            const string json = "{\n  \"orderId\": \"ORD-RESPONSIVE-1042\",\n  \"amount\": 149.90\n}";
            body.Text = json;
            Get<TextBox>(view, "PropertySubject").Text = "ResponsiveSubject";
            Get<Expander>(view, "ReplyRoutingExpander").IsExpanded = true;
            Get<TextBox>(view, "PropertyReplyTo").Text = "orders-replies-responsive";
            AssertWide(view, dispatcher);

            Get<Button>(view, "WideVariablesAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);
            Assert.Equal(Visibility.Visible, Get<FrameworkElement>(view, "VariablesEditorSurface").Visibility);
            Assert.Equal(Visibility.Visible, Get<FrameworkElement>(view, "BodyEditorSurface").Visibility);
            Assert.Equal("Variables", Get<TextBlock>(view, "WidePropertiesHeading").Text);
            Assert.True(body.TextArea.Focus());
            Resize(window, dispatcher, 1680, 1000);
            Assert.Equal(Visibility.Visible, Get<FrameworkElement>(view, "VariablesEditorSurface").Visibility);
            Assert.Equal("Variables", Get<TextBlock>(view, "WidePropertiesHeading").Text);

            Resize(window, dispatcher, 1150, 900);
            AssertTabsMode(view);
            Assert.Equal(Visibility.Visible, Get<FrameworkElement>(view, "BodyEditorSurface").Visibility);
            Get<Button>(view, "EditorPropertiesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);
            AssertTwoPropertyColumns(view);
            Get<TextBox>(view, "PropertySubject").Text = "Edited in medium";

            Get<Button>(view, "EditorVariablesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);
            var variables = Get<DataGrid>(view, "VariablesGrid");
            object customer = variables.Items.Cast<object>().Single(row => Read(row, "Name") == "CustomerId");
            variables.SelectedItem = customer;
            Get<CheckBox>(view, "UseVariableDefault").IsChecked = true;
            Get<TextBox>(view, "VariableDefaultValue").Text = "C-RESPONSIVE";
            WaitForLayout(dispatcher);

            Resize(window, dispatcher, 825, 800);
            AssertTabsMode(view);
            Assert.Equal(json, body.Text);
            Assert.Equal("Edited in medium", Get<TextBox>(view, "PropertySubject").Text);
            Assert.Equal("orders-replies-responsive", Get<TextBox>(view, "PropertyReplyTo").Text);
            Assert.Equal("template:Order created", Assert.IsType<TreeViewItem>(Get<TreeView>(view, "LibraryTree").SelectedItem).Tag);
            Assert.Equal("C-RESPONSIVE", Get<TextBox>(view, "VariableDefaultValue").Text);
            Assert.Same(customer, variables.SelectedItem);

            Get<Button>(view, "EditorPropertiesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);
            AssertOnePropertyColumn(view);
            AssertFullyInside(Get<ComboBox>(view, "TemplateDestination"), Get<ScrollViewer>(view, "PropertiesEditorSurface"), "Destination");
            AssertFullyInside(Get<DataGrid>(view, "ApplicationPropertiesGrid"), Get<ScrollViewer>(view, "PropertiesEditorSurface"), "Application properties");
            DataGrid propertiesGrid = Get<DataGrid>(view, "ApplicationPropertiesGrid");
            object longProperty = propertiesGrid.Items.Cast<object>().First(row => row.GetType().GetProperty("Name") is not null);
            longProperty.GetType().GetProperty("Name")!.SetValue(longProperty, new string('P', 64));
            longProperty.GetType().GetProperty("Value")!.SetValue(longProperty, new string('V', 120));
            propertiesGrid.Items.Refresh();
            WaitForLayout(dispatcher);
            Assert.InRange(propertiesGrid.Columns[0].ActualWidth, 1, 200);
            Assert.True(propertiesGrid.ActualWidth <= Get<ScrollViewer>(view, "PropertiesEditorSurface").ActualWidth + 0.5,
                "The application property table must remain within its compact editor surface.");

            Get<ComboBox>(view, "TemplateDestination").SelectedIndex = 0;
            WaitForLayout(dispatcher);
            Assert.Equal(Visibility.Visible, Get<Border>(view, "ComposeDestinationWarning").Visibility);
            Assert.True(Get<Button>(view, "ComposeDestinationAction").IsEnabled);
            Get<Button>(view, "ComposeDestinationAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);
            Assert.Equal(Visibility.Visible, Get<ScrollViewer>(view, "PropertiesEditorSurface").Visibility);
            Assert.Equal(json, body.Text);
            Assert.Equal("template:Order created", Assert.IsType<TreeViewItem>(Get<TreeView>(view, "LibraryTree").SelectedItem).Tag);

            Resize(window, dispatcher, 1150, 900);
            AssertTwoPropertyColumns(view);
            Assert.Equal("Edited in medium", Get<TextBox>(view, "PropertySubject").Text);
            Resize(window, dispatcher, 1700, 1000);
            AssertWide(view, dispatcher);
            Assert.Equal(json, body.Text);
            Assert.Equal("Edited in medium", Get<TextBox>(view, "PropertySubject").Text);
            Assert.Equal("C-RESPONSIVE", Get<TextBox>(view, "VariableDefaultValue").Text);
            Assert.Equal("template:Order created", Assert.IsType<TreeViewItem>(Get<TreeView>(view, "LibraryTree").SelectedItem).Tag);

            // Restore a valid destination and prove Compose -> Prepare -> Compose retains the same draft.
            var destination = Get<ComboBox>(view, "TemplateDestination");
            destination.SelectedItem = destination.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "order-events"));
            Get<Button>(view, "ContinueToPrepareButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);
            Assert.Equal(Visibility.Visible, Get<Grid>(view, "PreparePane").Visibility);
            Get<Button>(view, "BackToComposeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForLayout(dispatcher);
            Assert.Equal(Visibility.Visible, Get<Grid>(view, "AuthorPane").Visibility);
            Assert.Equal(json, body.Text);
            Assert.Equal("Edited in medium", Get<TextBox>(view, "PropertySubject").Text);
            Assert.Equal("C-RESPONSIVE", Get<TextBox>(view, "VariableDefaultValue").Text);
        }, 1700, 1000);

    private static void AssertWide(MessageLibraryPrototypeView view, Dispatcher dispatcher)
    {
        WaitForLayout(dispatcher);
        Assert.Equal(Visibility.Collapsed, Get<FrameworkElement>(view, "EditorTabsPanel").Visibility);
        Assert.Equal(Visibility.Visible, Get<FrameworkElement>(view, "EditorWideHeader").Visibility);
        Assert.Equal(Visibility.Visible, Get<FrameworkElement>(view, "BodyEditorSurface").Visibility);
        Assert.Equal(Visibility.Visible, Get<FrameworkElement>(view, "PropertiesEditorSurface").Visibility);
        AssertTwoPropertyColumns(view);
    }

    private static void AssertTabsMode(MessageLibraryPrototypeView view)
    {
        Assert.Equal(Visibility.Visible, Get<FrameworkElement>(view, "EditorTabsPanel").Visibility);
        Assert.Equal(Visibility.Collapsed, Get<FrameworkElement>(view, "EditorWideHeader").Visibility);
    }

    private static void AssertTwoPropertyColumns(MessageLibraryPrototypeView view)
    {
        var groups = Get<Grid>(view, "PropertyGroupsGrid");
        Assert.Equal(Visibility.Visible, Get<FrameworkElement>(view, "MessagePropertiesGroup").Visibility);
        Assert.Equal(Visibility.Visible, Get<FrameworkElement>(view, "RoutingPropertiesGroup").Visibility);
        Assert.True(groups.ColumnDefinitions[1].ActualWidth > 0, "The second property group should occupy a column when enough editor width is available.");
        Assert.Equal(0, Grid.GetRow(Get<FrameworkElement>(view, "RoutingPropertiesGroup")));
        Assert.Equal(1, Grid.GetColumn(Get<FrameworkElement>(view, "RoutingPropertiesGroup")));
    }

    private static void AssertOnePropertyColumn(MessageLibraryPrototypeView view)
    {
        var groups = Get<Grid>(view, "PropertyGroupsGrid");
        Assert.True(groups.ColumnDefinitions[1].ActualWidth <= 1, "The second property column should collapse at the narrowest editing width.");
        Assert.Equal(1, Grid.GetRow(Get<FrameworkElement>(view, "RoutingPropertiesGroup")));
        Assert.Equal(0, Grid.GetColumn(Get<FrameworkElement>(view, "RoutingPropertiesGroup")));
    }

    private static void SelectTemplate(MessageLibraryPrototypeView view, string name)
    {
        var tree = Get<TreeView>(view, "LibraryTree");
        TreeItem(tree.Items.OfType<TreeViewItem>())
            .Single(item => Equals(item.Tag, "template:" + name)).IsSelected = true;
    }

    private static IEnumerable<TreeViewItem> TreeItem(IEnumerable<TreeViewItem> roots)
    {
        foreach (TreeViewItem root in roots)
        {
            yield return root;
            foreach (TreeViewItem child in TreeItem(root.Items.OfType<TreeViewItem>())) yield return child;
        }
    }

    private static void AssertFullyInside(FrameworkElement element, FrameworkElement surface, string name)
    {
        Rect bounds = element.TransformToAncestor(surface).TransformBounds(new Rect(element.RenderSize));
        Assert.True(bounds.Left >= -0.5 && bounds.Right <= surface.ActualWidth + 0.5,
            $"{name} overflows the property surface: {bounds} in {surface.ActualWidth} DIP.");
    }

    private static void Resize(Window window, Dispatcher dispatcher, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        WaitForLayout(dispatcher);
        Assert.InRange(Math.Abs(window.ActualWidth - width), 0, 1);
        Assert.InRange(Math.Abs(window.ActualHeight - height), 0, 1);
        FrameworkElement content = window.Content as FrameworkElement ?? throw new InvalidOperationException("Window content is missing.");
        double editorWidth = Get<FrameworkElement>(content, "EditorArea").ActualWidth;
        Assert.True(editorWidth > 0, "The editor area should be arranged after resizing.");
        Assert.Equal(editorWidth < 1050 ? Visibility.Visible : Visibility.Collapsed,
            Get<FrameworkElement>(content, "EditorTabsPanel").Visibility);
    }

    private static void RunOnSta(Action<Dispatcher, MessageLibraryPrototypeView, Window> assertion, double width, double height)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            NativeWindowSizeOverride? sizeOverride = null;
            try
            {
                var view = new MessageLibraryPrototypeView();
                window = new Window { Width = width, Height = height, Content = view, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                window.SourceInitialized += (_, _) => sizeOverride = NativeWindowSizeOverride.Install(window, 1700, 1000);
                window.Show();
                window.Width = width;
                window.Height = height;
                WaitForLayout(window.Dispatcher);
                assertion(window.Dispatcher, view, window);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                sizeOverride?.Dispose();
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Responsive Workbench layout proof exceeded its time bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static string? Read(object row, string property) => row.GetType().GetProperty(property)?.GetValue(row)?.ToString();

    private static T Get<T>(FrameworkElement root, string name) where T : FrameworkElement => root.FindName(name) as T
        ?? throw new Xunit.Sdk.XunitException($"Named control '{name}' was not found.");

    private static void WaitForLayout(Dispatcher dispatcher) => dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
