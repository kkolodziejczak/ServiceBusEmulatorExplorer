using System.IO;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class ReplayBrokerTheoryAttribute : TheoryAttribute
{
    public ReplayBrokerTheoryAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_RUN_INTEGRATION_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            Skip = "Set SBE_RUN_INTEGRATION_TESTS=true to run the isolated local Service Bus replay history proof.";
    }
}

/// <summary>
/// End-to-end broker proof for the real InvestigationWorkspace replay, observation, cleanup,
/// and protected history paths. It is opt-in because it creates unique local emulator entities.
/// </summary>
public sealed class ReplayHistoryBrokerTests
{
    [InlineData(false)]
    [InlineData(true)]
    [ReplayBrokerTheory]
    [Trait("TestCategory", "Integration")]
    public async Task Replay_history_tracks_queue_and_topic_copies_through_cleanup_and_restart(bool topic)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        CancellationToken token = deadline.Token;
        ConnectionProfile profile = ConnectionProfileDefaults.LocalEmulator;
        var admin = new ServiceBusAdministrationClient(profile.AdministrationConnectionString);
        string suffix = Guid.NewGuid().ToString("N");
        string entityName = $"it-replay-history-{suffix}";
        string firstSubscription = "proof-left";
        string secondSubscription = "proof-right";
        var source = topic
            ? new EntityAddress(EntityKind.Subscription, firstSubscription, entityName)
            : new EntityAddress(EntityKind.Queue, entityName);
        var temporary = new TemporaryWorkspaceFile();
        bool fixtureCreated = false;

