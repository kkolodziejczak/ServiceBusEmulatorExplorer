using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

// Rendered WPF walkthrough. Uses routed control actions and real editor documents;
// it deliberately avoids physical mouse input, which is unavailable in some sessions.
internal static class InvestigationProof
{
    public static async Task<int> RunAsync(PrototypeWindow window, string output)
    {
        Directory.CreateDirectory(output);
        var report = new List<string> { "# Correlation investigation walkthrough", "", "Actual rendered WPF with synthetic messages; routed actions, no physical pointer automation.", "" };
        try
        {
            await Settle();
            await Browse(window, report, output);
            await NamespaceSearchProof.Exercise(window, report, output);
            await CorrelationSearch(window, report, output);
            await InlineReplay(window, report, output);
            await EmptySearch(window, report, output);
            await Compact(window, report, output);
            await BodyKinds(window, report, output);
            await RefreshAndConnection(window, report);
            File.WriteAllLines(Path.Combine(output, "report.md"), report);
            return 0;
        }
        catch (Exception exception)
        {
            report.Add("FAIL: " + exception);
            ProofCapture.Save(window, output, "failure");
            File.WriteAllLines(Path.Combine(output, "report.md"), report);
            return 1;
        }
    }

    private static async Task Browse(PrototypeWindow window, List<string> report, string output)
    {
        CheckSearchTextCenter(window, report);
        var workspace = window.Workspace;
        Check(workspace.Messages.Count == 50 && workspace.EntityPath == "order-events/billing", "Opening scope loads its first 50 sample messages", report);
        ProofCapture.Save(window, output, "01-desktop");
        await CheckboxSelectionProof.Exercise(window, report);
        var focused = workspace.FocusedMessage;
        var selected = workspace.Messages.Where(row => row.IsSelected).ToArray();
        Invoke(window, "RefreshButton");
        await Settle();
        Check(workspace.FocusedMessage == focused && selected.All(row => row.IsSelected), "Refresh preserves preview and checked messages", report);
        Invoke(window, "LoadMoreButton");
        await Settle();
        Check(workspace.Messages.Count == 100 && workspace.FocusedMessage == focused, "Load more retains preview and adds another page", report);
        Check(workspace.Messages.All(row => !string.IsNullOrEmpty(row.CorrelationId)), "Loaded rows expose typed correlation IDs", report);
        Check(Control<DataGrid>(window, "MessageGrid").Columns.Any(column => column.Header?.ToString()?.Contains("Correlation", StringComparison.OrdinalIgnoreCase) == true), "Message grid includes a correlation ID column", report);
        Invoke(window, "RawTab");
        await Settle();
        var viewer = Control<RichTextBox>(window, "BodyViewer");
        Check(new TextRange(viewer.Document.ContentStart, viewer.Document.ContentEnd).Text.Contains(workspace.FocusedMessage!.Body, StringComparison.Ordinal), "Raw tab retains the unformatted original body", report);
        Invoke(window, "PropertiesTab");
        await Settle();
        Check(new TextRange(viewer.Document.ContentStart, viewer.Document.ContentEnd).Text.Contains("correlationId", StringComparison.Ordinal), "Properties tab retains correlation metadata", report);
        Invoke(window, "JsonTab");
        Invoke(window, "FindButton");
        Control<TextBox>(window, "FindBox").Text = "orderId";
        Invoke(window, "FindNext");
        await Settle();
        Check(Control<JsonEditor>(window, "BodyEditor").SelectedText.Contains("orderId", StringComparison.Ordinal), "Find selects a match in the inline JSON editor", report);
        Control<FrameworkElement>(window, "FindPanel").Visibility = Visibility.Collapsed;
        Bounds(window, report);
    }

