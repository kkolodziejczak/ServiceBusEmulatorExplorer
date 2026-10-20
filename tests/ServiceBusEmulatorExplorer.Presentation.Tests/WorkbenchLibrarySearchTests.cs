using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class WorkbenchLibrarySearchTests
{
    private static readonly HashSet<string> NoneCreated = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Templates_match_by_name_description_or_folder_ignoring_case()
    {
        LibraryEntry[] templates =
        [
            new("Order created", "Emitted when an order is placed.", "Orders", "order-events"),
            new("Stock reserved", "Emitted when stock is held.", "Inventory", "inventory-events"),
            new("Reply", "Sample ORDER reply.", "", "order-replies"),
            new("Audit", "Nothing relevant.", "Archive/order-history", "audit")
        ];

        var suggestions = WorkbenchLibrarySearch.Suggest("order", "", templates, [], NoneCreated);

        Assert.Equal([
                new LibrarySuggestion("TEMPLATES", "Order created", "Orders", "Order created", false),
                new LibrarySuggestion("TEMPLATES", "Reply", "Root", "Reply", false),
                new LibrarySuggestion("TEMPLATES", "Audit", "Archive/order-history", "Audit", false)],
            suggestions);
    }

    [Fact]
    public void The_namespace_search_keeps_templates_whose_destination_contains_it()
    {
        LibraryEntry[] templates =
        [
            new("Order created", "", "Orders", "order-events"),
            new("Order reply", "", "Orders", "order-replies"),
            new("Order draft", "", "Orders", "")
        ];

        Assert.Equal(["Order created"], WorkbenchLibrarySearch.Suggest("order", "EVENTS", templates, [], NoneCreated)
            .Where(item => !item.IsFolder).Select(item => item.Title));
        Assert.Equal(3, WorkbenchLibrarySearch.Suggest("order", "", templates, [], NoneCreated).Count(item => !item.IsFolder));
    }

    [Fact]
    public void Folders_with_a_matching_segment_are_listed_when_they_hold_an_eligible_template_or_were_created()
    {
        LibraryEntry[] templates =
        [
            new("Refund", "", "Orders/Returns", "order-events"),
            new("Archived", "", "OrdersArchive", "audit")
        ];
        string[] folders = ["Orders", "Orders/Returns", "OrdersArchive", "Returns/Empty", "Orders/Old"];
        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "orders/old", "Returns/New" };

        var suggestions = WorkbenchLibrarySearch.Suggest("ret", "order", templates, folders, created)
            .Where(item => item.IsFolder).ToArray();

        Assert.Equal([
                new LibrarySuggestion("FOLDERS", "Returns", "Orders/Returns", "Orders/Returns", true),
                new LibrarySuggestion("FOLDERS", "New", "Returns/New", "Returns/New", true)],
            suggestions);
        Assert.Equal(["Orders", "Orders/Old", "Orders/Returns"], WorkbenchLibrarySearch.Suggest("orders", "order", templates, folders, created)
            .Where(item => item.IsFolder).Select(item => item.Path));
        Assert.Equal(["Orders/Returns", "Returns/New"], WorkbenchLibrarySearch.Suggest("TURN", "order", templates, folders, created)
            .Where(item => item.IsFolder).Select(item => item.Path));
        Assert.Equal(["OrdersArchive"], WorkbenchLibrarySearch.Suggest("orders", "", [new("Archived", "", "OrdersArchive", "")],
            ["Orders", "OrdersArchive"], NoneCreated).Where(item => item.IsFolder).Select(item => item.Path));
    }

    [Fact]
    public void Each_group_shows_at_most_five_suggestions_with_folders_sorted_by_path()
    {
        var templates = Enumerable.Range(1, 7).Select(index => new LibraryEntry($"Order {index}", "", $"F{8 - index}", "")).ToArray();
        string[] folders = templates.Select(template => template.Folder).ToArray();

        var suggestions = WorkbenchLibrarySearch.Suggest("o", "", templates, folders, NoneCreated);

        Assert.Equal(["Order 1", "Order 2", "Order 3", "Order 4", "Order 5"], suggestions.Where(item => !item.IsFolder).Select(item => item.Title));
        Assert.Empty(suggestions.Where(item => item.IsFolder));
        Assert.Equal(["a", "B", "c", "D", "e"], WorkbenchLibrarySearch.Suggest("x", "",
            [new("Box", "", "", "")], ["x/e", "x/D", "x/c", "X/B", "x/a", "x/f", "x/a"],
            new HashSet<string>(["x/e", "x/D", "x/c", "X/B", "x/a", "x/f"], StringComparer.OrdinalIgnoreCase))
            .Where(item => item.IsFolder).Select(item => item.Title));
    }

    [Fact]
    public void A_suggestion_names_its_type_for_screen_readers_and_icons()
    {
        var folder = new LibrarySuggestion("FOLDERS", "Returns", "Orders/Returns", "Orders/Returns", true);
        var template = new LibrarySuggestion("TEMPLATES", "Refund", "Orders/Returns", "Refund", false);

        Assert.Equal(("Folder", "Folder, Returns, Orders/Returns", "M1,4 H7 L9,6 H17 V16 H1 Z"),
            (folder.TypeLabel, folder.AccessibleName, folder.Icon));
        Assert.Equal(("Template", "Template, Refund, folder Orders/Returns", "M3,1 H13 L17,5 V19 H3 Z M6,8 H14 M6,11 H14 M6,14 H12"),
            (template.TypeLabel, template.AccessibleName, template.Icon));
    }

    [Theory]
    [InlineData(-1, true, 0)]
    [InlineData(-1, false, 2)]
    [InlineData(1, true, 2)]
    [InlineData(2, true, 0)]
    [InlineData(1, false, 0)]
    [InlineData(0, false, 2)]
    public void The_arrow_keys_move_through_the_suggestions_and_wrap(int selected, bool down, int expected) =>
        Assert.Equal(expected, WorkbenchLibrarySearch.NextIndex(selected, 3, down));
}