        try
        {
            await WaitForEmulatorAsync(admin, token);
            if (topic)
            {
                await admin.CreateTopicAsync(entityName, token);
                fixtureCreated = true;
                await admin.CreateSubscriptionAsync(entityName, firstSubscription, token);
                await admin.CreateSubscriptionAsync(entityName, secondSubscription, token);
            }
            else
            {
                await admin.CreateQueueAsync(entityName, token);
                fixtureCreated = true;
            }

            await using var client = new ServiceBusClient(profile.RuntimeConnectionString);
            await using var fixtureSender = client.CreateSender(entityName);
            await using var originalActive = topic
                ? client.CreateReceiver(entityName, firstSubscription)
                : client.CreateReceiver(entityName);
            await using var originalDlq = topic
                ? client.CreateReceiver(entityName, firstSubscription,
                    new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter })
                : client.CreateReceiver(entityName,
                    new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
            await using ServiceBusReceiver? siblingActive = topic ? client.CreateReceiver(entityName, secondSubscription) : null;
            var seed = new ServiceBusMessage(BinaryData.FromString("{\"stage\":\"original\"}"))
            {
                MessageId = $"original-{suffix}",
                ContentType = "application/json",
                Subject = "Replay history proof"
            };
            await fixtureSender.SendMessageAsync(seed, token);
            var original = await ReceiveRequiredAsync(originalActive, token);
            await originalActive.DeadLetterMessageAsync(original, "proof-original", "Isolated replay history fixture", cancellationToken: token);
            if (siblingActive is not null)
                await siblingActive.CompleteMessageAsync(await ReceiveRequiredAsync(siblingActive, token), token);

            var initialDlq = await PeekRequiredAsync(originalDlq, seed.MessageId, token);
            var store = new ProtectedWorkspacePreferencesStore(temporary.Path);
            // Prevent InitializeAsync from auto-connecting before the fixture is ready.
            await store.SaveAsync(new WorkspacePreferences { WasConnected = false }, token);
            {
                await using var workspace = CreateWorkspace(store);
                workspace.ConfirmWarning = _ => Task.FromResult(true);
                await workspace.InitializeAsync();
                await workspace.ConnectAsync();
                Assert.True(workspace.IsConnected, workspace.Status);
                Assert.Contains(workspace.Browse.AllEntities(), node => node.Address == source);
                EntityNode node = Assert.Single(workspace.Browse.AllEntities(), candidate => candidate.Address == source);
                await workspace.Browse.SelectAsync(node, deadLetter: true);
                MessageDelivery originalDelivery = Assert.Single(workspace.Browse.Messages,
                    row => row.MessageId == seed.MessageId).Delivery;
                Assert.Equal(initialDlq.SequenceNumber, originalDelivery.Identity.SequenceNumber);

                string reviewedId = $"replay-history-{suffix}";
                const string reviewedBody = "{\"stage\":\"reviewed\",\"approved\":true}";
                PreparedReplay prepared = workspace.PrepareReplay(originalDelivery, reviewedBody);
                ReplayCopyOutcome sent = await workspace.SendReplayAsync(prepared, reviewedId, token);
                Assert.Equal(ReplaySendStatus.Confirmed, sent.Status);
                Assert.Equal(reviewedId, sent.Reservation.MessageId);
                var savedAttempt = Assert.Single(workspace.CurrentReplayHistory);
                Assert.Equal(reviewedId, savedAttempt.Reservation.MessageId);
                Assert.Equal(ReplaySendStatus.Confirmed, savedAttempt.SendStatus);
                Assert.Equal(seed.MessageId, (await PeekRequiredAsync(originalDlq, seed.MessageId, token)).MessageId);

                await workspace.CheckReplayStatusAsync(token);
                ReplayAttempt activeObservation = Assert.Single(workspace.CurrentReplayHistory);
                Assert.NotNull(activeObservation.Observation);
                Assert.Equal(topic ? 2 : 1, activeObservation.Observation!.Locations.Count);
                Assert.All(activeObservation.Observation.Locations, location =>
                    Assert.Equal(MessageBucket.Active, location.Bucket));

                if (topic)
                {
                    await using var firstCopy = client.CreateReceiver(entityName, firstSubscription);
                    await using var secondCopy = client.CreateReceiver(entityName, secondSubscription);
                    ServiceBusReceivedMessage left = await ReceiveByIdAsync(firstCopy, reviewedId, token);
                    ServiceBusReceivedMessage right = await ReceiveByIdAsync(secondCopy, reviewedId, token);
                    AssertReviewedCopy(left, reviewedId, reviewedBody);
                    AssertReviewedCopy(right, reviewedId, reviewedBody);
                    await firstCopy.CompleteMessageAsync(left, token);
                    await secondCopy.DeadLetterMessageAsync(right, "proof-replayed-copy", "Exercise DLQ observation", cancellationToken: token);
                }
                else
                {
                    await using var copyReceiver = client.CreateReceiver(entityName);
                    ServiceBusReceivedMessage copy = await ReceiveByIdAsync(copyReceiver, reviewedId, token);
                    AssertReviewedCopy(copy, reviewedId, reviewedBody);
                    await copyReceiver.DeadLetterMessageAsync(copy, "proof-replayed-copy", "Exercise DLQ observation", cancellationToken: token);
                }

                await workspace.CheckReplayStatusAsync(token);
                ReplayAttempt observed = Assert.Single(workspace.CurrentReplayHistory);
                Assert.NotNull(observed.Observation);
                EntityAddress expectedDlqSource = topic
                    ? new EntityAddress(EntityKind.Subscription, secondSubscription, entityName)
                    : source;
                Assert.Contains(observed.Observation!.Locations, location =>
                    location.Source == expectedDlqSource && location.Bucket == MessageBucket.DeadLetter);

                await using var replayDlq = topic
                    ? client.CreateReceiver(entityName, secondSubscription,
                        new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter })
                    : client.CreateReceiver(entityName,
                        new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
                ServiceBusReceivedMessage dlqCopy = await ReceiveByIdAsync(replayDlq, reviewedId, token);
                await replayDlq.CompleteMessageAsync(dlqCopy, token);

                await workspace.CheckReplayStatusAsync(token);
                ReplayAttempt absent = Assert.Single(workspace.CurrentReplayHistory);
                Assert.NotNull(absent.Observation);
                Assert.True(absent.Observation!.IsAbsent, absent.Observation.Limitation ?? "The latest complete scan still found the replay.");
                Assert.Equal(ReplayOriginalStatus.Retained, absent.OriginalStatus);

                PreparedReplayCleanup cleanup = Assert.IsType<PreparedReplayCleanup>(
                    await workspace.PrepareReplayCleanupAsync(absent, token));
                await workspace.DeleteReplayOriginalAsync(cleanup, token);
                Assert.Null(await TryPeekByIdAsync(originalDlq, seed.MessageId, token));
                Assert.Equal(ReplayOriginalStatus.Deleted, Assert.Single(workspace.CurrentReplayHistory).OriginalStatus);
            }
            // A new workspace and protected-store instance represent reopening the history window.
            var reopenedStore = new ProtectedWorkspacePreferencesStore(temporary.Path);
            await using var reopened = CreateWorkspace(reopenedStore);
            reopened.ConfirmWarning = _ => Task.FromResult(true);
            await reopened.InitializeAsync();
            Assert.True(reopened.IsConnected, reopened.Status);
            ReplayAttempt persisted = Assert.Single(reopened.CurrentReplayHistory);
            Assert.Equal($"replay-history-{suffix}", persisted.Reservation.MessageId);
            Assert.Equal(ReplayOriginalStatus.Deleted, persisted.OriginalStatus);
            Assert.True(persisted.Observation?.IsAbsent);
        }
        finally
        {
            if (fixtureCreated)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                if (topic) await admin.DeleteTopicAsync(entityName, cleanup.Token);
                else await admin.DeleteQueueAsync(entityName, cleanup.Token);
            }
            temporary.Dispose();
        }
    }

    private static InvestigationWorkspace CreateWorkspace(ProtectedWorkspacePreferencesStore store) =>
        new(store, new BrokerConnectionWorkflow(
            () => new DirectServiceBusClientFactory(),
            factory => new InvestigationEntityBrowser(factory),
            factory => new ServiceBusMessageService(factory)));

    private static async Task WaitForEmulatorAsync(ServiceBusAdministrationClient admin, CancellationToken token)
    {
        Exception? last = null;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await foreach (QueueProperties _ in admin.GetQueuesAsync(token).WithCancellation(token)) break;
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) { last = exception; }
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        throw new TimeoutException("The local Service Bus emulator did not become ready within the 120-second proof budget.", last);
    }

    private static async Task<ServiceBusReceivedMessage> ReceiveRequiredAsync(ServiceBusReceiver receiver, CancellationToken token) =>
        Assert.IsType<ServiceBusReceivedMessage>(await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), token));

    private static async Task<ServiceBusReceivedMessage> ReceiveByIdAsync(ServiceBusReceiver receiver, string messageId, CancellationToken token)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(20);
        var held = new List<ServiceBusReceivedMessage>();
        try
        {
            while (DateTimeOffset.UtcNow < until)
            {
                ServiceBusReceivedMessage? message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2), token);
                if (message is null) continue;
                if (message.MessageId == messageId) return message;
                held.Add(message);
            }
            throw new TimeoutException($"Message {messageId} did not arrive in the isolated fixture.");
        }
        finally
        {
            if (held.Count > 0)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                foreach (ServiceBusReceivedMessage message in held)
                {
                    try { await receiver.AbandonMessageAsync(message, cancellationToken: cleanup.Token); }
                    catch (OperationCanceledException) when (cleanup.IsCancellationRequested) { break; }
                    catch (ServiceBusException) { }
                }
            }
        }
    }

    private static async Task<ServiceBusReceivedMessage> PeekRequiredAsync(ServiceBusReceiver receiver, string messageId, CancellationToken token) =>
        await TryPeekByIdAsync(receiver, messageId, token)
        ?? throw new Xunit.Sdk.XunitException($"Expected fixture message {messageId} was not present.");

    private static async Task<ServiceBusReceivedMessage?> TryPeekByIdAsync(ServiceBusReceiver receiver, string messageId, CancellationToken token)
    {
        long? fromSequenceNumber = 0;
        for (int page = 0; page < 8; page++)
        {
            IReadOnlyList<ServiceBusReceivedMessage> messages = await receiver.PeekMessagesAsync(50, fromSequenceNumber, token);
            ServiceBusReceivedMessage? match = messages.FirstOrDefault(message => message.MessageId == messageId);
            if (match is not null) return match;
            if (messages.Count < 50) return null;
            fromSequenceNumber = checked(messages[^1].SequenceNumber + 1);
        }
        throw new TimeoutException("The isolated fixture exceeded its bounded peek page budget.");
    }

    private static void AssertReviewedCopy(ServiceBusReceivedMessage message, string expectedId, string expectedBody)
    {
        Assert.Equal(expectedId, message.MessageId);
        Assert.Equal(expectedBody, message.Body.ToString());
    }

    private sealed class TemporaryWorkspaceFile : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SBE-replay-history-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(directory, "workspace.json");
        public TemporaryWorkspaceFile() => Directory.CreateDirectory(directory);
        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
