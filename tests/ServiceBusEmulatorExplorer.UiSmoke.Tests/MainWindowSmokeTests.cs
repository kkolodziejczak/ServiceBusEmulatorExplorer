using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.UiSmoke.Tests;

public sealed class MainWindowSmokeTests
{
    [UiSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task App_launches_main_window_and_exposes_shell_commands()
    {
        string executablePath = WpfAppPath.Resolve();
        Assert.True(File.Exists(executablePath), $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

        string profilePath = Path.Combine(AppContext.BaseDirectory, "ui-smoke-profiles",
            Guid.NewGuid().ToString("N"), "connection-profiles.json");
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)}");
        using var automation = new UIA3Automation();

        try
        {
            Window window = WaitForMainWindowWithAutomationId(
                application,
                automation,
                "ConnectionButton",
                TimeSpan.FromSeconds(15));

            Assert.Equal("Service Bus Emulator Explorer", window.Title);
            Assert.NotNull(WaitForAutomationId(window, "ConnectionSelector", TimeSpan.FromSeconds(10)));
            Assert.NotNull(WaitForAutomationId(window, "SearchBox", TimeSpan.FromSeconds(10)));
            Assert.NotNull(WaitForAutomationId(window, "RefreshButton", TimeSpan.FromSeconds(10)));
            Assert.NotNull(WaitForAutomationId(window, "SettingsButton", TimeSpan.FromSeconds(10)));

            InvokeButton(window, "SettingsButton", TimeSpan.FromSeconds(5));
            Window settings = WaitForWindowWithAutomationId(
                application,
                automation,
                "SettingsTabs",
                TimeSpan.FromSeconds(10));
            settings = FindAncestor(WaitForAutomationId(settings, "SettingsTabs", TimeSpan.FromSeconds(5)),
                ControlType.Window).AsWindow();

            Assert.Equal("Settings", settings.Title);
            Assert.NotNull(WaitForAutomationId(settings, "QueuePageSizeSelector", TimeSpan.FromSeconds(5)));
            Assert.NotNull(WaitForAutomationId(settings, "TopicPageSizeSelector", TimeSpan.FromSeconds(5)));
            Assert.NotNull(WaitForAutomationId(settings, "SubscriptionPageSizeSelector", TimeSpan.FromSeconds(5)));
            AutomationElement closeToTray = WaitForAutomationId(settings, "CloseToTrayToggle", TimeSpan.FromSeconds(5));
            Assert.True(closeToTray.IsEnabled);
            Assert.Equal(ToggleState.On, closeToTray.Patterns.Toggle.Pattern.ToggleState.Value);
            closeToTray.Patterns.Toggle.Pattern.Toggle();
            Assert.True(SpinWait.SpinUntil(() =>
                closeToTray.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.Off && closeToTray.IsEnabled,
                TimeSpan.FromSeconds(5)), "The close-to-tray preference did not finish saving. " +
                WaitForAutomationId(settings, "GeneralStatus", TimeSpan.FromSeconds(2)).Name);

            SelectTab(settings, "Connections", TimeSpan.FromSeconds(5));
            Assert.NotNull(WaitForAutomationId(settings, "ProfileName", TimeSpan.FromSeconds(5)));
            AutomationElement authenticationMode = WaitForAutomationId(settings, "AuthenticationModeSelector", TimeSpan.FromSeconds(5));
            authenticationMode.AsComboBox().Select("Azure CLI");
            Assert.NotNull(WaitForAutomationId(settings, "FullyQualifiedNamespace", TimeSpan.FromSeconds(5)));
            Assert.Null(settings.FindFirstDescendant(cf => cf.ByAutomationId("RuntimeConnection")));
            Assert.Null(settings.FindFirstDescendant(cf => cf.ByAutomationId("AdministrationConnection")));
            InvokeButton(settings, "DoneButton", TimeSpan.FromSeconds(5));
            Assert.DoesNotContain(
                application.GetAllTopLevelWindows(automation),
                topLevelWindow => topLevelWindow.Title.Contains("Exception", StringComparison.OrdinalIgnoreCase));
            window.Patterns.Window.Pattern.Close();
            Assert.True(SpinWait.SpinUntil(() => application.HasExited, TimeSpan.FromSeconds(10)),
                "With close-to-tray disabled, closing the main window must exit the application.");
            using Application restarted = Application.Launch(executablePath,
                $"--profile-store-path {QuoteProcessArgument(profilePath)}");
            try
            {
                Window restored = WaitForMainWindowWithAutomationId(restarted, automation,
                    "ConnectionButton", TimeSpan.FromSeconds(15));
                restored.Patterns.Window.Pattern.Close();
                Assert.True(SpinWait.SpinUntil(() => restarted.HasExited, TimeSpan.FromSeconds(10)),
                    "The saved close-to-tray setting must survive an application restart.");
            }
            finally { CloseApplication(restarted); }
        }
        finally
        {
            CloseApplication(application);
            Assert.True(SpinWait.SpinUntil(() => application.HasExited, TimeSpan.FromSeconds(5)),
                "The launch smoke application did not exit during cleanup.");
            if (File.Exists(profilePath)) File.Delete(profilePath);
            string directory = Path.GetDirectoryName(profilePath)!;
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }

        await Task.CompletedTask;
    }

