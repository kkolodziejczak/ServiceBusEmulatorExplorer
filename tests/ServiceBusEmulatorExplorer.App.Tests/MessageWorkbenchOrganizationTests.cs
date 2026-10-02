using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchOrganizationTests
{
    [Fact]
    public void Save_as_can_choose_a_folder_and_cancel_without_changing_the_current_draft()
        => Run((window, view) =>
        {
            const string body = "{\"customerId\":\"unsaved body\"}";
            Get<JsonEditor>(view, "EditorText").Text = body;
            Get<ComboBox>(view, "TemplateDestination").SelectedValue = "inventory-events";

            CompleteSaveAsDialog(window, view, "", null, cancel: true);

            Assert.Equal("Order created", Get<TextBlock>(view, "AuthorTitle").Text);
            Assert.Equal(body, Get<JsonEditor>(view, "EditorText").Text);
            Assert.Equal("inventory-events", Get<ComboBox>(view, "TemplateDestination").SelectedValue);
            Assert.False(HasTreeItem(view, "template:Order created copy"));

            CompleteSaveAsDialog(window, view, "Inventory", "Inventory copy");

            Assert.Equal("Inventory copy", Get<TextBlock>(view, "AuthorTitle").Text);
            Assert.Equal(body, Get<JsonEditor>(view, "EditorText").Text);
            Assert.Equal("inventory-events", Get<ComboBox>(view, "TemplateDestination").SelectedValue);
            Assert.True(HasTreeItem(view, "template:Order created"));
            Assert.True(HasTreeItem(view, "template:Inventory copy"));
            Assert.Contains(TreeDescendants([GetTreeItem(view, "folder:Inventory")]), item =>
                Equals(item.Tag, "template:Inventory copy"));
        });

    [Fact]
    public void Moving_the_open_template_preserves_its_unsaved_body_and_destination()
        => Run((window, view) =>
        {
            const string draft = "{\"customerId\":\"keep current draft\"}";
            Get<JsonEditor>(view, "EditorText").Text = draft;
            Get<ComboBox>(view, "TemplateDestination").SelectedValue = "inventory-events";

            CompleteMoveDialog(window, view, "template:Order created", "Inventory");

            Assert.Equal("Order created", Get<TextBlock>(view, "AuthorTitle").Text);
            Assert.Equal(draft, Get<JsonEditor>(view, "EditorText").Text);
            Assert.Equal("inventory-events", Get<ComboBox>(view, "TemplateDestination").SelectedValue);
            Assert.Contains(TreeDescendants([GetTreeItem(view, "folder:Inventory")]), item =>
                Equals(item.Tag, "template:Order created"));
        });

    [Fact]
    public void Moving_a_folder_moves_its_template_subtree_to_another_folder_and_then_root()
        => Run((window, view) =>
        {
            CompleteMoveDialog(window, view, "folder:Orders", "Inventory");

            TreeViewItem movedIntoInventory = GetTreeItem(view, "folder:Inventory/Orders");
            Assert.Contains(TreeDescendants([movedIntoInventory]), item => Equals(item.Tag, "template:Order created"));
            Assert.Contains(TreeDescendants([movedIntoInventory]), item => Equals(item.Tag, "template:Order updated"));
            Assert.Contains(TreeDescendants([movedIntoInventory]), item => Equals(item.Tag, "template:Order dispatched"));

            CompleteMoveDialog(window, view, "folder:Inventory/Orders", "");

            TreeViewItem movedToRoot = GetTreeItem(view, "folder:Orders");
            Assert.Contains(TreeDescendants([movedToRoot]), item => Equals(item.Tag, "template:Order created"));
            Assert.Contains(TreeDescendants([movedToRoot]), item => Equals(item.Tag, "template:Order updated"));
            Assert.Contains(TreeDescendants([movedToRoot]), item => Equals(item.Tag, "template:Order dispatched"));
            Assert.True(HasTreeItem(view, "template:Stock reserved"));
        });

    [Fact]
    public void Routed_drop_moves_a_template_to_a_folder_and_a_folder_subtree_to_root()
        => Run((window, view) =>
        {
            RaiseLibraryDrop(view, "template:Order created", GetTreeItem(view, "folder:Inventory"));
            Assert.Contains(TreeDescendants([GetTreeItem(view, "folder:Inventory")]), item =>
                Equals(item.Tag, "template:Order created"));

            RaiseLibraryDrop(view, "folder:Orders", GetTreeItem(view, "folder:Inventory"));
            TreeViewItem movedFolder = GetTreeItem(view, "folder:Inventory/Orders");
            Assert.Contains(TreeDescendants([movedFolder]), item => Equals(item.Tag, "template:Order updated"));
            Assert.Contains(TreeDescendants([movedFolder]), item => Equals(item.Tag, "template:Order dispatched"));

            RaiseLibraryDrop(view, "folder:Inventory/Orders", Get<TreeView>(view, "LibraryTree"));
            TreeViewItem movedToRoot = GetTreeItem(view, "folder:Orders");
            Assert.Contains(TreeDescendants([movedToRoot]), item => Equals(item.Tag, "template:Order updated"));
            Assert.Contains(TreeDescendants([movedToRoot]), item => Equals(item.Tag, "template:Order dispatched"));
        });

    [Fact]
    public void Moving_folders_rejects_self_descendant_and_existing_destination_conflicts()
        => Run((window, view) =>
        {
            CompleteMoveDialog(window, view, "folder:Orders", "Orders", "inside itself");

            GetTreeItem(view, "folder:Orders").IsSelected = true;
            AddFolder(window, view, "Child");
            CompleteMoveDialog(window, view, "folder:Orders", "Orders/Child", "inside itself");

            GetTreeItem(view, "folder:Inventory").IsSelected = true;
            AddFolder(window, view, "Archive");
            GetTreeItem(view, "folder:Inventory/Archive").IsSelected = true;
            AddFolder(window, view, "Orders");

            CompleteMoveDialog(window, view, "folder:Orders", "Inventory");
            CompleteMoveDialog(window, view, "folder:Inventory/Archive/Orders", "Inventory", "already exists");

            Assert.True(HasTreeItem(view, "folder:Inventory/Orders"));
            Assert.True(HasTreeItem(view, "folder:Inventory/Archive/Orders"));
        });

    private static void CompleteSaveAsDialog(Window owner, MessageLibraryPrototypeView view,
        string folderPath, string? name, bool cancel = false)
    {
        Exception? dialogFailure = null;
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Window dialog = owner.OwnedWindows.OfType<Window>().Single(child => child.Title == "Save as");
            try
            {
                if (cancel)
                {
                    Invoke(FindAutomationId<Button>(dialog, "LibraryNameCancel"));
                    return;
                }
                if (name is not null) Get<TextBox>(dialog, "NameInput").Text = name;
                ComboBox folders = Get<ComboBox>(dialog, "FolderInput");
                folders.SelectedItem = folders.Items.Cast<object>().Single(choice =>
                    string.Equals(choice.GetType().GetProperty("Path")?.GetValue(choice) as string, folderPath, StringComparison.Ordinal));
                FindAutomationId<Button>(dialog, "LibraryNameSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception exception)
            {
                dialogFailure = exception;
                dialog.Close();
            }
        }));

        FindAutomationId<Button>(view, "LibrarySaveAs").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Drain(owner);
        if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();
    }

    private static void CompleteMoveDialog(Window owner, MessageLibraryPrototypeView view,
        string sourceTag, string destination, string? expectedError = null)
    {
        Exception? dialogFailure = null;
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Window dialog = owner.OwnedWindows.OfType<Window>().Single(child => child.Title == "Move to");
            try
            {
                ComboBox folders = Get<ComboBox>(dialog, "FolderInput");
                folders.SelectedItem = folders.Items.Cast<object>().Single(choice =>
                    string.Equals(choice.GetType().GetProperty("Path")?.GetValue(choice) as string, destination, StringComparison.Ordinal));
                FindAutomationId<Button>(dialog, "LibraryNameSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (expectedError is not null)
                {
                    TextBlock error = Get<TextBlock>(dialog, "ErrorText");
                    Assert.Equal(Visibility.Visible, error.Visibility);
                    Assert.Contains(expectedError, error.Text, StringComparison.OrdinalIgnoreCase);
                    Invoke(FindAutomationId<Button>(dialog, "LibraryNameCancel"));
                }
            }
            catch (Exception exception)
            {
                dialogFailure = exception;
                dialog.Close();
            }
        }));

        TreeViewItem source = GetTreeItem(view, sourceTag);
        MenuItem move = source.ContextMenu!.Items.OfType<MenuItem>().Single(item =>
            AutomationProperties.GetAutomationId(item) == "LibraryMoveTo");
        move.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Drain(owner);
        if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();
    }

    private static void RaiseLibraryDrop(MessageLibraryPrototypeView view, string sourceTag, UIElement target)
    {
        Type payloadType = typeof(MessageLibraryPrototypeView).GetNestedType("LibraryDrag", BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException("The library drag payload type was not found.");
        ConstructorInfo payloadConstructor = payloadType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 2);
        object payload = payloadConstructor.Invoke([view, sourceTag]);
        var data = new DataObject(payloadType, payload);
        ConstructorInfo dropConstructor = typeof(DragEventArgs)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(constructor =>
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                return parameters.Length == 5 && parameters[0].ParameterType == typeof(IDataObject);
            });
        var drop = (DragEventArgs)dropConstructor.Invoke([data, DragDropKeyStates.None,
            DragDropEffects.Move, target, new Point(1, 1)]);
        drop.RoutedEvent = UIElement.DropEvent;
        target.RaiseEvent(drop);
    }

    private static void AddFolder(Window owner, MessageLibraryPrototypeView view, string name)
    {
        Exception? dialogFailure = null;
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Window dialog = owner.OwnedWindows.OfType<Window>().Single(child => child.Title == "Add folder");
            try
            {
                Descendants(dialog).OfType<TextBox>().Single().Text = name;
                Descendants(dialog).OfType<Button>().Single(button => Equals(button.Content, "Save"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception exception)
            {
                dialogFailure = exception;
                dialog.Close();
            }
        }));
        FindAutomationId<Button>(view, "LibraryAddFolder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Drain(owner);
        if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();
    }

    private static bool HasTreeItem(FrameworkElement root, string tag) => TreeItems(root).Any(item => Equals(item.Tag, tag));

    private static TreeViewItem GetTreeItem(FrameworkElement root, string tag) => TreeItems(root).Single(item => Equals(item.Tag, tag));

    private static IEnumerable<TreeViewItem> TreeItems(FrameworkElement root) =>
        TreeDescendants(Get<TreeView>(root, "LibraryTree").Items.OfType<TreeViewItem>());

    private static IEnumerable<TreeViewItem> TreeDescendants(IEnumerable<TreeViewItem> roots)
    {
        foreach (TreeViewItem root in roots)
        {
            yield return root;
            foreach (TreeViewItem child in root.Items.OfType<TreeViewItem>())
                foreach (TreeViewItem descendant in TreeDescendants([child])) yield return descendant;
        }
    }

    private static T FindAutomationId<T>(DependencyObject root, string automationId) where T : DependencyObject =>
        Descendants(root).OfType<T>().Single(element => AutomationProperties.GetAutomationId(element) == automationId);

    private static void Invoke(Button button) => ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!
        .GetPattern(PatternInterface.Invoke)!).Invoke();

    private static void Run(Action<Window, MessageLibraryPrototypeView> proof)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var view = new MessageLibraryPrototypeView();
                window = new Window
                {
                    Content = view,
                    Width = 1337,
                    Height = 850,
                    ShowActivated = false,
                    ShowInTaskbar = false
                };
                window.Show();
                Drain(window);
                window.UpdateLayout();
                proof(window, view);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Message Workbench organization proof exceeded 25 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Drain(Window window) => window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static T Get<T>(FrameworkElement root, string name) where T : class =>
        Assert.IsAssignableFrom<T>(root.FindName(name));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
