using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class SearchOperatorsProof
{
    public static async Task Exercise(PrototypeWindow window, List<string> report, string output)
    {
        var all = window.Workspace.SnapshotMessages().ToArray();
        var ids = all.Select(row => row.CorrelationId).Where(id => id.Length > 0).Distinct().Take(2).ToArray();
        await Search(window, ids[0] + " OR " + ids[1], "search-correlation");
        Check(window.Workspace.Messages.Select(row => row.Key).Order().SequenceEqual(all.Where(row => ids.Contains(row.CorrelationId)).Select(row => row.Key).Order()),
            "Correlation OR returns both exact IDs across the connection", report);
        ProofCapture.Save(window, output, "search-or");
        Check(window.Workspace.Suggestions(ids[0]).All(id => all.Any(row => row.CorrelationId == id)),
            "Compound searches are not offered as literal correlation IDs", report);
        var prefix = ids[0][..Math.Min(4, ids[0].Length)];
        await Search(window, prefix + "*", "search-correlation");
        Check(window.Workspace.Messages.Count > 0 && window.Workspace.Messages.Count == all.Count(row => row.CorrelationId.StartsWith(prefix, StringComparison.Ordinal)),
            "Correlation wildcard matches every ID with the requested prefix", report);
        await Search(window, "*", "search-message");
        Check(window.Workspace.Messages.Count == all.Length, "Message wildcard can search every sample message across the connection", report);
        var messages = all.Select(row => row.MessageId).Distinct().Take(2).ToArray();
        await Search(window, messages[0] + " or " + messages[1], "search-message");
        Check(window.Workspace.Messages.Count == all.Count(row => messages.Contains(row.MessageId)), "Message search supports case-insensitive OR operator syntax", report);
        await Search(window, ids[0].ToUpperInvariant(), "search-correlation");
        Check(window.Workspace.Messages.Count == all.Count(row => row.CorrelationId == ids[0].ToUpperInvariant()), "Search value matching stays case-sensitive", report);
        await Search(window, ".+?[](){}^$", "search-correlation");
        Check(window.Workspace.SearchQueryError.Length == 0 && window.Workspace.Messages.Count == 0,
            "Regex punctuation is treated as literal text rather than executable search syntax", report);
        await Search(window, "\"" + ids[0] + "\"", "search-correlation");
        Check(window.Workspace.Messages.Count == all.Count(row => row.CorrelationId == ids[0]), "Quoted exact correlation searches preserve literal IDs", report);
        await Search(window, ids[0] + " OR", "search-correlation");
        Check(window.Workspace.SearchQueryError.Length > 0 && !window.Workspace.IsSearching, "Incomplete OR query surfaces a validation error without starting a scan", report);
        Check(ProofCapture.Descendants(window).OfType<TextBlock>().Any(block => block.IsVisible && block.Text.Contains(window.Workspace.SearchQueryError)),
            "Invalid search explanation is visible in the rendered workspace", report);
        ProofCapture.Save(window, output, "search-invalid");
        ((Button)window.FindName("ClearSearchButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Settle();
        Check(window.Workspace.SearchQueryError.Length == 0 && !window.Workspace.IsCorrelationSearch && ((TextBox)window.FindName("SearchBox")).Text.Length == 0,
            "Clear recovers from invalid query into normal browsing", report);
    }

    private static async Task Search(PrototypeWindow window, string query, string kind)
    {
        var box = (TextBox)window.FindName("SearchBox");
        box.Focus();
        box.Text = query;
        await Settle();
        InvestigationProof.ChooseSearch(window, kind);
        for (var page = 0; window.Workspace.IsSearching && page < 1000; page++) window.Workspace.ScanNext();
        await Settle();
        if (window.Workspace.IsSearching) throw new InvalidOperationException("Operator search did not finish within bounded pages.");
    }

    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
