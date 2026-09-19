using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.UiSmoke.Tests;

/// <summary>
/// Daily-workflow audit cases. These exercise the production workspace, search, inspector,
/// and preference services against a live emulator. Only the preference store is in-memory,
/// so each case can keep its state isolated without touching the user's profile file.
/// </summary>
public sealed class DailySearchLifecycleAuditTests
{
    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Browse_active_page_and_inspect_json_body() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("browse-json");
        string id = fixture.Id("json");
        await fixture.SendAsync(fixture.PrimaryQueue, new ServiceBusMessage("{\"orderId\":\"ORD-1\"}") { MessageId = id });

        await fixture.SelectPrimaryAsync();
        Assert.Contains(fixture.Workspace.Browse.Messages, row => row.MessageId == id);
        var row = fixture.Workspace.Browse.Messages.Single(row => row.MessageId == id);
        fixture.Workspace.Inspector.Select(row.Delivery);
        Assert.Contains("ORD-1", fixture.Workspace.Inspector.Document.Text, StringComparison.Ordinal);
        Assert.True(fixture.Workspace.Inspector.IsValidJson);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Search_exact_message_id_returns_the_expected_delivery() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("search-id");
        string target = fixture.Id("target");
        await fixture.SendAsync(fixture.PrimaryQueue,
            new ServiceBusMessage("target") { MessageId = target },
            new ServiceBusMessage("other") { MessageId = fixture.Id("other") });

        await fixture.Workspace.Search.StartAsync($"message:{target}");

        Assert.True(fixture.Workspace.Search.IsComplete, fixture.Workspace.Search.Status);
        Assert.Single(fixture.Workspace.Search.Messages);
        Assert.Equal(target, fixture.Workspace.Search.Messages[0].MessageId);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Search_correlation_id_unions_queue_and_topic_subscription_deliveries() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("search-correlation", extraQueue: true, topic: true);
        string correlation = fixture.Id("correlation");
        await fixture.SendAsync(fixture.PrimaryQueue, new ServiceBusMessage("queue") { MessageId = fixture.Id("q"), CorrelationId = correlation });
        await fixture.SendAsync(fixture.SecondaryQueue!, new ServiceBusMessage("queue-2") { MessageId = fixture.Id("q2"), CorrelationId = correlation });
        await fixture.SendTopicAsync(new ServiceBusMessage("topic") { MessageId = fixture.Id("topic"), CorrelationId = correlation });

        await fixture.Workspace.Search.StartAsync($"correlation:{correlation}");

        Assert.True(fixture.Workspace.Search.IsComplete, fixture.Workspace.Search.Status);
        Assert.Equal(4, fixture.Workspace.Search.Messages.Count);
        Assert.Equal(4, fixture.Workspace.Search.Messages.Select(row => row.Source).Distinct(StringComparer.Ordinal).Count());
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Duplicate_message_ids_remain_distinct_deliveries() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("duplicate-ids");
        string duplicate = fixture.Id("duplicate");
        await fixture.SendAsync(fixture.PrimaryQueue,
            new ServiceBusMessage("first") { MessageId = duplicate },
            new ServiceBusMessage("second") { MessageId = duplicate });

        await fixture.SelectPrimaryAsync();
        var rows = fixture.Workspace.Browse.Messages.Where(row => row.MessageId == duplicate).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.NotEqual(rows[0].Key, rows[1].Key);
        Assert.NotEqual(rows[0].Key.SequenceNumber, rows[1].Key.SequenceNumber);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Search_no_match_completes_without_stale_rows() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("search-empty");
        await fixture.SendAsync(fixture.PrimaryQueue, new ServiceBusMessage("known") { MessageId = fixture.Id("known") });

        await fixture.Workspace.Search.StartAsync($"message:{fixture.Id("missing")}");

        Assert.True(fixture.Workspace.Search.IsComplete, fixture.Workspace.Search.Status);
        Assert.Empty(fixture.Workspace.Search.Messages);
        Assert.Contains("0 matches", fixture.Workspace.Search.CountSummary, StringComparison.Ordinal);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Invalid_query_is_reported_without_returning_rows() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("search-invalid");

        await fixture.Workspace.Search.StartAsync("message:\"unterminated");

