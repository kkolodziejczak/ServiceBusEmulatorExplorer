using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationStartupRenderTests
{
    [Theory]
    [InlineData(1500, 1000)]
    [InlineData(1100, 800)]
    [InlineData(980, 640)]
    [Trait("TestCategory", "UiRender")]
    public void Startup_empty_surfaces_remain_truthful_while_preferences_and_connection_are_pending(int width, int height)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RenderStartupStates(width, height); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(35)), "The Investigation startup render proof exceeded its 35-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void RenderStartupStates(int width, int height)
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var store = new DelayedStore();
        var factory = new DelayedFactory();
        var workflow = new BrokerConnectionWorkflow(() => factory, _ => new EmptyBrowser(), _ => new EmptyMessages());
        var workspace = new InvestigationWorkspace(store, workflow);
        var window = new InvestigationWindow(workspace)
        {
            Width = width,
            Height = height,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        try
        {
            window.Show();
            PumpUntil(dispatcher, () => store.LoadStarted.Task.IsCompleted, "preferences load to start");
            Drain(dispatcher, window);

            StackPanel emptyResults = (StackPanel)window.FindName("EmptyResults")!;
            StackPanel emptyInspector = (StackPanel)window.FindName("EmptyInspector")!;
            FrameworkElement inspectorHeading = (FrameworkElement)window.FindName("InspectorHeading")!;
            FrameworkElement namespaceEmpty = (FrameworkElement)window.FindName("NamespaceEmpty")!;
            TextBlock emptyMessage = (TextBlock)window.FindName("EmptyMessage")!;
            FrameworkElement bodyEditor = (FrameworkElement)window.FindName("BodyEditor")!;
            TextBlock emptyInspectorHeading = emptyInspector.Children.OfType<TextBlock>().First(text => text.Text == "Select a message to inspect");
            TextBlock emptyInspectorDescription = emptyInspector.Children.OfType<TextBlock>().First(text => text.Text.StartsWith("Choose a message", StringComparison.Ordinal));

            CaptureIfRequested(window, $"startup-preferences-pending-{width}x{height}.png");
            Assert.Equal(Visibility.Visible, emptyResults.Visibility);
            Assert.Equal("Connect to browse messages", emptyMessage.Text);
            Assert.Equal(Visibility.Visible, emptyInspector.Visibility);
            Assert.Equal(Visibility.Collapsed, inspectorHeading.Visibility);
            Assert.Equal(Visibility.Collapsed, bodyEditor.Visibility);
            Assert.Equal(Visibility.Collapsed, namespaceEmpty.Visibility);
            Assert.Same(window.FindResource("EditorForegroundBrush"), emptyInspectorHeading.Foreground);
            Assert.Same(window.FindResource("EditorLineNumberBrush"), emptyInspectorDescription.Foreground);

            store.Release.SetResult(new PreferencesLoadResult(new WorkspacePreferences
            {
                Profiles = [new InvestigationProfile("startup", new ConnectionProfile("Startup proof", "runtime", "admin"))],
                SelectedProfileId = "startup",
                WasConnected = true,
                WindowWidth = width,
                WindowHeight = height
            }));
            PumpUntil(dispatcher, () => factory.ConnectStarted.Task.IsCompleted, "broker connection to start");
            Drain(dispatcher, window);

            CaptureIfRequested(window, $"startup-connecting-{width}x{height}.png");
            Assert.Equal("Connecting…", ((TextBlock)window.FindName("ConnectionHealthText")!).Text);
            Assert.Equal("Connecting to Service Bus…", ((TextBlock)window.FindName("LastOperation")!).Text);
            Assert.Equal(Visibility.Visible, emptyResults.Visibility);
            Assert.Equal("Discovering entities…", ((TextBlock)window.FindName("NamespaceEmpty")!).Text);
            Assert.Equal(Visibility.Visible, namespaceEmpty.Visibility);
            Assert.Equal(Visibility.Visible, emptyInspector.Visibility);

            factory.Release.SetResult();
            PumpUntil(dispatcher, () => workspace.IsConnected && !workspace.IsConnecting, "empty broker snapshot to finish");
            Drain(dispatcher, window);

            CaptureIfRequested(window, $"startup-connected-empty-{width}x{height}.png");
            Assert.Equal("Warning", ((TextBlock)window.FindName("ConnectionHealthText")!).Text);
            Assert.Contains("cannot be verified without a queue or subscription", workspace.HealthDetail, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("No messages in this view", emptyMessage.Text);
            Assert.Equal(Visibility.Visible, emptyResults.Visibility);
            Assert.Equal(Visibility.Visible, emptyInspector.Visibility);
            Assert.Equal("No entities found on this connection.", ((TextBlock)window.FindName("NamespaceEmpty")!).Text);
            Assert.Equal(Visibility.Visible, namespaceEmpty.Visibility);

            TextBox searchBox = (TextBox)window.FindName("SearchBox")!;
            searchBox.Text = "missing-entity";
            Drain(dispatcher, window);
            Assert.Equal("No matching entities. Clear the search to show all entities.", ((TextBlock)window.FindName("NamespaceEmpty")!).Text);
            Assert.Equal(Visibility.Visible, namespaceEmpty.Visibility);
            searchBox.Clear();
            Drain(dispatcher, window);
            Assert.Equal("No entities found on this connection.", ((TextBlock)window.FindName("NamespaceEmpty")!).Text);
            Assert.Equal(Visibility.Visible, namespaceEmpty.Visibility);

            Complete(dispatcher, workspace.DisconnectAsync());
            factory.FailNextConnect = true;
            Complete(dispatcher, workspace.ConnectAsync());
            Drain(dispatcher, window);
            CaptureIfRequested(window, $"startup-connection-failed-{width}x{height}.png");
            Assert.False(workspace.IsConnected);
            Assert.False(workspace.IsConnecting);
            Assert.Equal("Disconnected", ((TextBlock)window.FindName("ConnectionHealthText")!).Text);
            Assert.Contains("See Activity log", ((TextBlock)window.FindName("LastOperation")!).Text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Connect to browse messages", emptyMessage.Text);
            Assert.Equal(Visibility.Visible, emptyResults.Visibility);
            Assert.Equal(Visibility.Visible, emptyInspector.Visibility);
            Assert.Equal(Visibility.Collapsed, namespaceEmpty.Visibility);
        }
        finally
        {
            window.Close();
            PumpUntil(dispatcher, () => !window.IsVisible, "window close");
            Complete(dispatcher, workspace.DisposeAsync().AsTask());
        }
    }

    private static void CaptureIfRequested(Window window, string fileName)
    {
        string? directory = Environment.GetEnvironmentVariable("SBE_STARTUP_PROOF");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        FrameworkElement content = (FrameworkElement)window.Content;
        ServiceBusEmulatorExplorer.ReadmeScreenshot.WpfScreenshot.SaveWindowContent(window,
            Path.Combine(directory, fileName), (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight));
    }

    private static void PumpUntil(Dispatcher dispatcher, Func<bool> condition, string state)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException($"Timed out waiting for {state}.");
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static void Drain(Dispatcher dispatcher, Window window)
    {
        window.UpdateLayout();
        dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static void Complete(Dispatcher dispatcher, Task task)
    {
        PumpUntil(dispatcher, () => task.IsCompleted, "workspace disposal");
        task.GetAwaiter().GetResult();
    }

    private sealed class DelayedStore : IWorkspacePreferencesStore
    {
        public TaskCompletionSource LoadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PreferencesLoadResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            LoadStarted.TrySetResult();
            return await Release.Task.WaitAsync(cancellationToken);
        }
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class DelayedFactory : IServiceBusClientFactory
    {
        public ServiceBusAdministrationClient AdministrationClient => null!;
        public ServiceBusClient RuntimeClient => null!;
        public TaskCompletionSource ConnectStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailNextConnect { get; set; }
        public async Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            if (FailNextConnect)
            {
                FailNextConnect = false;
                throw new InvalidOperationException("Synthetic connection failure.");
            }
            ConnectStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyBrowser : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
            => Task.FromResult(new EntityDiscoverySnapshot([], DateTimeOffset.UtcNow, IsComplete: true, Issues: []));
    }

    private sealed class EmptyMessages : IServiceBusMessageService
    {
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ExplorerMessage>>([]);
        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
