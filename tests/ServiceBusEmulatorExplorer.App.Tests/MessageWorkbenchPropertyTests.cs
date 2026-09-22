using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchPropertyTests
{
    [Fact]
    public void Add_selects_new_property_and_delete_invalidates_preview()
        => Run((window, view) =>
        {
            var grid = Get<DataGrid>(view, "ApplicationPropertiesGrid");
            var add = Descendants(view).OfType<Button>().Single(button =>
                Equals(button.Content, "+ Add property"));
            var delete = Get<Button>(view, "DeleteSelectedPropertyButton");

            Assert.False(delete.IsEnabled);
            Assert.Equal(3, grid.Items.Count);
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);

            Assert.Equal(4, grid.Items.Count);
            Assert.NotNull(grid.SelectedItem);
            Assert.True(delete.IsEnabled);

            Get<Button>(view, "ValidateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Get<Button>(view, "ReviewButton").IsEnabled);

            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(Get<Button>(view, "ReviewButton").IsEnabled);
            Assert.Equal(3, grid.Items.Count);
            Assert.False(delete.IsEnabled);
        });

    [Fact]
    public void Delete_key_removes_selected_properties()
        => Run((window, view) =>
        {
            var grid = Get<DataGrid>(view, "ApplicationPropertiesGrid");
            grid.SelectionMode = DataGridSelectionMode.Extended;
            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(grid.Items[0]);
            grid.SelectedItems.Add(grid.Items[1]);
            var key = new KeyEventArgs(Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(grid)!, 0, Key.Delete)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };

            grid.RaiseEvent(key);

            Assert.Single(grid.Items.Cast<object>());
            Assert.Equal("occurredAt", PropertyName(grid.Items[0]));
        });

    [Fact]
    public void Saved_property_removal_survives_switching_templates_and_reload()
        => Run((window, view) =>
        {
            var grid = Get<DataGrid>(view, "ApplicationPropertiesGrid");
            grid.SelectedIndex = 0;
            Get<Button>(view, "DeleteSelectedPropertyButton").RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, grid.Items.Count);

            Get<Button>(view, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var templates = Get<ListBox>(view, "TemplateList");
            templates.SelectedIndex = 1;
            templates.SelectedIndex = 0;
            Descendants(view).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "LibraryRefresh")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);

            Assert.Equal(2, grid.Items.Count);
            Assert.DoesNotContain(grid.Items.Cast<object>(), item =>
                string.Equals(PropertyName(item), "amount", StringComparison.Ordinal));
        });

    [Fact]
    public void Delete_key_in_a_cell_editor_does_not_remove_the_property()
        => Run((window, view) =>
        {
            var grid = Get<DataGrid>(view, "ApplicationPropertiesGrid");
            grid.SelectedIndex = 0;
            grid.CurrentCell = new DataGridCellInfo(grid.Items[0], grid.Columns[0]);
            Assert.True(grid.BeginEdit());
            Drain(window);
            grid.UpdateLayout();
            var editor = Descendants(grid).OfType<TextBox>().Single(control => control.IsVisible);
            var key = new KeyEventArgs(Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(editor)!, 0, Key.Delete)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            editor.RaiseEvent(key);
            Assert.False(key.Handled);
            Assert.Equal(3, grid.Items.Count);
            grid.CancelEdit();
        });

    [Fact]
    public void Reply_and_routing_starts_collapsed()
        => Run((_, view) => Assert.False(Descendants(view).OfType<Expander>().Single(expander =>
            Equals(expander.Header, "Reply and routing")).IsExpanded));

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
                Drain(window);
                Get<Button>(view, "EditorPropertiesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Drain(window);
                view.UpdateLayout();
                proof(window, view);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Workbench property proof exceeded 25 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Drain(Window window) =>
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static T Get<T>(FrameworkElement root, string name) where T : class =>
        Assert.IsAssignableFrom<T>(root.FindName(name));

    private static string PropertyName(object item) =>
        item.GetType().GetProperty("Name")?.GetValue(item)?.ToString() ?? "";

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
