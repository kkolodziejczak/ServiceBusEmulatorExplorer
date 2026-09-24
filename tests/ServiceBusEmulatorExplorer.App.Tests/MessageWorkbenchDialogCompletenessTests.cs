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
    public void Captured_draft_retains_filename_and_copied_properties_in_the_editor()
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
                    properties, "captured-order.sbetemplate.json");
                view.UpdateLayout();

                Assert.Equal("Order captured", ((TextBox)view.FindName("PropertySubject")!).Text);
                Assert.Equal("correlation-17", ((TextBox)view.FindName("PropertyCorrelationId")!).Text);
                Assert.Equal("session-4", ((TextBox)view.FindName("PropertySessionId")!).Text);
                Assert.Contains(Descendants(view).OfType<ListBoxItem>(), item =>
                    item.ToolTip?.ToString()?.Contains("captured-order.sbetemplate.json", StringComparison.Ordinal) == true);
            }
            finally { window.Close(); }
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Capture_association_uses_the_selected_dummy_destination_and_rejects_free_text()
        => OnSta(() =>
        {
            var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Capture, "Local", "order-events", 1);
            try
            {
                dialog.SetCaptureSource("order-events / billing · Active", "{}", "{}", "warehouse-typo");
                dialog.Show();
                dialog.UpdateLayout();

                var picker = (ComboBox)dialog.FindName("CaptureAssociation")!;
                Assert.False(picker.IsEditable);
                Assert.Equal(3, picker.Items.Count);
                Assert.Equal("order-events", dialog.CaptureTopic);

                picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(item =>
                    string.Equals(item.Tag?.ToString(), "order-replies", StringComparison.Ordinal));
                Assert.Equal("order-replies", dialog.CaptureTopic);
            }
            finally
            {
                dialog.Close();
            }
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Capture_preserves_a_real_source_topic_outside_the_dummy_options()
        => OnSta(() =>
        {
            var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Capture, "Local", "custom-topic", 1);
            try
            {
                var message = new ExplorerMessage("observed-1", 17, "{}", "{}", 2, null, null, 0,
                    "application/json", null, null, "Observed", new Dictionary<string, object?>(),
                    new Dictionary<string, object?>());
                dialog.SetCaptureSource("custom-topic · Active", "{}", "{}", "custom-topic", message);
                var picker = (ComboBox)dialog.FindName("CaptureAssociation")!;
                Assert.Equal(4, picker.Items.Count);
                Assert.Equal("custom-topic", dialog.CaptureTopic);
                Assert.False(picker.IsEditable);
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
                Assert.Equal(3, ((ComboBox)dialog.FindName("CaptureCollection")!).Items.Count);
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
}
