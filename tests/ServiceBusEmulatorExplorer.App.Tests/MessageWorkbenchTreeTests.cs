using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchTreeTests
{
    [Fact]
    public void Folder_selection_and_blank_tree_click_preserve_the_open_draft_and_set_creation_parent()
        => Run((window, view) =>
        {
            const string draft = "{\"customerId\":\"unsaved draft\"}";
            Get<JsonEditor>(view, "EditorText").Text = draft;

            TreeViewItem orders = GetTreeItem(view, "folder:Orders");
            orders.IsSelected = true;
            orders.IsExpanded = false;
            Drain(window);
            Assert.Equal(draft, Get<JsonEditor>(view, "EditorText").Text);
            AddFolder(window, view, "Returns");
            TreeViewItem createdChild = GetTreeItem(view, "folder:Orders/Returns");
            Assert.True(createdChild.IsSelected);
            Assert.True(GetTreeItem(view, "folder:Orders").IsExpanded);
            Assert.Equal(draft, Get<JsonEditor>(view, "EditorText").Text);

            TreeView tree = Get<TreeView>(view, "LibraryTree");
            tree.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Drain(window);
            Assert.Null(tree.SelectedItem);
            Assert.Equal(draft, Get<JsonEditor>(view, "EditorText").Text);

            AddFolder(window, view, "Loose");
            Assert.True(HasTreeItem(view, "folder:Loose"));
            TreeViewItem selectedTemplate = GetTreeItem(view, "template:Order created");
            selectedTemplate.IsSelected = true;
            Drain(window);

            Get<JsonEditor>(view, "EditorText").RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,
                Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Get<TextBox>(view, "PropertySubject").RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,
                Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Get<TextBlock>(view, "AuthorTitle").RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,
                Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            FindAutomationId<Button>(view, "LibraryCollapseAll").RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,
                Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Drain(window);

            Assert.Same(selectedTemplate, tree.SelectedItem);
            Assert.True(selectedTemplate.IsSelected);
            Assert.Equal(draft, Get<JsonEditor>(view, "EditorText").Text);
        });

    [Fact]
    public void Folder_created_from_template_is_a_sibling_under_the_templates_folder()
        => Run((window, view) =>
        {
            GetTreeItem(view, "template:Stock reserved").IsSelected = true;
            Drain(window);
            AddFolder(window, view, "Reserved examples");

            TreeViewItem sibling = GetTreeItem(view, "folder:Inventory/Reserved examples");
            Assert.Equal("Reserved examples", AutomationProperties.GetName(sibling));
            Assert.Equal("Stock reserved", Get<TextBlock>(view, "AuthorTitle").Text);
        });

    [Fact]
    public void Canceling_a_switch_from_a_dirty_template_keeps_selection_and_draft()
        => Run((window, view) =>
        {
            const string draft = "{\"customerId\":\"keep this draft\"}";
            Get<JsonEditor>(view, "EditorText").Text = draft;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                Window guard = window.OwnedWindows.OfType<Window>().Single(child => child.Title == "Save changes?");
                Descendants(guard).OfType<Button>().Single(button => button.IsVisible && Equals(button.Content, "Cancel"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));

            GetTreeItem(view, "template:Order updated").IsSelected = true;
            Drain(window);

            TreeView tree = Get<TreeView>(view, "LibraryTree");
            Assert.Equal("template:Order created", ((TreeViewItem)tree.SelectedItem!).Tag);
            Assert.Equal("Order created", Get<TextBlock>(view, "AuthorTitle").Text);
            Assert.Equal(draft, Get<JsonEditor>(view, "EditorText").Text);
        });

    [Fact]
    public void Clicking_an_unrelated_tree_preserves_library_selection_and_the_draft()
        => RunWithAnotherTree((window, view, otherItem) =>
        {
            const string draft = "{\"customerId\":\"click outside the library tree\"}";
            Get<JsonEditor>(view, "EditorText").Text = draft;
            GetTreeItem(view, "folder:Orders").IsSelected = true;
            Drain(window);

            otherItem.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,
                Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Drain(window);

            Assert.Equal("folder:Orders", ((TreeViewItem)Get<TreeView>(view, "LibraryTree").SelectedItem!).Tag);
            Assert.Equal(draft, Get<JsonEditor>(view, "EditorText").Text);
        });

    [Fact]
    public void Empty_folder_remains_visible_when_a_namespace_filter_hides_its_templates()
        => Run((window, view) =>
        {
            GetTreeItem(view, "folder:Orders").IsSelected = true;
            AddFolder(window, view, "Empty folder");
            view.FilterByNamespaceQuery("inventory-events");

            Assert.True(HasTreeItem(view, "folder:Orders/Empty folder"));
        });

    [Fact]
    public void Template_suggestions_group_matches_and_include_folder_context_then_open_selected_template()
        => Run((window, view) =>
        {
            TextBox search = Get<TextBox>(view, "TemplateSearch");
            search.Focus();
            search.Text = "Order";
            Drain(window);

            Popup popup = Get<Popup>(view, "LibrarySuggestionsPopup");
            ListBox suggestions = Get<ListBox>(view, "LibrarySuggestionsList");
            Assert.True(popup.IsOpen);
            Assert.False(popup.StaysOpen, "An outside interaction should dismiss the suggestion popup.");
            var collectionView = Assert.IsAssignableFrom<System.ComponentModel.ICollectionView>(suggestions.ItemsSource);
            Assert.Equal(2, collectionView.Groups.Count);

            object orderCreated = suggestions.Items.Cast<object>().Single(row => Read(row, "Title") == "Order created");
            Assert.Equal("TEMPLATES", Read(orderCreated, "Group"));
            Assert.Equal("Orders", Read(orderCreated, "Detail"));
            object ordersFolder = suggestions.Items.Cast<object>().Single(row => Read(row, "IsFolder") == "True");
            Assert.Equal("FOLDERS", Read(ordersFolder, "Group"));
            Assert.Equal("Orders", Read(ordersFolder, "Path"));

            PressPreview(search, Key.Up);
            Assert.Equal(suggestions.Items.Count - 1, suggestions.SelectedIndex);
            PressPreview(suggestions, Key.Escape);
            Drain(window);
            Assert.False(popup.IsOpen);
            Assert.True(search.IsKeyboardFocusWithin);

            search.Text = "Order updated";
            Drain(window);
            PressPreview(search, Key.Down);
            Assert.Equal(0, suggestions.SelectedIndex);
            PressPreview(search, Key.Enter);
            Assert.Equal("Order updated", Get<TextBlock>(view, "AuthorTitle").Text);
            Assert.Equal("template:Order updated", ((TreeViewItem)Get<TreeView>(view, "LibraryTree").SelectedItem!).Tag);
        });

    [Fact]
    public void Folder_suggestion_reveals_and_selects_folder_without_replacing_draft_and_combined_filter_hides_unrelated_rows()
        => Run((window, view) =>
        {
            const string draft = "{\"customerId\":\"unsaved folder suggestion draft\"}";
            Get<JsonEditor>(view, "EditorText").Text = draft;

            TextBox search = Get<TextBox>(view, "TemplateSearch");
            search.Focus();
            search.Text = "Orders";
            Drain(window);
            ListBox suggestions = Get<ListBox>(view, "LibrarySuggestionsList");
            int folderIndex = suggestions.Items.Cast<object>().ToList().FindIndex(row => Read(row, "IsFolder") == "True");
            Assert.True(folderIndex >= 0);
            suggestions.UpdateLayout();
            suggestions.SelectedIndex = folderIndex;
            ListBoxItem folderRow = Assert.IsAssignableFrom<ListBoxItem>(suggestions.ItemContainerGenerator.ContainerFromIndex(folderIndex));
            folderRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseUpEvent });
            Drain(window);

            Assert.Equal("folder:Orders", ((TreeViewItem)Get<TreeView>(view, "LibraryTree").SelectedItem!).Tag);
            Assert.True(GetTreeItem(view, "folder:Orders").IsExpanded);
            Assert.Equal(draft, Get<JsonEditor>(view, "EditorText").Text);

            view.FilterByNamespaceQuery("inventory-events");
            Drain(window);
            Assert.False(Get<Popup>(view, "LibrarySuggestionsPopup").IsOpen,
                "Changing namespace scope should close stale suggestions.");
            search.Text = "Stock reserved";
            Drain(window);
            Assert.True(Get<Popup>(view, "LibrarySuggestionsPopup").IsOpen);
            Assert.All(suggestions.Items.Cast<object>(), row => Assert.Equal("Stock reserved", Read(row, "Title")));
            search.Text = "Orders";
            Drain(window);
            Assert.False(Get<Popup>(view, "LibrarySuggestionsPopup").IsOpen,
                "A template/folder outside the active namespace filter should not remain as a stale suggestion.");
            Assert.Empty(suggestions.Items);

            search.Clear();
            Drain(window);
            Assert.False(Get<Popup>(view, "LibrarySuggestionsPopup").IsOpen);
            Assert.Equal(draft, Get<JsonEditor>(view, "EditorText").Text);
        });

    [Fact]
    public void Canceling_a_dirty_template_switch_from_suggestions_keeps_the_current_template_and_draft()
        => Run((window, view) =>
        {
            const string draft = "{\"customerId\":\"keep suggestion draft\"}";
            Get<JsonEditor>(view, "EditorText").Text = draft;
            TextBox search = Get<TextBox>(view, "TemplateSearch");
            search.Focus();
            search.Text = "Order updated";
            Drain(window);

            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                Window guard = window.OwnedWindows.OfType<Window>().Single(child => child.Title == "Save changes?");
                Descendants(guard).OfType<Button>().Single(button => button.IsVisible && Equals(button.Content, "Cancel"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            PressPreview(search, Key.Down);
            PressPreview(search, Key.Enter);
            Drain(window);

            Assert.Equal("template:Order created", ((TreeViewItem)Get<TreeView>(view, "LibraryTree").SelectedItem!).Tag);
            Assert.Equal("Order created", Get<TextBlock>(view, "AuthorTitle").Text);
            Assert.Equal(draft, Get<JsonEditor>(view, "EditorText").Text);
        });

    [Fact]
    public void Long_template_suggestion_trims_visually_and_keeps_full_title_and_folder_in_accessible_name()
        => Run((window, view) =>
        {
            string longName = "Long order template " + new string('x', 72);
            Get<Button>(view, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindAutomationId<Button>(view, "LibraryRename").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TextBox rename = VisibleRenameInput(view);
            rename.Text = longName;
            Press(rename, Key.Enter);
            Drain(window);

            TextBox search = Get<TextBox>(view, "TemplateSearch");
            search.Focus();
            search.Text = "Long order template";
            Drain(window);
            ListBox suggestions = Get<ListBox>(view, "LibrarySuggestionsList");
            object row = Assert.Single(suggestions.Items.Cast<object>());
            Assert.Equal(longName, Read(row, "Title"));
            Assert.Equal("Orders", Read(row, "Detail"));
            Assert.Equal($"Template, {longName}, folder Orders", Read(row, "AccessibleName"));

            suggestions.UpdateLayout();
            var container = Assert.IsAssignableFrom<ListBoxItem>(suggestions.ItemContainerGenerator.ContainerFromItem(row));
            container.UpdateLayout();
            Border accessibleRow = Descendants(container).OfType<Border>()
                .Single(element => AutomationProperties.GetName(element) == Read(row, "AccessibleName"));
            Assert.Equal($"Template, {longName}, folder Orders", AutomationProperties.GetName(accessibleRow));
            TextBlock title = Descendants(container).OfType<TextBlock>().Single(text => text.Text == longName);
            Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
            Assert.Equal(longName, title.ToolTip);
        });

    [Fact]
    public void Saving_a_renamed_new_root_template_keeps_it_visible_under_an_active_namespace_filter()
        => Run((window, view) =>
        {
            view.FilterByNamespaceQuery("inventory-events");
            Get<Button>(view, "LibraryNewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);

            FindAutomationId<Button>(view, "LibraryRename").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TextBox input = VisibleRenameInput(view);
            input.Text = "New root event";
            FindVisibleAutomationId<Button>(view, "LibraryRenameSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);

            Get<Button>(view, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);

            Assert.Equal("New root event", Get<TextBlock>(view, "AuthorTitle").Text);
            Assert.True(HasTreeItem(view, "template:New root event"));
            Assert.Equal("template:New root event", ((TreeViewItem)Get<TreeView>(view, "LibraryTree").SelectedItem!).Tag);
        });

    [Fact]
    public void Saving_a_renamed_new_root_template_stays_pinned_under_text_filter_until_navigating_away()
        => Run((window, view) =>
        {
            Get<TextBox>(view, "TemplateSearch").Text = "Stock reserved";
            Get<Button>(view, "LibraryNewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);

            FindAutomationId<Button>(view, "LibraryRename").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TextBox input = VisibleRenameInput(view);
            input.Text = "New root event";
            FindVisibleAutomationId<Button>(view, "LibraryRenameSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);

            Get<Button>(view, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);

            Assert.True(HasTreeItem(view, "template:New root event"));
            Assert.True(HasTreeItem(view, "template:Stock reserved"));

            GetTreeItem(view, "template:Stock reserved").IsSelected = true;
            Drain(window);

            Assert.Equal("Stock reserved", Get<TextBlock>(view, "AuthorTitle").Text);
            Assert.False(HasTreeItem(view, "template:New root event"));
        });

    [Fact]
    public void Renaming_folder_keeps_its_template_descendants_in_the_renamed_folder()
        => Run((window, view) =>
        {
            TreeViewItem orders = GetTreeItem(view, "folder:Orders");
            orders.IsSelected = true;
            BeginRenameWithF2(view);
            TextBox input = VisibleRenameInput(view);
            input.Text = "Sales";
            Press(input, Key.Enter);
            Drain(window);

            TreeViewItem renamed = GetTreeItem(view, "folder:Sales");
            Assert.Contains(TreeDescendants([renamed]), item =>
                Equals(item.Tag, "template:Order created"));
            Assert.Equal("Order created", Get<TextBlock>(view, "AuthorTitle").Text);
        });

    [Fact]
    public void Rename_template_preserves_body_properties_and_single_destination()
        => Run((window, view) =>
        {
            const string body = "{\"customerId\":\"rename proof\"}";
            Get<JsonEditor>(view, "EditorText").Text = body;
            Get<TextBox>(view, "PropertySubject").Text = "RenameSubject";
            Get<ComboBox>(view, "TemplateDestination").SelectedValue = "inventory-events";
            Get<Button>(view, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);

            FindAutomationId<Button>(view, "LibraryRename").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TextBox input = VisibleRenameInput(view);
            input.Text = "Order created renamed";
            Press(input, Key.Enter);
            Drain(window);

            Assert.Equal("Order created renamed", Get<TextBlock>(view, "AuthorTitle").Text);
            Assert.True(HasTreeItem(view, "template:Order created renamed"));
            Assert.Equal(body, Get<JsonEditor>(view, "EditorText").Text);
            Assert.Equal("RenameSubject", Get<TextBox>(view, "PropertySubject").Text);
            Assert.Equal("inventory-events", Get<ComboBox>(view, "TemplateDestination").SelectedValue);
        });

    [Fact]
    public void Invalid_and_duplicate_folder_renames_show_validation_and_escape_cancels()
        => Run((window, view) =>
        {
            GetTreeItem(view, "folder:Orders").IsSelected = true;
            BeginRenameWithF2(view);
            TextBox input = VisibleRenameInput(view);

            input.Text = string.Empty;
            Press(input, Key.Enter);
            Drain(window);
            Assert.True(input.IsVisible);
            Assert.True(VisibleRenameError(view));
            Assert.True(HasTreeItem(view, "folder:Orders"));

            input.Text = "Invalid/Path";
            Press(input, Key.Enter);
            Drain(window);
            Assert.True(input.IsVisible);
            Assert.True(VisibleRenameError(view));
            Assert.True(HasTreeItem(view, "folder:Orders"));

            input.Text = "Invalid\\Path";
            Press(input, Key.Enter);
            Drain(window);
            Assert.True(input.IsVisible);
            Assert.True(VisibleRenameError(view));
            Assert.True(HasTreeItem(view, "folder:Orders"));

            input.Text = "Inventory";
            Press(input, Key.Enter);
            Drain(window);
            Assert.True(input.IsVisible);
            Assert.True(VisibleRenameError(view));
            Assert.True(HasTreeItem(view, "folder:Orders"));

            input.Text = "Canceled rename";
            Press(input, Key.Escape);
            Drain(window);
            Assert.False(input.IsVisible);
            Assert.True(HasTreeItem(view, "folder:Orders"));
            Assert.False(HasTreeItem(view, "folder:Canceled rename"));
        });

    [Fact]
    public void Title_pencil_renames_the_open_template_through_its_visible_editor()
        => Run((window, view) =>
        {
            FindAutomationId<Button>(view, "LibraryRename").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TextBox input = VisibleRenameInput(view);
            Assert.Equal("Order created", input.Text);
            input.Text = "Title renamed";
            FindVisibleAutomationId<Button>(view, "LibraryRenameSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);

            Assert.Equal("Title renamed", Get<TextBlock>(view, "AuthorTitle").Text);
            Assert.True(HasTreeItem(view, "template:Title renamed"));
        });

    [Fact]
    public void Rename_focus_loss_does_not_commit_and_explicit_cancel_restores_the_original_name()
        => Run((window, view) =>
        {
            FindAutomationId<Button>(view, "LibraryRename").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TextBox input = VisibleRenameInput(view);
            input.Text = "Uncommitted title";
            Get<Button>(view, "EditorBodyTab").Focus();
            Drain(window);

            Assert.True(input.IsVisible);
            Assert.Equal("Order created", Get<TextBlock>(view, "AuthorTitle").Text);
            Assert.True(HasTreeItem(view, "template:Order created"));
            Assert.False(HasTreeItem(view, "template:Uncommitted title"));

            FindVisibleAutomationId<Button>(view, "LibraryRenameCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);
            Assert.False(input.IsVisible);
            Assert.Equal("Order created", Get<TextBlock>(view, "AuthorTitle").Text);
        });

    private static void BeginRenameWithF2(FrameworkElement view)
    {
        TreeView tree = Get<TreeView>(view, "LibraryTree");
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(tree)!, 0, Key.F2)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        tree.RaiseEvent(key);
    }

    private static void Press(UIElement target, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
        { RoutedEvent = Keyboard.KeyDownEvent };
        target.RaiseEvent(args);
    }

    private static void PressPreview(UIElement target, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(args);
    }

    private static string? Read(object row, string property) => row.GetType().GetProperty(property)?.GetValue(row)?.ToString();

    private static TextBox VisibleRenameInput(FrameworkElement root) =>
        Descendants(root).OfType<TextBox>().Single(input =>
            AutomationProperties.GetAutomationId(input) == "LibraryRenameInput" && input.IsVisible);

    private static bool VisibleRenameError(FrameworkElement root)
    {
        TextBox input = VisibleRenameInput(root);
        DependencyObject editor = VisualTreeHelper.GetParent(input)
            ?? throw new Xunit.Sdk.XunitException("Inline rename input has no editor container.");
        return Descendants(editor).OfType<TextBlock>().Any(text => text.IsVisible && !string.IsNullOrWhiteSpace(text.Text));
    }

    private static bool HasTreeItem(FrameworkElement root, string tag) =>
        TreeItems(root).Any(item => Equals(item.Tag, tag));

    private static TreeViewItem GetTreeItem(FrameworkElement root, string tag) =>
        TreeItems(root).Single(item => Equals(item.Tag, tag));

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
        Descendants(root).OfType<T>().Single(element =>
            AutomationProperties.GetAutomationId(element) == automationId);

    private static T FindVisibleAutomationId<T>(DependencyObject root, string automationId) where T : FrameworkElement =>
        Descendants(root).OfType<T>().Single(element =>
            AutomationProperties.GetAutomationId(element) == automationId && element.IsVisible);

    private static void AddFolder(Window window, FrameworkElement view, string name)
    {
        Exception? dialogFailure = null;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Window dialog = window.OwnedWindows.OfType<Window>().Single(child => child.Title == "Add folder");
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
        Descendants(view).OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "LibraryAddFolder")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Drain(window);
        if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();
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
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Workbench tree proof exceeded 25 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void RunWithAnotherTree(Action<Window, MessageLibraryPrototypeView, TreeViewItem> proof)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var view = new MessageLibraryPrototypeView();
                var otherTree = new TreeView { Height = 90 };
                var otherItem = new TreeViewItem { Header = "Other tree row" };
                otherTree.Items.Add(otherItem);
                var root = new DockPanel();
                DockPanel.SetDock(otherTree, Dock.Top);
                root.Children.Add(otherTree);
                root.Children.Add(view);
                window = new Window
                {
                    Content = root,
                    Width = 1337,
                    Height = 850,
                    ShowActivated = false,
                    ShowInTaskbar = false
                };
                window.Show();
                Drain(window);
                window.UpdateLayout();
                proof(window, view, otherItem);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Workbench unrelated-tree click proof exceeded 25 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Drain(Window window) =>
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static T Get<T>(FrameworkElement root, string name) where T : class =>
        Assert.IsAssignableFrom<T>(root.FindName(name));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
