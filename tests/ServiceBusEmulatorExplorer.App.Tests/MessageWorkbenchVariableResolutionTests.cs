using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchVariableResolutionTests
{
    [Fact]
    public void Invalid_variable_name_stays_in_the_prompt_until_corrected()
        => Run((window, view) =>
        {
            Exception? dialogFailure = null;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var dialog = window.OwnedWindows.Cast<Window>().Single(candidate => candidate.Title == "Add variable");
                try
                {
                    var input = Descendants(dialog).OfType<TextBox>().Single();
                    var save = Descendants(dialog).OfType<Button>().Single(button => Equals(button.Content, "Save"));
                    input.Text = "Order-ID";
                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(dialog.IsVisible);
                    Assert.Contains(Descendants(dialog).OfType<TextBlock>(), block =>
                        block.Visibility == Visibility.Visible && block.Text.StartsWith("Use letters", StringComparison.Ordinal));
                    input.Text = "Order_ID";
                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception exception) { dialogFailure = exception; dialog.Close(); }
            }));
            Get<Button>(view, "EditorVariablesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Descendants(view).OfType<Button>().Single(button => Equals(button.Content, "Add variable"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window);
            if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();
            Assert.Equal("Order_ID default", Get<TextBlock>(view, "SelectedVariableName").Text);
        });

    [Fact]
    public void Added_variable_default_materializes_in_body_and_property_without_recursive_expansion()
        => Run((window, view) =>
        {
            AddVariable(window, view, "Greeting");
            Get<CheckBox>(view, "UseVariableDefault").IsChecked = true;
            Get<TextBox>(view, "VariableDefaultValue").Text = "hello \"world\"\n$(Amount)";

            Get<Button>(view, "EditorBodyTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Get<JsonEditor>(view, "EditorText").Text = "{\n  \"greeting\": \"$(Greeting)\",\n  \"amount\": \"$(Amount)\"\n}";
            Get<Button>(view, "EditorPropertiesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SetPropertyValue(view, "amount", "$(Greeting)");
            Drain(window);

            Assert.True(Get<Button>(view, "ReviewButton").IsEnabled);
            using var body = JsonDocument.Parse(Get<JsonEditor>(view, "PreviewText").Text);
            Assert.Equal("hello \"world\"\n$(Amount)", body.RootElement.GetProperty("greeting").GetString());
            Assert.Equal(149.90m, body.RootElement.GetProperty("amount").GetDecimal());

            Get<Button>(view, "PropertiesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("amount (decimal) = hello \"world\"", Get<JsonEditor>(view, "PreviewText").Text);
            Assert.Contains("$(Amount)", Get<JsonEditor>(view, "PreviewText").Text);
        });

    [Fact]
    public void Referenced_variable_without_default_blocks_review_and_default_change_invalidates_preview()
        => Run((window, view) =>
        {
            AddVariable(window, view, "RequiredText");
            Get<Button>(view, "EditorBodyTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Get<JsonEditor>(view, "EditorText").Text = "{\n  \"value\": \"$(RequiredText)\"\n}";
            Drain(window);

            Assert.False(Get<Button>(view, "ReviewButton").IsEnabled);
            Assert.Contains("RequiredText", Get<TextBlock>(view, "PreviewHint").Text);
            Assert.Contains("default value", Get<TextBlock>(view, "PreviewHint").Text);

            Get<CheckBox>(view, "UseVariableDefault").IsChecked = true;
            Get<TextBox>(view, "VariableDefaultValue").Text = "ready";
            Assert.False(Get<Button>(view, "ReviewButton").IsEnabled);
            Drain(window);
            Assert.True(Get<Button>(view, "ReviewButton").IsEnabled);
        });

    private static void AddVariable(Window window, MessageLibraryPrototypeView view, string name)
    {
        Exception? dialogFailure = null;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var dialog = window.OwnedWindows.Cast<Window>().Single(candidate => candidate.Title == "Add variable");
            try
            {
                Assert.Equal(14, dialog.FontSize);
                Assert.Equal(view.FindResource("CanvasBrush"), dialog.Background);
                var input = Descendants(dialog).OfType<TextBox>().Single();
                input.Text = name;
                var save = Descendants(dialog).OfType<Button>().Single(button => Equals(button.Content, "Save"));
                Assert.Equal(dialog.FindResource("PrimaryButton"), save.Style);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception exception) { dialogFailure = exception; dialog.Close(); }
        }));
        Get<Button>(view, "EditorVariablesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Descendants(view).OfType<Button>().Single(button => Equals(button.Content, "Add variable"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Drain(window);
        if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();
        Assert.Equal(name, Get<TextBlock>(view, "SelectedVariableName").Text.Replace(" default", ""));
    }

    private static void SetPropertyValue(MessageLibraryPrototypeView view, string name, string value)
    {
        var grid = Get<DataGrid>(view, "ApplicationPropertiesGrid");
        var item = grid.Items.Cast<object>().Single(candidate =>
            candidate.GetType().GetProperty("Name")?.GetValue(candidate)?.ToString() == name);
        item.GetType().GetProperty("Value")!.SetValue(item, value);
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
                view.UpdateLayout();
                proof(window, view);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Workbench variable proof exceeded 25 seconds.");
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
