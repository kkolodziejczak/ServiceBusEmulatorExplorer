using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class WorkbenchLibraryTreeTests
{
    [Fact]
    public void Without_a_search_every_folder_and_template_is_shown_in_order()
    {
        var library = Library();

        var tree = WorkbenchLibraryTree.Build(library, "", "", _ => false);

        Assert.Equal([
                "archive/",
                "Orders/",
                "  Orders/archive/",
                "  Orders/Returns/",
                "    Orders/Returns/Late/",
                "    Refund",
                "  order updated",
                "  Order zeta",
                "Root b",
                "Root a"],
            Describe(tree));
    }

    [Fact]
    public void A_search_keeps_matching_templates_with_their_folders_and_created_folders()
    {
        var library = Library();
        library.MarkCreated("archive");

        Assert.Equal(["archive/", "Orders/", "  order updated"], Describe(WorkbenchLibraryTree.Build(library, "UPDATED", "", _ => false)));
        Assert.Equal(["archive/", "Orders/", "  Orders/Returns/", "    Refund"],
            Describe(WorkbenchLibraryTree.Build(library, "turn", "", _ => false)));
        Assert.Equal(["archive/", "Root a"], Describe(WorkbenchLibraryTree.Build(library, "first root", "", _ => false)));
    }

    [Fact]
    public void A_namespace_search_keeps_templates_whose_destination_contains_it()
    {
        var library = Library();

        Assert.Equal(["Orders/", "  Orders/Returns/", "    Refund", "  order updated", "  Order zeta"],
            Describe(WorkbenchLibraryTree.Build(library, "", "ORDER", _ => false)));
        Assert.Equal(["Orders/", "  order updated"], Describe(WorkbenchLibraryTree.Build(library, "updated", "order", _ => false)));
        Assert.Empty(WorkbenchLibraryTree.Build(library, "", "billing", _ => false));
    }

    [Fact]
    public void Templates_always_shown_stay_visible_with_their_folders()
    {
        var library = Library();

        Assert.Equal(["Orders/", "  Orders/Returns/", "    Refund", "Root b"],
            Describe(WorkbenchLibraryTree.Build(library, "nothing matches", "billing", template => template.Name is "Refund" or "Root b")));
    }

    private static WorkbenchLibrary Library() => new(
    [
        new("order updated", "order-events", "", "{}", "Orders"),
        new("Refund", "order-events", "", "{}", "orders/returns"),
        new("Order zeta", "order-events", "", "{}", "ORDERS"),
        new("Root b", "", "", "{}", ""),
        new("Root a", "inventory-events", "The first root template.", "{}", "")
    ], ["Orders", "Orders/Returns", "Orders/Returns/Late", "Orders/archive", "archive"]);

    private static IEnumerable<string> Describe(IEnumerable<LibraryTreeNode> nodes, int depth = 0) =>
        nodes.SelectMany(node => node switch
        {
            LibraryFolderNode folder => [new string(' ', depth * 2) + folder.Path + "/", .. Describe(folder.Children, depth + 1)],
            LibraryTemplateNode template => (IEnumerable<string>)[new string(' ', depth * 2) + template.Template.Name],
            _ => throw new ArgumentOutOfRangeException(nameof(nodes))
        });
}
