using System.IO;
using Azure;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.ReadmeScreenshot;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationInspectorSearchTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Routed_find_searches_inspector_modes_and_refreshes_without_stealing_query_focus() => OnSta(() =>
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        InvestigationWorkspace workspace = CreateWorkspace();
        var window = new InvestigationWindow(workspace)
        {
            Width = 1500,
            Height = 1000,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        NativeWindowSizeOverride? sizeOverride = null;
        window.SourceInitialized += (_, _) => sizeOverride = NativeWindowSizeOverride.Install(window, 1500, 1000);
        try
        {
            window.Show();
            PumpUntil(dispatcher, () => window.IsVisible, "window to show");
            Complete(dispatcher, workspace.ConnectAsync());
            EntityNode queue = workspace.Browse.AllEntities().Single(node => node.Kind == nameof(EntityKind.Queue));
            Complete(dispatcher, workspace.Browse.SelectAsync(queue, deadLetter: false));
            window.Width = 1500;
            window.Height = 1000;
            window.UpdateLayout();

            var editor = (JsonEditor)window.FindName("BodyEditor")!;
            var findPanel = (FrameworkElement)window.FindName("FindPanel")!;
            var findBox = (TextBox)window.FindName("FindBox")!;
            var matchCount = (TextBlock)window.FindName("FindMatchCount")!;
            var findPrevious = (Button)window.FindName("FindPrevious")!;
            var findNext = (Button)window.FindName("FindNext")!;
            var closeFind = (Button)window.FindName("CloseFindButton")!;
            var viewer = (RichTextBox)window.FindName("BodyViewer")!;
            var saveTemplate = (Button)window.FindName("SaveInspectedTemplateButton")!;
            var replay = (Button)window.FindName("ReplayButton")!;
            var documentSurface = (Grid)window.FindName("InspectorDocumentSurface")!;
            var copy = (Button)window.FindName("CopyButton")!;
            var modifiedBadge = (Border)window.FindName("ModifiedBadge")!;
            var discard = (Button)window.FindName("DiscardButton")!;
            var bodyTab = (ToggleButton)window.FindName("JsonTab")!;

            Assert.Equal(Visibility.Visible, saveTemplate.Visibility);
            Assert.InRange(Math.Abs(saveTemplate.ActualHeight - 32), 0, 0.5);
            Assert.Equal(Visibility.Collapsed, replay.Visibility);
            Assert.True(bodyTab.IsChecked);
            Assert.Contains(Descendants(bodyTab), element => ReferenceEquals(element, modifiedBadge));
            Border tabSurface = (Border)bodyTab.Template.FindName("TabSurface", bodyTab)!;
            Assert.True(tabSurface.BorderThickness.Bottom > 0, "The selected Body surface must carry the tab underline.");
            int firstAlpha = editor.Text.IndexOf("alpha", StringComparison.Ordinal);
            int secondAlpha = editor.Text.IndexOf("alpha", firstAlpha + "alpha".Length, StringComparison.Ordinal);
            editor.Select(firstAlpha, "alpha".Length);
            foreach (var fold in editor.JsonFoldings) fold.IsFolded = true;
            Capture(window, "inspector-folded-1500x1000.png");
            ApplicationCommands.Find.Execute(null, editor);
            Assert.Equal(Visibility.Visible, findPanel.Visibility);
            window.UpdateLayout();
            Assert.InRange(Math.Abs(findPanel.ActualWidth - documentSurface.ActualWidth), 0, 1);
            Assert.Equal("alpha", findBox.Text);
            Assert.Equal("1 of 2", matchCount.Text);
            Assert.Equal("alpha", editor.SelectedText);
            Assert.Equal(firstAlpha, editor.SelectionStart);
            Assert.DoesNotContain(editor.JsonFoldings, fold => fold.IsFolded && fold.StartOffset < firstAlpha && fold.EndOffset > firstAlpha);

            findNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, findNext));
            Assert.Equal("alpha", editor.SelectedText);
            Assert.Equal(secondAlpha, editor.SelectionStart);
            findNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, findNext));
            Assert.Equal(firstAlpha, editor.SelectionStart);
            findPrevious.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, findPrevious));
            Assert.Equal(secondAlpha, editor.SelectionStart);

            findBox.Focus();
            findBox.Text = "not-present";
            Assert.Equal("No matches", matchCount.Text);
            Assert.Same(findBox, Keyboard.FocusedElement);
            findBox.Text = "alpha";
            Capture(window, "inspector-active-found-1500x1000.png");

            EntityNode deadLetterQueue = workspace.Browse.AllEntities().Single(node => node.Kind == nameof(EntityKind.Queue));
            Complete(dispatcher, workspace.Browse.SelectAsync(deadLetterQueue, deadLetter: true));
            PumpUntil(dispatcher, () => workspace.Browse.Messages.Count == 2
                && workspace.Inspector.Current is not null
                && workspace.Inspector.Document.Text.Contains("alpha", StringComparison.Ordinal),
                "the DLQ message to load into the inspector");
            editor.Select(firstAlpha, "alpha".Length);
            ApplicationCommands.Find.Execute(null, editor);
            findBox.Text = "alpha";
            PumpUntil(dispatcher, () => matchCount.Text == "1 of 2", "the routed DLQ search results");
            Assert.Equal(Visibility.Visible, replay.Visibility);
            Assert.InRange(Math.Abs(replay.ActualHeight - 32), 0, 0.5);
            Assert.InRange(Math.Abs(saveTemplate.ActualHeight - 32), 0, 0.5);

            int insertionOffset = firstAlpha + "alpha".Length;
            editor.Select(insertionOffset, 0);
            editor.Focus();
            editor.CaretOffset = insertionOffset;
            Assert.True(editor.IsKeyboardFocusWithin, "The editable DLQ body must keep keyboard focus during query refresh.");
            editor.Document.Insert(editor.CaretOffset, "x");
            int caretAfterFirstEdit = insertionOffset + 1;
            Assert.Equal(caretAfterFirstEdit, editor.CaretOffset);
            Assert.Equal(caretAfterFirstEdit, editor.SelectionStart);
            Assert.Equal(0, editor.SelectionLength);
            Assert.Equal("2 matches", matchCount.Text);

            editor.Document.Insert(editor.CaretOffset, "y");
            Assert.Contains("alphaxy", editor.Text, StringComparison.Ordinal);
            Assert.Equal(Visibility.Visible, modifiedBadge.Visibility);
            Assert.Equal(Visibility.Visible, discard.Visibility);
            Assert.Contains(Descendants(bodyTab), element => ReferenceEquals(element, modifiedBadge));
            Assert.Equal(caretAfterFirstEdit + 1, editor.CaretOffset);
            Assert.Equal(caretAfterFirstEdit + 1, editor.SelectionStart);
            Assert.Equal(0, editor.SelectionLength);
            Assert.Equal("2 matches", matchCount.Text);

            editor.Focus();
            window.UpdateLayout();
            Assert.Equal(1, copy.Opacity, 3);
            editor.ScrollToEnd();
            window.UpdateLayout();
            double documentOffset = editor.VerticalOffset;
            Assert.True(documentOffset > 0, "The long inspector fixture must scroll vertically.");
            Assert.Equal(1, copy.Opacity, 3);
            AssertVisibleInside(copy, documentSurface);
            Assert.True(copy.Margin.Right >= 24, "Copy must leave a gap beyond the vertical scrollbar.");
            Assert.True(copy.Focus(), "The Copy button must remain keyboard reachable.");
            window.UpdateLayout();
            Assert.Equal(documentOffset, editor.VerticalOffset, 1);
            Assert.Equal(1, copy.Opacity, 3);
            editor.Focus();

            findNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, findNext));
            Assert.Equal("1 of 2", matchCount.Text);
            Assert.Equal("alpha", editor.SelectedText);
            findPrevious.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, findPrevious));
            Assert.Equal("2 of 2", matchCount.Text);
            Assert.Equal(secondAlpha + 2, editor.SelectionStart);

            foreach ((double width, double height) in new[] { (1500d, 1000d), (1100d, 800d), (980d, 640d) })
            {
                window.Width = width;
                window.Height = height;
                PumpUntil(dispatcher, () => Math.Abs(window.ActualWidth - width) < 1 && Math.Abs(window.ActualHeight - height) < 1,
                    $"the {width}x{height} inspector viewport");
                window.UpdateLayout();
                Assert.InRange(Math.Abs(replay.ActualHeight - 32), 0, 0.5);
                Assert.True(editor.Padding.Right >= copy.ActualWidth + copy.Margin.Right,
                    "Responsive layout must preserve the document action lane so Copy cannot obscure long lines.");
                editor.Focus();
                editor.ScrollToEnd();
                window.UpdateLayout();
                Assert.Equal(1, copy.Opacity, 3);
                AssertVisibleInside(copy, documentSurface);
                AssertHoverAtCurrentScroll(documentSurface, copy, findBox);
                editor.Focus();
                Capture(window, $"inspector-copy-body-scrolled-{(int)width}x{(int)height}.png");
                Assert.InRange(Math.Abs(saveTemplate.ActualHeight - 32), 0, 0.5);
                Capture(window, $"inspector-dlq-found-{(int)width}x{(int)height}.png");
            }

            window.Width = 980;
            window.Height = 640;
            window.UpdateLayout();
            findBox.Text = "this-is-a-very-long-query-that-has-no-match-and-must-remain-inside-the-inspector-panel-0123456789";
            Assert.Equal("No matches", matchCount.Text);
            AssertVisibleInside(findBox, (FrameworkElement)window.FindName("InspectorPane")!);
            Capture(window, "inspector-narrow-long-query-no-match-980x640.png");
            closeFind.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, closeFind));
            Assert.Equal(Visibility.Collapsed, findPanel.Visibility);
            Capture(window, "inspector-find-closed-980x640.png");

            ApplicationCommands.Find.Execute(null, editor);
            Assert.Equal(Visibility.Visible, findPanel.Visibility);
            RaisePreviewKey(findBox, Key.Escape);
            Assert.Equal(Visibility.Collapsed, findPanel.Visibility);

            Complete(dispatcher, workspace.Browse.SelectAsync(queue, deadLetter: false));
            Assert.Equal(Visibility.Collapsed, replay.Visibility);
            CaptureDialogScreenshots(dispatcher, window);
            Complete(dispatcher, workspace.Browse.SelectAsync(queue, deadLetter: true));

            ApplicationCommands.Find.Execute(null, editor);
            findBox.Text = "alpha";
            PumpUntil(dispatcher, () => matchCount.Text == "1 of 2", "the reopened inspector search results");
            var propertiesTab = (ToggleButton)window.FindName("PropertiesTab")!;
            propertiesTab.IsChecked = true;
            window.UpdateLayout();
            Assert.Equal("1 of 1", matchCount.Text);
            Assert.Contains("alpha", viewer.Selection.Text, StringComparison.OrdinalIgnoreCase);
            foreach ((int width, int height) in new[] { (1500, 1000), (1100, 800), (980, 640) })
            {
                window.Width = width;
                window.Height = height;
                window.UpdateLayout();
                viewer.Focus();
                viewer.ScrollToEnd();
                window.UpdateLayout();
                double propertiesOffset = viewer.VerticalOffset;
                Assert.True(propertiesOffset > 0, "Properties must scroll to reproduce the reported failure.");
                Assert.Equal(1, copy.Opacity, 3);
                AssertVisibleInside(copy, documentSurface);
                Assert.True(viewer.Padding.Right >= copy.ActualWidth + copy.Margin.Right);
                Assert.True(copy.Focus());
                window.UpdateLayout();
                Assert.Equal(propertiesOffset, viewer.VerticalOffset, 1);
                AssertHoverAtCurrentScroll(documentSurface, copy, findBox);
                viewer.Focus();
                Capture(window, $"inspector-copy-properties-scrolled-{width}x{height}.png");
            }

            bodyTab.IsChecked = true;
            window.UpdateLayout();
            Assert.Equal("1 of 2", matchCount.Text);
            Assert.Contains("alpha", editor.SelectedText, StringComparison.OrdinalIgnoreCase);

            discard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, discard));
            Assert.DoesNotContain("alphaxy", editor.Text, StringComparison.Ordinal);
            Assert.Equal(Visibility.Collapsed, modifiedBadge.Visibility);
            Assert.Equal(Visibility.Collapsed, discard.Visibility);

            workspace.Browse.FocusedMessage = workspace.Browse.Messages[1];
            PumpUntil(dispatcher, () => matchCount.Text == "No matches", "the result count to refresh for the newly focused message");
            Assert.Equal("alpha", findBox.Text);
        }
        finally
        {
            // The fixture intentionally creates a DLQ draft; do not leave a modal prompt during teardown.
            workspace.ConfirmDiscard = () => Task.FromResult(true);
            if (window.IsVisible)
            {
                sizeOverride?.Dispose();
                window.Close();
                PumpUntil(dispatcher, () => !window.IsVisible, "window to close");
            }
            workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    });

    private static void AssertHoverAtCurrentScroll(Grid surface, Button copy, TextBox outside)
    {
        outside.Focus();
        Assert.False(surface.IsKeyboardFocusWithin);
        // Drive WPF's actual hover state and routed input handlers without moving the user's cursor.
        var key = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        var writeFlag = typeof(UIElement).GetMethod("WriteFlag",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var mouseOverFlag = Enum.Parse(writeFlag.GetParameters()[0].ParameterType, "IsMouseOverCache");
        bool originalHover = surface.IsMouseOver;
        try
        {
            foreach (bool hovered in new[] { false, true, false })
            {
                surface.SetValue(key, hovered);
                // IsMouseOver's CLR getter uses WPF's cached flag, not the dependency-property value.
                writeFlag.Invoke(surface, [mouseOverFlag, hovered]);
                Assert.Equal(hovered, surface.IsMouseOver);
                surface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                {
                    RoutedEvent = hovered ? UIElement.MouseEnterEvent : UIElement.MouseLeaveEvent
                });
                Assert.Equal(hovered ? 1 : 0, copy.Opacity, 3);
                Assert.Equal(hovered, copy.IsHitTestVisible);
                if (hovered) AssertVisibleInside(copy, surface);
            }
        }
        finally
        {
            surface.ClearValue(key);
            writeFlag.Invoke(surface, [mouseOverFlag, originalHover]);
        }
    }

    private static void CaptureDialogScreenshots(Dispatcher dispatcher, InvestigationWindow owner)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        Exception? failure = null;
        bool completed = false;
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        timer.Tick += (_, _) =>
        {
            MessageLibraryPrototypeDialog? dialog = owner.OwnedWindows.OfType<MessageLibraryPrototypeDialog>()
                .SingleOrDefault(candidate => candidate.IsVisible);
            if (dialog is null)
            {
                if (DateTime.UtcNow < deadline) return;
                failure = new TimeoutException("Create template did not open its capture dialog.");
                completed = true;
                timer.Stop();
                return;
            }

            timer.Stop();
            try
            {
                var details = (Expander)dialog.FindName("CaptureDetails")!;
                var copied = (Expander)dialog.FindName("CaptureCopiedProperties")!;
                var scroll = (ScrollViewer)dialog.FindName("DialogScroll")!;
                Assert.False(details.IsExpanded);
                Assert.False(copied.IsExpanded);
                dialog.UpdateLayout();
                AssertCaptureFooterBounds(dialog);
                Capture(dialog, "capture-default-natural.png");

                dialog.SizeToContent = SizeToContent.Manual;
                foreach ((int width, string state) in new[] { (480, "480x640"), (360, "360x640") })
                {
                    dialog.Width = width;
                    dialog.Height = 640;
                    details.IsExpanded = false;
                    copied.IsExpanded = false;
                    scroll.ScrollToTop();
                    PumpUntil(dispatcher, () => Math.Abs(dialog.ActualWidth - width) < 1
                        && Math.Abs(dialog.ActualHeight - 640) < 1,
                        $"the {width}x640 capture dialog viewport");
                    dialog.UpdateLayout();
                    Assert.False(details.IsExpanded);
                    Assert.False(copied.IsExpanded);
                    AssertCaptureFooterBounds(dialog);
                    if (width == 480) Capture(dialog, $"capture-default-{state}.png");

                    details.IsExpanded = true;
                    dialog.UpdateLayout();
                    AssertCaptureFooterBounds(dialog);
                    if (width == 480) Capture(dialog, $"capture-details-top-{state}.png");
                    scroll.ScrollToBottom();
                    dialog.UpdateLayout();
                    AssertCaptureFooterBounds(dialog);
                    if (width == 480) Capture(dialog, $"capture-details-bottom-{state}.png");

                    copied.IsExpanded = true;
                    scroll.ScrollToTop();
                    dialog.UpdateLayout();
                    AssertCaptureFooterBounds(dialog);
                    Capture(dialog, $"capture-details-copied-top-{state}.png");
                    scroll.ScrollToBottom();
                    dialog.UpdateLayout();
                    AssertCaptureFooterBounds(dialog);
                    Capture(dialog, $"capture-details-copied-bottom-{state}.png");
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                dialog.Close();
                completed = true;
            }
        };

        timer.Start();
        ((Button)owner.FindName("SaveInspectedTemplateButton")!).RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent, owner.FindName("SaveInspectedTemplateButton")));
        timer.Stop();
        if (!completed) throw new TimeoutException("Capture dialog screenshot work did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static InvestigationWorkspace CreateWorkspace()
    {
        var preferences = new WorkspacePreferences
        {
            Profiles = [new InvestigationProfile("inspector-search", new ConnectionProfile("Inspector search", "runtime", "admin"))],
            SelectedProfileId = "inspector-search",
            LogExpanded = false
        };
        var workflow = new BrokerConnectionWorkflow(
            () => new FakeFactory(),
            _ => new FakeBrowser(CreateSnapshot()),
            _ => new FakeMessages());
        return new InvestigationWorkspace(new FakeStore(preferences), workflow);
    }

    private static EntityDiscoverySnapshot CreateSnapshot()
    {
        var queue = new ServiceBusEntityNode(
            EntityKind.Queue,
            "orders",
            null,
            new EntityRuntimeCounts(2, 0, 0, 2),
            new EntityMetadata("orders", "Active", null, null, null, null, null, null, null));
        return new(
            [new EntityObservation(queue, new EntityCountObservation(
                new(queue.Counts.ActiveMessageCount, CountAvailability.Known),
                new(queue.Counts.DeadLetterMessageCount, CountAvailability.Known),
                new(queue.Counts.ScheduledMessageCount, CountAvailability.Known)))],
            DateTimeOffset.UtcNow,
            IsComplete: true,
            Issues: []);
    }

    private static ExplorerMessage MakeMessage(string id, long sequence, string body, string marker) => new(
        id,
        sequence,
        body,
        id,
        body.Length,
        DateTimeOffset.UtcNow,
        null,
        0,
        "application/json",
        $"correlation-{id}",
        null,
        id,
        new Dictionary<string, object?> { ["searchableProperty"] = marker },
        Enumerable.Range(0, 40).ToDictionary(index => $"system-{index:D2}", index => (object?)$"value-{index:D2}"))
    {
        RawBody = BinaryData.FromString(body)
    };

    private static void Capture(Window window, string filename)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_INVESTIGATION_UI"), "true", StringComparison.OrdinalIgnoreCase)) return;
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        WpfScreenshot.SaveWindowContent(window, ScreenshotPath(filename),
            (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), minimumBytes: 1000);
    }

    private static string ScreenshotPath(string filename)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !Directory.Exists(Path.Combine(directory.FullName, ".git"))
            && !File.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        if (directory is null) throw new DirectoryNotFoundException("The repository root was not found for screenshot output.");
        string output = Path.Combine(directory.FullName, "artifacts", "inspector-refinement", "rendered");
        Directory.CreateDirectory(output);
        return Path.Combine(output, filename);
    }

    private static void AssertCaptureFooterBounds(MessageLibraryPrototypeDialog dialog)
    {
        var footer = (FrameworkElement)dialog.FindName("CaptureFooter")!;
        var scroll = (FrameworkElement)dialog.FindName("DialogScroll")!;
        Assert.Equal(Visibility.Visible, footer.Visibility);
        AssertVisibleInside((FrameworkElement)dialog.FindName("CaptureFooter")!, (FrameworkElement)dialog.Content);
        Rect footerBounds = footer.TransformToAncestor(dialog).TransformBounds(new Rect(footer.RenderSize));
        Rect scrollBounds = scroll.TransformToAncestor(dialog).TransformBounds(new Rect(scroll.RenderSize));
        Assert.True(footerBounds.Top >= scrollBounds.Bottom - 1,
            $"Pinned capture actions must remain below the scrolling details: footer {footerBounds}, scroll {scrollBounds}.");
        foreach (Button button in Descendants(footer).OfType<Button>())
            AssertVisibleInside(button, (FrameworkElement)dialog.Content);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }

    private static void AssertVisibleInside(FrameworkElement element, FrameworkElement container)
    {
        Rect bounds = element.TransformToAncestor(container).TransformBounds(new Rect(element.RenderSize));
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1
            && bounds.Right <= container.ActualWidth + 1 && bounds.Bottom <= container.ActualHeight + 1,
            $"{element.Name} is clipped by {container.Name}: bounds {bounds}, size {container.ActualWidth}x{container.ActualHeight}.");
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The inspector find render proof exceeded its 60-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Complete(Dispatcher dispatcher, Task task)
    {
        PumpUntil(dispatcher, () => task.IsCompleted, "workspace operation to complete");
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Dispatcher dispatcher, Func<bool> condition, string expectation)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException($"Timed out waiting for {expectation}.");
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
    }

    private static void RaisePreviewKey(UIElement element, Key key)
    {
        var keyEvent = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element), 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        element.RaiseEvent(keyEvent);
    }

    private sealed class FakeStore(WorkspacePreferences initial) : IWorkspacePreferencesStore
    {
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(initial));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeFactory : IServiceBusClientFactory
    {
        public Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient AdministrationClient => null!;
        public Azure.Messaging.ServiceBus.ServiceBusClient RuntimeClient => null!;
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        private static readonly EntityAddress Queue = new(EntityKind.Queue, "orders");
        private readonly IReadOnlyList<ExplorerMessage> messages =
        [
            MakeMessage("first", 1, CreateSearchBody("alpha"), "alpha"),
            MakeMessage("second", 2, CreateSearchBody("beta"), "beta")
        ];

        private static string CreateSearchBody(string value)
        {
            string details = string.Join(",\n", Enumerable.Range(0, 40)
                .Select(index => $"    \"detail-{index:D2}\": \"value-{index:D2}\""));
            return $"{{\n  \"word\": \"{value}\",\n  \"nested\": {{\n    \"match\": \"{value}\"\n  }},\n{details}\n}}";
        }

        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address, MessageBucket bucket, int take, long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            IReadOnlyList<ExplorerMessage> selected = address == Queue
                && bucket is MessageBucket.Active or MessageBucket.DeadLetter ? messages : [];
            return Task.FromResult<IReadOnlyList<ExplorerMessage>>(selected
                .Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber.Value)
                .Take(take)
                .ToArray());
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