    [UiSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Message_library_prototype_supports_an_in_memory_prepare_and_review_flow()
    {
        string executablePath = WpfAppPath.Resolve();
        Assert.True(File.Exists(executablePath), $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

        string profilePath = Path.Combine(AppContext.BaseDirectory, "ui-smoke-profiles",
            Guid.NewGuid().ToString("N"), "connection-profiles.json");
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)}");
        using var automation = new UIA3Automation();

        try
        {
            Window window = WaitForMainWindowWithAutomationId(
                application,
                automation,
                "ConnectionButton",
                TimeSpan.FromSeconds(15));

            window.Patterns.Transform.Pattern.Resize(1642, 958);
            Assert.True(SpinWait.SpinUntil(() =>
                Math.Abs(window.BoundingRectangle.Width - 1642) < 3 &&
                Math.Abs(window.BoundingRectangle.Height - 958) < 3,
                TimeSpan.FromSeconds(5)), "The main window did not reach the approved wide viewport.");

            AutomationElement libraryTab = WaitForAutomationId(window, "MessageLibraryTab", TimeSpan.FromSeconds(5));
            if (libraryTab.Patterns.Toggle.Pattern.ToggleState != ToggleState.On)
                libraryTab.Patterns.Toggle.Pattern.Toggle();
            WaitForAutomationId(window, "LibrarySearch", TimeSpan.FromSeconds(5));
            CapturePrototypeState(window, "workbench-wide");

            InvokeButton(window, "LibraryContinueToPrepare", TimeSpan.FromSeconds(5));
            Assert.False(WaitForAutomationId(window, "LibraryValidationDetails", TimeSpan.FromSeconds(5)).IsEnabled);

            InvokeButton(window, "LibraryMapColumns", TimeSpan.FromSeconds(5));
            Window mapping = WaitForWindowWithAutomationId(application, automation,
                "PrototypeApplyMapping", TimeSpan.FromSeconds(5));
            Assert.NotNull(WaitForText(mapping, "Map CSV inputs", TimeSpan.FromSeconds(5)));
            CapturePrototypeState(mapping, "map-csv-inputs");
            InvokeButton(mapping, "PrototypeApplyMapping", TimeSpan.FromSeconds(5));

            WaitForAutomationId(window, "LibrarySingleMode", TimeSpan.FromSeconds(5)).Patterns.SelectionItem.Pattern.Select();
            WaitForAutomationId(window, "LibraryCustomerId", TimeSpan.FromSeconds(5));
            AutomationElement review = WaitForAutomationId(window, "LibraryReview", TimeSpan.FromSeconds(5));
            Assert.False(review.IsEnabled);

            InvokeButton(window, "LibraryValidateSinglePreview", TimeSpan.FromSeconds(5));
            review = WaitForAutomationId(window, "LibraryReview", TimeSpan.FromSeconds(5));
            Assert.True(review.IsEnabled);
            Assert.Equal("Review 1 message", review.Name);

            InvokeButton(window, "LibraryReview", TimeSpan.FromSeconds(5));
            Assert.NotNull(WaitForText(window, "Review 1 message", TimeSpan.FromSeconds(5)));
            CapturePrototypeState(window, "review-single-message");
            InvokeButton(window, "PrototypeConfirmDispatch", TimeSpan.FromSeconds(5));
            WaitForAutomationId(window, "PrototypeRunResults", TimeSpan.FromSeconds(5));
            Assert.NotNull(WaitForText(window, "Confirmed sent", TimeSpan.FromSeconds(5)));
            CapturePrototypeState(window, "run-results");
            InvokeButton(window, "PrototypeViewDestination", TimeSpan.FromSeconds(5));
            InvokeButton(window, "LibraryViewRunResults", TimeSpan.FromSeconds(5));
            Assert.NotNull(WaitForText(window, "Confirmed sent", TimeSpan.FromSeconds(5)));
        }
        finally
        {
            CloseApplication(application);
            Assert.True(SpinWait.SpinUntil(() => application.HasExited, TimeSpan.FromSeconds(5)),
                "The prototype smoke application did not exit during cleanup.");
            if (File.Exists(profilePath)) File.Delete(profilePath);
            string directory = Path.GetDirectoryName(profilePath)!;
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }

        await Task.CompletedTask;
    }

    [UiSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Message_library_prototype_filters_topics_and_requires_an_explicit_unassociated_destination()
    {
        string executablePath = WpfAppPath.Resolve();
        string profilePath = Path.Combine(AppContext.BaseDirectory, "ui-smoke-profiles",
            Guid.NewGuid().ToString("N"), "connection-profiles.json");
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)}");
        using var automation = new UIA3Automation();
        try
        {
            Window window = WaitForMainWindowWithAutomationId(application, automation,
                "ConnectionButton", TimeSpan.FromSeconds(15));
            AutomationElement libraryTab = WaitForAutomationId(window, "MessageLibraryTab", TimeSpan.FromSeconds(5));
            if (libraryTab.Patterns.Toggle.Pattern.ToggleState != ToggleState.On)
                libraryTab.Patterns.Toggle.Pattern.Toggle();
            WaitForAutomationId(window, "LibraryTree", TimeSpan.FromSeconds(5));

            AutomationElement unassociated = WaitForText(window, "Unassociated sample", TimeSpan.FromSeconds(5));
            FindAncestor(unassociated, ControlType.ListItem).Patterns.SelectionItem.Pattern.Select();
            InvokeButton(window, "LibraryContinueToPrepare", TimeSpan.FromSeconds(5));
            Assert.False(WaitForAutomationId(window, "LibraryReview", TimeSpan.FromSeconds(5)).IsEnabled);
            InvokeButton(window, "LibraryChooseDestination", TimeSpan.FromSeconds(5));
            Window picker = WaitForWindowWithAutomationId(application, automation,
                "PrototypeApplyDestination", TimeSpan.FromSeconds(5));
            InvokeButton(picker, "PrototypeApplyDestination", TimeSpan.FromSeconds(5));
            InvokeButton(window, "LibraryValidatePreview", TimeSpan.FromSeconds(5));
            Assert.True(WaitForAutomationId(window, "LibraryReview", TimeSpan.FromSeconds(5)).IsEnabled);

            SetText(window, "SearchBox", "inventory-events");
            Assert.NotNull(WaitForText(window, "Stock reserved", TimeSpan.FromSeconds(5)));
            Assert.Null(window.FindFirstDescendant(cf => cf.ByText("Order reply")));
            SetText(window, "SearchBox", "");
            Assert.NotNull(WaitForText(window, "Order reply", TimeSpan.FromSeconds(5)));
            SetText(window, "SearchBox", "billing");
            Assert.NotNull(WaitForText(window, "Order created", TimeSpan.FromSeconds(5)));
            Assert.Null(window.FindFirstDescendant(cf => cf.ByText("Stock reserved")));
            SetText(window, "SearchBox", "");
            Assert.NotNull(WaitForText(window, "Stock reserved", TimeSpan.FromSeconds(5)));
        }
        finally
        {
            CloseApplication(application);
            if (File.Exists(profilePath)) File.Delete(profilePath);
            string directory = Path.GetDirectoryName(profilePath)!;
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        await Task.CompletedTask;
    }

    [UiSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Message_library_prototype_schedules_and_exposes_cancellation_results()
    {
        string executablePath = WpfAppPath.Resolve();
        string profilePath = Path.Combine(AppContext.BaseDirectory, "ui-smoke-profiles",
            Guid.NewGuid().ToString("N"), "connection-profiles.json");
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)}");
        using var automation = new UIA3Automation();
        try
        {
            Window window = WaitForMainWindowWithAutomationId(application, automation,
                "ConnectionButton", TimeSpan.FromSeconds(15));
            AutomationElement libraryTab = WaitForAutomationId(window, "MessageLibraryTab", TimeSpan.FromSeconds(5));
            if (libraryTab.Patterns.Toggle.Pattern.ToggleState != ToggleState.On)
                libraryTab.Patterns.Toggle.Pattern.Toggle();
            InvokeButton(window, "LibraryContinueToPrepare", TimeSpan.FromSeconds(5));
            InvokeButton(window, "LibraryValidatePreview", TimeSpan.FromSeconds(5));
            InvokeButton(window, "LibraryReview", TimeSpan.FromSeconds(5));
            WaitForAutomationId(window, "PrototypeScheduleLater", TimeSpan.FromSeconds(5))
                .Patterns.SelectionItem.Pattern.Select();
            Assert.Equal("Schedule 3 messages", WaitForAutomationId(window,
                "PrototypeConfirmDispatch", TimeSpan.FromSeconds(5)).Name);
            InvokeButton(window, "PrototypeConfirmDispatch", TimeSpan.FromSeconds(5));
            WaitForAutomationId(window, "PrototypeRunResults", TimeSpan.FromSeconds(8));
            Assert.NotNull(WaitForText(window, "Confirmed scheduled", TimeSpan.FromSeconds(5)));
            InvokeButton(window, "PrototypeShowCancellation", TimeSpan.FromSeconds(5));
            WaitForAutomationId(window, "PrototypeScheduledResults", TimeSpan.FromSeconds(5));
        }
        finally
        {
            CloseApplication(application);
            if (File.Exists(profilePath)) File.Delete(profilePath);
            string directory = Path.GetDirectoryName(profilePath)!;
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        await Task.CompletedTask;
    }