    private static async Task CorrelationSearch(PrototypeWindow window, List<string> report, string output)
    {
        var workspace = window.Workspace;
        var query = workspace.Messages[0].CorrelationId;
        var box = Control<TextBox>(window, "CorrelationBox");
        box.Focus();
        box.Text = query[..Math.Min(query.Length, 5)];
        await Settle();
        Check(workspace.Suggestions(box.Text).Any(), "Typing a correlation prefix suggests known IDs", report);
        // Popups have their own visual root; resolve the generated named element directly.
        var suggestions = (ListBox)window.FindName("SuggestionsList");
        Check(suggestions.IsVisible && suggestions.Items.Count > 0, "Suggestion popup is rendered while typing", report);
        ProofCapture.Save(window, output, "02-suggestions");
        SavePopup(suggestions, output);
        Key(box, System.Windows.Input.Key.Down);
        var firstSuggestion = suggestions.SelectedItem;
        Key(box, System.Windows.Input.Key.Down);
        Check(suggestions.SelectedIndex == 1, "Repeated Down advances to the second suggestion", report);
        Key(box, System.Windows.Input.Key.Up);
        Check(Equals(firstSuggestion, suggestions.SelectedItem), "Up returns to the first suggestion", report);
        Key(box, System.Windows.Input.Key.Enter);
        await Settle();
        Check(box.Text.Length > 5, "Keyboard Down and Enter choose a complete correlation ID", report);
        Check(Control<Button>(window, "FindMessagesButton").Content.ToString()!.Contains("Clear search criteria"), "Applied search changes the primary action to Clear search criteria", report);
        box.Text = "another-correlation";
        Check(Control<Button>(window, "FindMessagesButton").Content.ToString()!.Contains("Find messages"), "Changing the ID offers a new search", report);
        box.Text = "";
        Check(Control<Button>(window, "FindMessagesButton").IsEnabled && Control<Button>(window, "FindMessagesButton").Content.ToString()!.Contains("Clear search criteria"), "Emptying the input still allows clearing the applied filter", report);
        Invoke(window, "FindMessagesButton");
        await Settle();
        Check(!workspace.IsCorrelationSearch && box.Text == "" && workspace.Messages.Count > 0, "Primary Clear search criteria removes filter and restores browsing", report);
        box.Text = query;
        Invoke(window, "FindMessagesButton");
        await CompleteSearch(window);
        Check(workspace.IsCorrelationSearch && workspace.SearchComplete && !workspace.IsSearching, "Global correlation search completes visibly", report);
        CheckSearchTextCenter(window, report);
        Check(workspace.Messages.Count > 0 && workspace.Messages.All(row => row.CorrelationId == query), "Global results match the complete correlation ID exactly", report);
        var expectedKeys = PrototypeData.CreateMessages(workspace.Roots).Values.SelectMany(rows => rows)
            .Where(row => row.CorrelationId == query).Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        Check(expectedKeys.SetEquals(workspace.Messages.Select(row => row.Key)), "Search returns every matching fixture across the namespace, including later pages", report);
        Check(workspace.Messages.Select(row => row.Source).Distinct().Count() > 1, "Correlation results include multiple entity locations", report);
        Check(workspace.Messages.Any(row => row.IsDeadLetter) && workspace.Messages.Any(row => !row.IsDeadLetter), "Correlation results include both active and dead-letter messages", report);
        Check(workspace.ScannedMessages > 50, "Global search scans beyond the first page", report);
        Check(Control<TextBlock>(window, "SearchStatusText").Text.Length > 0, "Rendered search status reports scan progress or completion", report);
        var dlq = workspace.Messages.First(row => row.IsDeadLetter);
        Control<DataGrid>(window, "MessageGrid").SelectedItem = dlq;
        await Settle();
        CheckCopySpacing(window, report);
        var clipboard = Clipboard.GetDataObject();
        try
        {
            Invoke(window, "CopyCorrelationButton");
            for (var attempt = 0; Clipboard.GetText() != dlq.CorrelationId && attempt < 2
                && ((TextBox)window.FindName("LogText")).Text.Contains("Clipboard is busy", StringComparison.Ordinal); attempt++)
            {
                await Task.Delay(150);
                Invoke(window, "CopyCorrelationButton");
            }
            Check(Clipboard.GetText() == dlq.CorrelationId, "Copy correlation sends the exact ID to the clipboard for log lookup", report);
        }
        finally { await RestoreClipboard(clipboard); }
        Invoke(window, "FindRelatedButton");
        await CompleteSearch(window);
        Check(workspace.CorrelationQuery == dlq.CorrelationId, "Find related searches the focused message correlation globally", report);
        ProofCapture.Save(window, output, "03-related-results");
        box.Text = query.ToUpperInvariant();
        Invoke(window, "FindMessagesButton");
        await CompleteSearch(window);
        Check(workspace.Messages.Count == 0, "Correlation matching is case sensitive", report);
        Invoke(window, "FindMessagesButton");
        await Settle();
        Check(!workspace.IsCorrelationSearch && workspace.Messages.Count > 0, "Clear search returns to the entity workspace", report);
    }

