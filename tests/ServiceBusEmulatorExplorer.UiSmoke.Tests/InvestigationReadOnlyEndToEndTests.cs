using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.UiSmoke.Tests;

public sealed class InvestigationReadOnlyEndToEndTests
{
    private const int QueueFixtureCount = 53;
    private const int QueueActiveCount = QueueFixtureCount - 1;
    private const int QueuePageSize = 25;
    private const string SubscriptionOne = "first";
    private const string SubscriptionTwo = "second";

    [UiNavigationSmokeFact(Timeout = 150_000)]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Persisted_workspace_reads_queue_dlq_and_related_topic_messages_without_consuming()
    {
        string runId = Guid.NewGuid().ToString("N");
        string queueName = $"ui-read-queue-{runId}";
        string topicName = $"ui-read-topic-{runId}";
        string profileId = $"ui-read-{runId}";
        string profilePath = Path.Combine(
            AppContext.BaseDirectory,
            "ui-smoke-profiles",
            runId,
            "connection-profiles.json");
        string correlationId = $"ui-related-{runId}";
        string targetMessageId = $"{queueName}-target";
        var adminClient = new ServiceBusAdministrationClient(ServiceBusUiSmokeEnvironment.AdminConnectionString);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        Application? application = null;
        UIA3Automation? automation = null;
        Exception? testFailure = null;

        try
        {
            await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(testTimeout.Token);
            await adminClient.CreateQueueAsync(queueName, testTimeout.Token);
            await adminClient.CreateTopicAsync(topicName, testTimeout.Token);
            await adminClient.CreateSubscriptionAsync(topicName, SubscriptionOne, testTimeout.Token);
            await adminClient.CreateSubscriptionAsync(topicName, SubscriptionTwo, testTimeout.Token);

            await using var setupFactory = await ConnectFactoryAsync(testTimeout.Token);
            var setupService = new ServiceBusMessageService(setupFactory);
            await SendQueueFixturesAsync(setupFactory.RuntimeClient, queueName, correlationId, testTimeout.Token);
            await SendTopicFixtureAsync(setupFactory.RuntimeClient, topicName, correlationId, testTimeout.Token);
            await DeadLetterQueueTargetAsync(setupFactory.RuntimeClient, queueName, targetMessageId, testTimeout.Token);

            var peekSources = new[]
            {
                new EntityAddress(EntityKind.Queue, queueName),
                new EntityAddress(EntityKind.Subscription, SubscriptionOne, topicName),
                new EntityAddress(EntityKind.Subscription, SubscriptionTwo, topicName)
            };
            IReadOnlyList<PeekObservation> before = await WaitForExpectedPeekSnapshotAsync(
                setupService,
                peekSources,
                testTimeout.Token);
            AssertFixtureCounts(before, peekSources);
            Assert.Contains(before, item =>
                item.Source == peekSources[0] &&
                item.Bucket == MessageBucket.DeadLetter &&
                item.MessageId == targetMessageId);
            Assert.Contains(before, item =>
                item.Source == peekSources[1] &&
                item.Bucket == MessageBucket.Active &&
                item.MessageId == $"{topicName}-related");
            await SavePreferencesAsync(profilePath, profileId, queueName, testTimeout.Token);

            string executablePath = WpfAppPath.Resolve();
            Assert.True(File.Exists(executablePath), $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

            application = Application.Launch(executablePath, CreateProfileStoreArguments(profilePath));
            automation = new UIA3Automation();
            Window window = WaitForMainWindowWithAutomationId(application, automation, "ConnectionButton", TimeSpan.FromSeconds(15));

            WaitForText(window, "Connected", TimeSpan.FromSeconds(30));
            WaitForText(window, queueName, TimeSpan.FromSeconds(30));
            WaitForCountPrefix(window, "MessageCountSummary", "25 loaded", TimeSpan.FromSeconds(20));
            InvokeButton(window, "LoadMoreButton", TimeSpan.FromSeconds(10));
            WaitForCountPrefix(window, "MessageCountSummary", "50 loaded", TimeSpan.FromSeconds(20));
            InvokeButton(window, "LoadMoreButton", TimeSpan.FromSeconds(10));
            WaitForCountPrefix(window, "MessageCountSummary", $"{QueueActiveCount} loaded", TimeSpan.FromSeconds(20));

            WaitForAutomationId(window, "DeadLetterTab", TimeSpan.FromSeconds(10)).Patterns.Toggle.Pattern.Toggle();
            WaitForCountPrefix(window, "MessageCountSummary", "1 loaded", TimeSpan.FromSeconds(20));
            AutomationElement messageGrid = WaitForAutomationId(window, "MessageGrid", TimeSpan.FromSeconds(10));
            AutomationElement target = WaitForTextWithin(messageGrid, targetMessageId, TimeSpan.FromSeconds(15));
            FindAncestor(target, ControlType.DataItem).Patterns.SelectionItem.Pattern.Select();
            WaitForAutomationName(window, "InspectorMessageId", targetMessageId, TimeSpan.FromSeconds(10));

            InvokeButton(window, "FindRelatedButton", TimeSpan.FromSeconds(10));
            WaitForAutomationNameContains(window, "SearchStatusText", "Search complete", TimeSpan.FromSeconds(40));
            WaitForCountPrefix(window, "MessageCountSummary", "3 matches", TimeSpan.FromSeconds(10));
            AutomationElement relatedMessageGrid = WaitForAutomationId(window, "MessageGrid", TimeSpan.FromSeconds(10));
            WaitForTextWithin(relatedMessageGrid, $"{topicName}/{SubscriptionOne}", TimeSpan.FromSeconds(10));
            WaitForTextWithin(relatedMessageGrid, $"{topicName}/{SubscriptionTwo}", TimeSpan.FromSeconds(10));

            InvokeButton(window, "ClearSearchButton", TimeSpan.FromSeconds(10));
            WaitForCountPrefix(window, "MessageCountSummary", "1 loaded", TimeSpan.FromSeconds(20));
            Assert.Equal("Dead letter", WaitForAutomationId(window, "DeadLetterTab", TimeSpan.FromSeconds(10)).Name);

            window.Patterns.Window.Pattern.Close();
            WaitForApplicationExit(application, TimeSpan.FromSeconds(10));
            application = null;
            automation.Dispose();
            automation = null;

            IReadOnlyList<PeekObservation> after = await WaitForExpectedPeekSnapshotAsync(
                setupService,
                peekSources,
                testTimeout.Token);
            Assert.Equal(before, after);
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            if (automation is not null)
                automation.Dispose();
            if (application is not null)
                CloseApplication(application);

            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await DeleteEntitiesAsync(adminClient, queueName, topicName, cleanupTimeout.Token, testFailure is null);
            DeleteProfileArtifacts(profilePath);
        }
    }

    private static async Task SavePreferencesAsync(
        string profilePath,
        string profileId,
        string queueName,
        CancellationToken cancellationToken)
    {
        var profile = new InvestigationProfile(
            profileId,
            new ConnectionProfile(
                "UI read-only emulator",
                ServiceBusUiSmokeEnvironment.RuntimeConnectionString,
                ServiceBusUiSmokeEnvironment.AdminConnectionString));
        var preferences = new WorkspacePreferences
        {
            Profiles = [profile],
            SelectedProfileId = profileId,
            SelectedEntityPath = queueName,
            QueuePageSize = QueuePageSize,
            TopicPageSize = 25,
            SubscriptionPageSize = 25,
            WasConnected = true,
            CloseToTray = false
        };
        var store = new ProtectedWorkspacePreferencesStore(profilePath);
        await store.SaveAsync(preferences, cancellationToken);
    }

    private static async Task SendQueueFixturesAsync(
        ServiceBusClient runtimeClient,
        string queueName,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using ServiceBusSender sender = runtimeClient.CreateSender(queueName);
        var messages = new List<ServiceBusMessage>(QueueFixtureCount)
        {
            new ServiceBusMessage("queue target")
            {
                MessageId = $"{queueName}-target",
                CorrelationId = correlationId,
                Subject = "related-target"
            }
        };
        messages.AddRange(Enumerable.Range(1, QueueFixtureCount - 1).Select(index => new ServiceBusMessage($"queue filler {index:D2}")
        {
            MessageId = $"{queueName}-filler-{index:D2}",
            Subject = "filler"
        }));
        await sender.SendMessagesAsync(messages, cancellationToken);
    }

    private static async Task SendTopicFixtureAsync(
        ServiceBusClient runtimeClient,
        string topicName,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using ServiceBusSender sender = runtimeClient.CreateSender(topicName);
        await sender.SendMessageAsync(new ServiceBusMessage("topic related")
        {
            MessageId = $"{topicName}-related",
            CorrelationId = correlationId,
            Subject = "related-topic"
        }, cancellationToken);
    }

    private static async Task DeadLetterQueueTargetAsync(
        ServiceBusClient runtimeClient,
        string queueName,
        string targetMessageId,
        CancellationToken cancellationToken)
    {
        await using ServiceBusReceiver receiver = runtimeClient.CreateReceiver(queueName);
        ServiceBusReceivedMessage message = await receiver.ReceiveMessageAsync(
            maxWaitTime: TimeSpan.FromSeconds(20),
            cancellationToken);
        Assert.Equal(targetMessageId, message.MessageId);
        await receiver.DeadLetterMessageAsync(message, cancellationToken: cancellationToken);
    }

    private static async Task<DirectServiceBusClientFactory> ConnectFactoryAsync(
        CancellationToken cancellationToken)
    {
        var factory = new DirectServiceBusClientFactory();
        await factory.ConnectAsync(
            new ConnectionProfile(
                "UI read-only emulator",
                ServiceBusUiSmokeEnvironment.RuntimeConnectionString,
                ServiceBusUiSmokeEnvironment.AdminConnectionString),
            cancellationToken);
        return factory;
    }

    private static async Task<IReadOnlyList<PeekObservation>> WaitForExpectedPeekSnapshotAsync(
        IServiceBusMessageService messageService,
        IReadOnlyList<EntityAddress> sources,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            IReadOnlyList<PeekObservation> snapshot = await ReadPeekSnapshotAsync(
                messageService,
                sources,
                cancellationToken);
            if (HasExpectedFixtureCounts(snapshot, sources))
                return snapshot;

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        IReadOnlyList<PeekObservation> finalSnapshot = await ReadPeekSnapshotAsync(
            messageService,
            sources,
            cancellationToken);
        AssertFixtureCounts(finalSnapshot, sources);
        return finalSnapshot;
    }

    private static async Task<IReadOnlyList<PeekObservation>> ReadPeekSnapshotAsync(
        IServiceBusMessageService messageService,
        IReadOnlyList<EntityAddress> sources,
        CancellationToken cancellationToken)
    {
        var observations = new List<PeekObservation>();
        foreach (EntityAddress source in sources)
        {
            foreach (MessageBucket bucket in Enum.GetValues<MessageBucket>())
            {
                long? fromSequenceNumber = null;
                bool reachedEnd = false;
                for (int page = 0; page < 20; page++)
                {
                    IReadOnlyList<ExplorerMessage> messages = await messageService.PeekMessagesAsync(
                        source,
                        bucket,
                        take: 100,
                        fromSequenceNumber: fromSequenceNumber,
                        cancellationToken: cancellationToken);
                    if (messages.Count == 0)
                    {
                        reachedEnd = true;
                        break;
                    }

                    observations.AddRange(messages.Select(message => new PeekObservation(
                        source,
                        bucket,
                        message.SequenceNumber,
                        message.MessageId,
                        message.DeliveryCount)));
                    fromSequenceNumber = checked(messages[^1].SequenceNumber + 1);
                }

                if (!reachedEnd)
                    throw new InvalidOperationException($"Peek did not reach the end of {source.Name} ({bucket}).");
            }
        }

        return observations;
    }

    private static bool HasExpectedFixtureCounts(
        IReadOnlyList<PeekObservation> observations,
        IReadOnlyList<EntityAddress> sources) =>
        Count(observations, sources[0], MessageBucket.Active) == QueueActiveCount &&
        Count(observations, sources[0], MessageBucket.DeadLetter) == 1 &&
        Count(observations, sources[1], MessageBucket.Active) == 1 &&
        Count(observations, sources[1], MessageBucket.DeadLetter) == 0 &&
        Count(observations, sources[2], MessageBucket.Active) == 1 &&
        Count(observations, sources[2], MessageBucket.DeadLetter) == 0;

    private static void AssertFixtureCounts(
        IReadOnlyList<PeekObservation> observations,
        IReadOnlyList<EntityAddress> sources)
    {
        Assert.Equal(QueueActiveCount, Count(observations, sources[0], MessageBucket.Active));
        Assert.Equal(1, Count(observations, sources[0], MessageBucket.DeadLetter));
        Assert.Equal(1, Count(observations, sources[1], MessageBucket.Active));
        Assert.DoesNotContain(observations, item => item.Source == sources[1] && item.Bucket == MessageBucket.DeadLetter);
        Assert.Equal(1, Count(observations, sources[2], MessageBucket.Active));
        Assert.DoesNotContain(observations, item => item.Source == sources[2] && item.Bucket == MessageBucket.DeadLetter);

        Assert.Equal(
            observations.Count,
            observations.Select(item => (item.Source, item.Bucket, item.SequenceNumber, item.MessageId)).Distinct().Count());
    }

    private static int Count(
        IReadOnlyList<PeekObservation> observations,
        EntityAddress source,
        MessageBucket bucket) =>
        observations.Count(item => item.Source == source && item.Bucket == bucket);

    private static AutomationElement WaitForTextWithin(
        AutomationElement container,
        string text,
        TimeSpan timeout) =>
        WaitForElement(timeout, () => container.FindFirstDescendant(cf => cf.ByText(text)));

    private static void DeleteProfileArtifacts(string profilePath)
    {
        try
        {
            if (File.Exists(profilePath))
                File.Delete(profilePath);

            string? runDirectory = Path.GetDirectoryName(profilePath);
            if (!string.IsNullOrWhiteSpace(runDirectory) && Directory.Exists(runDirectory))
                Directory.Delete(runDirectory);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WaitForApplicationExit(Application application, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!application.HasExited && DateTimeOffset.UtcNow < deadline)
            Thread.Sleep(250);

        Assert.True(application.HasExited, $"The application did not exit within {timeout.TotalSeconds:0} seconds.");
    }

    private static string CreateProfileStoreArguments(string profilePath) =>
        $"--profile-store-path {QuoteProcessArgument(profilePath)}";

    private static string QuoteProcessArgument(string argument) =>
        argument.Contains(' ', StringComparison.Ordinal)
            ? "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : argument;

    private static Window WaitForMainWindowWithAutomationId(
        Application application,
        UIA3Automation automation,
        string automationId,
        TimeSpan timeout)
    {
        AutomationElement window = WaitForElement(timeout, () => application.GetAllTopLevelWindows(automation)
            .FirstOrDefault(candidate => candidate.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is not null));
        return window.AsWindow();
    }

    private static AutomationElement WaitForAutomationId(Window window, string automationId, TimeSpan timeout) =>
        WaitForElement(timeout, () => window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)));