    [UiSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Message_library_prototype_blocks_an_invalid_csv_batch_and_shows_row_errors()
    {
        string executablePath = WpfAppPath.Resolve();
        string profilePath = Path.Combine(AppContext.BaseDirectory, "ui-smoke-profiles",
            Guid.NewGuid().ToString("N"), "connection-profiles.json");
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)}");
        using var automation = new UIA3Automation();
        try
        {
            Window window = WaitForMainWindowWithAutomationId(application, automation,
                "ConnectionButton", TimeSpan.FromSeconds(15));
            AutomationElement libraryTab = WaitForAutomationId(window, "MessageLibraryTab", TimeSpan.FromSeconds(5));
            if (libraryTab.Patterns.Toggle.Pattern.ToggleState != ToggleState.On)
                libraryTab.Patterns.Toggle.Pattern.Toggle();
            InvokeButton(window, "LibraryContinueToPrepare", TimeSpan.FromSeconds(5));
            InvokeButton(window, "LibraryBrowseCsv", TimeSpan.FromSeconds(5));
            Window picker = WaitForWindowWithAutomationId(application, automation,
                "PrototypeUseSampleCsv", TimeSpan.FromSeconds(5));
            WaitForAutomationId(picker, "PrototypeSampleCsvPicker", TimeSpan.FromSeconds(5))
                .AsComboBox().Select("orders-invalid.csv — row 2 amount is abc");
            InvokeButton(picker, "PrototypeUseSampleCsv", TimeSpan.FromSeconds(5));
            InvokeButton(window, "LibraryValidatePreview", TimeSpan.FromSeconds(5));
            Assert.False(WaitForAutomationId(window, "LibraryReview", TimeSpan.FromSeconds(5)).IsEnabled);
            Assert.NotNull(WaitForText(window, "1 invalid row · Nothing can be sent", TimeSpan.FromSeconds(5)));
            InvokeButton(window, "LibraryValidationDetails", TimeSpan.FromSeconds(5));
            Window errors = WaitForWindowWithAutomationId(application, automation,
                "PrototypeValidationResults", TimeSpan.FromSeconds(5));
            Assert.NotNull(WaitForText(errors, "Expected number, got abc", TimeSpan.FromSeconds(5)));
        }
        finally
        {
            CloseApplication(application);
            if (File.Exists(profilePath)) File.Delete(profilePath);
            string directory = Path.GetDirectoryName(profilePath)!;
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        await Task.CompletedTask;
    }

    [UiSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Message_library_keeps_saved_properties_with_their_template()
    {
        string executablePath = WpfAppPath.Resolve();
        string profilePath = Path.Combine(AppContext.BaseDirectory, "ui-smoke-profiles",
            Guid.NewGuid().ToString("N"), "connection-profiles.json");
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)}");
        using var automation = new UIA3Automation();
        try
        {
            Window window = WaitForMainWindowWithAutomationId(application, automation,
                "ConnectionButton", TimeSpan.FromSeconds(15));
            AutomationElement libraryTab = WaitForAutomationId(window, "MessageLibraryTab", TimeSpan.FromSeconds(5));
            if (libraryTab.Patterns.Toggle.Pattern.ToggleState != ToggleState.On)
                libraryTab.Patterns.Toggle.Pattern.Toggle();
            InvokeButton(window, "LibraryEditorProperties", TimeSpan.FromSeconds(5));
            var routing = WaitForAutomationId(window, "LibraryReplyRouting", TimeSpan.FromSeconds(5));
            Assert.Equal(ExpandCollapseState.Collapsed, routing.Patterns.ExpandCollapse.Pattern.ExpandCollapseState);
            var propertyGrid = WaitForAutomationId(window, "LibraryApplicationProperties", TimeSpan.FromSeconds(5));
            int originalPropertyCount = propertyGrid.Patterns.Grid.Pattern.RowCount;
            var deleteProperty = WaitForAutomationId(window, "LibraryDeleteSelectedProperty", TimeSpan.FromSeconds(5));
            Assert.False(deleteProperty.IsEnabled);
            InvokeButton(window, "LibraryAddProperty", TimeSpan.FromSeconds(5));
            Assert.Equal(originalPropertyCount + 1, propertyGrid.Patterns.Grid.Pattern.RowCount);
            Assert.True(deleteProperty.IsEnabled);
            InvokeButton(window, "LibraryDeleteSelectedProperty", TimeSpan.FromSeconds(5));
            Assert.Equal(originalPropertyCount, propertyGrid.Patterns.Grid.Pattern.RowCount);
            Assert.False(deleteProperty.IsEnabled);
            SetText(window, "LibrarySubject", "OrderCreatedEdited");
            InvokeButton(window, "LibrarySave", TimeSpan.FromSeconds(5));

            FindAncestor(WaitForText(window, "Order updated", TimeSpan.FromSeconds(5)),
                ControlType.ListItem).Patterns.SelectionItem.Pattern.Select();
            InvokeButton(window, "LibraryEditorProperties", TimeSpan.FromSeconds(5));
            Assert.Equal("OrderCreated", WaitForAutomationId(window, "LibrarySubject",
                TimeSpan.FromSeconds(5)).Patterns.Value.Pattern.Value);

            FindAncestor(WaitForText(window, "Order created", TimeSpan.FromSeconds(5)),
                ControlType.ListItem).Patterns.SelectionItem.Pattern.Select();
            InvokeButton(window, "LibraryEditorProperties", TimeSpan.FromSeconds(5));
            Assert.Equal("OrderCreatedEdited", WaitForAutomationId(window, "LibrarySubject",
                TimeSpan.FromSeconds(5)).Patterns.Value.Pattern.Value);
        }
        finally
        {
            CloseApplication(application);
            if (File.Exists(profilePath)) File.Delete(profilePath);
            string directory = Path.GetDirectoryName(profilePath)!;
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        await Task.CompletedTask;
    }

