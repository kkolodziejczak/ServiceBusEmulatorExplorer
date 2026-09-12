using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.UiSmoke.Tests;

public sealed class InvestigationWatchEndToEndTests
{
    [UiNavigationSmokeFact(Timeout = 150_000)]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Persisted_watch_baselines_silently_polls_while_hidden_and_investigates_without_consuming()
    {
        string runId = Guid.NewGuid().ToString("N");
        string queueName = $"ui-watch-{runId}";
        string profilePath = Path.Combine(AppContext.BaseDirectory, "ui-smoke-profiles", runId, "connection-profiles.json");
        var admin = new ServiceBusAdministrationClient(ServiceBusUiSmokeEnvironment.AdminConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        Application? application = null;
        UIA3Automation? automation = null;
        bool passed = false;
        try
        {
            await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(timeout.Token);
            await admin.CreateQueueAsync(queueName, timeout.Token);
            await using var factory = new DirectServiceBusClientFactory();
            await factory.ConnectAsync(new ConnectionProfile("UI Watch emulator",
                ServiceBusUiSmokeEnvironment.RuntimeConnectionString,
                ServiceBusUiSmokeEnvironment.AdminConnectionString), timeout.Token);
            var messages = new ServiceBusMessageService(factory);
            await using ServiceBusSender sender = factory.RuntimeClient.CreateSender(queueName);
            await sender.SendMessageAsync(new ServiceBusMessage("existing DLQ") { MessageId = $"{runId}-dlq" }, timeout.Token);
            await using (ServiceBusReceiver receiver = factory.RuntimeClient.CreateReceiver(queueName))
            {
                ServiceBusReceivedMessage existing = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), timeout.Token);
                Assert.Equal($"{runId}-dlq", existing.MessageId);
                await receiver.DeadLetterMessageAsync(existing, cancellationToken: timeout.Token);
            }
            await sender.SendMessageAsync(new ServiceBusMessage("existing active") { MessageId = $"{runId}-baseline" }, timeout.Token);
            var source = new EntityAddress(EntityKind.Queue, queueName);
            IReadOnlyList<Observation> initial = await ReadSnapshotAsync(messages, source, timeout.Token);
            Assert.Single(initial, item => item.Bucket == MessageBucket.Active);
            Assert.Single(initial, item => item.Bucket == MessageBucket.DeadLetter);
            await SavePreferencesAsync(profilePath, runId, queueName, timeout.Token);

            string executable = WpfAppPath.Resolve();
            Assert.True(File.Exists(executable), $"Build the WPF app before running UI smoke tests. Missing: {executable}");
            application = Application.Launch(executable, $"--profile-store-path \"{profilePath}\"");
            automation = new UIA3Automation();
            Window main = WaitForWindow(application, automation, "ConnectionButton", timeout.Token);
            WaitForElement(() => main.FindFirstDescendant(cf => cf.ByText("Watch baseline ready. New arrivals will be reported.")),
                "Watch baseline ready activity", timeout.Token);
            Assert.Null(FindWindow(application, automation, "WatchNotificationSummary"));

            // Close follows the real persisted tray preference. Polling must continue without the main window.
            main.Patterns.Window.Pattern.Close();
            WaitUntil(() => FindWindow(application, automation, "ConnectionButton") is null,
                "main window hidden in tray", timeout.Token);
            Assert.False(application.HasExited);
            await sender.SendMessagesAsync(new[]
            {
                new ServiceBusMessage("first arrival") { MessageId = $"{runId}-first", CorrelationId = $"{runId}-case-one" },
                new ServiceBusMessage("newest arrival") { MessageId = $"{runId}-newest", CorrelationId = $"{runId}-case-two" }
            }, timeout.Token);
            IReadOnlyList<Observation> before = await ReadSnapshotAsync(messages, source, timeout.Token);
            Assert.Equal(3, before.Count(item => item.Bucket == MessageBucket.Active));
            Assert.Single(before, item => item.Bucket == MessageBucket.DeadLetter);
            Assert.All(initial, item => Assert.Contains(item, before));

            Window notification = WaitForWindow(application, automation, "WatchNotificationSummary", timeout.Token);
            WaitForName(notification, "WatchNotificationSummary", "2 new active messages", timeout.Token);
            Assert.Contains(queueName, FindById(notification, "WatchNotificationSource")!.Name, StringComparison.Ordinal);
            Assert.Null(FindWindow(application, automation, "ConnectionButton"));
            // An entire additional polling interval must not dismiss or recount an unacknowledged alert.
            await Task.Delay(TimeSpan.FromSeconds(16), timeout.Token);
            notification = WaitForWindow(application, automation, "WatchNotificationSummary", timeout.Token);
            Assert.Equal("2 new active messages", FindById(notification, "WatchNotificationSummary")!.Name);
            Invoke(notification, "InvestigateWatchNotification", timeout.Token);

            main = WaitForWindow(application, automation, "ConnectionButton", timeout.Token);
            WaitForElement(() => FindById(main, "SearchStatusText") is { } status &&
                    status.Name.Contains("Search complete", StringComparison.Ordinal) ? status : null,
                "completed global investigation search", timeout.Token);
            WaitForElement(() => FindById(main, "MessageCountSummary") is { } count &&
                    count.Name.StartsWith("2 matches", StringComparison.Ordinal) ? count : null,
                "both distinct watched cases in search", timeout.Token);
            WaitForName(main, "InspectorMessageId", $"{runId}-newest", timeout.Token);
            AutomationElement grid = FindById(main, "MessageGrid")!;
            Assert.NotNull(grid.FindFirstDescendant(cf => cf.ByText($"{runId}-first")));
            Assert.NotNull(grid.FindFirstDescendant(cf => cf.ByText($"{runId}-newest")));
            WaitUntil(() => FindWindow(application, automation, "WatchNotificationSummary") is null,
                "acknowledged notification closed", timeout.Token);
            IReadOnlyList<Observation> after = await ReadSnapshotAsync(messages, source, timeout.Token);
            Assert.Equal(before, after);
            passed = true;
        }
        finally
        {
            // Only the isolated launched process and uniquely named fixtures belong to this proof.
            if (application is not null && !application.HasExited)
            {
                try { application.Kill(); }
                catch when (application.HasExited) { }
            }
            automation?.Dispose();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                if (await admin.QueueExistsAsync(queueName, cleanup.Token))
                    await admin.DeleteQueueAsync(queueName, cleanup.Token);
            }
            catch when (!passed) { }
            if (File.Exists(profilePath)) File.Delete(profilePath);
            string runDirectory = Path.GetDirectoryName(profilePath)!;
            if (Directory.Exists(runDirectory)) Directory.Delete(runDirectory);
        }
    }

    private static Task SavePreferencesAsync(string profilePath, string profileId, string queueName, CancellationToken token) =>
        new ProtectedWorkspacePreferencesStore(profilePath).SaveAsync(new WorkspacePreferences
        {
            Profiles = [new InvestigationProfile(profileId, new ConnectionProfile("UI Watch emulator",
                ServiceBusUiSmokeEnvironment.RuntimeConnectionString, ServiceBusUiSmokeEnvironment.AdminConnectionString))],
            SelectedProfileId = profileId,
            SelectedEntityPath = queueName,
            WasConnected = true,
            CloseToTray = true,
            NotificationsEnabled = true,
            LogExpanded = true,
            Watches = new Dictionary<string, IReadOnlyList<WatchPreference>>
            {
                [profileId] = [new WatchPreference("queue:" + queueName, true, false)]
            }
        }, token);

    private static async Task<IReadOnlyList<Observation>> ReadSnapshotAsync(
        IServiceBusMessageService service, EntityAddress source, CancellationToken token)
    {
        var result = new List<Observation>();
        foreach (MessageBucket bucket in Enum.GetValues<MessageBucket>())
        {
            long? cursor = null;
            bool complete = false;
            for (int page = 0; page < 20; page++)
            {
                var messages = await service.PeekMessagesAsync(source, bucket, 100, cursor, token);
                if (messages.Count == 0) { complete = true; break; }
                result.AddRange(messages.Select(message => new Observation(bucket, message.MessageId,
                    message.SequenceNumber, message.DeliveryCount)));
                cursor = checked(messages[^1].SequenceNumber + 1);
            }
            Assert.True(complete, $"Fixture peek did not reach the end of {source.Name} ({bucket}).");
        }
        return result;
    }

    private static Window? FindWindow(Application application, UIA3Automation automation, string id) =>
        application.GetAllTopLevelWindows(automation).FirstOrDefault(window =>
            !window.IsOffscreen && FindById(window, id) is not null);

    private static Window WaitForWindow(Application application, UIA3Automation automation, string id, CancellationToken token) =>
        WaitForElement(() => FindWindow(application, automation, id), $"window containing {id}", token).AsWindow();

    private static AutomationElement? FindById(AutomationElement root, string id) =>
        root.FindFirstDescendant(cf => cf.ByAutomationId(id));

    private static void WaitForName(Window window, string id, string expected, CancellationToken token) =>
        WaitForElement(() => FindById(window, id) is { } element && element.Name == expected ? element : null,
            $"{id} = {expected}", token);

    private static void Invoke(Window window, string id, CancellationToken token) =>
        WaitForElement(() => FindById(window, id) is { IsEnabled: true } element ? element : null,
            $"enabled {id}", token).Patterns.Invoke.Pattern.Invoke();

    private static AutomationElement WaitForElement(Func<AutomationElement?> find, string description, CancellationToken token)
    {
        AutomationElement? found = null;
        WaitUntil(() => (found = find()) is not null, description, token);
        return found!;
    }

    private static void WaitUntil(Func<bool> condition, string description, CancellationToken token)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(35);
        while (DateTimeOffset.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (condition()) return;
            Thread.Sleep(250);
        }
        throw new InvalidOperationException($"Timed out waiting for {description}.");
    }

    private sealed record Observation(MessageBucket Bucket, string MessageId, long SequenceNumber, int DeliveryCount);
}
