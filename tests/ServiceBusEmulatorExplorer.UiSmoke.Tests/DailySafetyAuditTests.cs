using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.UiSmoke.Tests;

/// <summary>Live broker integration of the workspace's mutation safety branches.</summary>
public sealed class DailySafetyAuditTests
{
    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public Task Active_delivery_cannot_be_replayed_and_broker_is_unchanged() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(deadLetter: false);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Workspace.ReplayAsync(f.Delivery));
        await f.AssertOriginalOnlyAsync();
    });

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public Task Active_delivery_cannot_be_deleted_through_dlq_command() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(deadLetter: false);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Workspace.DeleteAsync([f.Delivery]));
        await f.AssertOriginalOnlyAsync();
    });

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public Task Replay_canceled_before_start_sends_nothing_and_keeps_original() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Workspace.ReplayAsync(f.Delivery, cancellationToken: new CancellationToken(true)));
        await f.AssertOriginalOnlyAsync();
    });

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public Task Delete_canceled_before_start_settles_nothing() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Workspace.DeleteAsync([f.Delivery], new CancellationToken(true)));
        await f.AssertOriginalOnlyAsync();
    });

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public Task Replay_of_previous_connection_delivery_is_rejected_after_reconnect() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync();
        await f.Workspace.DisconnectAsync();
        await f.Workspace.ConnectAsync();
        Assert.True(f.Workspace.IsConnected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workspace.ReplayAsync(f.Delivery));
        await f.AssertOriginalOnlyAsync();
    });

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public Task Delete_of_previous_connection_delivery_is_rejected_after_reconnect() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync();
        await f.Workspace.DisconnectAsync();
        await f.Workspace.ConnectAsync();
        Assert.True(f.Workspace.IsConnected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workspace.DeleteAsync([f.Delivery]));
        await f.AssertOriginalOnlyAsync();
    });

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public Task Failed_replay_reservation_persistence_prevents_broker_send() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync();
        f.Store.FailSaves = true;
        try { await Assert.ThrowsAsync<IOException>(() => f.Workspace.ReplayAsync(f.Delivery)); }
        finally { f.Store.FailSaves = false; }
        await f.AssertOriginalOnlyAsync();
        Assert.Empty(f.Workspace.Preferences.ReplayFamilies);
    });

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public Task Empty_delete_is_a_noop_without_clearing_loaded_delivery() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync();
        var before = Assert.Single(f.Workspace.Browse.Messages);
        var result = await f.Workspace.DeleteAsync([]);
        Assert.Empty(result.Deletion.Outcomes);
        Assert.Same(before, Assert.Single(f.Workspace.Browse.Messages));
        await f.AssertOriginalOnlyAsync();
    });

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public Task Disconnected_replay_cannot_send_using_old_credentials() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync();
        await f.Workspace.DisconnectAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workspace.ReplayAsync(f.Delivery));
        await f.AssertOriginalOnlyAsync();
    });

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public Task Invalid_edited_json_blocks_replay_selection_and_discard_restores_eligibility() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync();
        var row = Assert.Single(f.Workspace.Browse.Messages);
        f.Workspace.Inspector.Select(row.Delivery);
        f.Workspace.Inspector.Document.Text = "{broken";
        var invalid = ReplaySelection.Evaluate(true, f.Workspace.Browse.Messages, row, f.Workspace.Inspector);
        Assert.False(invalid.CanReplay);
        Assert.Contains("JSON", invalid.Problem);
        f.Workspace.Inspector.DiscardCurrent();
        Assert.True(ReplaySelection.Evaluate(true, f.Workspace.Browse.Messages, row, f.Workspace.Inspector).CanReplay);
        await f.AssertOriginalOnlyAsync();
    });

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public Task Valid_edited_json_replays_changed_copy_and_preserves_original_bytes() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync();
        var row = Assert.Single(f.Workspace.Browse.Messages);
        f.Workspace.Inspector.Select(row.Delivery);
        f.Workspace.Inspector.Document.Text = "{\"order\":2}";
        var selection = ReplaySelection.Evaluate(true, f.Workspace.Browse.Messages, row, f.Workspace.Inspector);
        Assert.True(selection.CanReplay);
        var result = await f.Workspace.ReplayAsync(selection.Targets.Single(), selection.EditedBody);
        Assert.Equal(ReplaySendStatus.Confirmed, result.Status);
        Assert.Equal("{\"order\":2}", Assert.Single(await f.PeekAsync(SubQueue.None)).Body.ToString());
        Assert.Equal("{\"order\":1}", Assert.Single(await f.PeekAsync(SubQueue.DeadLetter)).Body.ToString());
    });

    [UiNavigationSmokeFact(Timeout = 90_000), Trait("TestCategory", "DailyAudit")]
    public Task Concurrent_replay_requests_reserve_distinct_attempts_and_keep_original() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync();
        var outcomes = await Task.WhenAll(f.Workspace.ReplayAsync(f.Delivery), f.Workspace.ReplayAsync(f.Delivery));
        Assert.All(outcomes, outcome => Assert.Equal(ReplaySendStatus.Confirmed, outcome.Status));
        Assert.Equal(new long[] { 1, 2 }, outcomes.Select(outcome => outcome.Reservation.Family.LastAttempt).Order().ToArray());
        var copies = await f.PeekAsync(SubQueue.None);
        Assert.Equal(2, copies.Count);
        Assert.Equal(2, copies.Select(message => message.MessageId).Distinct().Count());
        Assert.Single(await f.PeekAsync(SubQueue.DeadLetter));
    });

    private sealed class Store(WorkspacePreferences preferences) : IWorkspacePreferencesStore
    {
        public bool FailSaves { get; set; }
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken token) => Task.FromResult(new PreferencesLoadResult(preferences));
        public Task SaveAsync(WorkspacePreferences value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (FailSaves) throw new IOException("Audit-injected preference write failure");
            preferences = value;
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        private readonly ServiceBusAdministrationClient admin = new(ServiceBusUiSmokeEnvironment.AdminConnectionString);
        private readonly ServiceBusClient client = new(ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
        private readonly string queue = "audit-safety-" + Guid.NewGuid().ToString("N");
        private bool isDeadLetter;
        public Store Store { get; private set; } = null!;
        public InvestigationWorkspace Workspace { get; private set; } = null!;
        public MessageDelivery Delivery { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync(bool deadLetter = true)
        {
            var f = new Fixture { isDeadLetter = deadLetter };
            try
            {
                await f.admin.CreateQueueAsync(f.queue, f.timeout.Token);
                await using (var sender = f.client.CreateSender(f.queue))
                    await sender.SendMessageAsync(new ServiceBusMessage("{\"order\":1}") { MessageId = "original", ContentType = "application/json" }, f.timeout.Token);
                if (deadLetter)
                {
                    await using var receiver = f.client.CreateReceiver(f.queue);
                    var original = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5), f.timeout.Token);
                    Assert.NotNull(original);
                    await receiver.DeadLetterMessageAsync(original, "Audit", "Synthetic fixture", f.timeout.Token);
                }
                var profile = new InvestigationProfile("audit", new ConnectionProfile("Daily safety audit",
                    ServiceBusUiSmokeEnvironment.RuntimeConnectionString, ServiceBusUiSmokeEnvironment.AdminConnectionString));
                f.Store = new Store(new WorkspacePreferences { Profiles = [profile], SelectedProfileId = profile.Id,
                    SelectedEntityPath = f.queue, DeadLetter = deadLetter, WasConnected = true });
                f.Workspace = new InvestigationWorkspace(f.Store, new BrokerConnectionWorkflow(
                    () => new DirectServiceBusClientFactory(), factory => new InvestigationEntityBrowser(factory), factory => new ServiceBusMessageService(factory)));
                await f.Workspace.InitializeAsync().WaitAsync(f.timeout.Token);
                Assert.True(f.Workspace.IsConnected, f.Workspace.Status);
                f.Delivery = Assert.Single(f.Workspace.Browse.Messages).Delivery;
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }

        public async Task AssertOriginalOnlyAsync()
        {
            foreach (var bucket in new[] { SubQueue.None, SubQueue.DeadLetter })
            {
                await using var receiver = client.CreateReceiver(queue, new ServiceBusReceiverOptions { SubQueue = bucket });
                var messages = await receiver.PeekMessagesAsync(100, 0, timeout.Token);
                if ((bucket == SubQueue.DeadLetter) == isDeadLetter)
                {
                    var message = Assert.Single(messages);
                    Assert.Equal("original", message.MessageId);
                    Assert.Equal("{\"order\":1}", message.Body.ToString());
                }
                else Assert.Empty(messages);
            }
        }

        public async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekAsync(SubQueue bucket)
        {
            await using var receiver = client.CreateReceiver(queue, new ServiceBusReceiverOptions { SubQueue = bucket });
            return await receiver.PeekMessagesAsync(100, 0, timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            try { if (Workspace is not null) await Workspace.DisposeAsync(); }
            finally
            {
                await client.DisposeAsync();
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                if ((await admin.QueueExistsAsync(queue, cleanup.Token)).Value) await admin.DeleteQueueAsync(queue, cleanup.Token);
                timeout.Dispose();
            }
        }
    }
}
