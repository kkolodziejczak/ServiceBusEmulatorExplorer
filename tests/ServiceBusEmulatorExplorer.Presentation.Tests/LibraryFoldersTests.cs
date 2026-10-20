using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class LibraryFoldersTests
{
    private static readonly string[] Folders = ["Orders", "Orders/Returns", "Orders/Returns/Late", "Inventory", "Archive"];
    private static readonly string[] Templates = ["Order created", "Stock reserved"];

    [Fact]
    public void Paths_split_join_and_rename_by_their_last_segment()
    {
        Assert.Equal(("Orders/Returns", "Late"), (LibraryFolders.Parent("Orders/Returns/Late"), LibraryFolders.Name("Orders/Returns/Late")));
        Assert.Equal(("", "Orders"), (LibraryFolders.Parent("Orders"), LibraryFolders.Name("Orders")));
        Assert.Equal("Orders/Returns", LibraryFolders.Join("Orders", "Returns"));
        Assert.Equal("Returns", LibraryFolders.Join("", "Returns"));
        Assert.Equal("Orders/Refunds", LibraryFolders.Renamed("Orders/Returns", "Refunds"));
        Assert.Equal("Sales", LibraryFolders.Renamed("Orders", "Sales"));
    }

    [Fact]
    public void A_path_is_inside_a_folder_when_it_is_the_folder_or_below_it_ignoring_case()
    {
        Assert.True(LibraryFolders.IsInside("orders", "Orders"));
        Assert.True(LibraryFolders.IsInside("ORDERS/Returns/Late", "Orders/returns"));
        Assert.False(LibraryFolders.IsInside("OrdersArchive", "Orders"));
        Assert.False(LibraryFolders.IsInside("Orders", "Orders/Returns"));

        Assert.Equal("Sales/Returns/Late", LibraryFolders.Relocated("orders/Returns/Late", "Orders", "Sales"));
        Assert.Equal("Sales", LibraryFolders.Relocated("ORDERS", "Orders", "Sales"));
        Assert.Equal("OrdersArchive", LibraryFolders.Relocated("OrdersArchive", "Orders", "Sales"));
    }

    [Theory]
    [InlineData("template:Order created", "Inventory", null)]
    [InlineData("template:Order created", "", null)]
    [InlineData("template:Order created", "inventory", null)]
    [InlineData("template:Order created", "Missing", "Choose an existing folder.")]
    [InlineData("template:order created", "Inventory", "The template is no longer available.")]
    [InlineData("folder:Orders/Returns", "Inventory", null)]
    [InlineData("folder:Orders/Returns", "", null)]
    [InlineData("folder:orders/returns", "Orders", null)]
    [InlineData("folder:Gone", "Inventory", "The folder is no longer available.")]
    [InlineData("folderXInventory", "Archive", "The folder is no longer available.")]
    [InlineData("folder:Orders", "Orders", "A folder cannot be moved inside itself or its descendants.")]
    [InlineData("folder:Orders", "orders/Returns/Late", "A folder cannot be moved inside itself or its descendants.")]
    [InlineData("folder:Archive", "Orders", null)]
    [InlineData("folder:Orders/Returns/Late", "", null)]
    [InlineData("folder:Inventory", "Archive", null)]
    public void Moves_go_to_existing_folders_outside_the_moved_folder_without_name_clashes(string tag, string destination, string? problem) =>
        Assert.Equal(problem, LibraryFolders.MoveProblem(tag, destination, Folders, Templates));

    [Fact]
    public void A_folder_cannot_move_next_to_a_folder_of_the_same_name()
    {
        string[] folders = ["Orders", "Orders/Returns", "Archive", "Archive/returns"];

        Assert.Equal("A folder with that name already exists here.", LibraryFolders.MoveProblem("folder:Orders/Returns", "Archive", folders, Templates));
        Assert.Equal("A folder with that name already exists here.", LibraryFolders.MoveProblem("folder:Archive/returns", "Orders", folders, Templates));
    }

    [Theory]
    [InlineData("folder:Orders/Returns", "Refunds", null)]
    [InlineData("folder:Orders/Returns", "returns", null)]
    [InlineData("folder:Orders/Returns", "", "Enter a name.")]
    [InlineData("folder:Orders/Returns", "a/b", "Use a folder name without path separators.")]
    [InlineData("folder:Orders/Returns", "a\\b", "Use a folder name without path separators.")]
    [InlineData("folder:Orders", "INVENTORY", "A folder with that name already exists here.")]
    [InlineData("folder:Orders/Returns", "Inventory", null)]
    [InlineData("folder:Orders/Returns/Late", "late", null)]
    [InlineData("template:Order created", "Order placed", null)]
    [InlineData("template:Order created", "ORDER CREATED", null)]
    [InlineData("template:Order created", "stock RESERVED", "A template with that name already exists here.")]
    [InlineData("template:Order created", "", "Enter a name.")]
    [InlineData("template:Order created", "a/b", null)]
    public void Renames_need_a_unique_name_in_the_same_place(string tag, string name, string? problem) =>
        Assert.Equal(problem, LibraryFolders.RenameProblem(tag, name, Folders, Templates));

    [Fact]
    public void A_renamed_folder_may_not_take_a_sibling_name_even_with_separators()
    {
        string[] folders = ["Orders", "Orders/a", "Orders/b"];

        Assert.Equal("A folder with that name already exists here.", LibraryFolders.RenameProblem("folder:Orders/a", "B", folders, []));
        Assert.Equal("Use a folder name without path separators.", LibraryFolders.RenameProblem("folder:Orders/a", @"x\y", ["Orders", "Orders/a", @"Orders/x\y"], []));
    }

    [Theory]
    [InlineData("", "Sales", true)]
    [InlineData("", "orders", false)]
    [InlineData("Orders", "returns", false)]
    [InlineData("Orders", "Inventory", true)]
    [InlineData("Orders/Returns", "Late", false)]
    [InlineData("Orders", "a/b", false)]
    [InlineData("Orders", "a\\b", false)]
    public void A_new_folder_name_is_unique_in_its_parent_and_has_no_separators(string parent, string name, bool valid) =>
        Assert.Equal(valid, LibraryFolders.IsNewFolderName(parent, name, Folders));
}
