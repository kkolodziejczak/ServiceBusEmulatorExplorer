using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InspectorEditabilityStatusTests
{
    [Theory]
    [InlineData(1500, 1000)]
    [InlineData(1100, 800)]
    [InlineData(980, 640)]
    [Trait("TestCategory", "UiRender")]
    public void Inspector_status_tracks_editability_without_covering_the_document(int width, int height) =>
        RunSta(() => ExerciseWindow(width, height));

    private static void ExerciseWindow(int width, int height)
    {
        var profile = new InvestigationProfile("inspector-status-proof",
            new ConnectionProfile("Inspector status proof", "runtime", "admin"));
        var preferences = new WorkspacePreferences
        {
            Profiles = [profile],
            SelectedProfileId = profile.Id,
            WindowWidth = width,
            WindowHeight = height
        };
        var workspace = new InvestigationWorkspace(
            new TablePreferencesStore(preferences),
            new BrokerConnectionWorkflow(() => null!, _ => null!, _ => null!));
        var window = new InvestigationWindow(workspace)
        {
            Width = width,
            Height = height,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize
        };

        try
        {
            window.Show();
            Drain(window.Dispatcher);
            var status = (FrameworkElement)window.FindName("InspectorEditabilityStatus")!;
            var statusText = (TextBlock)window.FindName("InspectorEditabilityText")!;
            var statusIcon = (System.Windows.Shapes.Path)window.FindName("InspectorEditabilityIcon")!;
            var bodyEditor = (JsonEditor)window.FindName("BodyEditor")!;
            var bodyViewer = (RichTextBox)window.FindName("BodyViewer")!;

            Assert.Equal(Visibility.Collapsed, status.Visibility);

            EntityAddress source = new(EntityKind.Queue, "orders");
            MessageRow active = CreateRow(source, MessageBucket.Active, "active-message");
            MessageRow dlq = CreateRow(source, MessageBucket.DeadLetter, "dlq-message");
            workspace.Browse.Messages.Add(active);
            workspace.Browse.Messages.Add(dlq);

            Focus(workspace, window, active);
            AssertStatus(window, status, statusText, statusIcon, "Read-only");
            Assert.True(bodyEditor.IsReadOnly);
            Assert.Equal(Visibility.Visible, bodyEditor.Visibility);
            AssertNoOverlap(window, status, bodyEditor);
            CaptureIfEnabled(window, width, height, "read-only");

            Focus(workspace, window, dlq);
            AssertStatus(window, status, statusText, statusIcon, "Editable");
            Assert.False(bodyEditor.IsReadOnly);
            Assert.Equal(Visibility.Visible, bodyEditor.Visibility);
            AssertNoOverlap(window, status, bodyEditor);
            string editableHelp = System.Windows.Automation.AutomationProperties.GetHelpText(status);
            Assert.Contains("Original stays unchanged", editableHelp, StringComparison.OrdinalIgnoreCase);

            bodyEditor.Document.Insert(bodyEditor.Document.TextLength, " ");
            Drain(window.Dispatcher);
            Assert.True(workspace.Inspector.IsDirty);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ModifiedBadge")!).Visibility);
            AssertStatus(window, status, statusText, statusIcon, "Editable");
            CaptureIfEnabled(window, width, height, "editable");
            ((Button)window.FindName("DiscardButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(window.Dispatcher);
            Assert.False(workspace.Inspector.IsDirty);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("ModifiedBadge")!).Visibility);

            ((ToggleButton)window.FindName("PropertiesTab")!).IsChecked = true;
            Drain(window.Dispatcher);
            AssertStatus(window, status, statusText, statusIcon, "Read-only");
            Assert.Equal(Visibility.Visible, bodyViewer.Visibility);
            Assert.True(bodyViewer.IsReadOnly);
            AssertNoOverlap(window, status, bodyViewer);
            CaptureIfEnabled(window, width, height, "properties");

            ((ToggleButton)window.FindName("JsonTab")!).IsChecked = true;
            Drain(window.Dispatcher);
            FieldInfo replayPending = typeof(InvestigationWindow).GetField(
                "replayPending", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(InvestigationWindow).FullName, "replayPending");
            MethodInfo updateInspector = typeof(InvestigationWindow).GetMethod(
                "UpdateInspector", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(InvestigationWindow).FullName, "UpdateInspector");
            replayPending.SetValue(window, true);
            updateInspector.Invoke(window, null);
            Drain(window.Dispatcher);
            AssertStatus(window, status, statusText, statusIcon, "Read-only");
            Assert.True(bodyEditor.IsReadOnly);
            string replayHelp = System.Windows.Automation.AutomationProperties.GetHelpText(status);
            Assert.Contains("replay", replayHelp, StringComparison.OrdinalIgnoreCase);
            AssertNoOverlap(window, status, bodyEditor);
            CaptureIfEnabled(window, width, height, "replay-pending");
        }
        finally
        {
            window.Close();
            workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void Focus(InvestigationWorkspace workspace, InvestigationWindow window, MessageRow row)
    {
        workspace.Browse.FocusedMessage = row;
        Drain(window.Dispatcher);
        window.UpdateLayout();
        Assert.Same(row.Delivery, workspace.Inspector.Current);
    }

    private static void AssertStatus(Window window, FrameworkElement container, TextBlock text,
        System.Windows.Shapes.Path icon, string expected)
    {
        Assert.Equal(Visibility.Visible, container.Visibility);
        Assert.Equal(expected, text.Text);
        Assert.Equal(Visibility.Visible, icon.Visibility);
        Assert.IsAssignableFrom<Geometry>(icon.Data);
        Assert.Equal(expected, System.Windows.Automation.AutomationProperties.GetName(container));
        Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetHelpText(container)));
        Assert.True(container.ActualWidth > 0 && container.ActualHeight > 0);
    }

    private static void AssertNoOverlap(Window window, FrameworkElement status, FrameworkElement document)
    {
        Rect statusBounds = status.TransformToAncestor(window).TransformBounds(new Rect(status.RenderSize));
        Rect documentBounds = document.TransformToAncestor(window).TransformBounds(new Rect(document.RenderSize));
        Assert.True(statusBounds.Width > 0 && statusBounds.Height > 0);
        Assert.True(documentBounds.Contains(statusBounds),
            $"Status {statusBounds} must overlay the document viewport {documentBounds}, without a separate row.");
        Assert.True(documentBounds.Right - statusBounds.Right >= 24);
    }

    private static MessageRow CreateRow(EntityAddress source, MessageBucket bucket, string id)
    {
        var message = new ExplorerMessage(
            id, 1, "{\"body\":true}", "{\"body\":true}", 1, DateTimeOffset.UtcNow, null, 0,
            "application/json", "correlation-" + id, null, null, new Dictionary<string, object?>(),
            new Dictionary<string, object?>());
        return new MessageRow(new MessageDelivery(
            new DeliveryIdentity(1, source, bucket, 1), message), TimestampDisplay.Utc);
    }

    private static void CaptureIfEnabled(Window window, int width, int height, string state)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_INSPECTOR_STATUS"), "true", StringComparison.OrdinalIgnoreCase))
            return;

        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(window.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(window.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git")) && !File.Exists(Path.Combine(root.FullName, ".git")))
            root = root.Parent;
        if (root is null)
            throw new DirectoryNotFoundException("Could not locate the repository root for the inspector-status proof image.");

        string outputDirectory = Path.Combine(root.FullName, "artifacts", "inspect40-proof");
        Directory.CreateDirectory(outputDirectory);
        using var output = new FileStream(
            Path.Combine(outputDirectory, $"inspect40-{width}x{height}-{state}.png"),
            FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(output);
    }

    private static void RunSta(Action assertion)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { assertion(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Inspector editability WPF proof exceeded its time bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Drain(Dispatcher dispatcher) =>
        dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private sealed class TablePreferencesStore(WorkspacePreferences initial) : IWorkspacePreferencesStore
    {
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreferencesLoadResult(initial));

        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
