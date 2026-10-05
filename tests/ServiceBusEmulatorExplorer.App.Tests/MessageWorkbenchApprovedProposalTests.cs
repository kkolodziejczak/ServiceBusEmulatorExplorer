using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchApprovedProposalTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Author_copy_button_contains_the_shared_copy_icon()
        => RunView((_, view) =>
        {
            Button copy = Descendants(view).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "LibraryCopyAuthor");
            Path icon = Descendants(copy).OfType<Path>().Single();
            Assert.Same(view.FindResource("CopyIcon"), icon.Style);
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Valid_csv_has_no_results_action_before_a_run()
        => RunView((_, view) =>
        {
            Click(view, "ContinueToPrepareButton");
            Button validation = Get<Button>(view, "ValidationDetailsButton");
            Assert.Equal(Visibility.Collapsed, validation.Visibility);
            Assert.Equal("View last run", Get<Button>(view, "ViewRunResultsButton").Content);
            Assert.Equal(Visibility.Collapsed, Get<Button>(view, "ViewRunResultsButton").Visibility);
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Invalid_csv_offers_view_errors()
        => RunView((window, view) =>
        {
            Assert.True(Get<RadioButton>(view, "CsvMode").IsChecked);
            Click(view, "ContinueToPrepareButton");
            Assert.Equal(Visibility.Visible, Get<Border>(view, "CsvValidationSummary").Visibility);
            Get<ComboBox>(view, "CsvDelimiter").SelectedIndex = 1;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Button validation = Get<Button>(view, "ValidationDetailsButton");
            Assert.Equal("View errors", validation.Content);
            Assert.Equal(Visibility.Visible, validation.Visibility);
            Assert.True(validation.IsEnabled);
            Assert.Equal(Visibility.Collapsed, Get<Button>(view, "ViewRunResultsButton").Visibility);
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Mapping_dialog_keeps_four_columns_and_sizes_around_its_content()
        => OnSta(() =>
        {
            var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Mapping, "Local", "orders", 3)
            { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                StackPanel surface = Get<StackPanel>(dialog, "MappingSurface");
                Assert.Equal(SizeToContent.Height, dialog.SizeToContent);

                Grid mappings = Descendants(surface).OfType<Grid>().Single(grid =>
                    Descendants(grid).OfType<TextBlock>().Any(text => text.Text == "Variable")
                    && Descendants(grid).OfType<TextBlock>().Any(text => text.Text == "Input source"));
                Assert.Equal(4, mappings.ColumnDefinitions.Count);
                Assert.DoesNotContain(Descendants(surface).OfType<TextBlock>(), text =>
                    text.Text.StartsWith("Input sources:", StringComparison.Ordinal));

                Button apply = Descendants(surface).OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button) == "PrototypeApplyMapping");
                Rect bounds = apply.TransformToAncestor(dialog).TransformBounds(
                    new Rect(0, 0, apply.ActualWidth, apply.ActualHeight));
                Assert.InRange(dialog.ActualHeight - bounds.Bottom, 0, 90);
            }
            finally { dialog.Close(); }
        });

    private static void RunView(Action<Window, MessageLibraryPrototypeView> proof)
        => OnSta(() =>
        {
            var view = new MessageLibraryPrototypeView();
            var window = new Window { Content = view, Width = 1337, Height = 850,
                ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                proof(window, view);
            }
            finally { window.Close(); }
        });

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Workbench proposal proof exceeded 25 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static T Get<T>(FrameworkElement root, string name) where T : class =>
        Assert.IsAssignableFrom<T>(root.FindName(name));

    private static void Click(FrameworkElement root, string name) =>
        Get<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }
}
