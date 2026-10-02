using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchStateTests
{
    [Fact]
    public void Body_editor_uses_one_json_surface_without_mode_buttons() => Run((window, view) =>
    {
        string before = Get<JsonEditor>(view, "PreviewText").Text;
        Assert.Null(view.FindName("EditorModeButtons"));
        Assert.Null(view.FindName("EditorPlainText"));
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.Equal(before, Get<JsonEditor>(view, "PreviewText").Text);
    });

    [Fact]
    public void New_template_is_a_direct_action_without_a_capture_menu() => Run((_, view) =>
    {
        Button create = Descendants(view).OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "LibraryNew");
        Assert.Null(create.ContextMenu);
        Assert.Equal("New template", AutomationProperties.GetName(create));
    });

    [Fact]
    public void New_template_starts_in_memory_with_empty_json_and_no_inherited_properties() => Run((_, view) =>
    {
        Descendants(view).OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "LibraryNew")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Untitled message", Get<TextBlock>(view, "AuthorTitle").Text);
        Assert.Equal("{}", Get<JsonEditor>(view, "EditorText").Text);
        Assert.Empty((System.Collections.IEnumerable)Get<DataGrid>(view, "ApplicationPropertiesGrid").ItemsSource);
        Assert.Empty((System.Collections.IEnumerable)Get<DataGrid>(view, "VariablesGrid").ItemsSource);
        Assert.Contains(TreeItems(Get<TreeView>(view, "LibraryTree")), item =>
            Equals(item.Tag, "template:Untitled message"));
    });

    [Fact]
    public void Added_folder_remains_visible_and_selectable_under_namespace_filter() => Run((window, view) =>
    {
        view.FilterByNamespaceQuery("order-events");
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Window dialog = window.OwnedWindows.OfType<Window>().Single(child => child.Title == "Add folder");
            Descendants(dialog).OfType<TextBox>().Single().Text = "New folder";
            Descendants(dialog).OfType<Button>().Single(button => Equals(button.Content, "Save"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Descendants(view).OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "LibraryAddFolder")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        TreeView tree = Get<TreeView>(view, "LibraryTree");
        Assert.Contains(TreeItems(tree), item =>
            Equals(item.Tag, "folder:Orders/New folder")
            && AutomationProperties.GetName(item) == "New folder");
    });

    [Fact]
    public void Editing_single_input_automatically_refreshes_validation_and_preview() => Run((window, view) =>
    {
        Click(view, "ContinueToPrepareButton");
        Get<RadioButton>(view, "SingleMode").IsChecked = true;
        Get<TextBox>(view, "AmountInput").Text = "invalid";
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.False(Get<Button>(view, "ReviewButton").IsEnabled);
        Get<TextBox>(view, "AmountInput").Text = "25.00";
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.True(Get<Button>(view, "ReviewButton").IsEnabled);
        using var body = JsonDocument.Parse(Get<JsonEditor>(view, "PreviewText").Text);
        Assert.Equal(25, body.RootElement.GetProperty("amount").GetDecimal());
    });

    [Fact]
    public void Single_generated_fields_match_prepared_body_and_clear_on_input_change() => Run((window, view) =>
    {
        Click(view, "ContinueToPrepareButton");
        Get<RadioButton>(view, "SingleMode").IsChecked = true;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        using var body = JsonDocument.Parse(Get<JsonEditor>(view, "PreviewText").Text);
        var eventId = Get<TextBox>(view, "SingleEventId");
        var occurredAt = Get<TextBox>(view, "SingleOccurredAt");
        Assert.Equal(body.RootElement.GetProperty("eventId").GetString(), eventId.Text);
        Assert.Equal(body.RootElement.GetProperty("occurredAt").GetString(), occurredAt.Text);
        Assert.True(Guid.TryParse(eventId.Text, out _));
        Assert.True(DateTimeOffset.TryParse(occurredAt.Text, out _));
        Get<TextBox>(view, "AmountInput").Text = "invalid";
        Assert.False(Get<Button>(view, "ReviewButton").IsEnabled);
        Assert.Equal("Waiting for valid inputs", eventId.Text);
        Assert.Equal("Waiting for valid inputs", occurredAt.Text);
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.False(Get<Button>(view, "ReviewButton").IsEnabled);
    });

    [Fact]
    public void Csv_preview_heading_follows_selected_row_on_body_and_properties_and_resets_for_single() => Run((window, view) =>
    {
        Click(view, "ContinueToPrepareButton");
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var rows = Get<DataGrid>(view, "CsvRowsGrid");
        var heading = Get<Grid>(view, "PreviewSurface").Children.OfType<TextBlock>().Single(block => Grid.GetRow(block) == 0);
        foreach (int index in new[] { 1, 2, 0 })
        {
            rows.SelectedIndex = index;
            Click(view, "BodyTab");
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            using var body = JsonDocument.Parse(Get<JsonEditor>(view, "PreviewText").Text);
            Assert.Equal($"C100{index + 1}", body.RootElement.GetProperty("customerId").GetString());
            Assert.Equal($"Row {index + 1} preview", heading.Text);
            Click(view, "PropertiesTab");
            Assert.Contains($"Correlation ID: C100{index + 1}", Get<JsonEditor>(view, "PreviewText").Text);
            Assert.Equal($"Row {index + 1} preview", heading.Text);
        }
        rows.SelectedIndex = 1;
        Click(view, "BodyTab");
        window.UpdateLayout();
        string? proofDirectory = Environment.GetEnvironmentVariable("SBE_ROW_PREVIEW_PROOF");
        if (!string.IsNullOrEmpty(proofDirectory))
        {
            var content = (FrameworkElement)window.Content;
            ServiceBusEmulatorExplorer.ReadmeScreenshot.WpfScreenshot.SaveWindowContent(window,
                System.IO.Path.Combine(proofDirectory, "row-2-body.png"),
                (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight));
        }
        Get<RadioButton>(view, "SingleMode").IsChecked = true;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.Equal("Row 1 preview", heading.Text);
        Get<RadioButton>(view, "CsvMode").IsChecked = true;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.Equal("Row 1 preview", heading.Text);
    });
    [Fact]
    public void Csv_selected_preview_and_review_keep_the_same_prepared_event_ids() => Run((window, view) =>
    {
        Click(view, "ContinueToPrepareButton");
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var rows = Get<DataGrid>(view, "CsvRowsGrid");
        var ids = new List<string>();
        for (int index = 0; index < rows.Items.Count; index++)
        {
            rows.SelectedIndex = index;
            using var body = JsonDocument.Parse(Get<JsonEditor>(view, "PreviewText").Text);
            ids.Add(body.RootElement.GetProperty("eventId").GetString()!);
        }
        Assert.Equal(3, ids.Distinct().Count());
        Click(view, "ReviewButton");
        var review = Assert.IsType<MessageLibraryPrototypeReviewSurface>(Get<ContentControl>(view, "ReviewHost").Content);
        Assert.Null(review.FindName("ReviewMessageIds"));
        Assert.Equal("3 valid messages", Get<TextBlock>(review, "ReviewMessageCount").Text);
        Assert.NotNull(review.FindName("ReviewTotalSize"));
        Click(view, "PrepareStepButton");
        rows.SelectedIndex = 0;
        using var selectedAgain = JsonDocument.Parse(Get<JsonEditor>(view, "PreviewText").Text);
        Assert.Equal(ids[0], selectedAgain.RootElement.GetProperty("eventId").GetString());
    });

    [Fact]
    public void Custom_message_id_is_not_listed_in_review_and_invalid_json_disables_review() => Run((window, view) =>
    {
        Get<RadioButton>(view, "SingleMode").IsChecked = true;
        Get<RadioButton>(view, "CustomMessageId").IsChecked = true;
        Get<TextBox>(view, "CustomMessageIdInput").Text = "my-dummy-id";
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Click(view, "ReviewButton");
        var review = Assert.IsType<MessageLibraryPrototypeReviewSurface>(Get<ContentControl>(view, "ReviewHost").Content);
        Assert.Null(review.FindName("ReviewMessageIds"));
        Assert.Equal("1 valid message", Get<TextBlock>(review, "ReviewMessageCount").Text);
        Click(review, "ConfirmDispatch");
        PumpUntil(window.Dispatcher, () => Get<DataGrid>(review, "ResultsGrid").Items.Count == 1, TimeSpan.FromSeconds(5));
        object sent = Assert.Single(Get<DataGrid>(review, "ResultsGrid").Items.Cast<object>());
        Assert.Equal("my-dummy-id", sent.GetType().GetProperty("MessageId")!.GetValue(sent));
        Click(view, "ComposeStepButton");
        Get<JsonEditor>(view, "EditorText").Text = "{ invalid json";
        Click(view, "ContinueToPrepareButton");
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.False(Get<Button>(view, "ReviewButton").IsEnabled);
        Assert.Equal("Waiting for valid inputs", Get<TextBox>(view, "SingleEventId").Text);
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
                window = new Window { Content = view, Width = 1337, Height = 850,
                    ShowActivated = false, ShowInTaskbar = false };
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                proof(window, view);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Workbench proof exceeded 25 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static T Get<T>(FrameworkElement root, string name) where T : class =>
        Assert.IsAssignableFrom<T>(root.FindName(name));

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

    private static void Click(FrameworkElement root, string name) =>
        Get<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void PumpUntil(Dispatcher dispatcher, Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Message Workbench did not reach the expected state.");

            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
