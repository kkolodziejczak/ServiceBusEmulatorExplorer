using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchAssociationTests
{
    [Fact]
    public void Inline_destination_selects_one_target_for_review_and_empty_selection_disables_review()
        => Run((window, view) =>
        {
            var destination = Get<ComboBox>(view, "TemplateDestination");
            Assert.False(destination.IsEditable);
            Assert.Equal(4, destination.Items.Count);

            destination.SelectedValue = "inventory-events";
            Drain(window);
            Get<Button>(view, "ContinueToPrepareButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);
            Assert.True(Get<Button>(view, "ReviewButton").IsEnabled);

            Get<Button>(view, "ReviewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);
            var review = Assert.IsType<MessageLibraryPrototypeReviewSurface>(Get<ContentControl>(view, "ReviewHost").Content);
            Assert.Equal("inventory-events", review.Target);

            Descendants(review).OfType<Button>().Single(button => Equals(button.Content, "Back to preparation"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            destination.SelectedValue = string.Empty;
            Drain(window);
            Assert.False(Get<Button>(view, "ReviewButton").IsEnabled);
            Assert.Equal(string.Empty, destination.SelectedValue);
        });

    [Fact]
    public void Saved_destination_and_template_content_return_when_reopened_from_another_tree_row()
        => Run((window, view) =>
        {
            const string body = "{\"customerId\":\"saved with destination\"}";
            Get<JsonEditor>(view, "EditorText").Text = body;
            Get<TextBox>(view, "PropertySubject").Text = "SavedSubject";
            Get<ComboBox>(view, "TemplateDestination").SelectedValue = "inventory-events";
            Get<Button>(view, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);

            GetTreeItem(view, "template:Order updated").IsSelected = true;
            GetTreeItem(view, "template:Order created").IsSelected = true;
            Drain(window);

            Assert.Equal(body, Get<JsonEditor>(view, "EditorText").Text);
            Assert.Equal("SavedSubject", Get<TextBox>(view, "PropertySubject").Text);
            Assert.Equal("inventory-events", Get<ComboBox>(view, "TemplateDestination").SelectedValue);
        });

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
                Get<Button>(view, "EditorPropertiesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Drain(window);
                view.UpdateLayout();
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Workbench destination proof exceeded 25 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Drain(Window window) =>
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static T Get<T>(FrameworkElement root, string name) where T : class =>
        Assert.IsAssignableFrom<T>(root.FindName(name));

    private static TreeViewItem GetTreeItem(FrameworkElement root, string tag) =>
        Descendants(root).OfType<TreeViewItem>().Single(item => Equals(item.Tag, tag));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
