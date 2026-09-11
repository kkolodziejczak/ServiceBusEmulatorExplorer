using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

// An executable walkthrough of the real WPF surface, kept with this throwaway prototype.
internal static class PrototypeProof
{
    public static async Task<int> RunAsync(PrototypeWindow window, string output)
    {
        Directory.CreateDirectory(output);
        var report = new List<string> { "# Rendered prototype walkthrough", "" };
        try
        {
            await Settle();
            var workspace = window.Workspace;
            Check(workspace.EntityPath == "order-events/billing" && workspace.Messages.Count == 50,
                "Initial billing scope: 50 loaded of 120 fixture messages", report);
            Check(workspace.SelectedCount == 2 && workspace.FocusedMessage is not null,
                "Initial multi-selection and focused inspector", report);
            ProofCapture.Save(window, output, "01-desktop");

            var focused = workspace.FocusedMessage!.Key;
            var checkedKeys = workspace.Messages.Where(row => row.IsSelected).Select(row => row.Key).ToArray();
            Invoke(window, "RefreshButton");
            await Settle();
            Check(workspace.FocusedMessage?.Key == focused && checkedKeys.All(key => workspace.Messages.Any(row => row.Key == key && row.IsSelected)),
                "Refresh preserves focused body and checked rows", report);
            Invoke(window, "LoadMoreButton");
            await Settle();
            Check(workspace.Messages.Count == 100 && workspace.FocusedMessage?.Key == focused,
                "Load more adds a second page without replacing inspector", report);

            var grid = ProofCapture.Control<DataGrid>(window, "MessageGrid");
            grid.SelectedItem = workspace.Messages[2];
            await Settle();
            Check(workspace.FocusedMessage?.Key == workspace.Messages[2].Key,
                "Selecting a different grid row updates inspector", report);
            var first = workspace.Messages[0];
            var second = workspace.Messages[1];
            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(first);
            grid.SelectedItems.Add(second);
            grid.CurrentCell = new DataGridCellInfo(first, grid.Columns[1]);
            grid.SelectedItems.Remove(second);
            await Settle();
            Check(workspace.FocusedMessage?.Key == first.Key,
                "Returning to an already selected row updates focus after reducing selection", report);
            grid.CurrentCell = new DataGridCellInfo(workspace.Messages[2], grid.Columns[1]);
            grid.SelectedItem = workspace.Messages[2];
            await Settle();
            Invoke(window, "RawTab");
            await Settle();
            Check(Body(window).Replace("\r\n", "\n").Contains(workspace.FocusedMessage!.Body.Replace("\r\n", "\n"), StringComparison.Ordinal),
                "Raw inspector preserves original body text", report);
            var clipboard = Clipboard.GetDataObject();
            try
            {
                Invoke(window, "CopyBody");
                Check(Clipboard.GetText() == workspace.FocusedMessage!.Body, "Copy writes exact original body to clipboard", report);
            }
            finally
            {
                await RestoreClipboard(clipboard);
            }
            Invoke(window, "PropertiesTab");
            await Settle();
            Check(Body(window).Contains("correlationId", StringComparison.Ordinal), "Properties tab shows metadata", report);
            Invoke(window, "JsonTab");
            Check(double.IsNaN(ProofCapture.Control<RichTextBox>(window, "BodyViewer").Document.PageWidth)
                && ProofCapture.Control<RichTextBox>(window, "BodyViewer").HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled,
                "Body always wraps to inspector width with no Wrap toggle", report);
            Invoke(window, "FindButton");
            await Settle();
            ProofCapture.Control<TextBox>(window, "FindBox").Text = "orderId";
            Invoke(window, "FindNext");
            await Settle();
            Check(ProofCapture.Control<RichTextBox>(window, "BodyViewer").Selection.Text.Contains("orderId", StringComparison.OrdinalIgnoreCase),
                "Find selects a matching term in the rendered inspector", report);
            ProofCapture.Save(window, output, "02-find-long-json");
            // Restore a compact inspector before geometry checks.
            ProofCapture.Control<FrameworkElement>(window, "FindPanel").Visibility = Visibility.Collapsed;

            Invoke(window, "DeadLetterTab");
            await Settle();
            Check(workspace.IsDeadLetter && workspace.Messages.Count == 3, "Dead letter tab loads three DLQ fixtures", report);
            Invoke(window, "PropertiesTab");
            await Settle();
            Check(Body(window).Contains("MaxDeliveryCountExceeded", StringComparison.Ordinal), "DLQ properties show reason", report);
            ProofCapture.Save(window, output, "03-dead-letter");
            var original = workspace.FocusedMessage!;
            var originalBody = original.Body;
            var originalDlq = workspace.Messages.Count;
            var activeCount = int.Parse(workspace.SelectedEntity!.MessageCount);
            Invoke(window, "ReplayButton");
            await Settle();
            Check(int.Parse(workspace.SelectedEntity.MessageCount) == activeCount + 1 && workspace.Messages.Count == originalDlq
                && workspace.Messages.Contains(original) && original.Body == originalBody,
                "Replay adds an active copy while retaining original DLQ message and body", report);
            grid.SelectedItems.Add(workspace.Messages[0]);
            grid.SelectedItems.Add(workspace.Messages[1]);
            await Settle();
            Check(!ProofCapture.Control<Button>(window, "EditReplayButton").IsEnabled,
                "Editing is disabled for multiple checked DLQ messages", report);
            Invoke(window, "ReplayButton");
            await Settle();
            Check(int.Parse(workspace.SelectedEntity.MessageCount) == activeCount + 3 && workspace.Messages.Count == originalDlq,
                "Batch replay creates one new send per checked message without deleting originals", report);
            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(original);
            grid.CurrentCell = new DataGridCellInfo(original, grid.Columns[1]);
            await Settle();
            DriveReplayDialog(window, dialog => Invoke(dialog, "CancelReplay"));
            Check(int.Parse(workspace.SelectedEntity.MessageCount) == activeCount + 3,
                "Cancelling Edit and Replay does not create a copy", report);
            DriveReplayDialog(window, dialog =>
            {
                ProofCapture.Control<TextBox>(dialog, "ReplayId").Text = original.MessageId;
                Invoke(dialog, "ConfirmReplay");
                Check(dialog.IsVisible, "Replay editor rejects original message ID", report);
                ProofCapture.Control<TextBox>(dialog, "ReplayId").Text = "edited-prototype-proof";
                ProofCapture.Control<TextBox>(dialog, "ReplayBody").Text = "{\"edited\":true}";
                ProofCapture.Save(dialog, output, "10-edit-replay");
                dialog.Width = 560;
                dialog.Height = 440;
                dialog.UpdateLayout();
                ProofCapture.CheckBounds(dialog, new FrameworkElement[] { ProofCapture.Control<TextBox>(dialog, "ReplayId"), ProofCapture.Control<TextBox>(dialog, "ReplayBody"), ProofCapture.Control<Button>(dialog, "ConfirmReplay"), ProofCapture.Control<Button>(dialog, "CancelReplay") });
                ProofCapture.Save(dialog, output, "12-compact-editor");
                Invoke(dialog, "ConfirmReplay");
            });
            await Settle();
            Check(workspace.Messages.Count == originalDlq && original.Body == originalBody,
                "Edit and Replay leaves original DLQ body unchanged", report);
            Invoke(window, "ActiveTab");
            await Settle();
            Check(workspace.Messages.Count == 50 && workspace.Messages.Any(row => row.MessageId == "edited-prototype-proof" && row.Body == "{\"edited\":true}"),
                "Returning to Active resets to 50 and shows edited copy with new ID", report);

            SelectEntity(window, "order-events");
            await Settle();
            Check(workspace.SelectedEntity?.Kind == "Topic" && workspace.Messages.Select(row => row.Source).Distinct().Count() == 3,
                "Topic view shows copies from all three subscriptions", report);
            Check(workspace.Messages.Select(row => row.Key).Distinct().Count() == workspace.Messages.Count,
                "Topic delivery identities stay unique across subscriptions", report);
            ProofCapture.Save(window, output, "04-topic");

            var search = ProofCapture.Control<TextBox>(window, "SearchBox");
            search.Text = "audit";
            await Settle();
            Check(!PrototypeData.Flatten(workspace.Roots[1]).First().IsVisible, "Tree search filters unrelated branches", report);
            SelectEntity(window, "audit-events");
            Invoke(window, "JsonTab");
            await Settle();
            Check(workspace.Footer.Contains("unavailable", StringComparison.OrdinalIgnoreCase) && Body(window).Contains("incomplete JSON", StringComparison.Ordinal),
                "Unknown total and malformed JSON remain inspectable", report);
            ProofCapture.Save(window, output, "05-invalid-json");
            search.Text = "";
            await Settle();
            SelectEntity(window, "empty-queue");
            await Settle();
            Check(workspace.Messages.Count == 0 && workspace.FocusedMessage is null, "Empty scope clears previous inspector", report);
            Check(!ProofCapture.Control<Button>(window, "CopyBody").IsEnabled && !ProofCapture.Control<Button>(window, "LoadMoreButton").IsEnabled,
                "Empty scope disables copy and paging", report);
            ProofCapture.Save(window, output, "06-empty");

            SelectEntity(window, "order-events/billing");
            await Settle();
            var beforeIncoming = workspace.FocusedMessage!.Key;
            var countBeforeAutomatic = workspace.SelectedEntity!.MessageCount;
            var interval = ProofCapture.Control<ComboBox>(window, "AutoInterval");
            interval.SelectedIndex = 1;
            await Task.Delay(TimeSpan.FromSeconds(5.6));
            await Settle();
            Check(workspace.SelectedEntity!.MessageCount != countBeforeAutomatic && workspace.FocusedMessage?.Key == beforeIncoming,
                "Real 5-second automatic refresh adds sample arrivals and preserves focus", report);
            Invoke(window, "PauseButton");
            var pausedCount = workspace.SelectedEntity.MessageCount;
            await Task.Delay(TimeSpan.FromSeconds(5.6));
            Check(workspace.SelectedEntity.MessageCount == pausedCount, "Pause stops automatic arrivals", report);
            interval.SelectedIndex = 0;

            Invoke(window, "ConnectionButton");
            await Settle();
            Check(!workspace.IsConnected && workspace.Messages.Count == 0 && workspace.FocusedMessage is null,
                "Disconnect clears messages and inspector", report);
            ProofCapture.Save(window, output, "07-disconnected");
            Invoke(window, "ConnectionButton");
            await Settle();
            Check(workspace.IsConnected && workspace.Messages.Count > 0, "Reconnect restores a usable sample scope", report);

            window.Width = 980;
            window.Height = 640;
            await Settle();
            VerifyGeometry(window);
            ProofCapture.Save(window, output, "08-compact");
            Check(true, "980 × 640 window: primary controls remain in bounds", report);
            Invoke(window, "DeadLetterTab");
            await Settle();
            VerifyGeometry(window);
            ProofCapture.CheckBounds(window, new[] { ProofCapture.Control<Button>(window, "ReplayButton"), ProofCapture.Control<Button>(window, "EditReplayButton") });
            ProofCapture.Save(window, output, "11-compact-dlq");
            Check(true, "DLQ replay actions fit the compact window", report);
            Invoke(window, "ActiveTab");
            search.Focus();
            Check(search.IsKeyboardFocusWithin, "Search accepts keyboard focus", report);
            search.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
            Check(!search.IsKeyboardFocusWithin, "Tab traversal advances from search", report);

            window.Width = 1500;
            window.Height = 900;
            await Settle();
            VerifyGeometry(window);
            ProofCapture.Save(window, output, "09-desktop-final");
            Check(true, "Desktop layout restored after compact flow", report);
            report.Add("\nPASS — rendered walkthrough completed. Screenshots require visual review; this does not claim a screen-reader audit or broker integration proof.");
            File.WriteAllLines(Path.Combine(output, "report.md"), report);
            return 0;
        }
        catch (Exception exception)
        {
            report.Add("\nFAIL — " + exception);
            File.WriteAllLines(Path.Combine(output, "report.md"), report);
            ProofCapture.Save(window, output, "failure");
            return 1;
        }
    }

    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    private static async Task RestoreClipboard(IDataObject? previous)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (previous is null) Clipboard.Clear();
                else Clipboard.SetDataObject(previous, true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException exception)
                when (exception.HResult == unchecked((int)0x800401D0) && attempt < 2)
            {
                await Task.Delay(150);
            }
        }
    }

    private static void DriveReplayDialog(Window owner, Action<Window> action)
    {
        Exception? failure = null;
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<ReplayDialog>().Single();
            try { action(dialog); }
            catch (Exception exception) { failure = exception; dialog.Close(); }
        }), DispatcherPriority.ApplicationIdle);
        Invoke(owner, "EditReplayButton");
        if (failure is not null) throw new InvalidOperationException("Replay dialog walkthrough failed", failure);
    }

    private static void Check(bool condition, string description, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(description);
        report.Add("- PASS: " + description);
    }

    private static void Invoke(Window window, string name)
    {
        var control = ProofCapture.Control<ButtonBase>(window, name);
        if (!control.IsEnabled) throw new InvalidOperationException("Disabled action: " + name);
        // Routed Click exercises the same WPF handler as pointer/keyboard activation.
        control.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, control));
    }

    private static string Body(Window window)
    {
        var document = ProofCapture.Control<RichTextBox>(window, "BodyViewer").Document;
        return new TextRange(document.ContentStart, document.ContentEnd).Text;
    }

    private static void SelectEntity(Window window, string path)
    {
        var item = ProofCapture.Descendants(window).OfType<TreeViewItem>()
            .First(item => item.DataContext is EntityNode node && node.Path == path);
        var peer = UIElementAutomationPeer.CreatePeerForElement(item);
        ((ISelectionItemProvider)peer!.GetPattern(PatternInterface.SelectionItem)).Select();
    }

    private static void VerifyGeometry(Window window)
    {
        var controls = new[] { "SearchBox", "AutoInterval", "PauseButton", "MessageGrid", "BodyViewer", "ConnectionButton" }
            .Select(name => ProofCapture.Control<FrameworkElement>(window, name));
        ProofCapture.CheckBounds(window, controls);
    }
}
