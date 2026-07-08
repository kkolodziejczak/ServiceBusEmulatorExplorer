using Azure.Messaging.ServiceBus.Administration;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.UiSmoke.Tests;

public sealed class MainWindowSmokeTests
{
    [UiSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public void App_launches_main_window_and_exposes_shell_commands()
    {
        string executablePath = WpfAppPath.Resolve();
        Assert.True(File.Exists(executablePath), $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

        using Application application = Application.Launch(executablePath);
        using var automation = new UIA3Automation();

        Window window = application.GetMainWindow(automation, TimeSpan.FromSeconds(10))
            ?? throw new InvalidOperationException("Main window did not appear within 10 seconds.");

        Assert.Equal("Service Bus Emulator Explorer", window.Title);
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("ConnectButton")));
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("DisconnectButton")));
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("RefreshButton")));
        Assert.DoesNotContain(
            application.GetAllTopLevelWindows(automation),
            topLevelWindow => topLevelWindow.Title.Contains("Exception", StringComparison.OrdinalIgnoreCase));

        window.Close();
    }

    [UiNavigationSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task App_loads_seeded_namespace_tree_and_selection_updates_detail_header()
    {
        string queueName = CreateEntityName("queue");
        string topicName = CreateEntityName("topic");
        string subscriptionName = "sub";

        var adminClient = new ServiceBusAdministrationClient(ServiceBusUiSmokeEnvironment.AdminConnectionString);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await SeedEntitiesAsync(adminClient, queueName, topicName, subscriptionName, testTimeout.Token);

        try
        {
            string executablePath = WpfAppPath.Resolve();
            Assert.True(File.Exists(executablePath), $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

            using Application application = Application.Launch(executablePath);
            using var automation = new UIA3Automation();

            Window window = application.GetMainWindow(automation, TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException("Main window did not appear within 10 seconds.");

            SetText(window, "ProfileNameTextBox", "UI smoke emulator");
            SetText(window, "RuntimeConnectionStringTextBox", ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
            SetText(window, "AdministrationConnectionStringTextBox", ServiceBusUiSmokeEnvironment.AdminConnectionString);
            window.FindFirstDescendant(cf => cf.ByAutomationId("ConnectButton"))?.AsButton().Click();

            AutomationElement queueElement = WaitForText(window, queueName, TimeSpan.FromSeconds(30));
            queueElement.Click();

            AutomationElement selectedTitle = WaitForAutomationName(window, "SelectedEntityTitleText", queueName, TimeSpan.FromSeconds(10));
            AutomationElement selectedKind = WaitForAutomationName(window, "SelectedEntityKindText", "Queue", TimeSpan.FromSeconds(10));
            AutomationElement selectedPath = WaitForAutomationName(window, "SelectedEntityPathText", queueName, TimeSpan.FromSeconds(10));
            AutomationElement selectedCounts = WaitForAutomationName(window, "SelectedEntityCountsText", "Active 0 | DLQ 0 | Scheduled 0 | Total 0", TimeSpan.FromSeconds(10));
            AutomationElement selectedMetadata = WaitForAutomationId(window, "SelectedEntityMetadataText", TimeSpan.FromSeconds(10));

            Assert.Equal(queueName, selectedTitle.Name);
            Assert.Equal("Queue", selectedKind.Name);
            Assert.Equal(queueName, selectedPath.Name);
            Assert.Equal("Active 0 | DLQ 0 | Scheduled 0 | Total 0", selectedCounts.Name);
            Assert.Contains("Status", selectedMetadata.Name);
            Assert.NotNull(window.FindFirstDescendant(cf => cf.ByText(topicName)));

            AutomationElement subscriptionElement = WaitForText(window, subscriptionName, TimeSpan.FromSeconds(10));
            subscriptionElement.Click();

            WaitForAutomationName(window, "SelectedEntityTitleText", subscriptionName, TimeSpan.FromSeconds(10));
            WaitForAutomationName(window, "SelectedEntityKindText", "Subscription", TimeSpan.FromSeconds(10));
            WaitForAutomationName(window, "SelectedEntityPathText", $"{topicName}/subscriptions/{subscriptionName}", TimeSpan.FromSeconds(10));
            WaitForAutomationName(window, "SelectedEntityCountsText", "Active 0 | DLQ 0 | Scheduled 0 | Total 0", TimeSpan.FromSeconds(10));

            window.Close();
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await DeleteSeedEntitiesAsync(adminClient, queueName, topicName, cleanupTimeout.Token);
        }
    }

    [UiNavigationSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task App_opens_entity_management_dialogs_and_requires_validation_confirmation()
    {
        string queueName = CreateEntityName("queue");
        string topicName = CreateEntityName("topic");
        string subscriptionName = "sub";

        var adminClient = new ServiceBusAdministrationClient(ServiceBusUiSmokeEnvironment.AdminConnectionString);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await SeedEntitiesAsync(adminClient, queueName, topicName, subscriptionName, testTimeout.Token);

        try
        {
            string executablePath = WpfAppPath.Resolve();
            Assert.True(File.Exists(executablePath), $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

            using Application application = Application.Launch(executablePath);
            using var automation = new UIA3Automation();

            Window window = application.GetMainWindow(automation, TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException("Main window did not appear within 10 seconds.");

            SetText(window, "ProfileNameTextBox", "UI smoke emulator");
            SetText(window, "RuntimeConnectionStringTextBox", ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
            SetText(window, "AdministrationConnectionStringTextBox", ServiceBusUiSmokeEnvironment.AdminConnectionString);
            window.FindFirstDescendant(cf => cf.ByAutomationId("ConnectButton"))?.AsButton().Click();

            AutomationElement queueElement = WaitForText(window, queueName, TimeSpan.FromSeconds(30));

            window.FindFirstDescendant(cf => cf.ByAutomationId("NewQueueButton"))?.AsButton().Click();
            Window newQueueDialog = WaitForWindow(application, automation, "New Queue", TimeSpan.FromSeconds(10));
            Assert.False(WaitForAutomationId(newQueueDialog, "EntityDialogAcceptButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
            WaitForAutomationName(newQueueDialog, "EntityDialogValidationMessageText", "Name is required.", TimeSpan.FromSeconds(5));
            WaitForAutomationId(newQueueDialog, "EntityDialogCancelButton", TimeSpan.FromSeconds(5)).AsButton().Click();

            window.FindFirstDescendant(cf => cf.ByAutomationId("NewTopicButton"))?.AsButton().Click();
            Window newTopicDialog = WaitForWindow(application, automation, "New Topic", TimeSpan.FromSeconds(10));
            Assert.False(WaitForAutomationId(newTopicDialog, "EntityDialogAcceptButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
            WaitForAutomationName(newTopicDialog, "EntityDialogValidationMessageText", "Name is required.", TimeSpan.FromSeconds(5));
            WaitForAutomationId(newTopicDialog, "EntityDialogCancelButton", TimeSpan.FromSeconds(5)).AsButton().Click();

            window.FindFirstDescendant(cf => cf.ByAutomationId("NewSubscriptionButton"))?.AsButton().Click();
            Window newSubscriptionDialog = WaitForWindow(application, automation, "New Subscription", TimeSpan.FromSeconds(10));
            Assert.False(WaitForAutomationId(newSubscriptionDialog, "EntityDialogAcceptButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
            WaitForAutomationName(newSubscriptionDialog, "EntityDialogValidationMessageText", "Topic name is required.", TimeSpan.FromSeconds(5));
            WaitForAutomationId(newSubscriptionDialog, "EntityDialogCancelButton", TimeSpan.FromSeconds(5)).AsButton().Click();

            queueElement.Click();
            window.FindFirstDescendant(cf => cf.ByAutomationId("UpdateEntityButton"))?.AsButton().Click();
            Window updateQueueDialog = WaitForWindow(application, automation, "Update Queue", TimeSpan.FromSeconds(10));
            Assert.True(WaitForAutomationId(updateQueueDialog, "EntityDialogAcceptButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
            WaitForAutomationId(updateQueueDialog, "EntityDialogCancelButton", TimeSpan.FromSeconds(5)).AsButton().Click();

            AutomationElement topicElement = WaitForText(window, topicName, TimeSpan.FromSeconds(10));
            topicElement.Click();
            window.FindFirstDescendant(cf => cf.ByAutomationId("UpdateEntityButton"))?.AsButton().Click();
            Window updateTopicDialog = WaitForWindow(application, automation, "Update Topic", TimeSpan.FromSeconds(10));
            Assert.True(WaitForAutomationId(updateTopicDialog, "EntityDialogAcceptButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
            WaitForAutomationId(updateTopicDialog, "EntityDialogCancelButton", TimeSpan.FromSeconds(5)).AsButton().Click();

            AutomationElement subscriptionElement = WaitForText(window, subscriptionName, TimeSpan.FromSeconds(10));
            subscriptionElement.Click();
            window.FindFirstDescendant(cf => cf.ByAutomationId("UpdateEntityButton"))?.AsButton().Click();
            Window updateSubscriptionDialog = WaitForWindow(application, automation, "Update Subscription", TimeSpan.FromSeconds(10));
            Assert.True(WaitForAutomationId(updateSubscriptionDialog, "EntityDialogAcceptButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
            WaitForAutomationId(updateSubscriptionDialog, "EntityDialogCancelButton", TimeSpan.FromSeconds(5)).AsButton().Click();

            queueElement.Click();
            window.FindFirstDescendant(cf => cf.ByAutomationId("DeleteEntityButton"))?.AsButton().Click();
            Window deleteDialog = WaitForWindow(application, automation, "Delete Entity", TimeSpan.FromSeconds(10));
            Assert.False(WaitForAutomationId(deleteDialog, "ConfirmDeleteEntityButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
            WaitForAutomationId(deleteDialog, "ConfirmDeleteEntityCheckBox", TimeSpan.FromSeconds(5)).AsCheckBox().Click();
            Assert.True(WaitForAutomationId(deleteDialog, "ConfirmDeleteEntityButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
            WaitForAutomationId(deleteDialog, "CancelDeleteEntityButton", TimeSpan.FromSeconds(5)).AsButton().Click();

            window.Close();
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await DeleteSeedEntitiesAsync(adminClient, queueName, topicName, cleanupTimeout.Token);
        }
    }

    private static string CreateEntityName(string prefix)
    {
        return $"ui-{prefix}-{Guid.NewGuid():N}".ToLowerInvariant();
    }

    private static async Task SeedEntitiesAsync(
        ServiceBusAdministrationClient adminClient,
        string queueName,
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken)
    {
        await adminClient.CreateQueueAsync(queueName, cancellationToken);
        await adminClient.CreateTopicAsync(topicName, cancellationToken);
        await adminClient.CreateSubscriptionAsync(topicName, subscriptionName, cancellationToken);
    }

    private static void SetText(Window window, string automationId, string text)
    {
        TextBox textBox = WaitForAutomationId(window, automationId, TimeSpan.FromSeconds(5)).AsTextBox();
        textBox.Text = text;
    }

    private static AutomationElement WaitForText(Window window, string text, TimeSpan timeout)
    {
        return WaitForElement(timeout, () => window.FindFirstDescendant(cf => cf.ByText(text)));
    }

    private static AutomationElement WaitForAutomationId(Window window, string automationId, TimeSpan timeout)
    {
        return WaitForElement(timeout, () => window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)));
    }

    private static Window WaitForWindow(
        Application application,
        UIA3Automation automation,
        string title,
        TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            Window? window = application
                .GetAllTopLevelWindows(automation)
                .FirstOrDefault(topLevelWindow => string.Equals(topLevelWindow.Title, title, StringComparison.Ordinal));
            if (window is not null)
            {
                return window;
            }

            Thread.Sleep(250);
        }

        throw new InvalidOperationException($"Window '{title}' did not appear within {timeout.TotalSeconds:0} seconds.");
    }

    private static AutomationElement WaitForAutomationName(
        Window window,
        string automationId,
        string expectedName,
        TimeSpan timeout)
    {
        return WaitForElement(timeout, () =>
        {
            AutomationElement? element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            return string.Equals(element?.Name, expectedName, StringComparison.Ordinal)
                ? element
                : null;
        });
    }

    private static AutomationElement WaitForElement(TimeSpan timeout, Func<AutomationElement?> findElement)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            AutomationElement? element = findElement();
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(250);
        }

        throw new InvalidOperationException($"UI element did not appear within {timeout.TotalSeconds:0} seconds.");
    }

    private static async Task DeleteSeedEntitiesAsync(
        ServiceBusAdministrationClient adminClient,
        string queueName,
        string topicName,
        CancellationToken cancellationToken)
    {
        if (await adminClient.QueueExistsAsync(queueName, cancellationToken))
        {
            await adminClient.DeleteQueueAsync(queueName, cancellationToken);
        }

        if (await adminClient.TopicExistsAsync(topicName, cancellationToken))
        {
            await adminClient.DeleteTopicAsync(topicName, cancellationToken);
        }
    }
}