    [UiSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Message_library_compact_layout_keeps_both_trees_and_switches_author_prepare()
    {
        string executablePath = WpfAppPath.Resolve();
        string profilePath = Path.Combine(AppContext.BaseDirectory, "ui-smoke-profiles",
            Guid.NewGuid().ToString("N"), "connection-profiles.json");
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)}");
        using var automation = new UIA3Automation();
        try
        {
            Window window = WaitForMainWindowWithAutomationId(application, automation,
                "ConnectionButton", TimeSpan.FromSeconds(15));
            window.Patterns.Transform.Pattern.Resize(980, 640);
            AutomationElement libraryTab = WaitForAutomationId(window, "MessageLibraryTab", TimeSpan.FromSeconds(5));
            if (libraryTab.Patterns.Toggle.Pattern.ToggleState != ToggleState.On)
                libraryTab.Patterns.Toggle.Pattern.Toggle();
            WaitForAutomationId(window, "PrototypeNamespaceTree", TimeSpan.FromSeconds(5));
            WaitForAutomationId(window, "LibraryTree", TimeSpan.FromSeconds(5));
            Assert.False(WaitForAutomationId(window, "LibraryAuthorEditor", TimeSpan.FromSeconds(5)).IsOffscreen);
            InvokeButton(window, "LibraryContinueToPrepare", TimeSpan.FromSeconds(5));
            Assert.False(WaitForAutomationId(window, "LibraryMapColumns", TimeSpan.FromSeconds(5)).IsOffscreen);
            InvokeButton(window, "LibraryValidatePreview", TimeSpan.FromSeconds(5));
            Assert.True(WaitForAutomationId(window, "LibraryReview", TimeSpan.FromSeconds(5)).IsEnabled);
        }
        finally
        {
            CloseApplication(application);
            if (File.Exists(profilePath)) File.Delete(profilePath);
            string directory = Path.GetDirectoryName(profilePath)!;
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        await Task.CompletedTask;
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
        await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(testTimeout.Token);
        await SeedEntitiesAsync(adminClient, queueName, topicName, subscriptionName, testTimeout.Token);

        try
        {
            string executablePath = WpfAppPath.Resolve();
            Assert.True(File.Exists(executablePath), $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

            using Application application = LaunchWpfApp(executablePath);
            using var automation = new UIA3Automation();

            try
            {
                Window window = WaitForMainWindowWithAutomationId(
                    application,
                    automation,
                    "ConnectButton",
                    TimeSpan.FromSeconds(15));

                SetText(window, "ProfileNameTextBox", "UI smoke emulator");
                SetText(window, "RuntimeConnectionStringTextBox", ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
                SetText(window, "AdministrationConnectionStringTextBox", ServiceBusUiSmokeEnvironment.AdminConnectionString);
                InvokeButton(window, "ConnectButton", TimeSpan.FromSeconds(5));

                AutomationElement queueElement = WaitForText(window, queueName, TimeSpan.FromSeconds(30));
                SelectTreeItem(queueElement);

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
                SelectTreeItem(subscriptionElement);

                WaitForAutomationName(window, "SelectedEntityTitleText", subscriptionName, TimeSpan.FromSeconds(10));
                WaitForAutomationName(window, "SelectedEntityKindText", "Subscription", TimeSpan.FromSeconds(10));
                WaitForAutomationName(window, "SelectedEntityPathText", $"{topicName}/subscriptions/{subscriptionName}", TimeSpan.FromSeconds(10));
                WaitForAutomationName(window, "SelectedEntityCountsText", "Active 0 | DLQ 0 | Scheduled 0 | Total 0", TimeSpan.FromSeconds(10));
            }
            finally
            {
                CloseApplication(application);
            }
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await DeleteSeedEntitiesAsync(adminClient, queueName, topicName, cleanupTimeout.Token);
        }
    }

    [UiNavigationSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Context_commands_hide_invalid_actions_and_topic_refresh_keeps_topic_grid_available()
    {
        string queueName = CreateEntityName("queue");
        string topicName = CreateEntityName("topic");
        string subscriptionName = "sub";

        var adminClient = new ServiceBusAdministrationClient(ServiceBusUiSmokeEnvironment.AdminConnectionString);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(testTimeout.Token);
        await SeedEntitiesAsync(adminClient, queueName, topicName, subscriptionName, testTimeout.Token);

        try
        {
            string executablePath = WpfAppPath.Resolve();
            Assert.True(File.Exists(executablePath), $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

            using Application application = LaunchWpfApp(executablePath);
            using var automation = new UIA3Automation();

            try
            {
                Window window = WaitForMainWindowWithAutomationId(
                    application,
                    automation,
                    "ConnectButton",
                    TimeSpan.FromSeconds(15));

                SetText(window, "ProfileNameTextBox", "UI smoke emulator");
                SetText(window, "RuntimeConnectionStringTextBox", ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
                SetText(window, "AdministrationConnectionStringTextBox", ServiceBusUiSmokeEnvironment.AdminConnectionString);
                InvokeButton(window, "ConnectButton", TimeSpan.FromSeconds(5));

                WaitForAutomationName(window, "ConnectButton", "Connected", TimeSpan.FromSeconds(10));
                Assert.False(WaitForAutomationId(window, "ConnectButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);

                SelectTreeItem(WaitForText(window, queueName, TimeSpan.FromSeconds(30)));
                Assert.NotNull(WaitForAutomationId(window, "SendMessageButton", TimeSpan.FromSeconds(5)));
                Assert.NotNull(WaitForAutomationId(window, "PeekActiveMessagesButton", TimeSpan.FromSeconds(5)));
                Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("RefreshSubscriptionsButton")));

                SelectTreeItem(WaitForText(window, topicName, TimeSpan.FromSeconds(10)));
                Assert.NotNull(WaitForAutomationId(window, "SendMessageButton", TimeSpan.FromSeconds(5)));
                Assert.NotNull(WaitForAutomationId(window, "RefreshSubscriptionsButton", TimeSpan.FromSeconds(5)));
                Assert.NotNull(WaitForAutomationId(window, "PeekActiveMessagesButton", TimeSpan.FromSeconds(5)));
                Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("ReplayDeadLetterButton")));

                InvokeButton(window, "RefreshSubscriptionsButton", TimeSpan.FromSeconds(10));
                WaitForText(window, $"Refreshed 1 subscription(s) for {topicName}.", TimeSpan.FromSeconds(20));
                WaitForAutomationName(window, "SelectedEntityTitleText", topicName, TimeSpan.FromSeconds(10));

                SelectTreeItem(WaitForText(window, subscriptionName, TimeSpan.FromSeconds(10)));
                Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("SendMessageButton")));
                Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("RefreshSubscriptionsButton")));
                Assert.NotNull(WaitForAutomationId(window, "PeekActiveMessagesButton", TimeSpan.FromSeconds(5)));
                Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("ReplayDeadLetterButton")));

