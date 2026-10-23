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
            // The authentication change is an unsaved profile edit, so Done asks first (Settings 70); discard it.
            Window question = WaitForWindowWithAutomationId(application, automation, "SaveChangesDiscardButton", TimeSpan.FromSeconds(10));
            Assert.Contains("unsaved changes to", WaitForAutomationId(question, "SaveChangesQuestion", TimeSpan.FromSeconds(5)).Name,
                StringComparison.Ordinal);
            InvokeButton(question, "SaveChangesDiscardButton", TimeSpan.FromSeconds(5));
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
        await new ServiceBusEmulatorExplorer.App.Investigation.Settings.ProtectedWorkspacePreferencesStore(profilePath)
            .SaveAsync(new ServiceBusEmulatorExplorer.Core.Investigation.WorkspacePreferences { WasConnected = false }, CancellationToken.None);
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)} --message-workbench-prototype");
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
            // A valid CSV hides the View errors action.
            Assert.NotNull(WaitForText(window, "3 of 3 rows valid", TimeSpan.FromSeconds(5)));
            Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryValidationDetails")));

            InvokeButton(window, "LibraryMapColumns", TimeSpan.FromSeconds(5));
            Window mapping = WaitForWindowWithAutomationId(application, automation,
                "PrototypeApplyMapping", TimeSpan.FromSeconds(5));
            Assert.NotNull(WaitForText(mapping, "Map CSV inputs", TimeSpan.FromSeconds(5)));
            CapturePrototypeState(mapping, "map-csv-inputs");
            InvokeButton(mapping, "PrototypeApplyMapping", TimeSpan.FromSeconds(5));

            WaitForAutomationId(window, "LibrarySingleMode", TimeSpan.FromSeconds(5)).Patterns.SelectionItem.Pattern.Select();
            WaitForAutomationId(window, "LibraryCustomerId", TimeSpan.FromSeconds(5));
            AutomationElement review = WaitForAutomationId(window, "LibraryReview", TimeSpan.FromSeconds(5));
            Assert.True(SpinWait.SpinUntil(() => review.IsEnabled, TimeSpan.FromSeconds(5)));
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
    public async Task Message_library_prototype_filters_topics_and_sets_unassociated_destination_inline()
    {
        string executablePath = WpfAppPath.Resolve();
        string profilePath = Path.Combine(AppContext.BaseDirectory, "ui-smoke-profiles",
            Guid.NewGuid().ToString("N"), "connection-profiles.json");
        await new ServiceBusEmulatorExplorer.App.Investigation.Settings.ProtectedWorkspacePreferencesStore(profilePath)
            .SaveAsync(new ServiceBusEmulatorExplorer.Core.Investigation.WorkspacePreferences { WasConnected = false }, CancellationToken.None);
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)} --message-workbench-prototype");
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
            SelectTreeItem(unassociated);
            // A template without a destination stays in Compose until one is chosen.
            Assert.True(SpinWait.SpinUntil(() => !WaitForAutomationId(window, "LibraryContinueToPrepare", TimeSpan.FromSeconds(5)).IsEnabled,
                TimeSpan.FromSeconds(5)));
            InvokeButton(window, "LibraryEditorProperties", TimeSpan.FromSeconds(5));
            var destination = WaitForAutomationId(window, "TemplateDestination", TimeSpan.FromSeconds(5)).AsComboBox();
            // Choosing a destination rebuilds the choices, so let the selection settle before closing the list;
            // ComboBox.Select(text) collapses immediately and the closing list restores "Choose destination".
            destination.Expand();
            destination.Items.First(item => item.Name == "inventory-events, Topic").Patterns.SelectionItem.Pattern.Select();
            Assert.True(SpinWait.SpinUntil(() => destination.SelectedItem?.Name == "inventory-events, Topic",
                TimeSpan.FromSeconds(5)), "The chosen destination was not applied.");
            destination.Collapse();
            Assert.Equal("inventory-events, Topic", destination.SelectedItem?.Name);
            InvokeButton(window, "LibraryContinueToPrepare", TimeSpan.FromSeconds(5));
            Assert.True(SpinWait.SpinUntil(() => WaitForAutomationId(window, "LibraryReview", TimeSpan.FromSeconds(5)).IsEnabled,
                TimeSpan.FromSeconds(5)));

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
    public async Task Message_library_prototype_adds_folders_and_renames_templates_in_memory()
    {
        string executablePath = WpfAppPath.Resolve();
        string profilePath = Path.Combine(AppContext.BaseDirectory, "ui-smoke-profiles",
            Guid.NewGuid().ToString("N"), "connection-profiles.json");
        await new ServiceBusEmulatorExplorer.App.Investigation.Settings.ProtectedWorkspacePreferencesStore(profilePath)
            .SaveAsync(new ServiceBusEmulatorExplorer.Core.Investigation.WorkspacePreferences { WasConnected = false }, CancellationToken.None);
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)} --message-workbench-prototype");
        using var automation = new UIA3Automation();

        try
        {
            Window window = WaitForMainWindowWithAutomationId(application, automation,
                "ConnectionButton", TimeSpan.FromSeconds(15));
            AutomationElement libraryTab = WaitForAutomationId(window, "MessageLibraryTab", TimeSpan.FromSeconds(5));
            if (libraryTab.Patterns.Toggle.Pattern.ToggleState != ToggleState.On)
                libraryTab.Patterns.Toggle.Pattern.Toggle();

            InvokeButton(window, "LibraryAddFolder", TimeSpan.FromSeconds(5));
            Window addFolder = WaitForWindowWithAutomationId(application, automation,
                "LibraryNameInput", TimeSpan.FromSeconds(5));
            AutomationElement folderName = WaitForAutomationId(addFolder, "LibraryNameInput", TimeSpan.FromSeconds(5));
            folderName.Patterns.Value.Pattern.SetValue(string.Empty);
            InvokeButton(addFolder, "LibraryNameSave", TimeSpan.FromSeconds(5));
            Assert.NotNull(WaitForText(addFolder, "Use a unique folder name without path separators.", TimeSpan.FromSeconds(5)));

            folderName.Patterns.Value.Pattern.SetValue("UI Smoke Folder");
            InvokeButton(addFolder, "LibraryNameSave", TimeSpan.FromSeconds(5));
            AutomationElement createdFolder = WaitForText(window, "UI Smoke Folder", TimeSpan.FromSeconds(5));
            Assert.Equal(ControlType.TreeItem, FindAncestor(createdFolder, ControlType.TreeItem).ControlType);

            AutomationElement orderCreated = WaitForElement(TimeSpan.FromSeconds(5), () =>
                window.FindAllDescendants().FirstOrDefault(element =>
                    element.ControlType == ControlType.TreeItem && element.Name == "Order created"));
            SelectTreeItem(orderCreated);
            InvokeButton(window, "LibraryRename", TimeSpan.FromSeconds(5));
            AutomationElement renameInput = WaitForElement(TimeSpan.FromSeconds(5), () =>
                window.FindAllDescendants(cf => cf.ByAutomationId("LibraryRenameInput"))
                    .FirstOrDefault(element => !element.IsOffscreen));
            renameInput.Patterns.Value.Pattern.SetValue("Order created smoke renamed");
            WaitForAutomationId(window, "LibrarySave", TimeSpan.FromSeconds(5)).Focus();
            Assert.NotNull(WaitForText(window, "Order created", TimeSpan.FromSeconds(5)));
            Assert.Null(window.FindFirstDescendant(cf => cf.ByText("Order created smoke renamed")));
            InvokeButton(window, "LibraryRenameSave", TimeSpan.FromSeconds(5));
            Assert.NotNull(WaitForText(window, "Order created smoke renamed", TimeSpan.FromSeconds(5)));
            Assert.Null(window.FindFirstDescendant(cf => cf.ByText("Order created")));
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
        await new ServiceBusEmulatorExplorer.App.Investigation.Settings.ProtectedWorkspacePreferencesStore(profilePath)
            .SaveAsync(new ServiceBusEmulatorExplorer.Core.Investigation.WorkspacePreferences { WasConnected = false }, CancellationToken.None);
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)} --message-workbench-prototype");
        using var automation = new UIA3Automation();
        try
        {
            Window window = WaitForMainWindowWithAutomationId(application, automation,
                "ConnectionButton", TimeSpan.FromSeconds(15));
            AutomationElement libraryTab = WaitForAutomationId(window, "MessageLibraryTab", TimeSpan.FromSeconds(5));
            if (libraryTab.Patterns.Toggle.Pattern.ToggleState != ToggleState.On)
                libraryTab.Patterns.Toggle.Pattern.Toggle();
            InvokeButton(window, "LibraryContinueToPrepare", TimeSpan.FromSeconds(5));
            Assert.True(SpinWait.SpinUntil(() => WaitForAutomationId(window, "LibraryReview", TimeSpan.FromSeconds(5)).IsEnabled,
                TimeSpan.FromSeconds(5)));
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
        await new ServiceBusEmulatorExplorer.App.Investigation.Settings.ProtectedWorkspacePreferencesStore(profilePath)
            .SaveAsync(new ServiceBusEmulatorExplorer.Core.Investigation.WorkspacePreferences { WasConnected = false }, CancellationToken.None);
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)} --message-workbench-prototype");
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
        await new ServiceBusEmulatorExplorer.App.Investigation.Settings.ProtectedWorkspacePreferencesStore(profilePath)
            .SaveAsync(new ServiceBusEmulatorExplorer.Core.Investigation.WorkspacePreferences { WasConnected = false }, CancellationToken.None);
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)} --message-workbench-prototype");
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
            Assert.True(propertyGrid.Patterns.Grid.Pattern.RowCount >= 3);
            Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryAddProperty")));
            Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryDeleteSelectedProperty")));
            AutomationElement amountRow = FindAncestor(WaitForText(window, "amount", TimeSpan.FromSeconds(5)), ControlType.DataItem);
            amountRow.Patterns.SelectionItem.Pattern.Select();
            AutomationElement? rowDelete = amountRow.FindFirstDescendant(cf => cf.ByAutomationId("LibraryDeleteProperty"));
            Assert.NotNull(rowDelete);
            Assert.False(rowDelete.IsOffscreen);
            SetText(window, "LibrarySubject", "OrderCreatedEdited");
            InvokeButton(window, "LibrarySave", TimeSpan.FromSeconds(5));

            SelectTreeItem(WaitForText(window, "Order updated", TimeSpan.FromSeconds(5)));
            InvokeButton(window, "LibraryEditorProperties", TimeSpan.FromSeconds(5));
            Assert.Equal("OrderCreated", WaitForAutomationId(window, "LibrarySubject",
                TimeSpan.FromSeconds(5)).Patterns.Value.Pattern.Value);

            SelectTreeItem(WaitForText(window, "Order created", TimeSpan.FromSeconds(5)));
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
    public async Task Message_library_compact_layout_uses_shared_tree_and_switches_author_prepare()
    {
        string executablePath = WpfAppPath.Resolve();
        string profilePath = Path.Combine(AppContext.BaseDirectory, "ui-smoke-profiles",
            Guid.NewGuid().ToString("N"), "connection-profiles.json");
        await new ServiceBusEmulatorExplorer.App.Investigation.Settings.ProtectedWorkspacePreferencesStore(profilePath)
            .SaveAsync(new ServiceBusEmulatorExplorer.Core.Investigation.WorkspacePreferences { WasConnected = false }, CancellationToken.None);
        using Application application = Application.Launch(executablePath,
            $"--profile-store-path {QuoteProcessArgument(profilePath)} --message-workbench-prototype");
        using var automation = new UIA3Automation();
        try
        {
            Window window = WaitForMainWindowWithAutomationId(application, automation,
                "ConnectionButton", TimeSpan.FromSeconds(15));
            window.Patterns.Transform.Pattern.Resize(980, 640);
            AutomationElement libraryTab = WaitForAutomationId(window, "MessageLibraryTab", TimeSpan.FromSeconds(5));
            if (libraryTab.Patterns.Toggle.Pattern.ToggleState != ToggleState.On)
                libraryTab.Patterns.Toggle.Pattern.Toggle();
            WaitForAutomationId(window, "NamespaceTree", TimeSpan.FromSeconds(5));
            Assert.Contains("Sample entities", WaitForAutomationId(window, "NamespaceModeLabel", TimeSpan.FromSeconds(5)).Name);
            WaitForAutomationId(window, "LibraryTree", TimeSpan.FromSeconds(5));
            Assert.False(WaitForAutomationId(window, "LibraryAuthorEditor", TimeSpan.FromSeconds(5)).IsOffscreen);
            InvokeButton(window, "LibraryContinueToPrepare", TimeSpan.FromSeconds(5));
            Assert.False(WaitForAutomationId(window, "LibraryMapColumns", TimeSpan.FromSeconds(5)).IsOffscreen);
            Assert.True(SpinWait.SpinUntil(() => WaitForAutomationId(window, "LibraryReview", TimeSpan.FromSeconds(5)).IsEnabled,
                TimeSpan.FromSeconds(5)));
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
