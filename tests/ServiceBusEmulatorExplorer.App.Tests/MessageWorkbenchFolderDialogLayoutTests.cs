using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchFolderDialogLayoutTests
{
    [Theory]
    [InlineData(1337, 850)]
    [InlineData(725, 564)]
    [Trait("TestCategory", "UiRender")]
    public void Add_folder_actions_remain_inside_the_client_area_before_and_after_validation(int width, int height)
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
                    Width = width,
                    Height = height,
                    ShowActivated = false,
                    ShowInTaskbar = false
                };
                window.Show();
                Drain(window.Dispatcher);

                Exception? dialogFailure = null;
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    Window dialog = window.OwnedWindows.OfType<Window>().Single(child => child.Title == "Add folder");
                    try
                    {
                        var content = Assert.IsType<StackPanel>(dialog.Content);
                        var buttons = Descendants(content).OfType<Button>().ToArray();
                        Button cancel = buttons.Single(button => Equals(button.Content, "Cancel"));
                        Button save = buttons.Single(button => Equals(button.Content, "Save"));
                        AssertActionsFit(dialog, content, cancel, save, "initial dialog");

                        TextBox input = Descendants(content).OfType<TextBox>().Single();
                        input.Text = string.Empty;
                        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Drain(dialog.Dispatcher);

                        Assert.Contains(Descendants(content).OfType<TextBlock>(), text =>
                            text.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(text.Text)
                            && (text.Text.Contains("name", StringComparison.OrdinalIgnoreCase)
                                || text.Text.Contains("folder", StringComparison.OrdinalIgnoreCase)));
                        AssertActionsFit(dialog, content, cancel, save, "validation message");
                        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception exception)
                    {
                        dialogFailure = exception;
                        dialog.Close();
                    }
                }));

                Descendants((DependencyObject)view).OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button) == "LibraryAddFolder")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Add folder dialog layout proof exceeded 25 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void AssertActionsFit(Window dialog, FrameworkElement content,
        FrameworkElement cancel, FrameworkElement save, string state)
    {
        Drain(dialog.Dispatcher);
        dialog.UpdateLayout();
        content.UpdateLayout();
        Assert.True(dialog.ActualWidth >= 420, $"The Add folder dialog should retain its 420px width: {dialog.ActualWidth}.");
        FrameworkElement client = VisualAncestors(content).OfType<FrameworkElement>()
            .FirstOrDefault(ancestor => ancestor is ContentPresenter or AdornerDecorator)
            ?? throw new Xunit.Sdk.XunitException("The Add folder dialog has no window client presenter.");

        foreach (FrameworkElement action in new[] { cancel, save })
        {
            Rect buttonBounds = action.TransformToAncestor(client).TransformBounds(new Rect(action.RenderSize));
            Assert.True(buttonBounds.Left >= -0.5 && buttonBounds.Top >= -0.5
                && buttonBounds.Right <= client.ActualWidth + 0.5
                && buttonBounds.Bottom <= client.ActualHeight + 0.5,
                $"{state}: {((Button)action).Content} is outside the dialog client area: {buttonBounds} in {client.ActualWidth}x{client.ActualHeight}.");
            Assert.True(client.ActualHeight - buttonBounds.Bottom >= 8,
                $"{state}: {((Button)action).Content} has less than 8px clearance to the native client edge: button={buttonBounds}, client height={client.ActualHeight}.");

            foreach (FrameworkElement ancestor in VisualAncestors(action).OfType<FrameworkElement>())
            {
                Geometry? visibleRegion = LayoutInformation.GetLayoutClip(ancestor);
                if (visibleRegion is null) continue;
                Rect ancestorBounds = action.TransformToAncestor(ancestor).TransformBounds(new Rect(action.RenderSize));
                Assert.True(visibleRegion.FillContains(new RectangleGeometry(ancestorBounds)),
                    $"{state}: {((Button)action).Content} is clipped by {ancestor.GetType().Name}; bounds={ancestorBounds}, visible region={visibleRegion.Bounds}.");
                if (ReferenceEquals(ancestor, client)) break;
            }
        }
    }

    private static IEnumerable<DependencyObject> VisualAncestors(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            yield return current;
    }

    private static void Drain(Dispatcher dispatcher) =>
        dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