    private static async Task InlineReplay(PrototypeWindow window, List<string> report, string output)
    {
        var workspace = window.Workspace;
        Invoke(window, "DeadLetterTab");
        await Settle();
        workspace.SetAllChecked(false);
        var grid = Control<DataGrid>(window, "MessageGrid");
        var original = workspace.Messages[0];
        grid.SelectedItem = original;
        await Settle();
        var editor = Control<JsonEditor>(window, "BodyEditor");
        var replay = Control<Button>(window, "ReplayButton");
        var body = original.Body;
        var formatted = JsonPresentation.Format(body);
        Check(editor.Text == formatted && editor.Text.Contains('\n'), "Inline JSON opens formatted without changing original bytes", report);
        Check(Label(replay) == "Replay" && !Control<FrameworkElement>(window, "ModifiedBadge").IsVisible, "Formatting alone leaves default Replay action and clean state", report);
        Check(editor.WordWrap && editor.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, "Inline JSON wraps to available inspector width", report);
        EditorProof.Exercise(editor, report);
        Invoke(window, "DiscardButton", requireEnabled: false);
        await Settle();
        var firstId = workspace.NextReplayId;
        Check(firstId.Contains("-replay-", StringComparison.Ordinal), "Default replay ID includes a readable replay suffix", report);
        Check(Control<TextBlock>(window, "NextReplayIdText").Text.Contains(firstId, StringComparison.Ordinal), "Inspector displays the proposed replay ID", report);
        Invoke(window, "ReplayButton");
        await Settle();
        Check(workspace.Messages.Contains(original) && original.Body == body, "Untouched replay retains the DLQ original", report);
        var secondId = workspace.NextReplayId;
        Check(secondId != firstId && Suffix(secondId) == Suffix(firstId) + 1, "Repeated replay advances the numbered default ID", report);
        Invoke(window, "ReplayButton");
        await Settle();
        Check(Suffix(workspace.NextReplayId) == Suffix(secondId) + 1, "A second replay advances the counter again", report);

        var changed = formatted.Replace("DHL", "FedEx", StringComparison.Ordinal);
        Check(changed != formatted, "Fixture includes a meaningful editable JSON value", report);
        editor.Text = changed;
        await Settle();
        Check(Label(replay) == "Edit and Replay" && Control<FrameworkElement>(window, "ModifiedBadge").IsVisible, "Changing JSON exposes Modified and Edit and Replay", report);
        Check(Control<Button>(window, "DiscardButton").IsVisible, "A dirty draft exposes Discard changes", report);
        ProofCapture.Save(window, output, "04-inline-modified");
        grid.SelectedItem = workspace.Messages[1];
        await Settle();
        grid.SelectedItem = original;
        await Settle();
        Check(editor.Text == changed && Label(replay) == "Edit and Replay", "Switching rows preserves the original row's draft", report);
        Invoke(window, "DiscardButton");
        await Settle();
        Check(editor.Text == formatted && Label(replay) == "Replay", "Discard restores original formatted JSON and Replay label", report);
        editor.Text = "{\"unfinished\":";
        await Settle();
        Check(!replay.IsEnabled, "Incomplete JSON cannot be sent as an edited replay", report);
        Invoke(window, "DiscardButton");
        editor.Text = changed;
        await Settle();
        var editedId = workspace.NextReplayId;
        Invoke(window, "ReplayButton");
        await Settle();
        Check(original.Body == body, "Edited replay leaves original DLQ body unchanged", report);

        Control<TextBox>(window, "CorrelationBox").Text = original.CorrelationId;
        Invoke(window, "FindMessagesButton");
        await CompleteSearch(window);
        Check(workspace.Messages.Where(row => row.MessageId == firstId).All(row => row.Body == body)
            && workspace.Messages.Any(row => row.MessageId == firstId), "Untouched replay copies exact original body bytes and preserves correlation", report);
        Check(workspace.Messages.Any(row => row.MessageId == editedId && row.Body == changed && !row.IsDeadLetter), "Edited JSON is sent in the new active copy with the same correlation", report);
        workspace.SetAllChecked(false);
        var dead = workspace.Messages.Where(row => row.IsDeadLetter && row.Body.Contains("DHL", StringComparison.Ordinal)).Take(2).ToArray();
        Check(dead.Length == 2, "Global results provide two DLQ messages for batch selection", report);
        var active = workspace.Messages.First(row => !row.IsDeadLetter);
        CheckboxSelectionProof.Click(CheckboxSelectionProof.RowCheck(grid, dead[0]));
        CheckboxSelectionProof.Click(CheckboxSelectionProof.RowCheck(grid, active));
        await Settle();
        Check(!replay.IsEnabled && !workspace.CanReplay, "Mixed active and DLQ checks block replay instead of silently skipping active rows", report);
        workspace.SetAllChecked(false);
        grid.SelectedItem = dead[0];
        await Settle();
        editor.Text = JsonPresentation.Format(dead[0].Body).Replace("DHL", "UPS", StringComparison.Ordinal);
        await Settle();
        ProofCapture.Save(window, output, "04-global-inline-modified");
        foreach (var row in dead) CheckboxSelectionProof.Click(CheckboxSelectionProof.RowCheck(grid, row));
        await Settle();
        Check(!replay.IsEnabled, "Batch selection with one draft blocks replay", report);
        grid.SelectedItem = dead[1];
        await Settle();
        Check(!replay.IsEnabled, "Focusing a clean row does not bypass a checked draft", report);
        editor.Text = JsonPresentation.Format(dead[1].Body) + "\n";
        await Settle();
        Check(!replay.IsEnabled, "Multiple checked drafts require individual replay or discard", report);
        grid.SelectedItem = dead[0];
        await Settle();
        Invoke(window, "DiscardButton");
        Check(!replay.IsEnabled, "Discarding one draft does not ignore another checked draft", report);
        grid.SelectedItem = dead[1];
        await Settle();
        Invoke(window, "DiscardButton");
        Check(workspace.ReplayTargets.Count == 2 && replay.IsEnabled, "Two checked DLQ results form a replay batch", report);
        Invoke(window, "ReplayButton");
        await Settle();
        Check(dead.All(row => row.Body.Length > 0 && row.IsDeadLetter), "Batch replay retains both DLQ originals", report);
        Invoke(window, "FindMessagesButton");
        await Settle();
    }

