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
                if (clipboard is null) Clipboard.Clear();
                else Clipboard.SetDataObject(clipboard, true);
            }
            Invoke(window, "PropertiesTab");
            await Settle();
            Check(Body(window).Contains("correlationId", StringComparison.Ordinal), "Properties tab shows metadata", report);
            Invoke(window, "JsonTab");
            var wrap = ProofCapture.Control<ToggleButton>(window, "WrapButton");
            wrap.IsChecked = false;
            Invoke(window, "WrapButton");
            Check(!double.IsNaN(ProofCapture.Control<RichTextBox>(window, "BodyViewer").Document.PageWidth),
                "Wrap off permits horizontal body scrolling", report);
            wrap.IsChecked = true;
            Invoke(window, "WrapButton");
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
            Invoke(window, "ActiveTab");

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
            var interval = ProofCapture.Control<ComboBox>(window, "AutoInterval");
            interval.SelectedIndex = 1;
            await Task.Delay(TimeSpan.FromSeconds(5.6));
            await Settle();
            Check(workspace.SelectedEntity!.MessageCount != "120" && workspace.FocusedMessage?.Key == beforeIncoming,
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