                SelectTab(window, "Dead Letter", TimeSpan.FromSeconds(5));

                AutomationElement replayButton = WaitForAutomationId(window, "ReplayDeadLetterButton", TimeSpan.FromSeconds(5));
                Assert.False(replayButton.AsButton().IsEnabled);
                Assert.Contains("Select exactly one DLQ message first.", replayButton.Properties.HelpText.Value);
            }
            finally
            {
                CloseApplication(application);
            }
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await DeleteSeedEntitiesAsync(adminClient, queueName, topicName, cleanupTimeout.Token);
        }
    }

    [UiNavigationSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Entity_management_dialogs_require_validation_and_confirmation()
    {
        string queueName = CreateEntityName("queue");
        string topicName = CreateEntityName("topic");
        string subscriptionName = "sub";

        var adminClient = new ServiceBusAdministrationClient(ServiceBusUiSmokeEnvironment.AdminConnectionString);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(testTimeout.Token);
        await SeedEntitiesAsync(adminClient, queueName, topicName, subscriptionName, testTimeout.Token);

        try
        {
            string executablePath = WpfAppPath.Resolve();
            Assert.True(File.Exists(executablePath), $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

            using Application application = LaunchWpfApp(executablePath);
            using var automation = new UIA3Automation();

            try
            {
                Window window = WaitForMainWindowWithAutomationId(
                    application,
                    automation,
                    "ConnectButton",
                    TimeSpan.FromSeconds(15));

                SetText(window, "ProfileNameTextBox", "UI smoke emulator");
                SetText(window, "RuntimeConnectionStringTextBox", ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
                SetText(window, "AdministrationConnectionStringTextBox", ServiceBusUiSmokeEnvironment.AdminConnectionString);
                InvokeButton(window, "ConnectButton", TimeSpan.FromSeconds(5));

                AutomationElement queueElement = WaitForText(window, queueName, TimeSpan.FromSeconds(30));

                InvokeButton(window, "NewQueueButton", TimeSpan.FromSeconds(5));
                Window newQueueDialog = WaitForWindowWithAutomationId(
                    application,
                    automation,
                    "EntityDialogAcceptButton",
                    TimeSpan.FromSeconds(10));
                Assert.False(WaitForAutomationId(newQueueDialog, "EntityDialogAcceptButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
                WaitForAutomationName(newQueueDialog, "EntityDialogValidationMessageText", "Name is required.", TimeSpan.FromSeconds(5));
                ClickButton(newQueueDialog, "EntityDialogCancelButton", TimeSpan.FromSeconds(5));

                InvokeButton(window, "NewTopicButton", TimeSpan.FromSeconds(5));
                Window newTopicDialog = WaitForWindowWithAutomationId(
                    application,
                    automation,
                    "EntityDialogAcceptButton",
                    TimeSpan.FromSeconds(10));
                Assert.False(WaitForAutomationId(newTopicDialog, "EntityDialogAcceptButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
                WaitForAutomationName(newTopicDialog, "EntityDialogValidationMessageText", "Name is required.", TimeSpan.FromSeconds(5));
                ClickButton(newTopicDialog, "EntityDialogCancelButton", TimeSpan.FromSeconds(5));

                InvokeButton(window, "NewSubscriptionButton", TimeSpan.FromSeconds(5));
                Window newSubscriptionDialog = WaitForWindowWithAutomationId(
                    application,
                    automation,
                    "EntityDialogAcceptButton",
                    TimeSpan.FromSeconds(10));
                Assert.False(WaitForAutomationId(newSubscriptionDialog, "EntityDialogAcceptButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
                WaitForAutomationName(newSubscriptionDialog, "EntityDialogValidationMessageText", "Topic name is required.", TimeSpan.FromSeconds(5));
                ClickButton(newSubscriptionDialog, "EntityDialogCancelButton", TimeSpan.FromSeconds(5));

                SelectTreeItem(queueElement);
                InvokeButton(window, "UpdateEntityButton", TimeSpan.FromSeconds(5));
                Window updateQueueDialog = WaitForWindowWithAutomationId(
                    application,
                    automation,
                    "EntityDialogAcceptButton",
                    TimeSpan.FromSeconds(10));
                Assert.True(WaitForAutomationId(updateQueueDialog, "EntityDialogAcceptButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
                ClickButton(updateQueueDialog, "EntityDialogCancelButton", TimeSpan.FromSeconds(5));

                AutomationElement topicElement = WaitForText(window, topicName, TimeSpan.FromSeconds(10));
                SelectTreeItem(topicElement);
                InvokeButton(window, "UpdateEntityButton", TimeSpan.FromSeconds(5));
                Window updateTopicDialog = WaitForWindowWithAutomationId(
                    application,
                    automation,
                    "EntityDialogAcceptButton",
                    TimeSpan.FromSeconds(10));
                Assert.True(WaitForAutomationId(updateTopicDialog, "EntityDialogAcceptButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
                ClickButton(updateTopicDialog, "EntityDialogCancelButton", TimeSpan.FromSeconds(5));

                AutomationElement subscriptionElement = WaitForText(window, subscriptionName, TimeSpan.FromSeconds(10));
                SelectTreeItem(subscriptionElement);
                InvokeButton(window, "UpdateEntityButton", TimeSpan.FromSeconds(5));
                Window updateSubscriptionDialog = WaitForWindowWithAutomationId(
                    application,
                    automation,
                    "EntityDialogAcceptButton",
                    TimeSpan.FromSeconds(10));
                Assert.True(WaitForAutomationId(updateSubscriptionDialog, "EntityDialogAcceptButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
                ClickButton(updateSubscriptionDialog, "EntityDialogCancelButton", TimeSpan.FromSeconds(5));

                SelectTreeItem(queueElement);
                InvokeButton(window, "DeleteEntityButton", TimeSpan.FromSeconds(5));
                Window deleteDialog = WaitForWindowWithAutomationId(
                    application,
                    automation,
                    "ConfirmDeleteEntityButton",
                    TimeSpan.FromSeconds(10));
                Assert.False(WaitForAutomationId(deleteDialog, "ConfirmDeleteEntityButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
                ToggleCheckBox(deleteDialog, "ConfirmDeleteEntityCheckBox", TimeSpan.FromSeconds(5));
                Assert.True(WaitForAutomationId(deleteDialog, "ConfirmDeleteEntityButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
                ClickButton(deleteDialog, "CancelDeleteEntityButton", TimeSpan.FromSeconds(5));
            }
            finally
            {
                CloseApplication(application);
            }
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await DeleteSeedEntitiesAsync(adminClient, queueName, topicName, cleanupTimeout.Token);
        }
    }

    [UiNavigationSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Selecting_message_row_updates_body_and_properties()
    {
        string queueName = CreateEntityName("queue");
        const string body = "ui smoke message body";

        var adminClient = new ServiceBusAdministrationClient(ServiceBusUiSmokeEnvironment.AdminConnectionString);
        await using var runtimeClient = new ServiceBusClient(ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(testTimeout.Token);
        await adminClient.CreateQueueAsync(queueName, testTimeout.Token);
        await SendSeedMessageAsync(runtimeClient, queueName, body, testTimeout.Token);

        try
        {
            string executablePath = WpfAppPath.Resolve();
            Assert.True(File.Exists(executablePath), $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

            using Application application = LaunchWpfApp(executablePath);
            using var automation = new UIA3Automation();

            try
            {
                Window window = WaitForMainWindowWithAutomationId(
                    application,
                    automation,
                    "ConnectButton",
                    TimeSpan.FromSeconds(15));

                SetText(window, "ProfileNameTextBox", "UI smoke emulator");
                SetText(window, "RuntimeConnectionStringTextBox", ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
                SetText(window, "AdministrationConnectionStringTextBox", ServiceBusUiSmokeEnvironment.AdminConnectionString);
                InvokeButton(window, "ConnectButton", TimeSpan.FromSeconds(5));

                AutomationElement queueElement = WaitForText(window, queueName, TimeSpan.FromSeconds(30));
                SelectTreeItem(queueElement);
                InvokeButton(window, "PeekActiveMessagesButton", TimeSpan.FromSeconds(10));

                AutomationElement previewElement = WaitForText(window, body, TimeSpan.FromSeconds(20));
                SelectDataItem(previewElement);

                TextBox bodyTextBox = WaitForAutomationId(window, "SelectedMessageBodyText", TimeSpan.FromSeconds(10)).AsTextBox();
                SelectTab(window, "Properties", TimeSpan.FromSeconds(5));
                TextBox systemPropertiesTextBox = WaitForAutomationId(window, "SelectedMessageSystemPropertiesText", TimeSpan.FromSeconds(10)).AsTextBox();
                TextBox applicationPropertiesTextBox = WaitForAutomationId(window, "SelectedMessageApplicationPropertiesText", TimeSpan.FromSeconds(10)).AsTextBox();

                Assert.Equal(body, bodyTextBox.Text);
                Assert.Contains("SequenceNumber", systemPropertiesTextBox.Text);
                Assert.Contains("kind: ui-smoke", applicationPropertiesTextBox.Text);
            }
            finally
            {
                CloseApplication(application);
            }
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await DeleteQueueAsync(adminClient, queueName, cleanupTimeout.Token);
        }
    }

    [UiNavigationSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Dlq_row_selection_enables_replay_delete_and_delete_requires_confirmation()
    {
        string queueName = CreateEntityName("queue");
        const string body = "ui smoke dlq body";

        var adminClient = new ServiceBusAdministrationClient(ServiceBusUiSmokeEnvironment.AdminConnectionString);
        await using var runtimeClient = new ServiceBusClient(ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(testTimeout.Token);
        await adminClient.CreateQueueAsync(queueName, testTimeout.Token);
        await SendSeedMessageAsync(runtimeClient, queueName, body, testTimeout.Token);
        await DeadLetterSeedMessageAsync(runtimeClient, queueName, testTimeout.Token);

        try
        {
            string executablePath = WpfAppPath.Resolve();
            Assert.True(File.Exists(executablePath), $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

            using Application application = LaunchWpfApp(executablePath);
            using var automation = new UIA3Automation();

            try
            {
                Window window = WaitForMainWindowWithAutomationId(
                    application,
                    automation,
                    "ConnectButton",
                    TimeSpan.FromSeconds(15));

                SetText(window, "ProfileNameTextBox", "UI smoke emulator");
                SetText(window, "RuntimeConnectionStringTextBox", ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
                SetText(window, "AdministrationConnectionStringTextBox", ServiceBusUiSmokeEnvironment.AdminConnectionString);
                InvokeButton(window, "ConnectButton", TimeSpan.FromSeconds(5));

                AutomationElement queueElement = WaitForText(window, queueName, TimeSpan.FromSeconds(30));
                SelectTreeItem(queueElement);
                InvokeButton(window, "PeekDeadLetterMessagesButton", TimeSpan.FromSeconds(10));

                SelectTab(window, "Dead Letter", TimeSpan.FromSeconds(5));
                AutomationElement previewElement = WaitForText(window, body, TimeSpan.FromSeconds(20));
                SelectDataItem(previewElement);

                Assert.True(WaitForAutomationId(window, "ReplayDeadLetterButton", TimeSpan.FromSeconds(10)).AsButton().IsEnabled);
                Assert.True(WaitForAutomationId(window, "EditReplayDeadLetterButton", TimeSpan.FromSeconds(10)).AsButton().IsEnabled);
                Assert.True(WaitForAutomationId(window, "DeleteSelectedDeadLetterButton", TimeSpan.FromSeconds(10)).AsButton().IsEnabled);

                Assert.True(WaitForAutomationId(window, "DeleteVisibleDeadLetterButton", TimeSpan.FromSeconds(10)).AsButton().IsEnabled);

                AssertDeleteSelectedDlqRequiresConfirmation(application, automation, window);
                AssertDeleteVisibleDlqRequiresTypedConfirmation(application, automation, window);
            }
            finally
            {
                CloseApplication(application);
            }
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await DeleteQueueAsync(adminClient, queueName, cleanupTimeout.Token);
        }
    }

    [UiNavigationSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Replay_dlq_copy_keeps_original_visible_until_explicit_delete()
    {
        string queueName = CreateEntityName("queue");
        const string body = "ui smoke replay proof body";

        var adminClient = new ServiceBusAdministrationClient(ServiceBusUiSmokeEnvironment.AdminConnectionString);
        await using var runtimeClient = new ServiceBusClient(ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(testTimeout.Token);
        await adminClient.CreateQueueAsync(queueName, testTimeout.Token);
        await SendSeedMessageAsync(runtimeClient, queueName, body, testTimeout.Token);
        await DeadLetterSeedMessageAsync(runtimeClient, queueName, testTimeout.Token);

        try
        {
            string executablePath = WpfAppPath.Resolve();
            Assert.True(File.Exists(executablePath), $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

            using Application application = LaunchWpfApp(executablePath);
            using var automation = new UIA3Automation();

            try
            {
                Window window = WaitForMainWindowWithAutomationId(
                    application,
                    automation,
                    "ConnectButton",
                    TimeSpan.FromSeconds(15));

                SetText(window, "ProfileNameTextBox", "UI smoke emulator");
                SetText(window, "RuntimeConnectionStringTextBox", ServiceBusUiSmokeEnvironment.RuntimeConnectionString);
                SetText(window, "AdministrationConnectionStringTextBox", ServiceBusUiSmokeEnvironment.AdminConnectionString);
                InvokeButton(window, "ConnectButton", TimeSpan.FromSeconds(5));

                AutomationElement queueElement = WaitForText(window, queueName, TimeSpan.FromSeconds(30));
                SelectTreeItem(queueElement);
                InvokeButton(window, "PeekDeadLetterMessagesButton", TimeSpan.FromSeconds(10));

                SelectTab(window, "Dead Letter", TimeSpan.FromSeconds(5));
                SelectDataItem(WaitForText(window, body, TimeSpan.FromSeconds(20)));
                InvokeButton(window, "ReplayDeadLetterButton", TimeSpan.FromSeconds(10));

                ServiceBusReceivedMessage activeReplay = await WaitForSingleMessageAsync(
                    runtimeClient,
                    queueName,
                    SubQueue.None,
                    testTimeout.Token);
                ServiceBusReceivedMessage remainingDeadLetter = await WaitForSingleMessageAsync(
                    runtimeClient,
                    queueName,
                    SubQueue.DeadLetter,
                    testTimeout.Token);

                Assert.Equal(body, activeReplay.Body.ToString());
                Assert.Equal("ui-smoke", activeReplay.ApplicationProperties["kind"]);
                Assert.Equal(body, remainingDeadLetter.Body.ToString());
                Assert.Equal("ui-smoke", remainingDeadLetter.ApplicationProperties["kind"]);

                SelectTab(window, "Dead Letter", TimeSpan.FromSeconds(5));
                SelectDataItem(WaitForText(window, body, TimeSpan.FromSeconds(20)));
                ConfirmDeleteSelectedDlq(application, automation, window);

                await WaitForMessageCountAsync(
                    runtimeClient,
                    queueName,
                    SubQueue.DeadLetter,
                    expectedCount: 0,
                    testTimeout.Token);
            }
            finally
            {
                CloseApplication(application);
            }
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await DeleteQueueAsync(adminClient, queueName, cleanupTimeout.Token);
        }
    }

    private static void AssertDeleteSelectedDlqRequiresConfirmation(
        Application application,
        UIA3Automation automation,
        Window window)
    {
        ClickButton(window, "DeleteSelectedDeadLetterButton", TimeSpan.FromSeconds(10));
        Window deleteDialog = WaitForWindowWithAutomationId(
            application,
            automation,
            "ConfirmDeleteDlqButton",
            TimeSpan.FromSeconds(10));

        Assert.False(WaitForAutomationId(deleteDialog, "ConfirmDeleteDlqButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
        ToggleCheckBox(deleteDialog, "ConfirmDeleteDlqCheckBox", TimeSpan.FromSeconds(5));
        Assert.True(WaitForAutomationId(deleteDialog, "ConfirmDeleteDlqButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
        InvokeButton(deleteDialog, "CancelDeleteDlqButton", TimeSpan.FromSeconds(5));
    }

    private static void AssertDeleteVisibleDlqRequiresTypedConfirmation(
        Application application,
        UIA3Automation automation,
        Window window)
    {
        ClickButton(window, "DeleteVisibleDeadLetterButton", TimeSpan.FromSeconds(10));
        Window deleteDialog = WaitForWindowWithAutomationId(
            application,
            automation,
            "ConfirmDeleteDlqButton",
            TimeSpan.FromSeconds(10));

        Assert.False(WaitForAutomationId(deleteDialog, "ConfirmDeleteDlqButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
        SetText(deleteDialog, "ConfirmDeleteDlqPhraseTextBox", "DELETE");
        Assert.False(WaitForAutomationId(deleteDialog, "ConfirmDeleteDlqButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
        SetText(deleteDialog, "ConfirmDeleteDlqPhraseTextBox", "DELETE VISIBLE");
        Assert.True(WaitForAutomationId(deleteDialog, "ConfirmDeleteDlqButton", TimeSpan.FromSeconds(5)).AsButton().IsEnabled);
        InvokeButton(deleteDialog, "CancelDeleteDlqButton", TimeSpan.FromSeconds(5));
    }

    private static void ConfirmDeleteSelectedDlq(
        Application application,
        UIA3Automation automation,
        Window window)
    {
        ClickButton(window, "DeleteSelectedDeadLetterButton", TimeSpan.FromSeconds(10));
        Window deleteDialog = WaitForWindowWithAutomationId(
            application,
            automation,
            "ConfirmDeleteDlqButton",
            TimeSpan.FromSeconds(10));

        ToggleCheckBox(deleteDialog, "ConfirmDeleteDlqCheckBox", TimeSpan.FromSeconds(5));
        InvokeButton(deleteDialog, "ConfirmDeleteDlqButton", TimeSpan.FromSeconds(5));
    }

    private static string CreateEntityName(string prefix)
    {
        return $"ui-{prefix}-{Guid.NewGuid():N}".ToLowerInvariant();
    }

    private static Application LaunchWpfApp(string executablePath)
    {
        return Application.Launch(executablePath, CreateProfileStoreArguments());
    }

    private static string CreateProfileStoreArguments()
    {
        string profilePath = Path.Combine(
            AppContext.BaseDirectory,
            "ui-smoke-profiles",
            Guid.NewGuid().ToString("N"),
            "connection-profiles.json");

        return $"--profile-store-path {QuoteProcessArgument(profilePath)}";
    }

    private static string QuoteProcessArgument(string argument)
    {
        return argument.Contains(' ', StringComparison.Ordinal)
            ? "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : argument;
    }

    private static void CloseApplication(Application application)
    {
        if (application.HasExited)
        {
            return;
        }

        try
        {
            application.Kill();
        }
        catch when (application.HasExited)
        {
        }
    }

    private static Window WaitForMainWindowWithAutomationId(
        Application application,
        UIA3Automation automation,
        string automationId,
        TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            foreach (Window window in application.GetAllTopLevelWindows(automation))
            {
                if (window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is not null)
                {
                    return window;
                }
            }

            Thread.Sleep(250);
        }

        throw new InvalidOperationException(
            $"Main window with UI element '{automationId}' did not appear within {timeout.TotalSeconds:0} seconds.");
    }

    private static Window WaitForWindowWithAutomationId(
        Application application,
        UIA3Automation automation,
        string automationId,
        TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            foreach (Window window in application.GetAllTopLevelWindows(automation))
            {
                if (window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is not null)
                {
                    return window;
                }
            }

            Thread.Sleep(250);
        }

        throw new InvalidOperationException(
            $"Window with UI element '{automationId}' did not appear within {timeout.TotalSeconds:0} seconds.");
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

    private static async Task SendSeedMessageAsync(
        ServiceBusClient runtimeClient,
        string queueName,
        string body,
        CancellationToken cancellationToken)
    {
        await using ServiceBusSender sender = runtimeClient.CreateSender(queueName);
        var message = new ServiceBusMessage(body)
        {
            ContentType = "text/plain"
        };
        message.ApplicationProperties["kind"] = "ui-smoke";

        await sender.SendMessageAsync(message, cancellationToken);
    }

    private static async Task<ServiceBusReceivedMessage> WaitForSingleMessageAsync(
        ServiceBusClient runtimeClient,
        string queueName,
        SubQueue subQueue,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ServiceBusReceivedMessage> messages = await WaitForMessagesAsync(
            runtimeClient,
            queueName,
            subQueue,
            expectedCount: 1,
            cancellationToken);

        return messages[0];
    }

    private static async Task WaitForMessageCountAsync(
        ServiceBusClient runtimeClient,
        string queueName,
        SubQueue subQueue,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        _ = await WaitForMessagesAsync(runtimeClient, queueName, subQueue, expectedCount, cancellationToken);
    }

    private static async Task<IReadOnlyList<ServiceBusReceivedMessage>> WaitForMessagesAsync(
        ServiceBusClient runtimeClient,
        string queueName,
        SubQueue subQueue,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            IReadOnlyList<ServiceBusReceivedMessage> messages = await PeekMessagesAsync(
                runtimeClient,
                queueName,
                subQueue,
                cancellationToken);
            if (messages.Count == expectedCount)
            {
                return messages;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        IReadOnlyList<ServiceBusReceivedMessage> finalMessages = await PeekMessagesAsync(
            runtimeClient,
            queueName,
            subQueue,
            cancellationToken);
        Assert.Equal(expectedCount, finalMessages.Count);
        return finalMessages;
    }

    private static async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesAsync(
        ServiceBusClient runtimeClient,
        string queueName,
        SubQueue subQueue,
        CancellationToken cancellationToken)
    {
        var options = new ServiceBusReceiverOptions
        {
            SubQueue = subQueue
        };

        await using ServiceBusReceiver receiver = runtimeClient.CreateReceiver(queueName, options);
        IReadOnlyList<ServiceBusReceivedMessage> messages = await receiver.PeekMessagesAsync(
            maxMessages: 10,
            cancellationToken: cancellationToken);

        return messages;
    }

    private static async Task DeadLetterSeedMessageAsync(
        ServiceBusClient runtimeClient,
        string queueName,
        CancellationToken cancellationToken)
    {
        await using ServiceBusReceiver receiver = runtimeClient.CreateReceiver(queueName);
        ServiceBusReceivedMessage message = await receiver.ReceiveMessageAsync(
            maxWaitTime: TimeSpan.FromSeconds(10),
            cancellationToken);

        await receiver.DeadLetterMessageAsync(message, cancellationToken: cancellationToken);
    }

    private static void SetText(Window window, string automationId, string text)
    {
        AutomationElement textBox = WaitForAutomationId(window, automationId, TimeSpan.FromSeconds(5));
        textBox.Patterns.Value.Pattern.SetValue(text);
    }

    private static void CapturePrototypeState(Window window, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("SBE_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var capture = Capture.Element(window);
        capture.ToFile(Path.Combine(directory, name + ".png"));
    }

    private static void InvokeButton(Window window, string automationId, TimeSpan timeout)
    {
        AutomationElement button = WaitForElement(timeout, () =>
        {
            AutomationElement? element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            return element is { IsEnabled: true } ? element : null;
        });

        button.Patterns.Invoke.Pattern.Invoke();
        Thread.Sleep(250);
    }

    private static void ClickButton(Window window, string automationId, TimeSpan timeout)
    {
        AutomationElement button = WaitForElement(timeout, () =>
        {
            AutomationElement? element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            return element is { IsEnabled: true } ? element : null;
        });

        button.Patterns.Invoke.Pattern.Invoke();
        Thread.Sleep(250);
    }

    private static void ToggleCheckBox(Window window, string automationId, TimeSpan timeout)
    {
        AutomationElement checkBox = WaitForElement(timeout, () =>
        {
            AutomationElement? element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            return element is { IsEnabled: true } ? element : null;
        });

        checkBox.Patterns.Toggle.Pattern.Toggle();
        Thread.Sleep(250);
    }

    private static void SelectTreeItem(AutomationElement descendant)
    {
        AutomationElement treeItem = FindAncestor(descendant, ControlType.TreeItem);
        treeItem.AsTreeItem().Select();
    }

    private static void SelectTab(Window window, string tabHeader, TimeSpan timeout)
    {
        AutomationElement tabText = WaitForText(window, tabHeader, timeout);
        AutomationElement tabItem = FindAncestor(tabText, ControlType.TabItem);
        tabItem.Patterns.SelectionItem.Pattern.Select();
    }

    private static void SelectDataItem(AutomationElement descendant)
    {
        AutomationElement dataItem = FindAncestor(descendant, ControlType.DataItem);
        dataItem.Patterns.SelectionItem.Pattern.Select();
    }

    private static AutomationElement FindAncestor(AutomationElement element, ControlType controlType)
    {
        for (AutomationElement? current = element; current is not null; current = current.Parent)
        {
            if (current.ControlType == controlType)
            {
                return current;
            }
        }

        throw new InvalidOperationException($"Could not find ancestor with control type {controlType}.");
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

    private static AutomationElement WaitForText(Window window, string text, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            AutomationElement? element = window.FindFirstDescendant(cf => cf.ByText(text));
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(250);
        }

        throw new InvalidOperationException(
            $"Text '{text}' did not appear within {timeout.TotalSeconds:0} seconds. Visible UI text: {CreateVisibleTextSnapshot(window)}");
    }

    private static string CreateVisibleTextSnapshot(Window window)
    {
        string[] names = window.FindAllDescendants()
            .Select(element => element.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Take(80)
            .ToArray();

        return names.Length == 0 ? "(none)" : string.Join(" | ", names);
    }

    private static AutomationElement WaitForAutomationId(Window window, string automationId, TimeSpan timeout)
    {
        return WaitForElement(timeout, () => window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)));
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
        await DeleteQueueAsync(adminClient, queueName, cancellationToken);

        if (await adminClient.TopicExistsAsync(topicName, cancellationToken))
        {
            await adminClient.DeleteTopicAsync(topicName, cancellationToken);
        }
    }

    private static async Task DeleteQueueAsync(
        ServiceBusAdministrationClient adminClient,
        string queueName,
        CancellationToken cancellationToken)
    {
        if (await adminClient.QueueExistsAsync(queueName, cancellationToken))
        {
            await adminClient.DeleteQueueAsync(queueName, cancellationToken);
        }
    }
}
