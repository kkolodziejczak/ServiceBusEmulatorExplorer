using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ServiceBusEmulatorExplorer.App.Agent;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class AgentAccessTests
{
    private const string Token = "test-token-value";

    [Theory]
    [InlineData("POST", "/mcp", null, "Bearer test-token-value", null)]
    [InlineData("POST", "/mcp", "http://127.0.0.1:47811", "Bearer test-token-value", null)]
    [InlineData("POST", "/mcp", "http://localhost:47811", "bearer test-token-value", null)]
    [InlineData("POST", "/mcp", null, null, 401)]
    [InlineData("POST", "/mcp", null, "Bearer wrong", 401)]
    [InlineData("POST", "/mcp", "https://evil.example", "Bearer test-token-value", 403)]
    [InlineData("POST", "/mcp", "http://localhost.attacker.example:47811", "Bearer test-token-value", 403)]
    [InlineData("POST", "/mcp", "http://127.0.0.1:9999", "Bearer test-token-value", 403)]
    [InlineData("POST", "/mcp", "null", "Bearer test-token-value", 403)]
    [InlineData("GET", "/mcp", null, "Bearer test-token-value", 405)]
    [InlineData("POST", "/other", null, "Bearer test-token-value", 404)]
    public void Guard_admits_only_authenticated_local_posts(string method, string path, string? origin, string? authorization, int? expected)
    {
        Assert.Equal(expected, AgentRequestGuard.Reject(new AgentRequest(method, path, origin, authorization, 10, FromLoopback: true), 47811, Token));
    }

    [Fact]
    public void Guard_rejects_everything_while_no_token_exists()
    {
        Assert.Equal(401, AgentRequestGuard.Reject(new AgentRequest("POST", "/mcp", null, "Bearer ", 10, FromLoopback: true), 47811, ""));
    }

    [Fact]
    public void Guard_rejects_remote_peers_even_with_a_valid_token()
    {
        Assert.Equal(403, AgentRequestGuard.Reject(
            new AgentRequest("POST", "/mcp", null, "Bearer " + Token, 10, FromLoopback: false), 47811, Token));
    }

    [Fact]
    public void Guard_rejects_oversized_bodies()
    {
        Assert.Equal(413, AgentRequestGuard.Reject(
            new AgentRequest("POST", "/mcp", null, "Bearer " + Token, AgentRequestGuard.MaxRequestBytes + 1, FromLoopback: true), 47811, Token));
    }

    [Fact]
    public void Journal_returns_only_newer_arrivals_and_reports_resets_and_eviction()
    {
        var journal = new AgentArrivalJournal(capacity: 3);
        journal.Append(Delivery(1));
        journal.Append(Delivery(2));
        var first = journal.Read(null, 10);
        Assert.Equal([1L, 2L], first.Arrivals.Select(arrival => arrival.Delivery.Message.SequenceNumber));
        Assert.False(first.Expired);

        journal.Append(Delivery(3));
        var second = journal.Read(first.NextCursor, 10);
        Assert.Equal([3L], second.Arrivals.Select(arrival => arrival.Delivery.Message.SequenceNumber));
        Assert.Empty(journal.Read(second.NextCursor, 10).Arrivals);

        foreach (long sequence in new long[] { 4, 5, 6, 7 }) journal.Append(Delivery(sequence));
        var evicted = journal.Read(second.NextCursor, 10);
        Assert.True(evicted.Expired);
        Assert.Equal([5L, 6L, 7L], evicted.Arrivals.Select(arrival => arrival.Delivery.Message.SequenceNumber));

        journal.Clear();
        var reset = journal.Read(evicted.NextCursor, 10);
        Assert.True(reset.Expired);
        Assert.Empty(reset.Arrivals);
        Assert.True(journal.Read("not-a-cursor", 10).Expired);
    }

    [Fact]
    public void Journal_pages_with_has_more()
    {
        var journal = new AgentArrivalJournal();
        for (long sequence = 1; sequence <= 5; sequence++) journal.Append(Delivery(sequence));
        var page = journal.Read(null, 2);
        Assert.True(page.HasMore);
        Assert.Equal(2, page.Arrivals.Count);
        Assert.Equal([3L, 4L], journal.Read(page.NextCursor, 2).Arrivals.Select(arrival => arrival.Delivery.Message.SequenceNumber));
    }

    [Fact]
    public void Snippets_mask_the_token_for_display_and_include_it_for_copy()
    {
        foreach (var (client, _) in AgentSnippets.Clients)
        {
            var copied = AgentSnippets.Build(client, "http://127.0.0.1:47811/mcp", Token);
            var shown = AgentSnippets.Build(client, "http://127.0.0.1:47811/mcp", AgentSnippets.MaskedToken);
            Assert.Contains("http://127.0.0.1:47811/mcp", copied.Text);
            Assert.Contains("Bearer " + Token, copied.Text);
            Assert.DoesNotContain(Token, shown.Text);
        }
    }

    [Fact]
    public async Task Blocked_profile_exposes_nothing_and_records_no_arrivals()
    {
        var blocked = Profile("prod", "Production") with { AllowAgentAccess = false };
        await using var workspace = Workspace(blocked, new FakeMessages());
        await workspace.InitializeAsync();
        await workspace.ConnectAsync();

        var snapshot = workspace.CaptureAgentSnapshot();
        Assert.False(snapshot.Allowed);
        Assert.Equal("", snapshot.ProfileName);
        Assert.Null(snapshot.Discovery);
        await Assert.ThrowsAsync<AgentAccessBlockedException>(() =>
            workspace.PeekForAgentAsync("orders", MessageBucket.DeadLetter, 5, null, CancellationToken.None));
        Assert.Throws<AgentAccessBlockedException>(() => workspace.ReadAgentArrivals(null, 10));
    }

    [Fact]
    public async Task Allowed_profile_snapshot_never_carries_connection_strings()
    {
        var messages = new FakeMessages { Messages = [Message(7, deadLetterReason: "MaxDeliveryCountExceeded")] };
        await using var workspace = Workspace(Profile("local", "Local"), messages);
        await workspace.InitializeAsync();
        await workspace.ConnectAsync();

        var snapshot = workspace.CaptureAgentSnapshot();
        Assert.True(snapshot.Allowed);
        Assert.Equal("Local", snapshot.ProfileName);
        string json = System.Text.Json.JsonSerializer.Serialize(AgentTools.AppState(snapshot));
        Assert.DoesNotContain("runtime-local", json);
        Assert.DoesNotContain("admin-local", json);

        var peeked = await workspace.PeekForAgentAsync("orders", MessageBucket.DeadLetter, 5, null, CancellationToken.None);
        var delivery = Assert.Single(peeked);
        Assert.Equal(MessageBucket.DeadLetter, delivery.Identity.Bucket);
        Assert.Equal("MaxDeliveryCountExceeded", delivery.Message.SystemProperties["DeadLetterReason"]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workspace.PeekForAgentAsync("missing", MessageBucket.Active, 5, null, CancellationToken.None));
    }

    [Fact]
    public async Task Peek_result_is_discarded_when_the_user_switches_to_a_blocked_profile_meanwhile()
    {
        var messages = new FakeMessages { Messages = [Message(1)] };
        var blocked = Profile("prod", "Production") with { AllowAgentAccess = false };
        await using var workspace = Workspace(Profile("local", "Local"), messages, blocked);
        await workspace.InitializeAsync();
        await workspace.ConnectAsync();
        messages.Hold = true;

        var peek = workspace.PeekForAgentAsync("orders", MessageBucket.Active, 5, null, CancellationToken.None);
        await messages.PeekStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await workspace.SwitchProfileAsync(blocked));
        messages.Release.TrySetResult(true);

        await Assert.ThrowsAnyAsync<Exception>(() => peek);
        Assert.False(workspace.CaptureAgentSnapshot().Allowed);
    }

    [Fact]
    public async Task Late_watch_arrival_from_an_invalidated_connection_is_not_recorded()
    {
        await using var workspace = Workspace(Profile("local", "Local"), new FakeMessages());
        await workspace.InitializeAsync();
        await workspace.ConnectAsync();
        long first = workspace.ConnectionGeneration;
        workspace.RecordAgentArrival(Delivery(1, generation: first));
        Assert.Single(workspace.ReadAgentArrivals(null, 10).Arrivals);

        await workspace.DisconnectAsync();
        workspace.RecordAgentArrival(Delivery(2, generation: first));
        await workspace.ConnectAsync();
        workspace.RecordAgentArrival(Delivery(3, generation: first));

        Assert.Empty(workspace.ReadAgentArrivals(null, 10).Arrivals);
        workspace.RecordAgentArrival(Delivery(4, generation: workspace.ConnectionGeneration));
        Assert.Equal([4L], workspace.ReadAgentArrivals(null, 10).Arrivals.Select(arrival => arrival.Delivery.Message.SequenceNumber));
    }

    [Fact]
    public async Task Store_loads_files_without_agent_fields_as_off_and_profiles_allowed()
    {
        string path = TempFile();
        var store = new ProtectedWorkspacePreferencesStore(path);
        await store.SaveAsync(new WorkspacePreferences
        {
            Profiles = [Profile("local", "Local")], SelectedProfileId = "local",
            AgentAccessEnabled = true, AgentAccessPort = 50123, AgentAccessToken = "secret-token-value-123"
        }, CancellationToken.None);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        RemoveAgentFields(root);
        await File.WriteAllTextAsync(path, root.ToJsonString());

        var loaded = await new ProtectedWorkspacePreferencesStore(path).LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.False(loaded.Preferences.AgentAccessEnabled);
        Assert.Equal(AgentAccessDefaults.Port, loaded.Preferences.AgentAccessPort);
        Assert.Equal("", loaded.Preferences.AgentAccessToken);
        Assert.True(Assert.Single(loaded.Preferences.Profiles).AllowAgentAccess);
    }

    [Fact]
    public async Task Store_round_trips_agent_settings_and_protects_the_token()
    {
        string path = TempFile();
        var store = new ProtectedWorkspacePreferencesStore(path);
        await store.SaveAsync(new WorkspacePreferences
        {
            Profiles = [Profile("local", "Local"), Profile("prod", "Production") with { AllowAgentAccess = false }],
            SelectedProfileId = "prod",
            AgentAccessEnabled = true, AgentAccessPort = 50123, AgentAccessToken = "secret-token-value-123"
        }, CancellationToken.None);

        Assert.DoesNotContain("secret-token-value-123", await File.ReadAllTextAsync(path));
        var loaded = (await store.LoadAsync(CancellationToken.None)).Preferences;
        Assert.True(loaded.AgentAccessEnabled);
        Assert.Equal(50123, loaded.AgentAccessPort);
        Assert.Equal("secret-token-value-123", loaded.AgentAccessToken);
        Assert.Equal([true, false], loaded.Profiles.Select(profile => profile.AllowAgentAccess));
    }

    [Fact]
    public async Task Store_turns_agent_access_off_when_the_token_cannot_be_read()
    {
        string path = TempFile();
        await new ProtectedWorkspacePreferencesStore(path).SaveAsync(new WorkspacePreferences
        {
            AgentAccessEnabled = true, AgentAccessToken = "secret-token-value-123"
        }, CancellationToken.None);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var settings = root.First(pair => pair.Key.Equals("settings", StringComparison.OrdinalIgnoreCase)).Value!.AsObject();
        settings[settings.First(pair => pair.Key.Equals("agentAccessToken", StringComparison.OrdinalIgnoreCase)).Key] = "bm90LXByb3RlY3RlZA==";
        await File.WriteAllTextAsync(path, root.ToJsonString());

        var loaded = await new ProtectedWorkspacePreferencesStore(path).LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.False(loaded.Preferences.AgentAccessEnabled);
        Assert.Equal("", loaded.Preferences.AgentAccessToken);
    }

    [Fact]
    public async Task Host_serves_read_only_tools_to_an_mcp_client_and_enforces_the_token()
    {
        var source = new FakeSource { Snapshot = new AgentSnapshot(true, DateTimeOffset.UtcNow, "Local", true, true, "Connected",
            Focused: Delivery(9, MessageBucket.DeadLetter, "MaxDeliveryCountExceeded")) };
        int port = FreePort();
        await using var host = new AgentAccessHost(source, () => Token);
        await host.ApplyAsync(true, port);
        Assert.Equal(AgentAccessState.Listening, host.Status.State);

        using (var http = new HttpClient())
        {
            var anonymous = await http.PostAsync($"http://127.0.0.1:{port}/mcp", new StringContent("{}"));
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        await using (var client = await Connect(port, Token))
        {
            var tools = await client.ListToolsAsync();
            Assert.Equal(["get_app_state", "get_focused_message", "get_watch_arrivals", "list_entities", "peek_messages"],
                tools.Select(tool => tool.Name).Order());
            Assert.All(tools, tool => Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint));

            // Optional arguments may be omitted (they must not be "required" in the tool schema).
            var peek = await client.CallToolAsync("peek_messages", new Dictionary<string, object?> { ["entity"] = "orders", ["bucket"] = "deadLetter" });
            Assert.NotEqual(true, peek.IsError);
            var arrivals = await client.CallToolAsync("get_watch_arrivals");
            Assert.NotEqual(true, arrivals.IsError);
            string peekRequired = tools.Single(tool => tool.Name == "peek_messages").ProtocolTool.InputSchema.GetProperty("required").ToString();
            Assert.DoesNotContain("take", peekRequired);
            Assert.DoesNotContain("fromSequenceNumber", peekRequired);

            var focused = await client.CallToolAsync("get_focused_message");
            string text = Text(focused);
            Assert.NotEqual(true, focused.IsError);
            Assert.Contains("MaxDeliveryCountExceeded", text);
            Assert.NotNull(host.Status.LastRequestUtc);

            source.Snapshot = AgentSnapshot.Blocked(DateTimeOffset.UtcNow);
            string blockedState = Text(await client.CallToolAsync("get_app_state"));
            Assert.Contains("blocked", blockedState);
            Assert.DoesNotContain("Local", blockedState);
            var blockedFocus = await client.CallToolAsync("get_focused_message");
            Assert.True(blockedFocus.IsError);
            Assert.Contains(AgentAccessBlockedException.UserMessage, Text(blockedFocus));
        }

        await host.ApplyAsync(false, port);
        Assert.Equal(AgentAccessState.Off, host.Status.State);
        await Assert.ThrowsAnyAsync<Exception>(() => Connect(port, Token));
    }

    [Fact]
    public async Task Second_host_on_the_same_port_reports_failure()
    {
        int port = FreePort();
        await using var first = new AgentAccessHost(new FakeSource(), () => Token);
        await using var second = new AgentAccessHost(new FakeSource(), () => Token);
        await first.ApplyAsync(true, port);
        await second.ApplyAsync(true, port);

        Assert.Equal(AgentAccessState.Listening, first.Status.State);
        Assert.Equal(AgentAccessState.Failed, second.Status.State);
        Assert.Contains(port.ToString(), second.Status.Detail);
    }

    private static async Task<McpClient> Connect(int port, string token) =>
        await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"http://127.0.0.1:{port}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
        }, NullLoggerFactory.Instance), cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static void RemoveAgentFields(JsonObject root)
    {
        foreach (var (key, value) in root.ToArray())
        {
            if (value is JsonObject child) RemoveAgentFields(child);
            else if (value is JsonArray array) foreach (var item in array.OfType<JsonObject>()) RemoveAgentFields(item);
            if (key.Contains("agentaccess", StringComparison.OrdinalIgnoreCase)) root.Remove(key);
        }
    }

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "sbe-agent-tests", Guid.NewGuid().ToString("N"), "profiles.json");

    private static InvestigationProfile Profile(string id, string name) =>
        new(id, new ConnectionProfile(name, $"runtime-{id}", $"admin-{id}"));

    private static InvestigationWorkspace Workspace(InvestigationProfile selected, FakeMessages messages, params InvestigationProfile[] others)
    {
        var preferences = new WorkspacePreferences { Profiles = [selected, .. others], SelectedProfileId = selected.Id, WasConnected = false };
        var snapshot = new EntityDiscoverySnapshot(
            [new EntityObservation(new DiscoveredEntity(EntityKind.Queue, "orders", null,
                new EntityMetadata("orders", "Active", null, null, TimeSpan.FromMinutes(1), 10, null, false, false)),
                new EntityCountObservation(new(null, CountAvailability.Unavailable), new(null, CountAvailability.Unavailable), new(null, CountAvailability.Unavailable)))],
            DateTimeOffset.UtcNow, IsComplete: true, Issues: []);
        return new InvestigationWorkspace(new MemoryStore(preferences),
            new BrokerConnectionWorkflow(() => new FakeFactory(), _ => new FakeBrowser(snapshot), _ => messages));
    }

    private static ExplorerMessage Message(long sequence, string? deadLetterReason = null) =>
        MessageProjection.Create(ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{\"orderId\":1}"), messageId: $"m-{sequence}", sequenceNumber: sequence,
            properties: deadLetterReason is null ? null : new Dictionary<string, object> { ["DeadLetterReason"] = deadLetterReason }));

    private static MessageDelivery Delivery(long sequence, MessageBucket bucket = MessageBucket.Active, string? deadLetterReason = null,
        long generation = 1) =>
        new(new DeliveryIdentity(generation, new EntityAddress(EntityKind.Queue, "orders"), bucket, sequence), Message(sequence, deadLetterReason));

    private sealed class FakeSource : IAgentStateSource
    {
        public AgentSnapshot Snapshot { get; set; } = AgentSnapshot.Blocked(DateTimeOffset.UtcNow);
        public Task<AgentSnapshot> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot);
        public Task<IReadOnlyList<MessageDelivery>> PeekAsync(string entityPath, MessageBucket bucket, int take, long? fromSequenceNumber,
            CancellationToken cancellationToken) => Snapshot.Allowed
                ? Task.FromResult<IReadOnlyList<MessageDelivery>>([])
                : throw new AgentAccessBlockedException();
        public Task<AgentArrivalPage> ReadArrivalsAsync(string? cursor, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentArrivalJournal().Read(cursor, limit));
    }

    private sealed class MemoryStore(WorkspacePreferences initial) : IWorkspacePreferencesStore
    {
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(initial));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeFactory : IServiceBusClientFactory
    {
        public Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient AdministrationClient => null!;
        public ServiceBusClient RuntimeClient => null!;
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        public IReadOnlyList<ExplorerMessage> Messages { get; init; } = [];
        /// <summary>Holds every later peek until released; set after the workspace's own connect-time browse.</summary>
        public bool Hold { get; set; }
        public TaskCompletionSource<bool> PeekStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket, int take,
            long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            if (Hold)
            {
                PeekStarted.TrySetResult(true);
                await Release.Task;
            }
            return Messages;
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