        Assert.True(fixture.Workspace.Search.IsActive);
        Assert.False(fixture.Workspace.Search.IsBusy);
        Assert.NotEmpty(fixture.Workspace.Search.QueryError);
        Assert.Contains("Invalid search", fixture.Workspace.Search.Status, StringComparison.Ordinal);
        Assert.Empty(fixture.Workspace.Search.Messages);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Search_stop_during_discovery_can_continue_into_message_scanning() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("search-stop");
        await fixture.ApplySearchBudgetAsync(deliveries: 1_000, seconds: 10);
        var messages = Enumerable.Range(0, 1_200)
            .Select(index => new ServiceBusMessage($"filler-{index}") { MessageId = fixture.Id($"filler-{index:D4}") })
            .ToArray();
        await fixture.SendAsync(fixture.PrimaryQueue, messages);

        Task search = fixture.Workspace.Search.StartAsync($"message:{fixture.Id("target")}");
        await WaitUntilAsync(() => fixture.Workspace.Search.IsBusy, TimeSpan.FromSeconds(10));
        fixture.Workspace.Search.Stop();
        await search;
        int stoppedScan = string.IsNullOrEmpty(fixture.Workspace.Search.CountSummary) ? 0 : ReadScanned(fixture.Workspace.Search.CountSummary);

        Assert.Contains("Search stopped", fixture.Workspace.Search.Status, StringComparison.Ordinal);
        Assert.True(fixture.Workspace.Search.CanContinue);
        await fixture.Workspace.Search.ContinueAsync();
        Assert.True(ReadScanned(fixture.Workspace.Search.CountSummary) > stoppedScan);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Clear_search_restores_browse_scope_and_selection() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("search-clear");
        string id = fixture.Id("clear-target");
        await fixture.SendAsync(fixture.PrimaryQueue, new ServiceBusMessage("clear") { MessageId = id });
        await fixture.SelectPrimaryAsync();
        string browsePath = fixture.Workspace.Browse.EntityPath;
        DeliveryIdentity? focusedKey = fixture.Workspace.Browse.FocusedMessage?.Key;
        await fixture.Workspace.Search.StartAsync($"message:{id}");
        Assert.NotEmpty(fixture.Workspace.Search.Messages);

        fixture.Workspace.Search.Clear();

        Assert.False(fixture.Workspace.Search.IsActive);
        Assert.Equal(browsePath, fixture.Workspace.Browse.EntityPath);
        Assert.Equal(focusedKey, fixture.Workspace.Browse.FocusedMessage?.Key);
        Assert.Contains(fixture.Workspace.Browse.Messages, row => row.MessageId == id);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Disconnect_clears_search_and_reconnect_starts_a_clean_browse_session() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("reconnect");
        await fixture.SendAsync(fixture.PrimaryQueue, new ServiceBusMessage("reconnect") { MessageId = fixture.Id("reconnect") });
        await fixture.Workspace.Search.StartAsync("message:missing");
        Assert.True(fixture.Workspace.Search.IsActive);

        await fixture.Workspace.DisconnectAsync();
        Assert.False(fixture.Workspace.IsConnected);
        Assert.False(fixture.Workspace.Search.IsActive);
        Assert.Empty(fixture.Workspace.Search.Messages);
        Assert.Empty(fixture.Workspace.Browse.Messages);

        await fixture.Workspace.ConnectAsync();
        await fixture.SelectPrimaryAsync();
        Assert.True(fixture.Workspace.IsConnected);
        Assert.Equal(fixture.PrimaryQueue, fixture.Workspace.Browse.EntityPath);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Dirty_inspector_draft_blocks_profile_switch_until_discard_is_confirmed() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("profile-switch");
        await fixture.SendAsync(fixture.PrimaryQueue, new ServiceBusMessage("editable") { MessageId = fixture.Id("editable") });
        await fixture.MoveFirstToDeadLetterAsync();
        await fixture.SelectPrimaryAsync(deadLetter: true);
        fixture.Workspace.Inspector.Select(fixture.Workspace.Browse.Messages.Single().Delivery);
        fixture.Workspace.Inspector.Document.Replace(0, fixture.Workspace.Inspector.Document.TextLength, "edited");
        Assert.True(fixture.Workspace.Inspector.HasDrafts);

        fixture.Workspace.ConfirmDiscard = () => Task.FromResult(false);
        Assert.False(await fixture.Workspace.SwitchProfileAsync(fixture.AlternateProfile));
        Assert.Equal(fixture.ProfileId, fixture.Workspace.Preferences.SelectedProfileId);