    private static AutomationElement WaitForAutomationName(
        Window window,
        string automationId,
        string expectedName,
        TimeSpan timeout) =>
        WaitForElement(timeout, () =>
        {
            AutomationElement? element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            return string.Equals(element?.Name, expectedName, StringComparison.Ordinal) ? element : null;
        });

    private static AutomationElement WaitForAutomationNameContains(
        Window window,
        string automationId,
        string expectedText,
        TimeSpan timeout) =>
        WaitForElement(timeout, () =>
        {
            AutomationElement? element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            return element?.Name.Contains(expectedText, StringComparison.OrdinalIgnoreCase) == true ? element : null;
        });

    private static AutomationElement WaitForText(Window window, string text, TimeSpan timeout) =>
        WaitForElement(timeout, () => window.FindFirstDescendant(cf => cf.ByText(text)));

    private static AutomationElement WaitForCountPrefix(
        Window window,
        string automationId,
        string expectedPrefix,
        TimeSpan timeout) =>
        WaitForElement(timeout, () =>
        {
            AutomationElement? element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            return element?.Name.StartsWith(expectedPrefix, StringComparison.Ordinal) == true ? element : null;
        });

    private static void InvokeButton(Window window, string automationId, TimeSpan timeout)
    {
        AutomationElement button = WaitForElement(timeout, () =>
        {
            AutomationElement? element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            return element is { IsEnabled: true } ? element : null;
        });
        button.Patterns.Invoke.Pattern.Invoke();
    }

