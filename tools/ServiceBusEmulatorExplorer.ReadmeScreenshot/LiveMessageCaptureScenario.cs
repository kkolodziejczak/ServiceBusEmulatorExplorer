using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json.Nodes;
using System.Data.Common;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

/// <summary>
/// Exercises message capture through the running WPF inspector against a short-lived
/// live emulator topic. The demo topic is deliberately retained for manual inspection.
/// </summary>
internal static class LiveMessageCaptureScenario
{
    private const string SubscriptionName = "capture-demo";
    private const int WindowWidth = 1500;
    private const int WindowHeight = 1000;

    public static int Run(string outputDirectory)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exitCode = 1;
        application.Startup += async (_, _) =>
        {
            try
            {
                await RunScenarioAsync(application, outputDirectory);
                exitCode = 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }
            finally
            {
                application.Shutdown(exitCode);
            }
        };
        application.Run();
        return exitCode;
    }

    private static async Task RunScenarioAsync(Application application, string outputDirectory)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        ConnectionProfile profile = CreateEmulatorProfile();
        string runId = Guid.NewGuid().ToString("N")[..8];
        string topicName = $"workbench-demo-{runId}";
        var subscriptionAddress = new EntityAddress(EntityKind.Subscription, SubscriptionName, topicName);
        string activeId = $"capture-active-{runId}";
        string deadLetterId = $"capture-dlq-{runId}";
        var expected = new[]
        {
            CreateFixture(deadLetterId, "dead-letter", runId),
            CreateFixture(activeId, "active", runId)
        };

        PeekConnection peeks = await CreateDemoMessagesAsync(profile, topicName, expected, timeout.Token);
        Console.WriteLine($"Demo topic: {topicName}; subscription: {SubscriptionName}.");
        Console.WriteLine($"Demo Message IDs: {activeId}, {deadLetterId}.");
        var workflow = new BrokerConnectionWorkflow(
            () => new DirectServiceBusClientFactory(),
            factory => new InvestigationEntityBrowser(factory),
            factory => new ServiceBusMessageService(factory));
        var profileForWorkspace = new InvestigationProfile("capture-demo", profile);
        var preferences = new WorkspacePreferences
        {
            Profiles = [profileForWorkspace],
            SelectedProfileId = profileForWorkspace.Id,
            SelectedEntityPath = $"{topicName}/{SubscriptionName}",
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight,
            LogExpanded = false
        };
        var workspace = new InvestigationWorkspace(new ScenarioWorkspacePreferencesStore(preferences), workflow);
        NativeWindowSizeOverride? sizeOverride = null;
        var window = new InvestigationWindow(workspace)
        {
            Width = WindowWidth,
            Height = WindowHeight,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0
        };
        window.SourceInitialized += (_, _) =>
            sizeOverride = NativeWindowSizeOverride.Install(window, WindowWidth, WindowHeight);
        var rendered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.ContentRendered += (_, _) => rendered.TrySetResult(true);
        window.Show();

        try
        {
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            window.Width = WindowWidth;
            window.Height = WindowHeight;
            await IdleAsync(window);
            await workspace.ConnectAsync();
            EntityNode subscription = workspace.Browse.AllEntities().SingleOrDefault(node =>
                    node.Address == subscriptionAddress)
                ?? throw new InvalidOperationException($"The live entity browser did not discover {topicName}/{SubscriptionName}.");

            await CaptureBucketAsync(application, window, workspace, subscription, subscriptionAddress,
                MessageBucket.Active, expected.Single(fixture => fixture.MessageId == activeId), expected, peeks.Service, outputDirectory, timeout.Token);
            await CaptureBucketAsync(application, window, workspace, subscription, subscriptionAddress,
                MessageBucket.DeadLetter, expected.Single(fixture => fixture.MessageId == deadLetterId), expected, peeks.Service, outputDirectory, timeout.Token);

            IReadOnlyList<ExplorerMessage> activeAfter = await peeks.Service.PeekMessagesAsync(
                subscriptionAddress, MessageBucket.Active, 10, null, timeout.Token);
            IReadOnlyList<ExplorerMessage> deadLetterAfter = await peeks.Service.PeekMessagesAsync(
                subscriptionAddress, MessageBucket.DeadLetter, 10, null, timeout.Token);
            AssertUnchanged(expected, activeAfter, deadLetterAfter);

            Console.WriteLine($"PASS live message capture: original active and DLQ messages remain peekable after both routed captures.");
            Console.WriteLine($"Screenshots: {outputDirectory}");
        }
        finally
        {
            window.Close();
            sizeOverride?.Dispose();
            await workspace.DisposeAsync();
            await peeks.Factory.DisposeAsync();
        }
    }

    private static async Task CaptureBucketAsync(
        Application application,
        InvestigationWindow window,
        InvestigationWorkspace workspace,
        EntityNode subscription,
        EntityAddress address,
        MessageBucket bucket,
        Fixture fixture,
        IReadOnlyList<Fixture> expected,
        IServiceBusMessageService messageService,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ExplorerMessage> beforeActive = await messageService.PeekMessagesAsync(
            address, MessageBucket.Active, 10, null, cancellationToken);
        IReadOnlyList<ExplorerMessage> beforeDeadLetter = await messageService.PeekMessagesAsync(
            address, MessageBucket.DeadLetter, 10, null, cancellationToken);
        AssertUnchanged(expected, beforeActive, beforeDeadLetter);

        ((ToggleButton)window.FindName("InvestigationWorkspaceTab")!).IsChecked = true;
        await IdleAsync(window);
        await workspace.Browse.SelectAsync(subscription, bucket == MessageBucket.DeadLetter);
        await IdleAsync(window);
        MessageRow row = workspace.Browse.Messages.SingleOrDefault(candidate => candidate.MessageId == fixture.MessageId)
            ?? throw new InvalidOperationException($"The WPF {bucket} view did not load {fixture.MessageId}.");
        workspace.Browse.FocusedMessage = row;
        await IdleAsync(window);
        Require(window.FindName("SaveInspectedTemplateButton") is Button captureButton
            && captureButton.IsVisible && captureButton.IsEnabled,
            "The selected broker message must expose the real Create template inspector action.");

        string label = bucket == MessageBucket.Active ? "active" : "dead-letter";
        CaptureWorkspace(window, Path.Combine(outputDirectory, $"{label}-inspector.png"));
        await OpenCaptureDialogThroughInspectorAsync(application, window, fixture, bucket, address.TopicName!, outputDirectory);
        await IdleAsync(window);

        var library = (MessageLibraryPrototypeView)window.FindName("MessageLibraryPrototype")!;
        var bodyEditor = (ServiceBusEmulatorExplorer.App.Investigation.Inspection.JsonEditor)library.FindName("EditorText")!;
        Require(JsonNode.DeepEquals(JsonNode.Parse(bodyEditor.Text), JsonNode.Parse(fixture.Body))
            && !bodyEditor.Text.Contains(fixture.MessageId, StringComparison.Ordinal),
            $"The captured {bucket} template must exactly preserve the JSON body without copying the observed Message ID into it.");

        var propertiesTab = (Button)library.FindName("EditorPropertiesTab")!;
        propertiesTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, propertiesTab));
        await IdleAsync(window);
        var applicationProperties = (DataGrid)library.FindName("ApplicationPropertiesGrid")!;
        Require(applicationProperties.Items.Cast<object>().Any(item =>
                item.GetType().GetProperty("Name")?.GetValue(item)?.ToString() == "captureRun"
                && item.GetType().GetProperty("Value")?.GetValue(item)?.ToString() == fixture.RunId),
            $"The captured {bucket} template must preserve its application properties.");
        var destination = (ComboBox)library.FindName("TemplateDestination")!;
        ComboBoxItem selectedDestination = destination.SelectedItem as ComboBoxItem
            ?? throw new InvalidOperationException("The captured template has no selected destination item.");
        object? destinationKind = selectedDestination.DataContext?.GetType().GetProperty("Kind")
            ?.GetValue(selectedDestination.DataContext);
        Require(destination.SelectedValue?.ToString() == address.TopicName
            && destinationKind?.Equals(EntityKind.Topic) == true,
            $"The captured {bucket} template must keep {address.TopicName} typed as a topic destination.");
        CaptureWorkspace(window, Path.Combine(outputDirectory, $"{label}-workbench-draft.png"));
        Console.WriteLine($"PASS routed {bucket} capture: body, application property, and observed broker message remain available.");
    }

    private static async Task OpenCaptureDialogThroughInspectorAsync(
        Application application,
        InvestigationWindow window,
        Fixture fixture,
        MessageBucket bucket,
        string topicName,
        string outputDirectory)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        timer.Tick += (_, _) =>
        {
            MessageLibraryPrototypeDialog? dialog = application.Windows.OfType<MessageLibraryPrototypeDialog>()
                .SingleOrDefault(candidate => ReferenceEquals(candidate.Owner, window) && candidate.IsVisible);
            if (dialog is null) return;

            timer.Stop();
            try
            {
                string modeName = bucket == MessageBucket.Active ? "active" : "dead-letter";
                var name = dialog.FindName("CaptureName") as TextBox
                    ?? throw new InvalidOperationException("The capture dialog is missing its template name input.");
                name.Text = $"Captured {modeName} {fixture.RunId}";
                Require(dialog.FindName("CaptureOriginal") is RadioButton { IsChecked: true },
                    "The capture should default to the body as it was received.");
                var details = dialog.FindName("CaptureDetails") as Expander
                    ?? throw new InvalidOperationException("The capture dialog is missing its message details disclosure.");
                details.IsExpanded = true;
                Require(JsonNode.DeepEquals(JsonNode.Parse(dialog.CaptureTemplateBody), JsonNode.Parse(fixture.Body)),
                    $"The {modeName} capture preview must exactly preserve the original JSON body.");
                Require(dialog.CaptureTopic == topicName && dialog.CaptureDestinationKind == EntityKind.Topic,
                    $"The captured {modeName} message must keep its originating topic destination.");
                var exclusions = dialog.FindName("CaptureExclusions") as TextBlock
                    ?? throw new InvalidOperationException("Capture dialog must summarize copied and regenerated message metadata.");
                Require(exclusions.Text.Contains(fixture.MessageId, StringComparison.Ordinal)
                    && exclusions.Text.Contains("Generate new", StringComparison.Ordinal),
                    "The observed message ID must be shown as replaced by a newly generated ID.");
                string dialogPath = Path.Combine(outputDirectory, $"{modeName}-capture-dialog.png");
                dialog.UpdateLayout();
                var dialogContent = (FrameworkElement)dialog.Content;
                dialogContent.UpdateLayout();
                int contentWidth = (int)Math.Ceiling(dialogContent.ActualWidth);
                int contentHeight = (int)Math.Ceiling(dialogContent.ActualHeight);
                WpfScreenshot.SaveWindowContent(dialog, dialogPath, contentWidth, contentHeight);

                var openDraft = FindAutomationButton(dialog, "PrototypeOpenDraft")
                    ?? throw new InvalidOperationException("The capture dialog is missing Create draft action.");
                openDraft.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, openDraft));
                completed.TrySetResult(dialog.DialogResult == true);
            }
            catch (Exception exception)
            {
                dialog.Close();
                completed.TrySetException(exception);
            }
        };
        timer.Start();
        var captureButton = (Button)window.FindName("SaveInspectedTemplateButton")!;
        captureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, captureButton));
        bool opened = await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        timer.Stop();
        Require(opened, "Create draft must close the dialog with a successful result.");
    }

    private static Button? FindAutomationButton(FrameworkElement root, string automationId) =>
        Descendants(root).OfType<Button>().SingleOrDefault(button =>
            AutomationProperties.GetAutomationId(button) == automationId && button.IsVisible);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static async Task<PeekConnection> CreateDemoMessagesAsync(
        ConnectionProfile profile,
        string topicName,
        IReadOnlyList<Fixture> fixtures,
        CancellationToken cancellationToken)
    {
        var administration = new ServiceBusAdministrationClient(profile.AdministrationConnectionString);
        await using var runtime = new ServiceBusClient(profile.RuntimeConnectionString);
        await administration.CreateTopicAsync(topicName, cancellationToken);
        Console.WriteLine($"Created live demo topic: {topicName}.");
        await administration.CreateSubscriptionAsync(topicName, SubscriptionName, cancellationToken);
        await using (ServiceBusSender sender = runtime.CreateSender(topicName))
        {
            foreach (Fixture fixture in fixtures)
            {
                var message = new ServiceBusMessage(BinaryData.FromString(fixture.Body))
                {
                    MessageId = fixture.MessageId,
                    Subject = "OrderCreated",
                    ContentType = "application/json",
                    CorrelationId = $"correlation-{fixture.RunId}"
                };
                message.ApplicationProperties["captureRun"] = fixture.RunId;
                message.ApplicationProperties["origin"] = "live-capture-demo";
                await sender.SendMessageAsync(message, cancellationToken);
            }
        }

        await using ServiceBusReceiver receiver = runtime.CreateReceiver(topicName, SubscriptionName);
        IReadOnlyList<ServiceBusReceivedMessage> received = await receiver.ReceiveMessagesAsync(
            1, TimeSpan.FromSeconds(20), cancellationToken);
        if (received.Count != 1 || received[0].MessageId != fixtures[0].MessageId)
            throw new InvalidOperationException("Could not receive the designated demo message for dead lettering.");
        await receiver.DeadLetterMessageAsync(received[0], cancellationToken: cancellationToken);
        Console.WriteLine($"Live demo Message IDs: {string.Join(", ", fixtures.Select(fixture => fixture.MessageId))}.");

        var factory = new DirectServiceBusClientFactory();
        bool retained = false;
        try
        {
            await factory.ConnectAsync(profile, cancellationToken);
            var service = new ServiceBusMessageService(factory);
            EntityAddress address = new(EntityKind.Subscription, SubscriptionName, topicName);
            IReadOnlyList<ExplorerMessage> active = [];
            IReadOnlyList<ExplorerMessage> deadLetter = [];
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            do
            {
                active = await service.PeekMessagesAsync(address, MessageBucket.Active, 10, null, cancellationToken);
                deadLetter = await service.PeekMessagesAsync(address, MessageBucket.DeadLetter, 10, null, cancellationToken);
                if (active.Count == 1 && deadLetter.Count == 1) break;
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            } while (DateTimeOffset.UtcNow < deadline);
            AssertUnchanged(fixtures, active, deadLetter);
            retained = true;
            return new PeekConnection(factory, service);
        }
        finally
        {
            if (!retained) await factory.DisposeAsync();
        }
    }

    private sealed record PeekConnection(DirectServiceBusClientFactory Factory, IServiceBusMessageService Service);

    private static ConnectionProfile CreateEmulatorProfile()
    {
        ConnectionProfile defaults = ConnectionProfileDefaults.LocalEmulator;
        string runtimeConnectionString = Environment.GetEnvironmentVariable("SBE_CONNECTION_STRING")
            ?? defaults.RuntimeConnectionString;
        string administrationConnectionString = Environment.GetEnvironmentVariable("SBE_ADMIN_CONNECTION_STRING")
            ?? defaults.AdministrationConnectionString;
        EnsureLocalEmulatorConnection(runtimeConnectionString, expectedPort: -1, "SBE_CONNECTION_STRING");
        EnsureLocalEmulatorConnection(administrationConnectionString, expectedPort: 5300, "SBE_ADMIN_CONNECTION_STRING");
        return defaults with
        {
            RuntimeConnectionString = runtimeConnectionString,
            AdministrationConnectionString = administrationConnectionString
        };
    }

    private static void EnsureLocalEmulatorConnection(string connectionString, int expectedPort, string settingName)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        bool hasEndpoint = builder.TryGetValue("Endpoint", out object? endpointValue);
        bool hasEmulatorFlag = builder.TryGetValue("UseDevelopmentEmulator", out object? emulatorValue)
            && bool.TryParse(Convert.ToString(emulatorValue, System.Globalization.CultureInfo.InvariantCulture), out bool isEmulator)
            && isEmulator;
        bool isExpectedPort = Uri.TryCreate(Convert.ToString(endpointValue, System.Globalization.CultureInfo.InvariantCulture),
                UriKind.Absolute, out Uri? endpoint)
            && endpoint.Scheme == "sb"
            && endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && endpoint.Port == expectedPort;

        if (!hasEndpoint || !hasEmulatorFlag || !isExpectedPort)
            throw new InvalidOperationException($"{settingName} must target the local Service Bus development emulator; no broker changes were made.");
    }

    private static Fixture CreateFixture(string messageId, string marker, string runId) => new(
        messageId,
        marker,
        runId,
        $$"""{"orderId":"{{runId}}","status":"pending","captureMarker":"{{marker}}"}""");

    private static void AssertUnchanged(
        IReadOnlyList<Fixture> expected,
        IReadOnlyList<ExplorerMessage> active,
        IReadOnlyList<ExplorerMessage> deadLetter)
    {
        AssertBucket(expected.Where(fixture => fixture.BodyMarker != "active").ToArray(), deadLetter, "dead-letter");
        AssertBucket(expected.Where(fixture => fixture.BodyMarker == "active").ToArray(), active, "active");
    }

    private static void AssertBucket(IReadOnlyList<Fixture> expected, IReadOnlyList<ExplorerMessage> actual, string bucket)
    {
        Require(actual.Count == expected.Count,
            $"Expected {expected.Count} original message(s) in {bucket}, observed {actual.Count}.");
        foreach (Fixture fixture in expected)
        {
            ExplorerMessage observed = actual.SingleOrDefault(message => message.MessageId == fixture.MessageId)
                ?? throw new InvalidOperationException($"Original Message ID {fixture.MessageId} is missing from {bucket}.");
            Require(JsonNode.DeepEquals(JsonNode.Parse(observed.Body), JsonNode.Parse(fixture.Body)),
                $"The full JSON body of original Message ID {fixture.MessageId} changed in {bucket}.");
            Require(observed.ApplicationProperties.TryGetValue("captureRun", out object? runId)
                && runId?.ToString() == fixture.RunId,
                $"The {bucket} source application properties changed during template capture.");
        }
    }

    private static void CaptureWorkspace(Window window, string path)
    {
        window.UpdateLayout();
        WpfScreenshot.SaveWindowContent(window, path, WindowWidth, WindowHeight);
    }

    private static async Task IdleAsync(Window window)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Fixture(string MessageId, string BodyMarker, string RunId, string Body);
}