    private static async Task EmptySearch(PrototypeWindow window, List<string> report, string output)
    {
        var workspace = window.Workspace;
        const string absent = "missing-correlation-proof-48391";
        Control<TextBox>(window, "CorrelationBox").Text = absent;
        Invoke(window, "FindMessagesButton");
        await CompleteSearch(window);
        Check(workspace.SearchComplete && workspace.Messages.Count == 0 && workspace.FocusedMessage is null, "Completed zero-result search clears stale rows and focused message", report);
        Check(Control<FrameworkElement>(window, "EmptyResults").IsVisible && Control<FrameworkElement>(window, "EmptyInspector").IsVisible, "No-results state is rendered in list and inspector", report);
        Check(!Control<Button>(window, "ReplayButton").IsVisible, "No-results state offers no stale replay action", report);
        ProofCapture.Save(window, output, "05-no-results");
        workspace.StartCorrelationSearch(absent);
        workspace.StopCorrelationSearch();
        await Settle();
        Check(!workspace.SearchComplete && !workspace.IsSearching && workspace.SearchStatus.Contains("incomplete", StringComparison.OrdinalIgnoreCase), "Canceled zero-result scan explicitly says search incomplete", report);
        ProofCapture.Save(window, output, "06-incomplete-search");
        Invoke(window, "FindMessagesButton");
        await Settle();
        Check(!workspace.IsCorrelationSearch && workspace.Messages.Count > 0, "Empty search clear action restores entity browsing", report);
    }

