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
        Assert.Contains(Descendants((DependencyObject)create.Content).OfType<TextBlock>(),
            text => text.Text == "+ New template");
    });

    [Fact]
    public void New_template_starts_with_empty_json_and_no_inherited_properties() => Run((window, view) =>
    {
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Window dialog = window.OwnedWindows.OfType<Window>().Single(child => child.Title == "New template");
            Descendants(dialog).OfType<TextBox>().Single().Text = "Empty example";
            Descendants(dialog).OfType<Button>().Single(button => Equals(button.Content, "Save"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Descendants(view).OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "LibraryNew")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Empty example", Get<TextBlock>(view, "AuthorTitle").Text);
        Assert.Equal("{}", Get<JsonEditor>(view, "EditorText").Text);
        Assert.Empty((System.Collections.IEnumerable)Get<DataGrid>(view, "ApplicationPropertiesGrid").ItemsSource);
        Assert.Empty((System.Collections.IEnumerable)Get<DataGrid>(view, "VariablesGrid").ItemsSource);
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
        Panel folders = Get<Panel>(view, "AddedFolders");
        Assert.Contains(Descendants(folders).OfType<Button>(), button =>
            AutomationProperties.GetName(button) == "Folder New folder");
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
        string summary = Get<TextBlock>(review, "ReviewMessageIds").Text;
        foreach (string id in ids) Assert.Contains(id, summary);
        Assert.NotNull(review.FindName("ReviewTotalSize"));
        Click(view, "PrepareStepButton");
        rows.SelectedIndex = 0;
        using var selectedAgain = JsonDocument.Parse(Get<JsonEditor>(view, "PreviewText").Text);
        Assert.Equal(ids[0], selectedAgain.RootElement.GetProperty("eventId").GetString());
    });

    [Theory]
    [InlineData("PrototypeConflictKeepEditing", false)]
    [InlineData("PrototypeConflictReload", true)]
    public void External_change_sample_refresh_reaches_conflict_and_preserves_or_reloads_draft(
        string choice, bool reload) => Run((window, view) =>
    {
        var templates = Get<ListBox>(view, "TemplateList");
        var sample = templates.Items.OfType<ListBoxItem>().SingleOrDefault(item =>
            AutomationProperties.GetName(item) == "External change sample");
        Assert.NotNull(sample);
        templates.SelectedItem = sample;
        const string draft = "{\"customerId\":\"my unsaved customer\"}";
        Get<JsonEditor>(view, "EditorText").Text = draft;
        Exception? dialogFailure = null;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var dialog = window.OwnedWindows.OfType<MessageLibraryPrototypeDialog>().Single();
            try
            {
                Assert.Equal(Visibility.Visible, Get<StackPanel>(dialog, "ConflictSurface").Visibility);
                var button = Descendants(dialog).OfType<Button>().Single(control =>
                    AutomationProperties.GetAutomationId(control) == choice);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception exception) { dialogFailure = exception; dialog.Close(); }
        }));
        var refresh = Descendants(view).OfType<Button>().Single(control =>
            AutomationProperties.GetAutomationId(control) == "LibraryRefresh");
        refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();
        string actual = Get<JsonEditor>(view, "EditorText").Text;
        if (reload) Assert.Contains("externalRevision", actual);
        else Assert.Equal(draft, actual);
    });

    [Fact]
    public void Conflict_save_as_keeps_both_external_revision_and_edited_copy() => Run((window, view) =>
    {
        var templates = Get<ListBox>(view, "TemplateList");
        templates.SelectedItem = templates.Items.OfType<ListBoxItem>().Single(item =>
            AutomationProperties.GetName(item) == "External change sample");
        const string draft = "{\"customerId\":\"preserve this copy\"}";
        Get<JsonEditor>(view, "EditorText").Text = draft;
        Exception? dialogFailure = null;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var conflict = window.OwnedWindows.OfType<MessageLibraryPrototypeDialog>().Single();
            try
            {
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var prompt = window.OwnedWindows.Cast<Window>().Single(dialog => dialog.Title == "Save as");
                    try
                    {
                        Descendants(prompt).OfType<TextBox>().Single().Text = "Preserved conflict copy";
                        Descendants(prompt).OfType<Button>().Single(button => Equals(button.Content, "Save"))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception exception) { dialogFailure = exception; prompt.Close(); }
                }));
                Descendants(conflict).OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button) == "PrototypeConflictSaveAs")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception exception) { dialogFailure = exception; conflict.Close(); }
        }));
        Descendants(view).OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "LibraryRefresh")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();
        Assert.Equal("Preserved conflict copy", Get<TextBlock>(view, "AuthorTitle").Text);
        Assert.Equal(draft, Get<JsonEditor>(view, "EditorText").Text);
        templates.SelectedItem = templates.Items.OfType<ListBoxItem>().Single(item =>
            AutomationProperties.GetName(item) == "External change sample");
        Assert.Contains("\"externalRevision\": 2", Get<JsonEditor>(view, "EditorText").Text);
    });

    [Fact]
    public void Custom_message_id_is_frozen_in_review_and_invalid_json_disables_review() => Run((window, view) =>
    {
        Get<RadioButton>(view, "SingleMode").IsChecked = true;
        Get<RadioButton>(view, "CustomMessageId").IsChecked = true;
        Get<TextBox>(view, "CustomMessageIdInput").Text = "my-dummy-id";
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Click(view, "ReviewButton");
        var review = Assert.IsType<MessageLibraryPrototypeReviewSurface>(Get<ContentControl>(view, "ReviewHost").Content);
        Assert.Contains("my-dummy-id", Get<TextBlock>(review, "ReviewMessageIds").Text);
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

    private static void Click(FrameworkElement root, string name) =>
        Get<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
