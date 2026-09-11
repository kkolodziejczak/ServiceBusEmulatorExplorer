using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class UnifiedWorkspaceProof
{
    public static async Task Exercise(PrototypeWindow window, List<string> report, string output)
    {
        var workspace = window.Workspace;
        var original = workspace.SelectedEntity!;
        var box = (TextBox)window.FindName("SearchBox");
        var list = (ListBox)window.FindName("SuggestionsList");
        var popup = (Popup)window.FindName("SuggestionsPopup");
        var message = workspace.Messages.First();
        box.Focus();
        box.Text = message.MessageId;
        await Settle();
        var items = list.Items.Cast<SearchSuggestion>().ToArray();
        Check(items.Any(item => item.Kind == "message" && item.Message?.Key == message.Key), "Unified search suggests a loaded message with its typed identity", report);
        Check(items.Any(item => item.Kind == "search-correlation") && items.Any(item => item.Kind == "search-message"), "Unified search offers explicit namespace-wide correlation and message ID actions", report);
        Check(items.All(item => !string.IsNullOrEmpty(item.Group) && !string.IsNullOrEmpty(item.Title)), "Unified suggestions identify their category and target", report);
        var eventId = message.MessageId;
        InvestigationProof.ChooseSearch(window, "search-message");
        await Finish(window);
        Check(workspace.Messages.Count > 0 && workspace.Messages.All(row => row.MessageId == eventId), "Explicit message ID search returns only exact message ID matches", report);
        Check(!popup.IsOpen, "Choosing a global message search dismisses suggestions", report);
        Click(window, "ClearSearchButton");
        await Settle();
        Check(!workspace.IsCorrelationSearch && workspace.Roots.SelectMany(PrototypeData.Flatten).All(node => node.IsVisible), "Unified clear restores browsing and the complete namespace tree", report);
        box.Focus();
        box.Text = message.MessageId;
        await Settle();
        list.SelectedItem = list.Items.Cast<SearchSuggestion>().First(item => item.Kind == "message" && item.Message?.Key == message.Key);
        list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        await Settle();
        Check(workspace.FocusedMessage?.Key == message.Key && !workspace.IsCorrelationSearch, "Clicking a loaded message suggestion opens its entity and focuses that message", report);
        Click(window, "ClearSearchButton");
        await Settle();
        var log = (Grid)window.FindName("LogPanel");
        if (!log.IsVisible) Click(window, "LogToggle");
        await Settle();
        var text = (RichTextBox)window.FindName("LogText");
        Check(text.IsVisible && text.ActualWidth >= window.ActualWidth - 80, "Expanded activity log spans the application width", report);
        ProofCapture.CheckBounds(window, [log, text]);
        ProofCapture.Save(window, output, "unified-expanded-log");
        Click(window, "LogToggle");
        await Settle();
        Check(!text.IsVisible && !log.IsVisible, "Collapsing the full-width activity log releases workspace height", report);
        Click(window, "LogToggle");
        await Settle();
        Check(text.IsVisible && new TextRange(text.Document.ContentStart, text.Document.ContentEnd).Text.Length > 0, "Reopening the activity log retains operation history", report);
        Check(((TextBlock)window.FindName("LastOperation")).IsVisible, "Last operation remains visible in the bottom footer", report);
        ProofCapture.Descendants(window).OfType<TreeViewItem>().First(item => ReferenceEquals(item.DataContext, original)).IsSelected = true;
        await Settle();
    }

    private static async Task Finish(PrototypeWindow window)
    {
        for (var page = 0; window.Workspace.IsSearching && page < 1000; page++) window.Workspace.ScanNext();
        Check(!window.Workspace.IsSearching, "Bounded fixture search completes", []);
        await Settle();
    }

    private static void Click(PrototypeWindow window, string name) => ((ButtonBase)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
