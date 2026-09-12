using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationTrayRenderTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Settings_keeps_unavailable_tray_disabled_after_independent_preference_save()
    {
        RunOnSta(() =>
        {
            var initial = new WorkspacePreferences
            {
                Profiles = [new InvestigationProfile("tray-proof", new ConnectionProfile("Tray proof", "runtime", "admin"))],
                SelectedProfileId = "tray-proof",
                CloseToTray = true
            };
            var saved = new List<WorkspacePreferences>();
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task SaveAsync(WorkspacePreferences preferences)
            {
                await release.Task;
                saved.Add(preferences);
            }
            var window = new SettingsWindow(initial, SaveAsync, trayAvailable: false);
            try
            {
                window.Show();
                PumpUntil(() => window.IsVisible && window.ActualWidth > 0);
                var toggle = (ToggleButton)window.FindName("CloseToTrayToggle")!;
                var description = (TextBlock)window.FindName("CloseToTrayDescription")!;
                var queue = (ComboBox)window.FindName("QueuePageSizeSelector")!;
                var done = (Button)window.FindName("DoneButton")!;

                foreach (var size in new[] { new Size(520, 820), new Size(460, 520) })
                {
                    window.Width = size.Width;
                    window.Height = size.Height;
                    window.UpdateLayout();
                    Assert.False(toggle.IsEnabled);
                    Assert.True(toggle.IsChecked);
                    Assert.Equal("System tray is unavailable in this session.", description.Text);
                    foreach (FrameworkElement element in new FrameworkElement[] { toggle, description, done })
                    {
                        Assert.True(element.IsVisible);
                        Assert.True(element.ActualWidth > 0 && element.ActualHeight > 0);
                        Rect bounds = element.TransformToAncestor(window).TransformBounds(
                            new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                        Assert.True(bounds.Left >= 0 && bounds.Top >= 0
                            && bounds.Right <= window.ActualWidth && bounds.Bottom <= window.ActualHeight,
                            $"{element.Name} is clipped at {size}: {bounds}.");
                    }
                    CaptureIfEnabled(window, size.Width == 520 ? "settings-tray-unavailable-default" : "settings-tray-unavailable-minimum");
                }

                queue.SelectedItem = 100;
                Assert.False(done.IsEnabled);
                Assert.False(queue.IsEnabled);
                Assert.False(toggle.IsEnabled);
                release.SetResult(true);
                PumpUntil(() => saved.Count == 1 && done.IsEnabled && queue.IsEnabled);
                Assert.Equal(100, saved[0].QueuePageSize);
                Assert.True(saved[0].CloseToTray);
                Assert.True(window.CurrentPreferences.CloseToTray);
                Assert.True(toggle.IsChecked);
                Assert.False(toggle.IsEnabled);
                Assert.Equal("System tray is unavailable in this session.", description.Text);
            }
            finally
            {
                release.TrySetResult(true);
                PumpUntil(() => ((Button)window.FindName("DoneButton")!).IsEnabled);
                window.Close();
                PumpUntil(() => !window.IsVisible);
            }
        });
    }

    private static void CaptureIfEnabled(Window window, string name)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_INVESTIGATION_UI"), "true", StringComparison.OrdinalIgnoreCase)) return;
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git"))) root = root.Parent;
        Assert.NotNull(root);
        string directory = Path.Combine(root.FullName, "artifacts", "investigation-ui");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Close_hides_connected_workspace_and_tray_exit_honors_draft_cancellation()
    {
        RunOnSta(() =>
        {
            var factory = new FakeFactory();
            var workspace = CreateWorkspace(factory, closeToTray: true);
            var window = new InvestigationWindow(workspace);
            var discardApproval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool closed = false;
            window.Closed += (_, _) => closed = true;
            try
            {
                window.InitializeSystemTray();
                object tray = Field(window, "tray");
                object icon = Field(tray, "icon");
                object menu = Field(tray, "menu");
                Assert.True(Property<bool>(icon, "Visible"));
                window.InitializeSystemTray();
                Assert.Same(tray, Field(window, "tray"));
                window.Show();
                PumpUntil(() => workspace.Preferences.SelectedProfileId == "tray-proof");
                Await(workspace.ConnectAsync());
                Assert.True(workspace.IsConnected);
                AddDraft(workspace);
                var document = workspace.Inspector.Document;
                int approvalCalls = 0;
                workspace.ConfirmDiscard = () => { approvalCalls++; return discardApproval.Task; };

                window.Close();
                PumpUntil(() => !window.IsVisible);
                Assert.False(closed);
                Assert.True(workspace.IsConnected);
                Assert.Equal(0, factory.DisposeCount);
                Assert.Equal(0, approvalCalls);
                Assert.True(workspace.Inspector.HasDrafts);
                Assert.Same(document, workspace.Inspector.Document);
                Assert.Equal("{\"edited\":true}", document.Text);
                Assert.True(Property<bool>(icon, "Visible"));

                window.WindowState = WindowState.Minimized;
                ClickMenu(menu, "Open Service Bus Explorer");
                PumpUntil(() => window.IsVisible && window.WindowState == WindowState.Normal);
                Assert.Same(document, workspace.Inspector.Document);

                window.Close();
                ClickMenu(menu, "Exit");
                PumpUntil(() => approvalCalls == 1);
                Assert.True((bool)Field(window, "closePending"));
                ClickMenu(menu, "Exit");
                window.Close();
                Assert.Equal(1, approvalCalls);
                Assert.True(window.IsVisible);
                Assert.False(closed);
                Assert.True(workspace.IsConnected);
                Assert.True(workspace.Inspector.HasDrafts);
                Assert.Equal(0, factory.DisposeCount);
                Assert.True(Property<bool>(icon, "Visible"));

                discardApproval.SetResult(false);
                PumpUntil(() => !(bool)Field(window, "closePending"));

                // Canceling Exit must not make the next ordinary Close destructive.
                window.Close();
                PumpUntil(() => !window.IsVisible);
                Assert.False(closed);
                Assert.Equal(1, approvalCalls);
                workspace.ConfirmDiscard = () => { approvalCalls++; return Task.FromResult(true); };
                ClickMenu(menu, "Exit");
                PumpUntil(() => closed);
                Assert.Equal(2, approvalCalls);
                Assert.False(workspace.IsConnected);
                Assert.Equal(1, factory.DisposeCount);
                Assert.False(Property<bool>(icon, "Visible"));
                Assert.True(Property<bool>(menu, "IsDisposed"));
            }
            finally
            {
                discardApproval.TrySetResult(false);
                PumpUntil(() => !(bool)Field(window, "closePending"));
                Cleanup(window, workspace, () => closed);
            }
        });
    }

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Close_to_tray_disabled_closes_window_and_disposes_real_icon()
    {
        RunOnSta(() =>
        {
            var factory = new FakeFactory();
            var workspace = CreateWorkspace(factory, closeToTray: false);
            var window = new InvestigationWindow(workspace);
            bool closed = false;
            window.Closed += (_, _) => closed = true;
            try
            {
                window.InitializeSystemTray();
                object tray = Field(window, "tray");
                object icon = Field(tray, "icon");
                object menu = Field(tray, "menu");
                window.Show();
                PumpUntil(() => workspace.Preferences.SelectedProfileId == "tray-proof");
                Await(workspace.ConnectAsync());
                Assert.True(workspace.IsConnected);
                Assert.True(Property<bool>(icon, "Visible"));
                window.Close();
                PumpUntil(() => closed);
                Assert.False(workspace.IsConnected);
                Assert.Equal(1, factory.DisposeCount);
                Assert.False(Property<bool>(icon, "Visible"));
                Assert.True(Property<bool>(menu, "IsDisposed"));
            }
            finally
            {
                Cleanup(window, workspace, () => closed);
            }
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try { action(); }
            catch (Exception error) { failure = error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The production tray lifetime proof exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static InvestigationWorkspace CreateWorkspace(FakeFactory factory, bool closeToTray)
    {
        var preferences = new WorkspacePreferences
        {
            Profiles = [new InvestigationProfile("tray-proof", new ConnectionProfile("Tray proof", "runtime", "admin"))],
            SelectedProfileId = "tray-proof",
            CloseToTray = closeToTray,
            WasConnected = false
        };
        return new InvestigationWorkspace(new MemoryStore(preferences),
            new BrokerConnectionWorkflow(() => factory, _ => new EmptyBrowser(), _ => new EmptyMessages()));
    }

    private static void AddDraft(InvestigationWorkspace workspace)
    {
        const string body = "{\"edited\":false}";
        var message = new ExplorerMessage("tray-draft", 1, body, body, body.Length,
            DateTimeOffset.UtcNow, null, 0, "application/json", "tray-proof", null, "Draft",
            new Dictionary<string, object?>(), new Dictionary<string, object?>())
        { RawBody = BinaryData.FromString(body) };
        workspace.Inspector.Select(new MessageDelivery(
            new DeliveryIdentity(1, new EntityAddress(EntityKind.Queue, "tray-proof"), MessageBucket.DeadLetter, 1), message));
        workspace.Inspector.Document.Text = "{\"edited\":true}";
        Assert.True(workspace.Inspector.HasDrafts);
    }

    private static object Field(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)
        ?? throw new Xunit.Sdk.XunitException($"Expected initialized {name} on {instance.GetType().Name}.");

    private static T Property<T>(object instance, string name) =>
        (T)instance.GetType().GetProperty(name)!.GetValue(instance)!;

    private static void ClickMenu(object menu, string text)
    {
        object item = Property<IEnumerable>(menu, "Items").Cast<object>()
            .Single(item => Property<string?>(item, "Text") == text);
        item.GetType().GetMethod("PerformClick", Type.EmptyTypes)!.Invoke(item, null);
    }

    private static void Cleanup(InvestigationWindow window, InvestigationWorkspace workspace, Func<bool> isClosed)
    {
        workspace.ConfirmDiscard = () => Task.FromResult(true);
        if (!isClosed())
        {
            object? tray = window.GetType().GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
            if (tray is not null) ClickMenu(Field(tray, "menu"), "Exit");
            else window.Close();
            PumpUntil(isClosed);
        }
        Await(workspace.DisposeAsync().AsTask());
    }

    private static void Await(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The tray lifetime transition exceeded five seconds.");
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
    }

    private sealed class MemoryStore(WorkspacePreferences initial) : IWorkspacePreferencesStore
    {
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(initial));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeFactory : IServiceBusClientFactory
    {
        public int DisposeCount { get; private set; }
        public Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient AdministrationClient => null!;
        public Azure.Messaging.ServiceBus.ServiceBusClient RuntimeClient => null!;
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }

    private sealed class EmptyBrowser : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new EntityDiscoverySnapshot([], DateTimeOffset.UtcNow, IsComplete: true, Issues: []));
    }

    private sealed class EmptyMessages : IServiceBusMessageService
    {
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExplorerMessage>>([]);
        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