    private static AutomationElement FindAncestor(AutomationElement element, ControlType controlType)
    {
        for (AutomationElement? current = element; current is not null; current = current.Parent)
            if (current.ControlType == controlType) return current;
        throw new InvalidOperationException($"Could not find ancestor with control type {controlType}.");
    }

    private static AutomationElement WaitForElement(TimeSpan timeout, Func<AutomationElement?> findElement)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            AutomationElement? element = findElement();
            if (element is not null) return element;
            Thread.Sleep(250);
        }

        throw new InvalidOperationException($"UI element did not appear within {timeout.TotalSeconds:0} seconds.");
    }

    private static void CloseApplication(Application application)
    {
        if (application.HasExited) return;
        try { application.Kill(); }
        catch when (application.HasExited) { }
    }

    private static async Task DeleteEntitiesAsync(
        ServiceBusAdministrationClient adminClient,
        string queueName,
        string topicName,
        CancellationToken cancellationToken,
        bool failOnError)
    {
        try
        {
            if (await adminClient.QueueExistsAsync(queueName, cancellationToken))
                await adminClient.DeleteQueueAsync(queueName, cancellationToken);
            if (await adminClient.TopicExistsAsync(topicName, cancellationToken))
                await adminClient.DeleteTopicAsync(topicName, cancellationToken);
        }
        catch when (!failOnError)
        {
        }
    }

    private sealed record PeekObservation(
        EntityAddress Source,
        MessageBucket Bucket,
        long SequenceNumber,
        string MessageId,
        int DeliveryCount);
}
