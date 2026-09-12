using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.UiSmoke.Tests;

public sealed class InvestigationDeleteEndToEndTests
{
    [UiNavigationSmokeFact(Timeout = 180_000)]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Real_ui_replay_and_typed_deletion_remove_saved_family_only_after_last_dlq_copy()
    {
        string run = Guid.NewGuid().ToString("N");
        string queue = $"ui-delete-{run}";
        string originalId = $"{run}-original";
        string profilePath = Path.Combine(AppContext.BaseDirectory, "ui-smoke-profiles", run, "connection-profiles.json");
        var store = new ProtectedWorkspacePreferencesStore(profilePath);
        var admin = new ServiceBusAdministrationClient(ServiceBusUiSmokeEnvironment.AdminConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        Application? application = null;
        UIA3Automation? automation = null;
        bool created = false;
        Exception? failure = null;
        try
        {
            await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(timeout.Token);
            await admin.CreateQueueAsync(queue, timeout.Token);
            created = true;
            await using var client = new ServiceBusClient(ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
            await using var sender = client.CreateSender(queue);
            await using var active = client.CreateReceiver(queue);
            await using var dlq = client.CreateReceiver(queue, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
            await sender.SendMessageAsync(new ServiceBusMessage("{\"event\":\"original\"}") { MessageId = originalId }, timeout.Token);
            await active.DeadLetterMessageAsync(await ReceiveAsync(active, timeout.Token), cancellationToken: timeout.Token);
            await sender.SendMessageAsync(new ServiceBusMessage("unrelated DLQ") { MessageId = $"{run}-dlq-neighbor" }, timeout.Token);
            await active.DeadLetterMessageAsync(await ReceiveAsync(active, timeout.Token), cancellationToken: timeout.Token);
            var before = await PeekAsync(dlq, timeout.Token);
            var original = Assert.Single(before, message => message.MessageId == originalId);
            var neighbor = Assert.Single(before, message => message.MessageId != originalId);
            await store.SaveAsync(new WorkspacePreferences
            {
                Profiles = [new InvestigationProfile(run, new ConnectionProfile("Delete UI emulator",
                    ServiceBusUiSmokeEnvironment.RuntimeConnectionString, ServiceBusUiSmokeEnvironment.AdminConnectionString))],
                SelectedProfileId = run, SelectedEntityPath = queue, WasConnected = true, DeadLetter = true,
                CloseToTray = false, NotificationsEnabled = false, LogExpanded = true
            }, timeout.Token);
            string executable = WpfAppPath.Resolve();
            Assert.True(File.Exists(executable), $"Build the WPF app before running UI smoke tests. Missing: {executable}");
            application = Application.Launch(executable, $"--profile-store-path \"{profilePath}\"");
            automation = new UIA3Automation();
            var main = WaitWindow(application, automation, "ConnectionButton", timeout.Token);
            SelectMessage(main, originalId, timeout.Token);
            Invoke(main, "ReplayButton", timeout.Token);
            var copy = await ReceiveAsync(active, timeout.Token);
            Assert.NotEqual(originalId, copy.MessageId);
            Assert.Equal(original.Body.ToArray(), copy.Body.ToArray());
            Assert.Equal(1L, Assert.IsType<long>(copy.ApplicationProperties[ReplayLineage.AttemptProperty]));
            var persisted = await store.LoadAsync(timeout.Token);
            Assert.Null(persisted.Warning);
            var family = Assert.Single(persisted.Preferences.ReplayFamilies[run]);
            Assert.Equal(originalId, family.OriginalMessageId);
            Assert.Equal(1L, family.LastAttempt);
            Assert.Equal(family.FamilyId.ToString("N"), copy.ApplicationProperties[ReplayLineage.FamilyProperty]);
            Assert.Equal(original.SequenceNumber, Assert.Single(await PeekAsync(dlq, timeout.Token), message => message.MessageId == originalId).SequenceNumber);

            // Only this explicit fixture consumer creates the replay's DLQ delivery.
            await active.DeadLetterMessageAsync(copy, cancellationToken: timeout.Token);
            await sender.SendMessageAsync(new ServiceBusMessage("unrelated Active") { MessageId = $"{run}-active-neighbor" }, timeout.Token);
            var activeBefore = Assert.Single(await PeekAsync(active, timeout.Token));
            Invoke(main, "RefreshButton", timeout.Token);
            WaitElement(() => ById(main, "MessageGrid")?.FindFirstDescendant(cf => cf.ByText(copy.MessageId)), "replay copy in DLQ grid", timeout.Token);

            // An incomplete confirmation must neither enable deletion nor remove a broker delivery.
            SelectMessage(main, originalId, timeout.Token);
            Invoke(main, "DeleteButton", timeout.Token);
            var dialog = WaitWindow(application, automation, "DeleteConfirmationInput", timeout.Token);
            Assert.False(ById(dialog, "ConfirmDeleteButton")!.IsEnabled);
            ById(dialog, "DeleteConfirmationInput")!.Patterns.Value.Pattern.SetValue("DELET");
            Assert.False(ById(dialog, "ConfirmDeleteButton")!.IsEnabled);
            Invoke(dialog, "CancelDeleteButton", timeout.Token);
            Assert.Equal(3, (await PeekAsync(dlq, timeout.Token)).Count);

            DeleteThroughUi(application, automation, main, originalId, timeout.Token);
            await WaitAsync(async () => !(await PeekAsync(dlq, timeout.Token)).Any(message => message.MessageId == originalId), "original broker deletion", timeout.Token);
            var afterFirst = await PeekAsync(dlq, timeout.Token);
            Assert.Single(afterFirst, message => message.MessageId == copy.MessageId);
            AssertPayload(neighbor, Assert.Single(afterFirst, message => message.MessageId == neighbor.MessageId));
            await WaitAsync(async () =>
            {
                var retained = await store.LoadAsync(timeout.Token);
                Assert.Null(retained.Warning);
                var pending = Assert.Single(retained.Preferences.ReplayFamilies[run]);
                Assert.Equal(family.FamilyId, pending.FamilyId);
                return pending.CleanupNamespace is not null;
            }, "saved deletion bookkeeping retaining the replay family", timeout.Token);
            Assert.Single(await PeekAsync(dlq, timeout.Token), message => message.MessageId == copy.MessageId);

            DeleteThroughUi(application, automation, main, copy.MessageId, timeout.Token);
            await WaitAsync(async () => !(await PeekAsync(dlq, timeout.Token)).Any(message => message.MessageId == copy.MessageId), "replay broker deletion", timeout.Token);
            await WaitAsync(async () =>
            {
                var saved = await store.LoadAsync(timeout.Token);
                Assert.Null(saved.Warning);
                return !saved.Preferences.ReplayFamilies.TryGetValue(run, out var families) || families.Count == 0;
            }, "saved replay family cleanup", timeout.Token);
            AssertPayload(neighbor, Assert.Single(await PeekAsync(dlq, timeout.Token)));
            var activeAfter = Assert.Single(await PeekAsync(active, timeout.Token));
            AssertPayload(activeBefore, activeAfter);
            Assert.Equal(activeBefore.DeliveryCount, activeAfter.DeliveryCount);
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            try
            {
                if (application is not null && !application.HasExited)
                {
                    try { application.Kill(); }
                    catch when (application.HasExited) { }
                }
            }
            finally
            {
                try { automation?.Dispose(); }
                finally
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try { if (created) await admin.DeleteQueueAsync(queue, cleanup.Token); }
                    catch (Exception cleanupFailure)
                    {
                        if (failure is not null) throw new AggregateException("Delete UI proof and fixture cleanup failed.", failure, cleanupFailure);
                        throw;
                    }
                    finally
                    {
                        if (File.Exists(profilePath)) File.Delete(profilePath);
                        string directory = Path.GetDirectoryName(profilePath)!;
                        if (Directory.Exists(directory)) Directory.Delete(directory);
                    }
                }
            }
        }
    }

    private static async Task<ServiceBusReceivedMessage> ReceiveAsync(ServiceBusReceiver receiver, CancellationToken token) =>
        Assert.IsType<ServiceBusReceivedMessage>(await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), token));

