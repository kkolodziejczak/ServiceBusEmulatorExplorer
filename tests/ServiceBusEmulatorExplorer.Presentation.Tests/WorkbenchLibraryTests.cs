using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class WorkbenchLibraryTests
{
    private static readonly PrototypeAuthorSettings Saved = Settings("saved");

    [Fact]
    public void The_sample_library_files_inventory_templates_under_inventory_and_the_rest_under_orders()
    {
        var library = WorkbenchLibrary.CreateSample();

        Assert.Equal(["Orders", "Inventory"], library.Folders);
        Assert.Equal(["Order created", "Order updated", "Order dispatched", "Stock reserved", "Order reply", "Unassociated sample"],
            library.TemplateNames);
        Assert.Equal(["Orders", "Orders", "Orders", "Inventory", "Orders", "Orders"], library.Templates.Select(template => template.Folder));
        Assert.Equal(EntityKind.Queue, library.Find("Order reply")!.DestinationKind);
        Assert.All(library.Templates.Where(template => template.Name != "Order reply"),
            template => Assert.Equal(EntityKind.Topic, template.DestinationKind));
        Assert.Empty(library.CreatedFolders);
    }

    [Fact]
    public void Names_are_found_exactly_and_made_unique_ignoring_case()
    {
        var library = Library();

        Assert.NotNull(library.Find("Order created"));
        Assert.Null(library.Find("order created"));
        Assert.Equal("Fresh", library.UniqueName("Fresh"));
        Assert.Equal("ORDER CREATED 2", library.UniqueName("ORDER CREATED"));
        library.Add(Template("Order created 2"), Saved);
        Assert.Equal("Order created 3", library.UniqueName("Order created"));
    }

    [Fact]
    public void Added_templates_keep_their_settings_until_removed()
    {
        var library = Library();
        var draft = Settings("draft");

        library.Add(Template("Draft"), draft);

        Assert.Same(draft, library.SettingsOf("Draft"));
        library.Remove("draft");
        Assert.NotNull(library.Find("Draft"));
        library.Remove("Draft");
        Assert.Null(library.Find("Draft"));
        Assert.Throws<KeyNotFoundException>(() => library.SettingsOf("Draft"));
        Assert.Equal(2, library.Templates.Count);
    }

    [Fact]
    public void Saving_stores_the_body_destination_and_settings()
    {
        var library = Library();
        var settings = Settings("edited");

        Assert.True(library.Save("Order created", "{\"x\":1}", "inventory-events", EntityKind.Queue, settings));

        var saved = library.Find("Order created")!;
        Assert.Equal(("{\"x\":1}", "inventory-events", EntityKind.Queue, "Orders"), (saved.Body, saved.Topic, saved.DestinationKind, saved.Folder));
        Assert.Equal(["inventory-events"], saved.Associations);
        Assert.Same(settings, library.SettingsOf("Order created"));
        library.Save("Order created", "{}", "", EntityKind.Topic, settings);
        Assert.Empty(library.Find("Order created")!.Associations!);
        Assert.False(library.Save("Missing", "{}", "", EntityKind.Topic, settings));
        Assert.Null(library.Find("Missing"));
    }

    [Fact]
    public void Unsaved_changes_are_any_difference_in_body_destination_kind_or_settings()
    {
        var library = Library();
        var template = library.Find("Order created")!;

        Assert.False(library.HasUnsavedChanges("Order created", template.Body, template.Topic, template.DestinationKind, Settings("saved")));
        Assert.True(library.HasUnsavedChanges("Order created", "{}", template.Topic, template.DestinationKind, Saved));
        Assert.True(library.HasUnsavedChanges("Order created", template.Body, "other", template.DestinationKind, Saved));
        Assert.True(library.HasUnsavedChanges("Order created", template.Body, template.Topic, EntityKind.Queue, Saved));
        Assert.True(library.HasUnsavedChanges("Order created", template.Body, template.Topic, template.DestinationKind, Settings("changed")));
        Assert.True(library.HasUnsavedChanges("Order created", template.Body, template.Topic, template.DestinationKind,
            Saved with { Variables = [new("Extra", "string", "Input", false, "")] }));
        Assert.False(library.HasUnsavedChanges("Missing", "{}", "", EntityKind.Topic, Saved));
    }

    [Fact]
    public void Renaming_a_template_moves_its_settings()
    {
        var library = Library();
        var settings = Settings("renamed");
        library.Save("Order created", "{}", "order-events", EntityKind.Topic, settings);

        Assert.True(library.RenameTemplate("Order created", "Order placed"));

        Assert.Null(library.Find("Order created"));
        Assert.Equal("{}", library.Find("Order placed")!.Body);
        Assert.Same(settings, library.SettingsOf("Order placed"));
        Assert.Throws<KeyNotFoundException>(() => library.SettingsOf("Order created"));
        Assert.False(library.RenameTemplate("order placed", "Other"));
        Assert.Equal(["Order placed", "Stock reserved"], library.TemplateNames);
    }

    [Fact]
    public void Folders_are_added_with_their_parents_once_ignoring_case()
    {
        var library = Library();

        library.AddFolderAndParents("orders/Returns/Late");
        library.AddFolderAndParents("Inventory");

        Assert.Equal(["Orders", "Inventory", "orders/Returns", "orders/Returns/Late"], library.Folders);
        library.MarkCreated("orders/returns/late");
        Assert.Contains("Orders/Returns/Late", library.CreatedFolders);
    }

    [Fact]
    public void Moving_and_relocating_change_template_folders_folders_and_created_folders()
    {
        var library = Library();
        library.AddFolderAndParents("Orders/Returns");
        library.AddFolderAndParents("OrdersArchive");
        library.MarkCreated("orders/returns");
        library.MarkCreated("OrdersArchive");
        library.MoveTemplate("Stock reserved", "Orders/Returns");

        library.RelocateFolder("Orders", "Inventory/Orders");

        Assert.Equal(["Inventory/Orders", "Inventory", "Inventory/Orders/Returns", "OrdersArchive"], library.Folders);
        Assert.Equal(["Inventory/Orders", "Inventory/Orders/Returns"], library.Templates.Select(template => template.Folder));
        Assert.Equal(["Inventory/Orders/returns", "OrdersArchive"], library.CreatedFolders.Order(StringComparer.Ordinal));
    }

    private static WorkbenchLibrary Library()
    {
        var library = new WorkbenchLibrary([Template("Order created"), Template("Stock reserved", "Inventory")], ["Orders", "Inventory"]);
        library.SaveSettingsForAll(Saved);
        return library;
    }

    private static PrototypeTemplate Template(string name, string folder = "Orders") =>
        new(name, "order-events", "", "{\"name\":\"" + name + "\"}", folder, ["order-events"]);

    private static PrototypeAuthorSettings Settings(string subject) =>
        new(subject, 0, "", "", false, "", false, "", "", "", "", [new("amount", "decimal", "$(Amount)")], [new("CustomerId", "string", "Input", false, "")]);
}
