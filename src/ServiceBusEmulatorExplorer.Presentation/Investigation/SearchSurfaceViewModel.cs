using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// What the message search shows around the result list: the status and summary, the empty-list and empty-namespace
/// messages, and which search buttons apply. A search for replay copies started from replay history summarises the
/// copies by delivery state instead of counting matches.
/// </summary>
public sealed class SearchSurfaceViewModel
{
    private readonly InvestigationWorkspace workspace;

    public SearchSurfaceViewModel(InvestigationWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
    }

    /// <summary>The query of the last search for replay copies, or <c>null</c>.</summary>
    public string? ReplayQuery { get; private set; }

    /// <summary>Remembers and returns the query that finds the given replay copies by message ID.</summary>
    public string QueryForReplays(IEnumerable<string> messageIds)
    {
        ArgumentNullException.ThrowIfNull(messageIds);
        ReplayQuery = string.Join(" OR ", messageIds.Distinct(StringComparer.Ordinal).Select(id => "message:" + MessageSearchQuery.QuoteLiteral(id)));
        return ReplayQuery;
    }

    /// <summary>Returns the query that finds a replay's original message; it is summarised as an ordinary search.</summary>
    public string QueryForOriginal(string messageId)
    {
        ArgumentNullException.ThrowIfNull(messageId);
        ReplayQuery = null;
        return "message:" + MessageSearchQuery.QuoteLiteral(messageId);
    }

    /// <param name="wide">The window is wide enough to show the summary of an ordinary search.</param>
    /// <param name="searchText">The text in the search box.</param>
    /// <param name="libraryVisible">The Message Library is shown instead of the message list.</param>
    public SearchSurfaceState StateFor(bool wide, string searchText, bool libraryVisible)
    {
        ArgumentNullException.ThrowIfNull(searchText);
        var search = workspace.Search;
        bool complete = search.IsComplete;
        bool replaySearch = ReplayQuery is not null && search.QueryText == ReplayQuery;
        string summary = replaySearch ? ReplayDeliverySummary.Format(search.Messages, complete)
            : $"{search.QueryText} · {search.Messages.Count:N0} matches{(complete ? "" : " so far")}";
        return new(search.IsActive, search.Status, complete, search.IsBusy,
            search.IsActive && (replaySearch || wide), summary, replaySearch, search.QueryText,
            search.IsActive && !libraryVisible ? "Counts show matching messages found so far. Clear search to restore entity totals." : null,
            search.IsActive ? "Continue" : "Load more",
            workspace.Surface.FocusedMessage is { CorrelationId.Length: > 0 },
            EmptyList(), CanClear(searchText));
    }

    /// <summary>The search box can be cleared while a search is shown or while it has text.</summary>
    public bool CanClear(string searchText) => workspace.Search.IsActive || searchText.Length > 0;

    /// <summary>Whether the namespace list says that no entity is listed, and what it says.</summary>
    public NamespaceEmptyState NamespaceEmptyFor(string searchText, bool libraryVisible)
    {
        ArgumentNullException.ThrowIfNull(searchText);
        var search = workspace.Search;
        bool hasVisibleNamespace = workspace.Surface.Roots.Any(node => node.IsVisible);
        bool showsEmpty = !libraryVisible && !hasVisibleNamespace
            && (search.IsActive || workspace.IsConnecting
                || workspace.IsConnected && workspace.Browse.DiscoverySnapshot is not null
                || !string.IsNullOrWhiteSpace(searchText));
        return new(showsEmpty, search.IsActive ? search.IsBusy ? "Searching for matching entities…"
            : "No matching entities found in the scanned deliveries. Clear the search to show all entities."
            : workspace.IsConnecting ? "Discovering entities…"
            : !string.IsNullOrWhiteSpace(searchText) ? "No matching entities. Clear the search to show all entities."
            : "No entities found on this connection.");
    }

    private SurfaceEmptyState? EmptyList()
    {
        var search = workspace.Search;
        if (search.IsActive)
            return new(search.QueryError.Length > 0 ? "Invalid search" : search.IsBusy ? "Searching messages…"
                    : search.IsComplete ? "No matching messages" : "No matches in the scanned deliveries",
                search.QueryError.Length > 0 ? search.QueryError + "\nUse OR between IDs and * to match any characters." : search.Status,
                ShowsClear: true, ReplacesList: false);
        if (workspace.Surface.Messages.Count > 0) return null;
        var surface = workspace.Surface;
        return new(surface.IsBusy ? "Loading messages…"
                : workspace.IsConnecting ? "Connecting to Service Bus…"
                : workspace.IsConnected ? "No messages in this view" : "Connect to browse messages",
            surface.IsBusy ? "Reading without consuming messages."
                : workspace.IsConnecting ? "Discovering available entities." : "",
            ShowsClear: false, ReplacesList: true);
    }
}

/// <summary>
/// The search surface: whether a search is shown, its status, whether it can be stopped, the summary and whether it
/// wraps (a replay summary does), the namespace tooltip while searching, the Load more or Continue label, whether
/// Find related applies, the empty-list message, and whether the search box can be cleared.
/// </summary>
public sealed record SearchSurfaceState(bool IsSearching, string Status, bool IsComplete, bool CanStop, bool ShowsSummary,
    string Summary, bool IsReplaySearch, string QueryText, string? NamespaceToolTip, string LoadMoreLabel, bool CanFindRelated,
    SurfaceEmptyState? EmptyList, bool CanClear)
{
    /// <summary>The status of a replay search when the list heading that carries the summary is hidden.</summary>
    public string StatusWithSummary => $"{Summary}\n{Status}";
}

/// <summary>Whether the namespace list says that no entity is listed, and the text it would say.</summary>
public sealed record NamespaceEmptyState(bool IsShown, string Text);

/// <summary>
/// The empty message list: the message and description, whether it offers to clear the search, and whether it
/// replaces the list (an empty browse view) or only fills an empty search result.
/// </summary>
public sealed record SurfaceEmptyState(string Message, string Description, bool ShowsClear, bool ReplacesList);
