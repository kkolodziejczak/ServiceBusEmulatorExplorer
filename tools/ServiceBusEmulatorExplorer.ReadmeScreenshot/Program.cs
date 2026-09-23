using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.App.Investigation;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool prototypeCapture = args.Length == 2 && args[1] is
            "--message-library-prepare" or "--message-library-prepare-1500" or "--message-library-prepare-compact" or "--message-library-prepare-minimum"
            or "--message-library-properties" or "--message-library-properties-bottom" or "--message-library-properties-selected" or "--message-library-variables" or "--message-library-single"
            or "--message-library-author-minimum" or "--message-library-properties-minimum" or "--message-library-wizard-review" or "--message-library-wizard-review-minimum" or "--message-library-wizard-review-queue" or "--message-library-wizard-review-large-batch"
            or "--message-library-single-minimum" or "--message-library-prepare-log";
        bool dialogCapture = args.Length == 2 && args[1] is
            "--message-library-map" or "--message-library-capture" or "--message-library-review" or "--message-library-results"
            or "--message-library-schedule" or "--message-library-conflict" or "--message-library-validation"
            or "--message-library-scheduled-results" or "--message-library-cancellation-history"
            or "--message-library-association" or "--message-library-association-edit";
        int captureWidth = dialogCapture ? 760 :
            args.Length == 2 && args[1] is "--message-library-prepare" or "--message-library-properties" or "--message-library-properties-bottom" or "--message-library-properties-selected" or "--message-library-variables" or "--message-library-single" or "--message-library-prepare-log" or "--message-library-wizard-review" or "--message-library-wizard-review-queue" or "--message-library-wizard-review-large-batch" ? 1642 :
            args.Length == 2 && args[1] == "--message-library-prepare-1500" ? 1500 :
            args.Length == 2 && args[1] == "--message-library-prepare-compact" ? 1100 :
            args.Length == 2 && args[1] is "--message-library-prepare-minimum" or "--message-library-author-minimum" or "--message-library-properties-minimum" or "--message-library-single-minimum" or "--message-library-wizard-review-minimum" ? 980 : WpfScreenshot.CaptureWidth;
        int captureHeight = dialogCapture ? args[1] is "--message-library-association" or "--message-library-association-edit" ? 500 :
            args[1] == "--message-library-capture" ? 925 :
            args[1] == "--message-library-map" ? 590 : args[1] == "--message-library-conflict" ? 500 : 620 :
            captureWidth == 1642 ? 958 : captureWidth == 1500 ? 1000 : captureWidth == 1100 ? 800 : captureWidth == 980 ? 640 : WpfScreenshot.CaptureHeight;
        if ((args.Length != 1 && !prototypeCapture && !dialogCapture) || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: ServiceBusEmulatorExplorer.ReadmeScreenshot <output-png> [--message-library-prepare|--message-library-prepare-1500|--message-library-prepare-compact|--message-library-prepare-minimum|--message-library-properties|--message-library-variables|--message-library-single|--message-library-wizard-review|--message-library-wizard-review-minimum]");
            return 2;
        }

        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exitCode = 1;
        application.Startup += async (_, _) =>
        {
            InvestigationWorkspace? workspace = null;
            NativeWindowSizeOverride? nativeWindowSizeOverride = null;
            try
            {
                if (dialogCapture)
                {
                    var mode = args[1] is "--message-library-association" or "--message-library-association-edit" ? PrototypeDialogMode.Destination :
                        args[1] == "--message-library-map" ? PrototypeDialogMode.Mapping :
                        args[1] == "--message-library-capture" ? PrototypeDialogMode.Capture :
                        args[1] is "--message-library-results" or "--message-library-scheduled-results" or "--message-library-cancellation-history" ? PrototypeDialogMode.Results :
                        args[1] == "--message-library-validation" ? PrototypeDialogMode.Validation :
                        args[1] == "--message-library-conflict" ? PrototypeDialogMode.Conflict : PrototypeDialogMode.Review;
                    var dialog = new MessageLibraryPrototypeDialog(mode, "Local emulator", "order-events", 3)
                    {
                        ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize, Width = captureWidth, Height = captureHeight,
                        WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0
                    };
                    if (mode == PrototypeDialogMode.Destination)
                        dialog.ConfigureAssociationPicker("order-events", args[1] == "--message-library-association-edit");
                    if (mode == PrototypeDialogMode.Capture)
                        dialog.SetCaptureSource("order-events / billing · Active",
                            "{\n  \"customerId\": \"C1001\",\n  \"amount\": 149.90\n}",
                            "{\n  \"customerId\": \"C1001\",\n  \"amount\": 149.90,\n  \"reviewed\": true\n}", "order-events");
                    if (args[1] == "--message-library-schedule")
                        ((RadioButton)dialog.FindName("ReviewSchedule")!).IsChecked = true;
                    if (mode == PrototypeDialogMode.Conflict)
                        dialog.SetConflictVersions(new string('A', 64), new string('B', 64));
                    if (mode == PrototypeDialogMode.Validation)
                        dialog.SetValidationRows([(1, "C1001", "Ready"), (2, "C1002", "Amount must be a number"), (3, "C1003", "Ready")]);
                    if (mode == PrototypeDialogMode.Results)
                        dialog.RestoreRun(new PrototypeRunSnapshot("Local emulator", "order-events", false,
                            new[] { (1, "00000000-0000-0000-0000-000000000001", "Confirmed sent", "Sample acknowledgement"),
                                (2, "00000000-0000-0000-0000-000000000002", "Confirmed sent", "Sample acknowledgement"),
                                (3, "00000000-0000-0000-0000-000000000003", "Confirmed sent", "Sample acknowledgement") },
                            Array.Empty<(int, string, string, string, string, bool, string, string)>()));
                    if (args[1] is "--message-library-scheduled-results" or "--message-library-cancellation-history")
                    {
                        dialog.RestoreRun(new PrototypeRunSnapshot("Local emulator", "order-events", true,
                            [(1, "00000000-0000-0000-0000-000000000001", "Scheduled", "Sample acknowledgement")],
                            [(1, "1001", "Order created", "Scheduled", "2026-09-23 14:30", true, "Cancellation acknowledged", "14:00 UTC"),
                             (2, "1002", "Order updated", "Scheduled", "2026-09-23 14:30", false, "Outcome unknown", "14:00 UTC")]));
                        ((Button)dialog.FindName("CancelScheduled")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (args[1] == "--message-library-cancellation-history")
                        {
                            // Fixture capture only; real confirmation is covered by UI smoke.
                            ((FrameworkElement)dialog.FindName("CancellationSurface")!).Visibility = Visibility.Collapsed;
                            ((FrameworkElement)dialog.FindName("HistorySurface")!).Visibility = Visibility.Visible;
                        }
                    }
                    var dialogRendered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    dialog.ContentRendered += (_, _) => dialogRendered.TrySetResult(true);
                    dialog.Show();
                    await dialogRendered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    dialog.UpdateLayout();
                    await dialog.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    WpfScreenshot.SaveWindowContent(dialog, Path.GetFullPath(args[0]), captureWidth - 32, captureHeight - 32);
                    exitCode = 0;
                    dialog.Close();
                    return;
                }
                workspace = await RetailScreenshotScenario.CreateWorkspaceAsync();
                var window = new InvestigationWindow(workspace)
                {
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    Width = captureWidth,
                    Height = captureHeight,
                    Left = 0,
                    Top = 0
                };

                var contentRendered = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                window.SourceInitialized += (_, _) =>
                {
                    nativeWindowSizeOverride = NativeWindowSizeOverride.Install(
                        window,
                        captureWidth,
                        captureHeight);
                };
                window.ContentRendered += (_, _) => contentRendered.TrySetResult(true);
                window.Show();
                await contentRendered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                window.Width = captureWidth;
                window.Height = captureHeight;
                await window.Dispatcher.InvokeAsync(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
                window.UpdateLayout();
                if (captureWidth == WpfScreenshot.CaptureWidth)
                    RetailScreenshotScenario.ValidateRenderedWindow(window, workspace);
                if (prototypeCapture)
                {
                    ((ToggleButton)window.FindName("MessageLibraryTab")!).IsChecked = true;
                    var topics = (TreeViewItem)((TreeView)window.FindName("PrototypeNamespaceTree")!).Items[1];
                    ((TreeViewItem)topics.Items[0]).IsSelected = true;
                    var prototype = (MessageLibraryPrototypeView)window.FindName("MessageLibraryPrototype")!;
                    if (args[1] == "--message-library-wizard-review-queue")
                    {
                        prototype.SelectEntityContext("queue:order-replies");
                        var library = (ListBox)prototype.FindName("TemplateList")!;
                        library.SelectedItem = library.Items.OfType<ListBoxItem>().Single(item => (string)item.Tag == "Order reply");
                        ((RadioButton)prototype.FindName("SingleMode")!).IsChecked = true;
                    }
                    if (args[1] is "--message-library-properties" or "--message-library-properties-bottom" or "--message-library-properties-selected" or "--message-library-properties-minimum")
                    {
                        var properties = (Button)prototype.FindName("EditorPropertiesTab")!;
                        properties.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, properties));
                        if (args[1] == "--message-library-properties-selected")
                        {
                            var propertyRows = (DataGrid)prototype.FindName("ApplicationPropertiesGrid")!;
                            propertyRows.SelectedIndex = 1;
                            properties.Focus();
                        }
                        if (args[1] == "--message-library-properties-bottom")
                        {
                            prototype.UpdateLayout();
                            ((ScrollViewer)prototype.FindName("PropertiesEditorSurface")!).ScrollToEnd();
                        }
                    }
                    else if (args[1] == "--message-library-variables")
                    {
                        var variables = (Button)prototype.FindName("EditorVariablesTab")!;
                        variables.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, variables));
                    }
                    else if (args[1] is "--message-library-single" or "--message-library-single-minimum")
                    {
                        ((RadioButton)prototype.FindName("SingleMode")!).IsChecked = true;
                    }
                    else if (args[1] == "--message-library-author-minimum")
                    {
                        var author = (Button)prototype.FindName("ComposeStepButton")!;
                        author.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, author));
                    }
                    if (args[1] is "--message-library-prepare" or "--message-library-prepare-1500" or "--message-library-prepare-compact" or "--message-library-prepare-minimum" or "--message-library-prepare-log" or "--message-library-single" or "--message-library-single-minimum" or "--message-library-wizard-review" or "--message-library-wizard-review-minimum" or "--message-library-wizard-review-queue" or "--message-library-wizard-review-large-batch")
                    {
                        var next = (Button)prototype.FindName("ContinueToPrepareButton")!;
                        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, next));
                    }
                    if (args[1] is "--message-library-wizard-review" or "--message-library-wizard-review-minimum" or "--message-library-wizard-review-queue" or "--message-library-wizard-review-large-batch")
                    {
                        var review = (Button)prototype.FindName("ReviewButton")!;
                        review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, review));
                        if (args[1] == "--message-library-wizard-review-large-batch")
                        {
                            var surface = (MessageLibraryPrototypeReviewSurface)((ContentControl)prototype.FindName("ReviewHost")!).Content;
                            surface.Configure("Demo retail workspace", "order-events", 1000);
                        }
                    }
                    if (args[1] == "--message-library-prepare-log")
                    {
                        var logToggle = (Button)window.FindName("LogToggle")!;
                        logToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, logToggle));
                    }
                    window.UpdateLayout();
                    await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
                WpfScreenshot.SaveWindowContent(window, Path.GetFullPath(args[0]), captureWidth, captureHeight);
                exitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            finally
            {
                nativeWindowSizeOverride?.Dispose();
                if (workspace is not null)
                {
                    await workspace.DisposeAsync();
                }
                application.Shutdown(exitCode);
            }
        };

        application.Run();
        return exitCode;
    }
}