        fixture.Workspace.ConfirmDiscard = () => Task.FromResult(true);
        Assert.True(await fixture.Workspace.SwitchProfileAsync(fixture.AlternateProfile));
        Assert.Equal(fixture.AlternateProfile.Id, fixture.Workspace.Preferences.SelectedProfileId);
        Assert.False(fixture.Workspace.Inspector.HasDrafts);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Inspector_preserves_invalid_json_and_unicode_text() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("inspector-text");
        string body = "{\"message\": \"café 🚚\", \"broken\": }";
        await fixture.SendAsync(fixture.PrimaryQueue, new ServiceBusMessage(body) { MessageId = fixture.Id("invalid-json") });
        await fixture.SelectPrimaryAsync();
        fixture.Workspace.Inspector.Select(fixture.Workspace.Browse.Messages.Single().Delivery);

        Assert.Equal(body, fixture.Workspace.Inspector.RawText);
        Assert.Equal(body, fixture.Workspace.Inspector.Document.Text);
        Assert.False(fixture.Workspace.Inspector.IsValidJson);
        Assert.Equal("Body (not JSON)", fixture.Workspace.Inspector.JsonLabel);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Inspector_shows_non_utf8_body_as_base64_without_data_loss() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("inspector-binary");
        byte[] bytes = [0, 159, 146, 150, 255];
        await fixture.SendAsync(fixture.PrimaryQueue, new ServiceBusMessage(BinaryData.FromBytes(bytes)) { MessageId = fixture.Id("binary") });
        await fixture.SelectPrimaryAsync();
        fixture.Workspace.Inspector.Select(fixture.Workspace.Browse.Messages.Single().Delivery);

        string expected = Convert.ToBase64String(bytes);
        Assert.Equal(expected, fixture.Workspace.Inspector.RawText);
        Assert.Equal(expected, fixture.Workspace.Inspector.Document.Text);
        Assert.Contains("Base64", fixture.Workspace.Inspector.RawDescription, StringComparison.Ordinal);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Preferences_round_trip_updates_search_budget_and_timestamp_display() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("preferences");
        await fixture.ApplySearchBudgetAsync(1_000, 60);
        await fixture.Workspace.UpdateDisplayPreferencesAsync(timestampDisplay: TimestampDisplay.Local);

        Assert.Equal(60, fixture.Workspace.Preferences.SearchTimeBudgetSeconds);
        Assert.Equal(TimestampDisplay.Local, fixture.Workspace.Preferences.TimestampDisplay);
        Assert.Equal(60, fixture.Store.Value.SearchTimeBudgetSeconds);
        Assert.Equal(TimestampDisplay.Local, fixture.Store.Value.TimestampDisplay);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Failed_connection_can_recover_by_switching_to_a_valid_profile() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("connection-recovery");
        var broken = new InvestigationProfile(
            fixture.Id("broken-profile"),
            new ConnectionProfile("Broken daily audit profile", string.Empty, string.Empty));
        await fixture.Workspace.ApplyPreferencesAsync(fixture.Store.Value with
        {
            Profiles = [.. fixture.Store.Value.Profiles, broken]
        });

        Assert.True(await fixture.Workspace.SwitchProfileAsync(broken));
        await fixture.Workspace.ConnectAsync();
        Assert.False(fixture.Workspace.IsConnected);
        Assert.Contains("failed", fixture.Workspace.Status, StringComparison.OrdinalIgnoreCase);

        Assert.True(await fixture.Workspace.SwitchProfileAsync(fixture.AlternateProfile));
        await fixture.Workspace.ConnectAsync();
        Assert.True(fixture.Workspace.IsConnected, fixture.Workspace.HealthDetail);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Profile_warning_denial_preserves_session_and_approved_auto_connect_switches_profile() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("profile-warning");
        InvestigationProfile warned = fixture.AlternateProfile with { WarningMessage = "Use the audit namespace?" };
        await fixture.Workspace.ApplyPreferencesAsync(fixture.Store.Value with
        {
            AutoConnectOnSwitch = true,
            Profiles = [fixture.Store.Value.Profiles[0], warned]
        });

        fixture.Workspace.ConfirmWarning = _ => Task.FromResult(false);
        Assert.False(await fixture.Workspace.SwitchProfileAsync(warned));
        Assert.Equal(fixture.ProfileId, fixture.Workspace.Preferences.SelectedProfileId);
        Assert.True(fixture.Workspace.IsConnected);

