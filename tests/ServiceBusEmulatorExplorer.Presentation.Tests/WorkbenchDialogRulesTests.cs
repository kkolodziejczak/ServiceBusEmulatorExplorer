using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class WorkbenchDialogRulesTests
{
    [Fact]
    public void Location_choices_start_with_the_root_then_each_folder_once_by_path()
    {
        var location = new TemplateLocationViewModel(["orders/Returns", "Inventory", "", "Orders", "ORDERS/returns", "archive"], "inventory", (_, _) => null);

        Assert.Equal([new FolderChoice("", "Root"), new FolderChoice("archive", "archive"), new FolderChoice("Inventory", "Inventory"),
            new FolderChoice("Orders", "Orders"), new FolderChoice("orders/Returns", "orders / Returns")], location.Choices);
        Assert.Same(location.Choices[2], location.InitialChoice);
        Assert.Equal(location.Choices[0], new TemplateLocationViewModel(["Orders"], "Gone", (_, _) => null).InitialChoice);
        Assert.Equal(location.Choices[0], new TemplateLocationViewModel(["Orders"], "", (_, _) => null).InitialChoice);
    }

    [Fact]
    public void A_location_needs_a_name_before_the_caller_checks_it()
    {
        var checkedWith = new List<(string, string)>();
        var location = new TemplateLocationViewModel(["Orders"], "Orders", (name, folder) =>
        {
            checkedWith.Add((name, folder));
            return name == "Taken" ? "A template with that name already exists." : null;
        });

        Assert.Equal("Enter a name.", location.Problem("   ", location.Choices[1]));
        Assert.Empty(checkedWith);
        Assert.Null(location.Problem("  Fresh ", location.Choices[1]));
        Assert.Equal("A template with that name already exists.", location.Problem("Taken", null));
        Assert.Equal([("Fresh", "Orders"), ("Taken", "")], checkedWith);
    }

    [Fact]
    public void A_captured_time_to_live_is_copied_in_whole_minutes_rounded_up()
    {
        var enqueued = new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);

        Assert.Equal(90, WorkbenchCapture.TtlMinutes(true, enqueued.AddMinutes(90), enqueued));
        Assert.Equal(2, WorkbenchCapture.TtlMinutes(true, enqueued.AddSeconds(61), enqueued));
        Assert.Null(WorkbenchCapture.TtlMinutes(false, enqueued.AddMinutes(90), enqueued));
        Assert.Null(WorkbenchCapture.TtlMinutes(true, null, enqueued));
        Assert.Null(WorkbenchCapture.TtlMinutes(true, enqueued, null));
    }

    [Theory]
    [InlineData(true, "boolean")]
    [InlineData(7, "int")]
    [InlineData(7L, "long")]
    [InlineData(1.5, "double")]
    [InlineData("text", "string")]
    [InlineData((short)3, "string")]
    [InlineData(null, "string")]
    public void Captured_properties_get_the_compose_type_of_their_value(object? value, string type) =>
        Assert.Equal(type, WorkbenchCapture.PropertyType(value));

    [Fact]
    public void Decimal_guid_and_time_properties_keep_their_types()
    {
        Assert.Equal("decimal", WorkbenchCapture.PropertyType(1.5m));
        Assert.Equal("guid", WorkbenchCapture.PropertyType(Guid.Empty));
        Assert.Equal("dateTimeUtc", WorkbenchCapture.PropertyType(DateTime.UnixEpoch));
        Assert.Equal("dateTimeUtc", WorkbenchCapture.PropertyType(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void A_property_row_is_empty_only_while_blank_and_a_string()
    {
        Assert.True(new PrototypeProperty().IsEmpty);
        Assert.True(new PrototypeProperty(" ", "string", "\t").IsEmpty);
        Assert.False(new PrototypeProperty("amount", "string", "").IsEmpty);
        Assert.False(new PrototypeProperty("", "string", "1").IsEmpty);
        Assert.False(new PrototypeProperty("", "int", "").IsEmpty);
    }

    [Fact]
    public void A_variable_shows_its_default_or_where_its_value_comes_from()
    {
        var input = new PrototypeVariable("Region", "string", "Input", "ignored");
        Assert.Equal("none (required)", input.Default);
        input.HasDefault = true;
        input.DefaultValue = "eu";
        Assert.Equal("eu", input.Default);
        Assert.Equal("not applicable", new PrototypeVariable("EventId", "string", "New GUID", "not applicable") { HasDefault = true, DefaultValue = "x" }.Default);
    }

    [Theory]
    [InlineData("Region", true)]
    [InlineData("_region2", true)]
    [InlineData("2region", false)]
    [InlineData("re-gion", false)]
    [InlineData("", false)]
    [InlineData("Región", false)]
    public void Variable_names_use_letters_digits_and_underscores(string name, bool valid) =>
        Assert.Equal(valid, PrototypeVariable.IsValidName(name));

    [Fact]
    public void A_mapping_source_starts_with_its_column_a_sample_constant_or_the_declared_default()
    {
        Assert.Equal("CustomerId", WorkbenchSampleData.MappingStartValue(0, "CustomerId", "C1001", "C-DEFAULT"));
        Assert.Equal("C1001", WorkbenchSampleData.MappingStartValue(1, "CustomerId", "C1001", "C-DEFAULT"));
        Assert.Equal("C-DEFAULT", WorkbenchSampleData.MappingStartValue(2, "CustomerId", "C1001", "C-DEFAULT"));
        Assert.Equal("", WorkbenchSampleData.MappingStartValue(2, "CustomerId", "C1001", null));
    }

    [Theory]
    [InlineData("CustomerId", "Amount", false)]
    [InlineData(" ", "Amount", true)]
    [InlineData("CustomerId", "", true)]
    [InlineData("CustomerId", "  ", true)]
    public void A_mapping_needs_both_values(string customer, string amount, bool problem) =>
        Assert.Equal(problem ? "CustomerId and Amount are required. Map a CSV column, enter a constant, or declare a default in Variables." : null,
            WorkbenchSampleData.MappingProblem(customer, amount));
}