    private static async Task Compact(PrototypeWindow window, List<string> report, string output)
    {
        Invoke(window, "ActiveTab");
        await Settle();
        window.Width = 1100;
        window.Height = 800;
        await Settle();
        await CheckboxSelectionProof.Exercise(window, report);
        Control<DataGrid>(window, "MessageGrid").SelectedItem = window.Workspace.Messages.OrderByDescending(row => row.Body.Length).First();
        await Settle();
        Bounds(window, report);
        Check(Control<JsonEditor>(window, "BodyEditor").WordWrap, "Compact inspector retains JSON wrapping", report);
        ProofCapture.Save(window, output, "07-compact");
        window.Width = 980;
        window.Height = 640;
        Invoke(window, "DeadLetterTab");
        await Settle();
        window.Workspace.SetAllChecked(false);
        var editor = Control<JsonEditor>(window, "BodyEditor");
        editor.Text += "\n";
        await Settle();
        Bounds(window, report);
        Check(editor.ActualHeight - editor.Padding.Top - editor.Padding.Bottom >= 64, "Minimum viewport retains four lines of usable JSON editor", report);
        var scroll = ProofCapture.Descendants(Control<DataGrid>(window, "MessageGrid")).OfType<System.Windows.Controls.Primitives.DataGridRowsPresenter>().First();
        Check(scroll.ActualHeight >= Control<DataGrid>(window, "MessageGrid").RowHeight, "Minimum viewport displays at least one complete message row", report);
        var firstRow = ProofCapture.Descendants(Control<DataGrid>(window, "MessageGrid")).OfType<DataGridRow>().First(row => row.Item == window.Workspace.Messages[0]);
        foreach (var text in ProofCapture.Descendants(firstRow).OfType<TextBlock>().Where(text => text.Text == window.Workspace.Messages[0].MessageId))
            Check(text.TransformToAncestor(firstRow).TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight)).Bottom <= firstRow.ActualHeight,
                "Message ID text fits fully inside the compact row", report);
        Check(Control<Button>(window, "ReplayButton").IsVisible && Control<Button>(window, "DiscardButton").IsVisible, "Minimum viewport exposes replay and discard for a draft", report);
        ProofCapture.Save(window, output, "08-minimum-draft");
        Invoke(window, "DiscardButton");
        window.Width = 1580;
        window.Height = 960;
        await Settle();
    }

    private static async Task RefreshAndConnection(PrototypeWindow window, List<string> report)
    {
        Invoke(window, "ActiveTab");
        await Settle();
        var workspace = window.Workspace;
        var initialCount = int.Parse(workspace.SelectedEntity!.MessageCount);
        var focus = workspace.FocusedMessage;
        var interval = Control<ComboBox>(window, "AutoInterval");
        interval.SelectedIndex = 1;
        Control<TextBox>(window, "CorrelationBox").Text = "checkout-80341";
        Invoke(window, "FindMessagesButton");
        await CompleteSearch(window);
        var otherEntity = ProofCapture.Descendants(window).OfType<TreeViewItem>()
            .First(item => item.DataContext is EntityNode node && node.Path == "order-events/analytics");
        otherEntity.IsSelected = true;
        await Settle();
        initialCount = int.Parse(workspace.SelectedEntity!.MessageCount);
        focus = workspace.FocusedMessage;
        await Task.Delay(TimeSpan.FromSeconds(5.6));
        await Settle();
        Check(!workspace.IsCorrelationSearch && int.Parse(workspace.SelectedEntity.MessageCount) > initialCount && workspace.FocusedMessage == focus, "Exiting search through the tree resumes five-second automatic refresh and preserves preview", report);
        Invoke(window, "PauseButton");
        var pausedCount = workspace.SelectedEntity.MessageCount;
        await Task.Delay(TimeSpan.FromSeconds(5.6));
        await Settle();
        Check(workspace.SelectedEntity.MessageCount == pausedCount, "Pausing automatic refresh prevents incoming sample messages", report);
        Invoke(window, "PauseButton");
        interval.SelectedIndex = 0;
        Invoke(window, "ConnectionButton");
        await Settle();
        Check(!workspace.IsConnected && workspace.Messages.Count == 0 && workspace.FocusedMessage is null, "Disconnect clears rows and focused inspector", report);
        Check(Control<FrameworkElement>(window, "EmptyInspector").IsVisible && !Control<Button>(window, "ReplayButton").IsVisible, "Disconnected state hides stale replay controls", report);
        Invoke(window, "ConnectionButton");
        await Settle();
        Check(workspace.IsConnected && workspace.Messages.Count == 50 && workspace.FocusedMessage is not null, "Reconnect restores a focused first page of sample messages", report);
    }

    private static async Task BodyKinds(PrototypeWindow window, List<string> report, string output)
    {
        var audit = ProofCapture.Descendants(window).OfType<TreeViewItem>().First(item => item.DataContext is EntityNode node && node.Path == "audit-events");
        audit.IsSelected = true;
        Invoke(window, "ActiveTab");
        await Settle();
        var grid = Control<DataGrid>(window, "MessageGrid");
        foreach (var row in window.Workspace.Messages.Take(2))
        {
            grid.SelectedItem = row;
            await Settle();
            Check(Control<JsonEditor>(window, "BodyEditor").Text == row.Body && Control<ToggleButton>(window, "JsonTab").Content.ToString() == "Body (not JSON)", "Plain text and malformed JSON are labelled and preserved exactly", report);
        }
        ProofCapture.Save(window, output, "09-non-json");
        grid.SelectedItem = window.Workspace.Messages[2];
        await Settle();
        Check(Control<ToggleButton>(window, "JsonTab").Content.ToString() == "JSON", "Valid JSON returns to the formatted JSON label", report);
        var billing = ProofCapture.Descendants(window).OfType<TreeViewItem>().First(item => item.DataContext is EntityNode node && node.Path == "order-events/billing");
        billing.IsSelected = true;
        await Settle();
    }

    private static void CheckCopySpacing(PrototypeWindow window, List<string> report)
    {
        var value = Control<TextBlock>(window, "CorrelationValue");
        var copy = Control<Button>(window, "CopyCorrelationButton");
        var end = value.TranslatePoint(new Point(value.ActualWidth, 0), window).X;
        var start = copy.TranslatePoint(new Point(0, 0), window).X;
        Check(start - end is >= 0 and <= 10, "Correlation copy icon sits within 10 pixels of its ID", report);
    }

    private static void CheckSearchTextCenter(PrototypeWindow window, List<string> report)
    {
        var button = Control<Button>(window, "FindMessagesButton");
        button.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)button.ActualWidth, (int)button.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(button), null, new Rect(0, 0, button.ActualWidth, button.ActualHeight));
        bitmap.Render(visual);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var rows = Enumerable.Range(0, bitmap.PixelHeight).Where(y => Enumerable.Range(0, bitmap.PixelWidth).Any(x =>
        {
            var i = (y * bitmap.PixelWidth + x) * 4;
            return pixels[i + 3] > 32 && pixels[i] >= pixels[i + 3] * 0.85 && pixels[i + 1] >= pixels[i + 3] * 0.85 && pixels[i + 2] >= pixels[i + 3] * 0.85;
        })).ToArray();
        var center = (rows.First() + rows.Last() + 1) / 2.0;
        var offset = Math.Abs(center - button.ActualHeight / 2);
        Check(offset <= 1, $"Search button visible label is vertically centered (offset {offset:F1}px)", report);
    }

    private static async Task CompleteSearch(PrototypeWindow window)
    {
        for (var page = 0; window.Workspace.IsSearching && page < 1000; page++) window.Workspace.ScanNext();
        if (window.Workspace.IsSearching) throw new InvalidOperationException("Synthetic correlation scan failed to finish within 1000 pages.");
        await Settle();
    }

    private static void Bounds(PrototypeWindow window, List<string> report)
    {
        ProofCapture.CheckBounds(window, new FrameworkElement[] { Control<TextBox>(window, "CorrelationBox"), Control<Button>(window, "FindMessagesButton"), Control<DataGrid>(window, "MessageGrid"), Control<FrameworkElement>(window, "InspectorPane"), Control<JsonEditor>(window, "BodyEditor"), Control<Button>(window, "ReplayButton"), Control<Button>(window, "DiscardButton"), Control<Button>(window, "FindRelatedButton"), Control<Button>(window, "CopyCorrelationButton") });
        Check(true, $"Primary controls fit the {window.ActualWidth:F0} × {window.ActualHeight:F0} viewport", report);
    }

    private static int Suffix(string id) => int.Parse(id[(id.LastIndexOf('-') + 1)..]);
    private static string Label(ContentControl control) => (control.Content is string text ? text : string.Join(" ", ProofCapture.Descendants(control).OfType<TextBlock>().Select(block => block.Text))).TrimStart('▶', ' ');
    private static T Control<T>(DependencyObject root, string name) where T : FrameworkElement => ProofCapture.Control<T>(root, name);
    private static void Invoke(PrototypeWindow window, string name, bool requireEnabled = true)
    {
        var button = Control<ButtonBase>(window, name);
        if (requireEnabled && !button.IsEnabled) throw new InvalidOperationException("Action disabled: " + name);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
    private static void Key(UIElement control, Key key) => control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(control), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void SavePopup(FrameworkElement suggestions, string output)
    {
        var source = PresentationSource.FromVisual(suggestions)?.RootVisual as FrameworkElement
            ?? throw new InvalidOperationException("Suggestion popup has no rendered visual root.");
        source.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(source.ActualWidth), (int)Math.Ceiling(source.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(source);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, "02-suggestions-popup.png"));
        encoder.Save(stream);
    }
    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string text, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(text);
        report.Add("- PASS: " + text);
    }
    private static async Task RestoreClipboard(IDataObject? original)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { if (original is null) Clipboard.Clear(); else Clipboard.SetDataObject(original, true); return; }
            catch (System.Runtime.InteropServices.COMException exception) when (exception.HResult == unchecked((int)0x800401D0) && attempt < 2) { await Task.Delay(80); }
        }
    }
}