        fixture.Workspace.ConfirmWarning = _ => Task.FromResult(true);
        Assert.True(await fixture.Workspace.SwitchProfileAsync(warned));
        Assert.Equal(warned.Id, fixture.Workspace.Preferences.SelectedProfileId);
        Assert.True(fixture.Workspace.IsConnected, fixture.Workspace.HealthDetail);
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Unqualified_message_id_OR_search_ignores_correlation_only_matches() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("search-message-or");
        string firstId = fixture.Id("message-a");
        string secondId = fixture.Id("message-b");
        await fixture.SendAsync(fixture.PrimaryQueue,
            new ServiceBusMessage("first") { MessageId = firstId, CorrelationId = fixture.Id("correlation-only") },
            new ServiceBusMessage("second") { MessageId = secondId, CorrelationId = fixture.Id("other-correlation") },
            new ServiceBusMessage("correlation-only") { MessageId = fixture.Id("message-c"), CorrelationId = firstId });

        string query = $"{MessageSearchQuery.QuoteLiteral(firstId)} OR {MessageSearchQuery.QuoteLiteral(secondId)}";
        await fixture.Workspace.Search.StartAsync(query, defaultMessageId: true);

        Assert.True(fixture.Workspace.Search.IsComplete, fixture.Workspace.Search.Status);
        Assert.True(fixture.Workspace.Search.DefaultMessageId);
        Assert.Equal([firstId, secondId], fixture.Workspace.Search.Messages.Select(row => row.MessageId).OrderBy(id => id));
    });

    [UiNavigationSmokeFact(Timeout = 90_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Search_scope_filters_queue_topic_subscription_and_clear_restores_all_matches() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await LiveFixture.CreateAsync("search-scope", extraQueue: true, topic: true);
        string correlation = fixture.Id("scope-correlation");
        await fixture.SendAsync(fixture.PrimaryQueue, new ServiceBusMessage("queue") { MessageId = fixture.Id("queue"), CorrelationId = correlation });
        await fixture.SendAsync(fixture.SecondaryQueue!, new ServiceBusMessage("other-queue") { MessageId = fixture.Id("other-queue"), CorrelationId = correlation });
        await fixture.SendTopicAsync(new ServiceBusMessage("topic") { MessageId = fixture.Id("topic"), CorrelationId = correlation });
        await fixture.Workspace.Search.StartAsync($"correlation:{correlation}");
        Assert.Equal(4, fixture.Workspace.Search.Messages.Count);

        EntityNode queue = fixture.Workspace.Search.Roots.Single(root => root.Name == "Queues")
            .Children.Single(node => node.Name == fixture.PrimaryQueue);
        EntityNode topic = fixture.Workspace.Search.Roots.Single(root => root.Name == "Topics")
            .Children.Single(node => node.Name == fixture.TopicName);
        EntityNode subscription = topic.Children.Single(node => node.Name == "audit-a");

        fixture.Workspace.Search.SelectScope(queue);
        Assert.Single(fixture.Workspace.Search.Messages);
        Assert.Equal(fixture.PrimaryQueue, fixture.Workspace.Search.Messages[0].Source);

        fixture.Workspace.Search.SelectScope(topic);
        Assert.Equal(2, fixture.Workspace.Search.Messages.Count);
        Assert.All(fixture.Workspace.Search.Messages, row => Assert.StartsWith(fixture.TopicName + "/", row.Source, StringComparison.Ordinal));

        fixture.Workspace.Search.SelectScope(subscription);
        Assert.Single(fixture.Workspace.Search.Messages);
        Assert.Equal(fixture.TopicName + "/audit-a", fixture.Workspace.Search.Messages[0].Source);

        fixture.Workspace.Search.SelectScope(null);
        Assert.Equal(4, fixture.Workspace.Search.Messages.Count);
    });

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("The audit operation did not reach the expected state.");
            await Task.Delay(50);
        }
    }

    private static int ReadScanned(string summary)
    {
        string scanned = summary.Split('·').Last().Trim();
        Assert.EndsWith("scanned", scanned, StringComparison.Ordinal);
        return int.Parse(scanned[..^"scanned".Length].Trim(), System.Globalization.NumberStyles.Integer | System.Globalization.NumberStyles.AllowThousands,
            System.Globalization.CultureInfo.CurrentCulture);
    }

    private sealed class LiveFixture : IAsyncDisposable
    {
        private readonly ServiceBusClient seedClient;
        private readonly ServiceBusAdministrationClient admin;
        private readonly string topicSubscriptionA = "audit-a";
        private readonly string topicSubscriptionB = "audit-b";
        private readonly DateTimeOffset deadline;
        private bool disposed;

        private LiveFixture(string prefix, string primaryQueue, string? secondaryQueue, string? topicName,
            InMemoryPreferencesStore store, InvestigationWorkspace workspace, ServiceBusClient seedClient,
            ServiceBusAdministrationClient admin, string profileId, InvestigationProfile alternateProfile,
            DateTimeOffset deadline)
        {
            Prefix = prefix;
            PrimaryQueue = primaryQueue;
            SecondaryQueue = secondaryQueue;
            TopicName = topicName;
            Store = store;
            Workspace = workspace;
            this.seedClient = seedClient;
            this.admin = admin;
            ProfileId = profileId;
            AlternateProfile = alternateProfile;
            this.deadline = deadline;
        }

        public string Prefix { get; }
        public string PrimaryQueue { get; }
        public string? SecondaryQueue { get; }
        public string? TopicName { get; }
        public string ProfileId { get; }
        public InvestigationProfile AlternateProfile { get; }
        public InMemoryPreferencesStore Store { get; }
        public InvestigationWorkspace Workspace { get; }

        public static async Task<LiveFixture> CreateAsync(string scenario, bool extraQueue = false, bool topic = false)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(70));
            await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(timeout.Token);
            string prefix = $"daily-{scenario}-{Guid.NewGuid():N}";
            string primary = prefix + "-q";
            string? secondary = extraQueue ? prefix + "-q2" : null;
            string? topicName = topic ? prefix + "-topic" : null;
            var admin = new ServiceBusAdministrationClient(ServiceBusUiSmokeEnvironment.AdminConnectionString);
            var createdQueues = new List<string>();
            bool createdTopic = false;
            bool createdSubscriptionA = false;
            bool createdSubscriptionB = false;
            InvestigationWorkspace? workspace = null;
            ServiceBusClient? seedClient = null;
            try
            {
                await admin.CreateQueueAsync(primary, timeout.Token);
                createdQueues.Add(primary);
                if (secondary is not null)
                {
                    await admin.CreateQueueAsync(secondary, timeout.Token);
                    createdQueues.Add(secondary);
                }
                if (topicName is not null)
                {
                    await admin.CreateTopicAsync(topicName, timeout.Token);
                    createdTopic = true;
                    await admin.CreateSubscriptionAsync(topicName, "audit-a", timeout.Token);
                    createdSubscriptionA = true;
                    await admin.CreateSubscriptionAsync(topicName, "audit-b", timeout.Token);
                    createdSubscriptionB = true;
                }

                string profileId = "profile-" + Guid.NewGuid().ToString("N");
                var profile = new InvestigationProfile(profileId, new ConnectionProfile(
                    "Daily audit emulator", ServiceBusUiSmokeEnvironment.RuntimeConnectionString,
                    ServiceBusUiSmokeEnvironment.AdminConnectionString));
                var alternate = new InvestigationProfile(profileId + "-alternate", profile.Connection with { Name = "Daily audit alternate" });
                var preferences = new WorkspacePreferences
                {
                    Profiles = [profile, alternate],
                    SelectedProfileId = profileId,
                    SelectedEntityPath = primary,
                    WasConnected = false,
                    SearchTimeBudgetSeconds = 10,
                    SearchDeliveryBudget = 10_000,
                    QueuePageSize = 100,
                    TopicPageSize = 100,
                    SubscriptionPageSize = 100
                };
                var store = new InMemoryPreferencesStore(preferences);
                var workflow = new BrokerConnectionWorkflow(
                    () => new DirectServiceBusClientFactory(),
                    factory => new InvestigationEntityBrowser(factory),
                    factory => new ServiceBusMessageService(factory));
                workspace = new InvestigationWorkspace(store, workflow);
                seedClient = new ServiceBusClient(ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
                await workspace.InitializeAsync();
                await workspace.ConnectAsync();
                Assert.True(workspace.IsConnected, workspace.HealthDetail);
                return new LiveFixture(prefix, primary, secondary, topicName, store, workspace, seedClient, admin, profileId, alternate,
                    DateTimeOffset.UtcNow.AddSeconds(75));
            }
            catch
            {
                try { if (workspace is not null) await workspace.DisposeAsync(); }
                catch { }
                try { if (seedClient is not null) await seedClient.DisposeAsync(); }
                catch { }
                await DeleteCreatedEntitiesAsync(admin, createdTopic ? topicName : null, createdSubscriptionA, createdSubscriptionB,
                    createdQueues);
                throw;
            }
        }

        public string Id(string suffix) => $"{Prefix}-{suffix}";

        public async Task SendAsync(string queue, params ServiceBusMessage[] messages)
        {
            await using ServiceBusSender sender = seedClient.CreateSender(queue);
            using CancellationTokenSource timeout = CreateOperationTimeout();
            for (int offset = 0; offset < messages.Length; offset += 100)
            {
                ServiceBusMessage[] batch = messages.Skip(offset).Take(100).ToArray();
                await sender.SendMessagesAsync(batch, timeout.Token);
            }
        }

        public async Task SendTopicAsync(params ServiceBusMessage[] messages)
        {
            if (TopicName is null) throw new InvalidOperationException("This fixture has no topic.");
            await using ServiceBusSender sender = seedClient.CreateSender(TopicName);
            using CancellationTokenSource timeout = CreateOperationTimeout();
            for (int offset = 0; offset < messages.Length; offset += 100)
            {
                ServiceBusMessage[] batch = messages.Skip(offset).Take(100).ToArray();
                await sender.SendMessagesAsync(batch, timeout.Token);
            }
        }

        public async Task SelectPrimaryAsync(bool deadLetter = false)
        {
            EntityNode node = Workspace.Browse.AllEntities().Single(entity => entity.Path == PrimaryQueue);
            await Workspace.Browse.SelectAsync(node, deadLetter);
        }

        public async Task MoveFirstToDeadLetterAsync()
        {
            await using ServiceBusReceiver receiver = seedClient.CreateReceiver(PrimaryQueue);
            using CancellationTokenSource timeout = CreateOperationTimeout();
            ServiceBusReceivedMessage message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), timeout.Token)
                ?? throw new InvalidOperationException("The seeded message was not available for DLQ setup.");
            await receiver.DeadLetterMessageAsync(message, "DailyAudit", "Synthetic draft fixture", timeout.Token);
        }

        public async Task ApplySearchBudgetAsync(int deliveries, int seconds)
        {
            Store.Value = Store.Value with { SearchDeliveryBudget = deliveries, SearchTimeBudgetSeconds = seconds };
            await Workspace.ApplyPreferencesAsync(Store.Value);
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            try { await Workspace.DisposeAsync(); }
            catch { }
            try { await seedClient.DisposeAsync(); }
            catch { }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (TopicName is not null)
            {
                try { await admin.DeleteSubscriptionAsync(TopicName, topicSubscriptionA, timeout.Token); } catch { }
                try { await admin.DeleteSubscriptionAsync(TopicName, topicSubscriptionB, timeout.Token); } catch { }
                try { await admin.DeleteTopicAsync(TopicName, timeout.Token); } catch { }
            }
            if (SecondaryQueue is not null) try { await admin.DeleteQueueAsync(SecondaryQueue, timeout.Token); } catch { }
            try { await admin.DeleteQueueAsync(PrimaryQueue, timeout.Token); } catch { }
        }

        private CancellationTokenSource CreateOperationTimeout()
        {
            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            return new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1));
        }

        private static async Task DeleteCreatedEntitiesAsync(ServiceBusAdministrationClient admin, string? topic,
            bool subscriptionA, bool subscriptionB, IReadOnlyList<string> queues)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (topic is not null)
            {
                if (subscriptionA) try { await admin.DeleteSubscriptionAsync(topic, "audit-a", timeout.Token); } catch { }
                if (subscriptionB) try { await admin.DeleteSubscriptionAsync(topic, "audit-b", timeout.Token); } catch { }
                try { await admin.DeleteTopicAsync(topic, timeout.Token); } catch { }
            }
            foreach (string queue in queues)
            {
                try { await admin.DeleteQueueAsync(queue, timeout.Token); } catch { }
            }
        }
    }

    private sealed class InMemoryPreferencesStore(WorkspacePreferences initial) : IWorkspacePreferencesStore
    {
        public WorkspacePreferences Value { get; set; } = initial;
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(Value));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
        {
            Value = preferences;
            return Task.CompletedTask;
        }
    }
}
