using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.ReadmeScreenshot;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchAssociationTests
{
    [Fact]
    public void Add_association_uses_noneditable_dummy_destination_picker_and_disables_ambiguous_review()
        => Run((window, view) =>
        {
            Drain(window);
            Assert.True(Get<Button>(view, "ReviewButton").IsEnabled);

            ChooseDestination(window, "inventory-events");

            Assert.Contains("inventory-events", AssociationNames(view));
            Assert.False(Get<Button>(view, "ReviewButton").IsEnabled);
        });

    [Fact]
    public void Edit_preselects_current_destination_and_cancel_preserves_association()
        => Run((window, view) =>
        {
            var edit = Descendants(Get<Panel>(view, "AssociationChips")).OfType<Button>().Single(button =>
                Equals(button.Content, "Edit"));
            Exception? dialogFailure = null;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var dialog = window.OwnedWindows.OfType<MessageLibraryPrototypeDialog>().Single();
                try
                {
                    var picker = (ComboBox)dialog.FindName("DestinationPicker")!;
                    Assert.False(picker.IsEditable);
                    Assert.Equal("order-events", ((ComboBoxItem)picker.SelectedItem!).Tag);
                    Descendants(dialog).OfType<Button>().Single(button => button.IsVisible && Equals(button.Content, "Cancel"))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception exception) { dialogFailure = exception; dialog.Close(); }
            }));
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();

            Assert.Contains("order-events", AssociationNames(view));
            Assert.DoesNotContain("inventory-events", AssociationNames(view));
        });

    [Fact]
    public void Edit_applies_selected_dummy_destination()
        => Run((window, view) =>
        {
            var edit = Descendants(Get<Panel>(view, "AssociationChips")).OfType<Button>().Single(button =>
                Equals(button.Content, "Edit"));
            Exception? dialogFailure = null;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var dialog = window.OwnedWindows.OfType<MessageLibraryPrototypeDialog>().Single();
                try
                {
                    var picker = (ComboBox)dialog.FindName("DestinationPicker")!;
                    picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(item =>
                        Equals(item.Tag, "inventory-events"));
                    Descendants(dialog).OfType<Button>().Single(button =>
                        AutomationProperties.GetAutomationId(button) == "PrototypeApplyDestination")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception exception) { dialogFailure = exception; dialog.Close(); }
            }));
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();

            Assert.Contains("inventory-events", AssociationNames(view));
            Assert.DoesNotContain("order-events", AssociationNames(view));
        });

    [Fact]
    public void Duplicate_association_choice_is_ignored()
        => Run((window, view) =>
        {
            ChooseDestination(window, "order-events");

            Assert.Equal(1, AssociationNames(view).Count(name => name == "order-events"));
        });

    private static void ChooseDestination(Window window, string destination)
    {
        Exception? dialogFailure = null;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var dialog = window.OwnedWindows.OfType<MessageLibraryPrototypeDialog>().Single();
            try
            {
                var picker = (ComboBox)dialog.FindName("DestinationPicker")!;
                Assert.False(picker.IsEditable);
                Assert.Equal(3, picker.Items.Count);
                picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(item =>
                    string.Equals(item.Tag?.ToString(), destination, StringComparison.Ordinal));
                Descendants(dialog).OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button) == "PrototypeApplyDestination")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception exception) { dialogFailure = exception; dialog.Close(); }
        }));
        var add = Descendants((DependencyObject)window.Content!).OfType<Button>().Single(button =>
            Equals(button.Content, "+ Add association"));
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Workbench association proof exceeded 25 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Drain(Window window) =>
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IReadOnlyList<string> AssociationNames(FrameworkElement view) =>
        Descendants(Get<Panel>(view, "AssociationChips")).OfType<TextBlock>()
            .Select(text => text.Text).Where(text => text is "order-events" or "inventory-events" or "order-replies")
            .ToArray();

    private static T Get<T>(FrameworkElement root, string name) where T : class =>
        Assert.IsAssignableFrom<T>(root.FindName(name));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

}
