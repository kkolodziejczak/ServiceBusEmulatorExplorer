using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.UiSmoke.Tests;

/// <summary>Real-window daily-workflow audit. Failures are evidence, not permission to repair production.</summary>
public sealed class DailyWindowAuditTests
{
    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public async Task Empty_queue_then_external_send_and_manual_refresh_loads_without_consuming()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Open();
        fixture.WaitLoaded(0);
        await fixture.SendAsync("external-arrival");
        fixture.Invoke("RefreshButton");
        fixture.WaitLoaded(1);
        Assert.Equal("external-arrival", fixture.Element("InspectorMessageId").Name);
        Assert.Single(await fixture.PeekAsync());
    }

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public async Task Manual_refresh_does_not_present_zero_active_total_beside_observed_active_messages()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Open();
        fixture.WaitLoaded(0);
        await fixture.SendAsync("active-count-proof");
        fixture.Invoke("RefreshButton");
        fixture.WaitLoaded(1);
        // Audit acceptance: an unavailable count is honest; a numeric zero contradicts the visible delivery.
        // Existing tooltip caveats do not change the visible numeric assertion.
        Assert.DoesNotContain("· 0 Active", fixture.Element("MessageCountSummary").Name);
        Assert.Contains("· — Active", fixture.Element("MessageCountSummary").Name);
        fixture.AssertUnavailableTreeCountsAtSupportedSizes("active");
    }

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public async Task Dead_letter_navigation_does_not_present_zero_total_beside_observed_dead_letters()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SendAsync("dlq-count-proof");
        await using (var receiver = fixture.Client.CreateReceiver(fixture.Queue))
        {
            var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5), fixture.Token);
            Assert.NotNull(message);
            await receiver.DeadLetterMessageAsync(message, "Audit", "Synthetic fixture", fixture.Token);
        }
        fixture.Open();
        fixture.WaitLoaded(0);
        fixture.Element("DeadLetterTab").Patterns.Toggle.Pattern.Toggle();
        fixture.WaitLoaded(1);
        Assert.DoesNotContain("· 0 DLQ", fixture.Element("MessageCountSummary").Name);
        Assert.Contains("· — DLQ", fixture.Element("MessageCountSummary").Name);
        fixture.AssertUnavailableTreeCountsAtSupportedSizes("dlq");
    }

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public async Task Refresh_preserves_user_focused_message_after_external_arrival()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SendAsync("first");
        await fixture.SendAsync("keep-focused");
        fixture.Open();
        fixture.WaitLoaded(2);
        var text = fixture.Element("MessageGrid").FindFirstDescendant(cf => cf.ByText("keep-focused"));
        Assert.NotNull(text);
        var row = text;
        while (row is not null && row.ControlType != ControlType.DataItem) row = row.Parent;
        Assert.NotNull(row);
        row.Patterns.SelectionItem.Pattern.Select();
        fixture.Wait(() => fixture.Element("InspectorMessageId").Name == "keep-focused");
        await fixture.SendAsync("new-arrival");
        fixture.Invoke("RefreshButton");
        fixture.WaitLoaded(3);
        Assert.Equal("keep-focused", fixture.Element("InspectorMessageId").Name);
        Assert.Equal(3, (await fixture.PeekAsync()).Count);
    }

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public async Task Automatic_refresh_observes_external_arrival_without_clicking_refresh()
    {
        await using var fixture = await Fixture.CreateAsync(autoRefresh: 5);
        fixture.Open();
        fixture.WaitLoaded(0);
        await fixture.SendAsync("automatic-arrival");
        fixture.WaitLoaded(1);
        Assert.Single(await fixture.PeekAsync());
    }

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public async Task Closing_and_reopening_restores_connection_selected_queue_and_fresh_messages()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SendAsync("before-close");
        fixture.Open();
        fixture.WaitLoaded(1);
        fixture.CloseNormally();
        await fixture.SendAsync("while-closed");
        fixture.Open();
        fixture.WaitLoaded(2);
        Assert.Equal(2, (await fixture.PeekAsync()).Count);
    }

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public Task Scheduled_due_time_is_visible_in_the_real_message_properties_inspector() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var sender = fixture.Client.CreateSender(fixture.Queue);
        await sender.ScheduleMessageAsync(new ServiceBusMessage("{\"scheduled\":true}") { MessageId = "future-order" },
            DateTimeOffset.UtcNow.AddMinutes(5), fixture.Token);
        await using var receiver = fixture.Client.CreateReceiver(fixture.Queue);
        var brokerMessage = await receiver.PeekMessageAsync(cancellationToken: fixture.Token);
        Assert.NotNull(brokerMessage);
        var workflow = new BrokerConnectionWorkflow(
            () => new DirectServiceBusClientFactory(),
            factory => new InvestigationEntityBrowser(factory),
            factory => new ServiceBusMessageService(factory));
        await using var workspace = new InvestigationWorkspace(new ProtectedWorkspacePreferencesStore(fixture.ProfilePath), workflow);
        var appWindow = new InvestigationWindow(workspace);
        try
        {
            appWindow.Show();
            while (workspace.Inspector.Current?.Message.MessageId != "future-order")
                await Task.Delay(50, fixture.Token);
            var propertiesTab = (System.Windows.Controls.Primitives.ToggleButton)appWindow.FindName("PropertiesTab");
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(propertiesTab);
            ((System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)).Toggle();
            var viewer = (System.Windows.Controls.RichTextBox)appWindow.FindName("BodyViewer");
            Assert.Equal(System.Windows.Visibility.Visible, viewer.Visibility);
            // WPF's external UIA TextPattern returned empty despite the visible FlowDocument.
            // Read the actual rendered document in-process, retaining the real broker and window.
            string properties = new System.Windows.Documents.TextRange(viewer.Document.ContentStart, viewer.Document.ContentEnd).Text;
            Assert.Contains("ScheduledEnqueueTimeUtc", properties);
            Assert.Contains(brokerMessage.ScheduledEnqueueTime.UtcDateTime.ToString("yyyy-MM-dd"), properties);
            Assert.Contains(brokerMessage.ScheduledEnqueueTime.UtcDateTime.ToString("HH:mm:ss"), properties);
        }
        finally
        {
            appWindow.Close();
            while (appWindow.IsVisible) await Task.Delay(50, fixture.Token);
        }
    });

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(75));
        private readonly ServiceBusAdministrationClient admin = new(ServiceBusUiSmokeEnvironment.AdminConnectionString);
        private readonly string directory = Path.Combine(AppContext.BaseDirectory, "daily-audit-profiles", Guid.NewGuid().ToString("N"));
        private Application? application;
        private UIA3Automation? automation;
        private Window? window;
        public string Queue { get; } = "audit-window-" + Guid.NewGuid().ToString("N");
        public ServiceBusClient Client { get; } = new(ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
        public CancellationToken Token => timeout.Token;

        public static async Task<Fixture> CreateAsync(int autoRefresh = 0)
        {
            var fixture = new Fixture();
            try
            {
                await fixture.admin.CreateQueueAsync(fixture.Queue, fixture.Token);
                var profile = new InvestigationProfile("audit", new ConnectionProfile("Daily audit",
                    ServiceBusUiSmokeEnvironment.RuntimeConnectionString, ServiceBusUiSmokeEnvironment.AdminConnectionString));
                await new ProtectedWorkspacePreferencesStore(fixture.ProfilePath).SaveAsync(new WorkspacePreferences
                {
                    Profiles = [profile], SelectedProfileId = profile.Id, SelectedEntityPath = fixture.Queue,
                    WasConnected = true, CloseToTray = false, NotificationsEnabled = false,
                    AutoRefreshSeconds = autoRefresh, WindowWidth = 1100, WindowHeight = 800
                }, fixture.Token);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public string ProfilePath => Path.Combine(directory, "profiles.json");

        public void Open()
        {
            application = Application.Launch(WpfAppPath.Resolve(), $"--profile-store-path \"{ProfilePath}\"");
            automation = new UIA3Automation();
            Wait(() =>
            {
                window = application.GetAllTopLevelWindows(automation).FirstOrDefault(candidate =>
                    candidate.FindFirstDescendant(cf => cf.ByAutomationId("ConnectionButton")) is not null);
                return window is not null && window.FindFirstDescendant(cf => cf.ByText("Connected")) is not null;
            });
        }

        public AutomationElement Element(string id) => window!.FindFirstDescendant(cf => cf.ByAutomationId(id))
            ?? throw new InvalidOperationException($"Missing control {id}");

        public void Invoke(string id)
        {
            Wait(() => Element(id).IsEnabled);
            Element(id).Patterns.Invoke.Pattern.Invoke();
        }

        public void WaitLoaded(int count) => Wait(() => Element("MessageCountSummary").Name.StartsWith($"{count} loaded", StringComparison.Ordinal));

        public void AssertUnavailableTreeCountsAtSupportedSizes(string bucket)
        {
            foreach (var (width, height) in new[] { (1500, 1000), (1100, 800), (980, 640) })
            {
                window!.Patterns.Transform.Pattern.Resize(width, height);
                Wait(() => Math.Abs(window.BoundingRectangle.Width - width) < 3);
                var label = Element("NamespaceTree").FindFirstDescendant(cf => cf.ByText(Queue));
                Assert.NotNull(label);
                var node = label;
                while (node is not null && node.ControlType != ControlType.TreeItem) node = node.Parent;
                Assert.NotNull(node);
                Assert.Equal(3, node.FindAllDescendants(cf => cf.ByText("—")).Length);
                Assert.False(Element("MessageCountSummary").IsOffscreen);
                Assert.False(Element("RefreshButton").IsOffscreen);
                if (Environment.GetEnvironmentVariable("SBE_CAPTURE_DAILY_FIX_UI") == "true")
                {
                    string captures = Path.Combine(AppContext.BaseDirectory, "daily-fix-captures");
                    Directory.CreateDirectory(captures);
                    window.SetForeground();
                    FlaUI.Core.Capturing.Capture.Element(window).ToFile(Path.Combine(captures, $"counts-{bucket}-{width}x{height}.png"));
                }
            }
        }

        public void Wait(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                Token.ThrowIfCancellationRequested();
                if (condition()) return;
                Thread.Sleep(100);
            }
            throw new TimeoutException($"Daily UI audit condition timed out. Summary: {window?.FindFirstDescendant(cf => cf.ByAutomationId("MessageCountSummary"))?.Name}");
        }

        public async Task SendAsync(string id)
        {
            await using var sender = Client.CreateSender(Queue);
            await sender.SendMessageAsync(new ServiceBusMessage("{\"audit\":true}") { MessageId = id, ContentType = "application/json" }, Token);
        }

        public async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekAsync()
        {
            await using var receiver = Client.CreateReceiver(Queue);
            return await receiver.PeekMessagesAsync(100, 0, Token);
        }

        public void CloseNormally()
        {
            window!.Patterns.Window.Pattern.Close();
            Wait(() => application!.HasExited);
            application!.Dispose(); application = null;
            automation!.Dispose(); automation = null; window = null;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (application is { HasExited: false }) application.Kill();
                application?.Dispose();
                automation?.Dispose();
            }
            finally
            {
                await Client.DisposeAsync();
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                if ((await admin.QueueExistsAsync(Queue, cleanup.Token)).Value) await admin.DeleteQueueAsync(Queue, cleanup.Token);
                // Exactly the per-fixture directory constructed above; never user profile storage.
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                timeout.Dispose();
            }
        }
    }
}
