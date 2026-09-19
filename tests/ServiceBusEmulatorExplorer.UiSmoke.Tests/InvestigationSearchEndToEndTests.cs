using System.Diagnostics;
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
using Xunit.Abstractions;

namespace ServiceBusEmulatorExplorer.UiSmoke.Tests;

public sealed class InvestigationSearchEndToEndTests
{
    private const int FixtureMessageCount = 1_500;
    private const int PageSize = 50;
    private const int SearchBatchSize = 10_000;

    private readonly ITestOutputHelper output;

    public InvestigationSearchEndToEndTests(ITestOutputHelper output) => this.output = output;

    [UiNavigationSmokeFact(Timeout = 150_000)]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Global_search_stop_continue_and_reconnect_use_only_current_broker_session()
    {
        string runId = Guid.NewGuid().ToString("N");
        string queueName = $"ui-search-{runId}";
        string profilePath = Path.Combine(
            AppContext.BaseDirectory,
            "ui-smoke-profiles",
            runId,
            "connection-profiles.json");
        string profileId = $"ui-search-{runId}";
        string correlationId = $"ui-search-correlation-{runId}";
        string targetMessageId = $"{queueName}-target";
        var admin = new ServiceBusAdministrationClient(ServiceBusUiSmokeEnvironment.AdminConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(140));
        Application? application = null;
        UIA3Automation? automation = null;
        bool passed = false;
        var phaseTimer = Stopwatch.StartNew();

        try
        {
            await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(timeout.Token);
            output.WriteLine($"Phase readiness complete at {phaseTimer.Elapsed}.");
            await admin.CreateQueueAsync(queueName, timeout.Token);
            output.WriteLine($"Phase fixture queue created at {phaseTimer.Elapsed}: {queueName}.");
            await using (var factory = new DirectServiceBusClientFactory())
            {
                await factory.ConnectAsync(
                    new ConnectionProfile(
                        "UI search emulator",
                        ServiceBusUiSmokeEnvironment.RuntimeConnectionString,
                        ServiceBusUiSmokeEnvironment.AdminConnectionString),
                    timeout.Token);
                output.WriteLine($"Phase runtime/admin connection established at {phaseTimer.Elapsed}.");

                await SendFixtureAsync(factory.RuntimeClient, queueName, targetMessageId, correlationId, timeout.Token);
                output.WriteLine($"Phase fixture seeded at {phaseTimer.Elapsed}: {FixtureMessageCount} messages.");
            }

            await SavePreferencesAsync(profilePath, profileId, queueName, timeout.Token);

            string executable = WpfAppPath.Resolve();
            Assert.True(File.Exists(executable), $"Build the WPF app before running UI smoke tests. Missing: {executable}");
            application = Application.Launch(executable, $"--profile-store-path \"{profilePath}\"");
            output.WriteLine($"Phase executable launched at {phaseTimer.Elapsed}: {executable}.");
            automation = new UIA3Automation();
            Window window = WaitForWindow(application, automation, "ConnectionButton", timeout.Token);
            output.WriteLine($"Phase UI window found at {phaseTimer.Elapsed}.");

            WaitForConnectionState(window, "Connected", timeout.Token);
            WaitForText(window, queueName, timeout.Token);
            WaitForCountPrefix(window, "MessageCountSummary", $"{PageSize} loaded", timeout.Token);
            WaitForName(window, "InspectorMessageId", targetMessageId, timeout.Token);

            // FindRelated starts the same production global-search path as the user-facing correlation search.
            Invoke(window, "FindRelatedButton", timeout.Token);
            AutomationElement stopSearch = WaitForSearchScanBusy(window, timeout.Token);
            stopSearch.Patterns.Invoke.Pattern.Invoke();
            output.WriteLine("Stop invoked immediately after observing Searching and enabled Stop.");

            AutomationElement stoppedStatus = WaitForNameContains(window, "SearchStatusText", "Search stopped", timeout.Token);
            output.WriteLine($"Stopped search status: {stoppedStatus.Name}");
            Assert.Contains("Partial results", stoppedStatus.Name, StringComparison.Ordinal);
            string stoppedSummary = WaitForAutomationId(window, "MessageCountSummary", timeout.Token).Name;
            int scannedBeforeContinue = ReadScannedCount(stoppedSummary);
            output.WriteLine($"Stopped search summary: {stoppedSummary}; target rows: {CountText(window, targetMessageId)}");
            Assert.InRange(scannedBeforeContinue, 0, SearchBatchSize);
            Assert.InRange(CountText(window, targetMessageId), 0, 1);
            Assert.True(ById(window, "LoadMoreButton")!.IsEnabled, "Partial search results must expose Continue.");

            Invoke(window, "LoadMoreButton", timeout.Token);
            AutomationElement continueBusyStatus = WaitForBusySearch(window, timeout.Token);
            output.WriteLine($"Continue busy status: {continueBusyStatus.Name}");
            AutomationElement continueStatus = WaitForSearchIdleStatus(window, timeout.Token);
            string continueSummary = WaitForAutomationId(window, "MessageCountSummary", timeout.Token).Name;
            int scannedAfterContinue = ReadScannedCount(continueSummary);
            output.WriteLine($"Continue result status: {continueStatus.Name}; summary: {continueSummary}; target rows: {CountText(window, targetMessageId)}");
            Assert.True(scannedAfterContinue > scannedBeforeContinue, $"Continue must advance the scan ({scannedBeforeContinue} -> {scannedAfterContinue}).");
            Assert.Equal(1, CountText(window, targetMessageId));

            // Start a second real search and disconnect before the broker scan returns.
            Invoke(window, "ClearSearchButton", timeout.Token);
            WaitForCountPrefix(window, "MessageCountSummary", $"{PageSize} loaded", timeout.Token);
            WaitForName(window, "InspectorMessageId", targetMessageId, timeout.Token);
            AutomationElement connection = ById(window, "ConnectionButton")!;
            Invoke(window, "FindRelatedButton", timeout.Token);
            WaitForSearchScanBusy(window, timeout.Token);
            connection.Patterns.Invoke.Pattern.Invoke();
            output.WriteLine("Disconnect invoked immediately after observing Searching and enabled Stop.");
            WaitForConnectionState(window, "Disconnected", timeout.Token);
            string disconnectedSummary = ById(window, "MessageCountSummary")!.Name;
            output.WriteLine($"Disconnected summary: '{disconnectedSummary}'");
            Assert.DoesNotContain("matches", disconnectedSummary, StringComparison.OrdinalIgnoreCase);

            // Reconnect creates a new generation and must restore a fresh browse surface without old search rows.
            Invoke(window, "ConnectionButton", timeout.Token);
            WaitForConnectionState(window, "Connected", timeout.Token);
            WaitForText(window, queueName, timeout.Token);
            Assert.DoesNotContain("matches", ById(window, "MessageCountSummary")!.Name, StringComparison.OrdinalIgnoreCase);
            SelectQueue(window, queueName, timeout.Token);
            WaitForCountPrefix(window, "MessageCountSummary", $"{PageSize} loaded", timeout.Token);
            string reconnectedSummary = ById(window, "MessageCountSummary")!.Name;
            output.WriteLine($"Reconnected summary: {reconnectedSummary}; target rows: {CountText(window, targetMessageId)}");
            Assert.DoesNotContain("matches", reconnectedSummary, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, CountText(window, targetMessageId));

            window.Patterns.Window.Pattern.Close();
            WaitForApplicationExit(application, TimeSpan.FromSeconds(10));
            application = null;
            passed = true;
        }
        finally
        {
            var cleanupFailures = new List<Exception>();
            try
            {
                if (application is not null && !application.HasExited)
                {
                    application.Kill();
                    output.WriteLine("Launched application terminated during cleanup.");
                }
            }
            catch (Exception exception)
            {
                output.WriteLine($"Application cleanup failed: {exception.GetType().Name}: {exception.Message}");
                cleanupFailures.Add(exception);
            }

            try
            {
                automation?.Dispose();
                output.WriteLine("UI automation disposed.");
            }
            catch (Exception exception)
            {
                output.WriteLine($"UI automation cleanup failed: {exception.GetType().Name}: {exception.Message}");
                cleanupFailures.Add(exception);
            }

            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                bool exists = await admin.QueueExistsAsync(queueName, cleanup.Token);
                if (exists)
                {
                    await admin.DeleteQueueAsync(queueName, cleanup.Token);
                    output.WriteLine($"Deleted fixture queue {queueName}.");
                }
                else
                {
                    output.WriteLine($"Fixture queue {queueName} was already absent.");
                }
            }
            catch (Exception exception)
            {
                output.WriteLine($"Fixture queue cleanup failed (test passed={passed}): {exception.GetType().Name}: {exception.Message}");
                cleanupFailures.Add(exception);
            }

            try
            {
                DeleteProfileArtifacts(profilePath);
                output.WriteLine($"Deleted profile artifacts under {Path.GetDirectoryName(profilePath)}.");
            }
            catch (Exception exception)
            {
                output.WriteLine($"Profile cleanup failed: {exception.GetType().Name}: {exception.Message}");
                cleanupFailures.Add(exception);
            }

            if (passed && cleanupFailures.Count > 0)
                throw new AggregateException("Search proof cleanup failed after the test passed.", cleanupFailures);
        }
    }

    private static async Task SendFixtureAsync(
        ServiceBusClient client,
        string queueName,
        string targetMessageId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using ServiceBusSender sender = client.CreateSender(queueName);
        for (int start = 0; start < FixtureMessageCount; start += 100)
        {
            var batch = new List<ServiceBusMessage>(Math.Min(100, FixtureMessageCount - start));
            for (int offset = 0; offset < 100 && start + offset < FixtureMessageCount; offset++)
            {
                int index = start + offset;
                batch.Add(index == 0
                    ? new ServiceBusMessage("global-search-target")
                    {
                        MessageId = targetMessageId,
                        CorrelationId = correlationId,
                        Subject = "global-search-target"
                    }
                    : new ServiceBusMessage($"global-search-filler-{index:D5}")
                    {
                        MessageId = $"{queueName}-filler-{index:D5}"
                    });
            }

            await sender.SendMessagesAsync(batch, cancellationToken);
        }
    }

    private static Task SavePreferencesAsync(
        string profilePath,
        string profileId,
        string queueName,
        CancellationToken cancellationToken) =>
        new ProtectedWorkspacePreferencesStore(profilePath).SaveAsync(new WorkspacePreferences
        {
            Profiles =
            [
                new InvestigationProfile(profileId, new ConnectionProfile(
                    "UI search emulator",
                    ServiceBusUiSmokeEnvironment.RuntimeConnectionString,
                    ServiceBusUiSmokeEnvironment.AdminConnectionString))
            ],
            SelectedProfileId = profileId,
            SelectedEntityPath = queueName,
            QueuePageSize = PageSize,
            TopicPageSize = PageSize,
            SubscriptionPageSize = PageSize,
            SearchTimeBudgetSeconds = 30,
            SearchDeliveryBudget = SearchBatchSize,
            WasConnected = true,
            CloseToTray = false,
            NotificationsEnabled = false
        }, cancellationToken);

    private static Window WaitForWindow(
        Application application,
        UIA3Automation automation,
        string automationId,
        CancellationToken cancellationToken) =>
        WaitForElement(
            () => application.GetAllTopLevelWindows(automation)
                .FirstOrDefault(window => ById(window, automationId) is not null),
            $"window containing {automationId}",
            cancellationToken).AsWindow();

    private static AutomationElement WaitForBusySearch(Window window, CancellationToken cancellationToken)
    {
        return WaitForElement(
            () =>
            {
                AutomationElement? status = ById(window, "SearchStatusText");
                AutomationElement? stop = ById(window, "StopSearchButton");
                return stop is { IsEnabled: true }
                    && status is not null
                    && (status.Name.Contains("Discovering", StringComparison.Ordinal)
                        || status.Name.Contains("Searching", StringComparison.Ordinal)
                        || status.Name.Contains("Continuing", StringComparison.Ordinal))
                    ? status
                    : null;
            },
            "broker-backed search in progress",
            cancellationToken);
    }

    private static AutomationElement WaitForSearchScanBusy(Window window, CancellationToken cancellationToken)
    {
        return WaitForElement(
            () =>
            {
                AutomationElement? status = ById(window, "SearchStatusText");
                AutomationElement? stop = ById(window, "StopSearchButton");
                return stop is { IsEnabled: true }
                    && status?.Name.Contains("Searching", StringComparison.Ordinal) == true
                    ? stop
                    : null;
            },
            "broker-backed message scan in progress",
            cancellationToken);
    }

    private static AutomationElement WaitForSearchIdleStatus(Window window, CancellationToken cancellationToken)
    {
        return WaitForElement(
            () =>
            {
                AutomationElement? status = ById(window, "SearchStatusText");
                if (status is null || status.Name.Contains("Searching", StringComparison.Ordinal)
                    || status.Name.Contains("Continuing", StringComparison.Ordinal)
                    || status.Name.Contains("Discovering", StringComparison.Ordinal))
                    return null;
                return status.Name.Contains("Search complete", StringComparison.Ordinal)
                    || status.Name.Contains("Search paused", StringComparison.Ordinal)
                    || status.Name.Contains("Partial results", StringComparison.Ordinal)
                    ? status
                    : null;
            },
            "search completion or partial state",
            cancellationToken);
    }

    private static int ReadScannedCount(string summary)
    {
        string scanned = summary.Split('·').Last().Trim();
        Assert.EndsWith("scanned", scanned, StringComparison.Ordinal);
        return int.Parse(scanned[..^"scanned".Length].Trim(),
            System.Globalization.NumberStyles.Integer | System.Globalization.NumberStyles.AllowThousands,
            System.Globalization.CultureInfo.CurrentCulture);
    }

    private static void SelectQueue(Window window, string queueName, CancellationToken cancellationToken)
    {
        AutomationElement text = WaitForElement(
            () => ById(window, "NamespaceTree")?.FindFirstDescendant(cf => cf.ByText(queueName)),
            $"refreshed queue tree item {queueName}", cancellationToken);
        for (AutomationElement? current = text; current is not null; current = current.Parent)
        {
            if (current.ControlType != FlaUI.Core.Definitions.ControlType.TreeItem) continue;
            current.Patterns.SelectionItem.Pattern.Select();
            return;
        }
        throw new InvalidOperationException("The fixture queue has no selectable tree ancestor.");
    }

    private static int CountText(Window window, string text) =>
        ById(window, "MessageGrid")?.FindAllDescendants(cf => cf.ByText(text)).Length ?? 0;

    private static AutomationElement? ById(AutomationElement root, string automationId) =>
        root.FindFirstDescendant(cf => cf.ByAutomationId(automationId));

    private static AutomationElement WaitForAutomationId(Window window, string automationId, CancellationToken cancellationToken) =>
        WaitForElement(() => ById(window, automationId), $"{automationId}", cancellationToken);

    private static void WaitForName(Window window, string automationId, string expected, CancellationToken cancellationToken) =>
        WaitForElement(() => ById(window, automationId) is { } element && element.Name == expected ? element : null,
            $"{automationId} = {expected}", cancellationToken);

    private static void WaitForConnectionState(Window window, string expected, CancellationToken cancellationToken) =>
        WaitForConnectionStateCore(window, expected, cancellationToken);

    private static void WaitForConnectionStateCore(Window window, string expected, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(45);
        string buttonName = string.Empty;
        string descendants = string.Empty;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AutomationElement? button = ById(window, "ConnectionButton");
            if (button is not null)
            {
                buttonName = button.Name;
                descendants = string.Join(", ", button.FindAllDescendants().Select(element => element.Name));
                if (button.FindFirstDescendant(cf => cf.ByText(expected)) is not null)
                    return;
            }

            Thread.Sleep(100);
        }

        throw new InvalidOperationException(
            $"Timed out waiting for connection state {expected}. ConnectionButton name='{buttonName}', descendants=[{descendants}].");
    }

    private static AutomationElement WaitForNameContains(Window window, string automationId, string expected, CancellationToken cancellationToken) =>
        WaitForElement(() => ById(window, automationId) is { } element && element.Name.Contains(expected, StringComparison.Ordinal)
            ? element
            : null,
            $"{automationId} containing {expected}", cancellationToken);

    private static AutomationElement WaitForText(Window window, string text, CancellationToken cancellationToken) =>
        WaitForElement(() => window.FindFirstDescendant(cf => cf.ByText(text)), $"text {text}", cancellationToken);

    private static AutomationElement WaitForCountPrefix(Window window, string automationId, string expected, CancellationToken cancellationToken) =>
        WaitForElement(() => ById(window, automationId) is { } element && element.Name.StartsWith(expected, StringComparison.Ordinal)
            ? element
            : null,
            $"{automationId} starting with {expected}", cancellationToken);

    private static void Invoke(Window window, string automationId, CancellationToken cancellationToken)
    {
        AutomationElement button = WaitForElement(
            () => ById(window, automationId) is { IsEnabled: true } element ? element : null,
            $"enabled {automationId}",
            cancellationToken);
        button.Patterns.Invoke.Pattern.Invoke();
    }

    private static AutomationElement WaitForElement(
        Func<AutomationElement?> find,
        string description,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (find() is { } element)
                return element;
            Thread.Sleep(100);
        }

        throw new InvalidOperationException($"Timed out waiting for {description}.");
    }

    private static void WaitForApplicationExit(Application application, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!application.HasExited && DateTimeOffset.UtcNow < deadline)
            Thread.Sleep(250);
        Assert.True(application.HasExited, $"The application did not exit within {timeout.TotalSeconds:0} seconds.");
    }

    private static void DeleteProfileArtifacts(string profilePath)
    {
        if (File.Exists(profilePath)) File.Delete(profilePath);
        string? directory = Path.GetDirectoryName(profilePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
