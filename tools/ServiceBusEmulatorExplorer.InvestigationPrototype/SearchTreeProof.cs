using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class SearchTreeProof
{
    public static async Task Exercise(PrototypeWindow window, List<string> report, string output)
    {
        var workspace = window.Workspace;
        var nodes = workspace.Roots.SelectMany(PrototypeData.Flatten).ToArray();
        var totals = nodes.ToDictionary(node => node, node => (node.MessageCount, node.DlqCount));
        var source = (DataGridColumn)window.FindName("SourceColumn");
        Check(source.Visibility == Visibility.Collapsed, "Normal entity browsing hides redundant Location / State column", report);
        ProofCapture.Save(window, output, "browse-no-location");
        var ids = workspace.SnapshotMessages().Select(row => row.CorrelationId).Distinct().Take(2).ToArray();
        Begin(window, ids[0] + " OR " + ids[1], false);
        await Complete(window);
        Check(source.Visibility == Visibility.Visible, "Global correlation search shows Location / State for cross-entity results", report);
        AssertTree(window, report, "OR search");
        Check(nodes.All(node => totals[node] == (node.MessageCount, node.DlqCount)), "Search projections preserve underlying namespace totals", report);
        ProofCapture.Save(window, output, "search-tree");
        Begin(window, "*", true);
        await Complete(window);
        Check(source.Visibility == Visibility.Visible, "Global message ID search also shows Location / State", report);
        AssertTree(window, report, "Wildcard message search");
        Begin(window, "*", false);
        workspace.ScanNext();
        ((Button)window.FindName("StopSearchButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Settle();
        Check(!workspace.SearchComplete && !workspace.IsSearching && workspace.ScannedMessages == 50, "Stopping after one page retains a bounded partial search", report);
        AssertTree(window, report, "Stopped partial search");
        Begin(window, "no-results-tree-proof-unique", false);
        await Complete(window);
        Check(workspace.Messages.Count == 0 && nodes.All(node => !node.IsVisible), "No-results search hides every unmatched tree branch", report);
        ((Button)window.FindName("ClearSearchButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Settle();
        Check(!workspace.IsCorrelationSearch && source.Visibility == Visibility.Collapsed && nodes.All(node => node.IsVisible),
            "Clearing search restores the whole namespace tree and removes redundant result location column", report);
        Check(nodes.All(node => node.DisplayMessageCount == totals[node].MessageCount && node.DisplayDlqCount == totals[node].DlqCount),
            "Clearing search restores displayed namespace totals for every node", report);
    }

    private static void Begin(PrototypeWindow window, string query, bool messageId)
    {
        ((TextBox)window.FindName("SearchBox")).Text = query;
        typeof(PrototypeWindow).GetMethod("BeginGlobalSearch", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [messageId]);
    }

    private static async Task Complete(PrototypeWindow window)
    {
        for (var page = 0; window.Workspace.IsSearching && page < 1000; page++) window.Workspace.ScanNext();
        if (window.Workspace.IsSearching) throw new InvalidOperationException("Search tree proof exceeded bounded scan.");
        await Settle();
    }

    private static void AssertTree(PrototypeWindow window, List<string> report, string scenario)
    {
        var matches = window.Workspace.Messages.ToArray();
        foreach (var node in window.Workspace.Roots.SelectMany(PrototypeData.Flatten))
        {
            var sources = PrototypeData.Flatten(node).Where(child => !child.IsGroup && child.Kind != "Topic").Select(child => child.Path).ToHashSet();
            var rows = matches.Where(row => sources.Contains(row.Source)).ToArray();
            if (node.IsVisible != (rows.Length > 0)) throw new InvalidOperationException($"{scenario}: tree visibility differs for {node.Path}.");
            if (node.IsGroup) continue;
            if (node.DisplayMessageCount != rows.Count(row => !row.IsDeadLetter).ToString()
                || node.DisplayDlqCount != rows.Count(row => row.IsDeadLetter).ToString())
                throw new InvalidOperationException($"{scenario}: incorrect projected counts for {node.Path}: {node.DisplayMessageCount}/{node.DisplayDlqCount}.");
        }
        Check(true, scenario + " shows only matching sources and parents with exact Active / DLQ aggregate counts", report);
    }

    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