    private static Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekAsync(ServiceBusReceiver receiver, CancellationToken token) =>
        receiver.PeekMessagesAsync(10, fromSequenceNumber: 0, cancellationToken: token);

    private static void AssertPayload(ServiceBusReceivedMessage before, ServiceBusReceivedMessage after)
    {
        Assert.Equal(before.MessageId, after.MessageId);
        Assert.Equal(before.SequenceNumber, after.SequenceNumber);
        Assert.Equal(before.Body.ToArray(), after.Body.ToArray());
    }

    private static void SelectMessage(Window main, string messageId, CancellationToken token)
    {
        var text = WaitElement(() => ById(main, "MessageGrid")?.FindFirstDescendant(cf => cf.ByText(messageId)), messageId, token);
        var row = text;
        while (row.ControlType != ControlType.DataItem)
            row = row.Parent ?? throw new InvalidOperationException("Message text has no DataGrid row ancestor.");
        row.Patterns.SelectionItem.Pattern.Select();
        WaitElement(() => ById(main, "InspectorMessageId") is { } id && id.Name == messageId ? id : null, "focused message", token);
    }

    private static void DeleteThroughUi(Application application, UIA3Automation automation, Window main, string messageId, CancellationToken token)
    {
        SelectMessage(main, messageId, token);
        Invoke(main, "DeleteButton", token);
        var dialog = WaitWindow(application, automation, "DeleteConfirmationInput", token);
        ById(dialog, "DeleteConfirmationInput")!.Patterns.Value.Pattern.SetValue("DELETE");
        Invoke(dialog, "ConfirmDeleteButton", token);
        WaitElement(() => ById(main, "DeleteButton") is { } button && button.Name != "Cancel delete"
                && ById(main, "MessageGrid") is { } grid && grid.FindFirstDescendant(cf => cf.ByText(messageId)) is null
                ? button : null,
            $"deleted row {messageId} removed and delete operation idle", token);
    }

    private static AutomationElement? ById(AutomationElement root, string id) => root.FindFirstDescendant(cf => cf.ByAutomationId(id));

    private static Window WaitWindow(Application application, UIA3Automation automation, string id, CancellationToken token) =>
        WaitElement(() => application.GetAllTopLevelWindows(automation).FirstOrDefault(window => !window.IsOffscreen && ById(window, id) is not null), id, token).AsWindow();

    private static void Invoke(Window window, string id, CancellationToken token) =>
        WaitElement(() => ById(window, id) is { IsEnabled: true } element ? element : null, $"enabled {id}", token).Patterns.Invoke.Pattern.Invoke();

    private static AutomationElement WaitElement(Func<AutomationElement?> find, string description, CancellationToken token)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(35);
        while (DateTimeOffset.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (find() is { } found) return found;
            Thread.Sleep(200);
        }
        throw new InvalidOperationException($"Timed out waiting for {description}.");
    }

    private static async Task WaitAsync(Func<Task<bool>> condition, string description, CancellationToken token)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(35);
        while (DateTimeOffset.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (await condition()) return;
            await Task.Delay(250, token);
        }
        throw new InvalidOperationException($"Timed out waiting for {description}.");
    }
}
