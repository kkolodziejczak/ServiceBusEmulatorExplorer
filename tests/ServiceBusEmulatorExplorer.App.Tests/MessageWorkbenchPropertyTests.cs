using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchPropertyTests
{
    [Fact]
    public void New_item_placeholder_stays_available_and_an_untouched_row_is_discarded()
        => Run((window, view) =>
        {
            var grid = Get<DataGrid>(view, "ApplicationPropertiesGrid");
            AssertNoLegacyPropertyActions(view);
            Assert.Equal(3, PropertyRows(grid).Count());
            Assert.Contains(grid.Items.Cast<object>(), item => ReferenceEquals(item, CollectionView.NewItemPlaceholder));
            grid.ScrollIntoView(CollectionView.NewItemPlaceholder);
            grid.UpdateLayout();
            grid.CurrentCell = new DataGridCellInfo(CollectionView.NewItemPlaceholder, grid.Columns[0]);
            Assert.True(grid.BeginEdit());
            Drain(window);
            TextBox subject = Get<TextBox>(view, "PropertySubject");
            Assert.True(subject.Focus());
            bool gridKeepsFocus = grid.IsKeyboardFocusWithin;
            Drain(window);
            Assert.Contains(grid.Items.Cast<object>(), item => ReferenceEquals(item, CollectionView.NewItemPlaceholder));
            Assert.True(PropertyRows(grid).Count() == 3,
                $"An untouched placeholder row must be discarded on focus loss. " +
                $"gridKeepsFocus={gridKeepsFocus}.");
        });

    [Fact]
    public void New_property_name_type_and_value_are_real_edits_saved_reopened_and_previewed()
        => Run((window, view) =>
        {
            var grid = Get<DataGrid>(view, "ApplicationPropertiesGrid");
            object property = StartNewProperty(window, grid, "shipmentCount");
            EditComboCell(window, grid, property, 1, "int");
            EditTextCell(window, grid, property, 2, "17");

            Assert.Contains(PropertyRows(grid), item => ReferenceEquals(item, property));
            Assert.Equal("shipmentCount", ReadProperty(property, "Name"));
            Assert.Equal("int", ReadProperty(property, "Type"));
            Assert.Equal("17", ReadProperty(property, "Value"));

            Get<Button>(view, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetTreeItem(view, "template:Order updated").IsSelected = true;
            GetTreeItem(view, "template:Order created").IsSelected = true;
            Drain(window);

            object reopened = PropertyRows(grid).Single(item => ReadProperty(item, "Name") == "shipmentCount");
            Assert.Equal("int", ReadProperty(reopened, "Type"));
            Assert.Equal("17", ReadProperty(reopened, "Value"));

            Get<Button>(view, "PropertiesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);
            Assert.Contains("shipmentCount (int) = 17", Get<JsonEditor>(view, "PreviewText").Text);
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Value_only_or_type_only_new_property_is_retained_after_save_and_reopen(bool enterType)
        => Run((window, view) =>
        {
            var grid = Get<DataGrid>(view, "ApplicationPropertiesGrid");
            object property = StartNewProperty(window, grid, "pendingName");
            if (enterType) EditComboCell(window, grid, property, 1, "int");
            else EditTextCell(window, grid, property, 2, "value-only");
            EditTextCell(window, grid, property, 0, "", "LibraryPropertyName");
            Drain(window);

            Assert.Equal(4, PropertyRows(grid).Count());
            Assert.Equal("", ReadProperty(property, "Name"));
            Assert.Equal(enterType ? "int" : "string", ReadProperty(property, "Type"));
            Assert.Equal(enterType ? "" : "value-only", ReadProperty(property, "Value"));

            Get<Button>(view, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetTreeItem(view, "template:Order updated").IsSelected = true;
            GetTreeItem(view, "template:Order created").IsSelected = true;
            Drain(window);

            object reopened = PropertyRows(grid).Single(item => ReadProperty(item, "Name") == "");
            Assert.Equal(enterType ? "int" : "string", ReadProperty(reopened, "Type"));
            Assert.Equal(enterType ? "" : "value-only", ReadProperty(reopened, "Value"));
        });

    [Fact]
    public void Clearing_an_existing_property_name_value_and_type_prunes_the_row_on_exit()
        => Run((window, view) =>
        {
            var grid = Get<DataGrid>(view, "ApplicationPropertiesGrid");
            object amount = PropertyRows(grid).Single(item => ReadProperty(item, "Name") == "amount");
            EditComboCell(window, grid, amount, 1, "string");
            EditTextCell(window, grid, amount, 0, "", "LibraryPropertyName");
            EditTextCell(window, grid, amount, 2, "", allowRowCancellation: true);
            Drain(window);

            Assert.Equal(2, PropertyRows(grid).Count());
            Assert.DoesNotContain(PropertyRows(grid), item => ReadProperty(item, "Name") == "amount");
            Assert.DoesNotContain(PropertyRows(grid), item => ReadProperty(item, "IsEmpty") == "True");
        });

    [Fact]
    public void Row_delete_targets_its_bound_property_and_delete_key_still_removes_selected_rows()
        => Run((window, view) =>
        {
            var grid = Get<DataGrid>(view, "ApplicationPropertiesGrid");
            object amount = PropertyRows(grid).Single(item => ReadProperty(item, "Name") == "amount");
            DataGridRow row = RowFor(grid, amount);
            var delete = FindAutomationId<Button>(row, "LibraryDeleteProperty");
            Assert.Same(amount, delete.DataContext);
            row.IsSelected = true;
            grid.UpdateLayout();
            Assert.True(delete.IsVisible);
            Assert.True(delete.Focus());
            row.IsSelected = false;
            grid.UpdateLayout();
            Assert.False(row.IsSelected);
            Assert.True(row.IsKeyboardFocusWithin);
            Assert.True(delete.IsVisible);
            row.IsSelected = true;
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);

            Assert.DoesNotContain(PropertyRows(grid), item => ReadProperty(item, "Name") == "amount");
            Assert.Equal(2, PropertyRows(grid).Count());

            grid.SelectionMode = DataGridSelectionMode.Extended;
            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(grid.Items[0]);
            var key = new KeyEventArgs(Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(grid)!, 0, Key.Delete)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            grid.RaiseEvent(key);

            Assert.Single(PropertyRows(grid));
            Assert.Equal("occurredAt", ReadProperty(PropertyRows(grid).Single(), "Name"));
        });

    [Fact]
    public void Saved_property_removal_survives_switching_templates_and_reopening_in_memory()
        => Run((window, view) =>
        {
            var grid = Get<DataGrid>(view, "ApplicationPropertiesGrid");
            object amount = PropertyRows(grid).Single(item => ReadProperty(item, "Name") == "amount");
            FindAutomationId<Button>(RowFor(grid, amount), "LibraryDeleteProperty")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Get<Button>(view, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetTreeItem(view, "template:Order updated").IsSelected = true;
            GetTreeItem(view, "template:Order created").IsSelected = true;
            Drain(window);

            Assert.Equal(2, PropertyRows(grid).Count());
            Assert.DoesNotContain(PropertyRows(grid), item => ReadProperty(item, "Name") == "amount");
        });

    [Fact]
    public void Delete_key_in_a_cell_editor_does_not_remove_the_property()
        => Run((window, view) =>
        {
            var grid = Get<DataGrid>(view, "ApplicationPropertiesGrid");
            object amount = PropertyRows(grid).Single(item => ReadProperty(item, "Name") == "amount");
            grid.CurrentCell = new DataGridCellInfo(amount, grid.Columns[0]);
            Assert.True(grid.BeginEdit());
            Drain(window);
            grid.UpdateLayout();
            TextBox editor = FindAutomationId<TextBox>(RowFor(grid, amount), "LibraryPropertyName");
            var key = new KeyEventArgs(Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(editor)!, 0, Key.Delete)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            editor.RaiseEvent(key);

            Assert.False(key.Handled);
            Assert.Equal(3, PropertyRows(grid).Count());
            grid.CancelEdit();
        });

    [Fact]
    public void Reply_and_routing_starts_collapsed()
        => Run((_, view) => Assert.False(Descendants(view).OfType<Expander>().Single(expander =>
            Equals(expander.Header, "Reply and routing")).IsExpanded));

    private static object StartNewProperty(Window window, DataGrid grid, string name)
    {
        Assert.Contains(grid.Items.Cast<object>(), item => ReferenceEquals(item, CollectionView.NewItemPlaceholder));
        grid.ScrollIntoView(CollectionView.NewItemPlaceholder);
        grid.UpdateLayout();
        grid.CurrentCell = new DataGridCellInfo(CollectionView.NewItemPlaceholder, grid.Columns[0]);
        Assert.True(grid.BeginEdit());
        Drain(window);
        grid.UpdateLayout();
        DataGridRow row = grid.ItemContainerGenerator.ContainerFromIndex(grid.Items.Count - 2) as DataGridRow
            ?? throw new Xunit.Sdk.XunitException("The new application property row was not realized.");
        FindAutomationId<TextBox>(row, "LibraryPropertyName").Text = name;
        Assert.True(grid.CommitEdit(DataGridEditingUnit.Cell, true));
        Assert.True(grid.CommitEdit(DataGridEditingUnit.Row, true));
        Drain(window);
        object property = PropertyRows(grid).Single(item => ReadProperty(item, "Name") == name);
        Assert.Contains(grid.Items.Cast<object>(), item => ReferenceEquals(item, CollectionView.NewItemPlaceholder));
        return property;
    }

    private static void EditTextCell(Window window, DataGrid grid, object item, int column,
        string value, string? automationId = null, bool allowRowCancellation = false)
    {
        grid.CurrentCell = new DataGridCellInfo(item, grid.Columns[column]);
        Assert.True(grid.BeginEdit());
        Drain(window);
        grid.UpdateLayout();
        TextBox editor = automationId is null
            ? Descendants(RowFor(grid, item)).OfType<TextBox>().Single(control => control.IsVisible)
            : FindAutomationId<TextBox>(RowFor(grid, item), automationId);
        editor.Text = value;
        Assert.True(grid.CommitEdit(DataGridEditingUnit.Cell, true));
        bool rowCommitted = grid.CommitEdit(DataGridEditingUnit.Row, true);
        if (!allowRowCancellation) Assert.True(rowCommitted);
        Drain(window);
    }

    private static void EditComboCell(Window window, DataGrid grid, object item, int column, string value)
    {
        grid.CurrentCell = new DataGridCellInfo(item, grid.Columns[column]);
        Assert.True(grid.BeginEdit());
        Drain(window);
        grid.UpdateLayout();
        ComboBox editor = Descendants(RowFor(grid, item)).OfType<ComboBox>()
            .Single(control => control.IsVisible && control.IsHitTestVisible);
        editor.SelectedItem = value;
        Assert.True(grid.CommitEdit(DataGridEditingUnit.Cell, true));
        Assert.True(grid.CommitEdit(DataGridEditingUnit.Row, true));
        Drain(window);
    }

    private static void AssertNoLegacyPropertyActions(FrameworkElement view)
    {
        Assert.DoesNotContain(Descendants(view).OfType<Button>(), button =>
            AutomationProperties.GetAutomationId(button) is "LibraryAddProperty" or "LibraryDeleteSelectedProperty");
    }

    private static IEnumerable<object> PropertyRows(DataGrid grid) => grid.Items.Cast<object>()
        .Where(item => !ReferenceEquals(item, CollectionView.NewItemPlaceholder));

    private static DataGridRow RowFor(DataGrid grid, object item)
    {
        grid.ScrollIntoView(item);
        grid.UpdateLayout();
        return grid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow
            ?? throw new Xunit.Sdk.XunitException("The application property row was not realized.");
    }

    private static string ReadProperty(object item, string propertyName) =>
        item.GetType().GetProperty(propertyName)?.GetValue(item)?.ToString() ?? "";

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

    private static TreeViewItem GetTreeItem(FrameworkElement root, string tag) =>
        TreeItems(Get<TreeView>(root, "LibraryTree")).Single(item => Equals(item.Tag, tag));

    private static IEnumerable<TreeViewItem> TreeItems(TreeView tree)
    {
        foreach (TreeViewItem root in tree.Items.OfType<TreeViewItem>())
        {
            yield return root;
            foreach (var child in TreeItems(root)) yield return child;
        }
    }

    private static IEnumerable<TreeViewItem> TreeItems(TreeViewItem parent)
    {
        foreach (TreeViewItem child in parent.Items.OfType<TreeViewItem>())
        {
            yield return child;
            foreach (var descendant in TreeItems(child)) yield return descendant;
        }
    }

    private static T FindAutomationId<T>(DependencyObject root, string automationId) where T : DependencyObject =>
        Descendants(root).OfType<T>().Single(element =>
            AutomationProperties.GetAutomationId(element) == automationId);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
