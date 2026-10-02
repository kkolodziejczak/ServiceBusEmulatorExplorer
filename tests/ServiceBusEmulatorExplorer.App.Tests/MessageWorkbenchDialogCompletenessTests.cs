using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchDialogCompletenessTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Captured_draft_retains_copied_properties_and_selected_folder_in_the_editor()
        => OnSta(() =>
        {
            var view = new MessageLibraryPrototypeView();
            var window = new Window { Content = view, Width = 1337, Height = 850,
                ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                var properties = new PrototypeCaptureProperties("Order captured", "application/json",
                    "correlation-17", "session-4", null,
                    new Dictionary<string, object?> { ["source"] = "sample" });
                view.OpenCapturedDraft("Captured order", "{\"ok\":true}", "order-events", "Archive",
                    properties);
                view.UpdateLayout();

                Assert.Equal("Order captured", ((TextBox)view.FindName("PropertySubject")!).Text);
                Assert.Equal("correlation-17", ((TextBox)view.FindName("PropertyCorrelationId")!).Text);
                Assert.Equal("session-4", ((TextBox)view.FindName("PropertySessionId")!).Text);
                var tree = (TreeView)view.FindName("LibraryTree")!;
                var captured = TreeItems(tree).Single(item => Equals(item.Tag, "template:Captured order"));
                Assert.Contains(TreeItems(tree), item => Equals(item.Tag, "folder:Archive"));
                Assert.Equal("Captured order", captured.Header is FrameworkElement header
                    ? Descendants(header).OfType<TextBlock>().First().Text : captured.Header?.ToString());
            }
            finally { window.Close(); }
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Capture_uses_the_source_destination_and_keeps_message_body_and_properties_read_only_until_chosen()
        => OnSta(() =>
        {
            var enqueued = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
            var message = new ExplorerMessage("observed-1", 17, "original body", "original body", 13,
                enqueued, enqueued.AddMinutes(90), 2, "application/json", "correlation-17", "session-4", "Observed",
                new Dictionary<string, object?> { ["attempt"] = 2, ["source"] = "billing" },
                new Dictionary<string, object?> { ["LockedUntilUtc"] = enqueued.AddMinutes(1) });
            var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Capture, "Local", "order-events", 1);
            try
            {
                dialog.SetCaptureSource("order-events / billing ? DLQ", "original body", "edited body",
                    "order-events", message, EntityKind.Topic);
                Assert.Equal("order-events", dialog.CaptureTopic);
                Assert.Equal(EntityKind.Topic, dialog.CaptureDestinationKind);
                Assert.Equal("order-events (Topic)", ((TextBlock)dialog.FindName("CaptureAssociation")!).Text);
                Assert.IsType<TextBlock>(dialog.FindName("CaptureAssociation"));
                Assert.Null(dialog.FindName("CaptureFileName"));
                Assert.Null(dialog.FindName("CaptureAcknowledge"));
                Assert.False(((Expander)dialog.FindName("CaptureDetails")!).IsExpanded);

                var properties = Assert.IsType<PrototypeCaptureProperties>(dialog.CaptureProperties);
                Assert.Equal("Observed", properties.Subject);
                Assert.Equal("application/json", properties.ContentType);
                Assert.Equal("correlation-17", properties.CorrelationId);
                Assert.Equal("session-4", properties.SessionId);
                Assert.Null(properties.TtlMinutes);
                Assert.Equal(2, properties.ApplicationProperties["attempt"]);
                Assert.Equal("billing", properties.ApplicationProperties["source"]);
                Assert.DoesNotContain("observed-1", string.Join(" ", properties.ApplicationProperties.Values));

                ((Expander)dialog.FindName("CaptureDetails")!).IsExpanded = true;
                var copyTtl = (CheckBox)dialog.FindName("CaptureCopyTtl")!;
                Assert.True(copyTtl.IsEnabled);
                copyTtl.IsChecked = true;
                Assert.Equal(90, dialog.CaptureProperties!.TtlMinutes);
                Assert.Equal("observed-1", message.MessageId);
                Assert.Equal("original body", message.Body);

                ((RadioButton)dialog.FindName("CaptureEdited")!).IsChecked = true;
                Assert.Equal("edited body", dialog.CaptureTemplateBody);
                ((RadioButton)dialog.FindName("CaptureOriginal")!).IsChecked = true;
                Assert.Equal("original body", dialog.CaptureTemplateBody);
                Assert.Equal("observed-1", message.MessageId);
                Assert.Equal("original body", message.Body);
            }
            finally { dialog.Close(); }
        });

    [Fact]
    public void Capture_keeps_a_queue_destination_read_only()
        => OnSta(() =>
        {
            var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Capture, "Local", "order-replies", 1);
            try
            {
                dialog.SetCaptureSource("order-replies ? Active", "{}", "{}", "order-replies",
                    destinationKind: EntityKind.Queue);
                Assert.Equal("order-replies", dialog.CaptureTopic);
                Assert.Equal(EntityKind.Queue, dialog.CaptureDestinationKind);
                Assert.Equal("order-replies (Queue)", ((TextBlock)dialog.FindName("CaptureAssociation")!).Text);
            }
            finally { dialog.Close(); }
        });

    [Fact]
    public void Capture_collection_picker_includes_new_session_folders()
        => OnSta(() =>
        {
            var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Capture, "Local", "order-events", 1);
            try
            {
                dialog.SetCaptureCollections(["Orders", "Archive", "My samples"], "My samples");
                Assert.Equal("My samples", dialog.CaptureCollectionName);
                Assert.False(((Expander)dialog.FindName("CaptureDetails")!).IsExpanded);
                var picker = (ComboBox)dialog.FindName("CaptureCollection")!;
                Assert.Equal(4, picker.Items.Count);
                Assert.Contains(picker.Items.OfType<ComboBoxItem>(), item =>
                    Equals(item.Tag, "") && Equals(item.Content, "Root"));
                foreach (string folder in new[] { "Orders", "Archive", "My samples" })
                    Assert.Contains(picker.Items.OfType<ComboBoxItem>(), item => Equals(item.Tag, folder));
            }
            finally { dialog.Close(); }
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Cancellation_with_no_eligible_selection_shows_inline_feedback()
        => OnSta(() =>
        {
            var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Results, "Local", "order-events", 1);
            try
            {
                dialog.RestoreRun(new PrototypeRunSnapshot(
                    "Local",
                    "order-events",
                    true,
                    [(1, "message-1", "Confirmed scheduled", "Sample receipt retained")],
                    [(1, "701", "order-events-1.json", "Confirmed scheduled", "23 Sep 2026 14:30 UTC", false, "Eligible", "—")]));
                dialog.Show();
                dialog.UpdateLayout();

                ((Button)dialog.FindName("CancelScheduled")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var confirm = Descendants(dialog).OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button) == "PrototypeCancelScheduled");
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var error = (TextBlock)dialog.FindName("CancellationError")!;
                Assert.Equal(Visibility.Visible, error.Visibility);
                Assert.Contains("Select at least one", error.Text, StringComparison.Ordinal);
                Assert.Equal(Visibility.Visible, ((StackPanel)dialog.FindName("CancellationSurface")!).Visibility);
            }
            finally
            {
                dialog.Close();
            }
        });

    [Fact]
    public void Capture_run_snapshot_retains_scheduled_cancellation_fields_for_export()
        => OnSta(() =>
        {
            var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Results, "Local", "order-events", 1);
            try
            {
                dialog.RestoreRun(new PrototypeRunSnapshot(
                    "Local",
                    "order-events",
                    true,
                    [(1, "message-1", "Confirmed scheduled", "Sample receipt retained")],
                    [(1, "701", "order-events-1.json", "Outcome unknown", "23 Sep 2026 14:30 UTC", true, "Outcome unknown", "14:31 UTC")]));

                PrototypeRunSnapshot? snapshot = dialog.CaptureRun();
                Assert.NotNull(snapshot);
                var scheduled = Assert.Single(snapshot!.ScheduledResults);
                Assert.Equal("701", scheduled.Receipt);
                Assert.Equal("23 Sep 2026 14:30 UTC", scheduled.DueTime);
                Assert.Equal("Outcome unknown", scheduled.CancellationStatus);
                Assert.Equal("14:31 UTC", scheduled.AttemptTime);
            }
            finally
            {
                dialog.Close();
            }
        });

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Message Workbench dialog proof exceeded 30 seconds.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }

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
}
