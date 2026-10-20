using System.Globalization;
using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class WorkbenchSampleDataTests
{
    private static readonly SampleMapping Columns = new("CSV column", "CustomerId", "CSV column", "Amount");

    [Fact]
    public void The_sample_file_maps_columns_to_ready_rows()
    {
        var rows = WorkbenchSampleData.Rows("orders.csv", Columns, commaDelimited: true, Unexpected);

        Assert.Equal([new SampleRow(1, "C1001", 149.90m, "Ready"), new SampleRow(2, "C1002", 224.00m, "Ready"),
            new SampleRow(3, "C1003", 79.50m, "Ready")], rows);
        Assert.Equal("3 of 3 rows valid", WorkbenchSampleData.Summary(rows));
    }

    [Fact]
    public void The_invalid_sample_file_has_a_row_whose_amount_is_not_a_number()
    {
        var rows = WorkbenchSampleData.Rows(WorkbenchSampleData.InvalidSampleFile, Columns, commaDelimited: true, Unexpected);

        Assert.Equal(new SampleRow(2, "C1002", null, "Expected number, got abc"), rows[1]);
        Assert.Equal(["Ready", "Ready"], new[] { rows[0].Status, rows[2].Status });
        Assert.Equal("1 invalid row · Nothing can be sent", WorkbenchSampleData.Summary(rows));
    }

    [Fact]
    public void Another_delimiter_makes_every_row_invalid()
    {
        var rows = WorkbenchSampleData.Rows("orders.csv", Columns, commaDelimited: false, Unexpected);

        Assert.All(rows, row => Assert.Equal("Invalid delimiter", row.Status));
        Assert.Equal(149.90m, rows[0].Amount);
        Assert.Equal("3 invalid rows · Nothing can be sent", WorkbenchSampleData.Summary(rows));
    }

    [Fact]
    public void Constants_declared_defaults_and_swapped_columns_are_mapped()
    {
        var requested = new List<string>();
        string Default(string name) { requested.Add(name); return name == "CustomerId" ? "C-DEFAULT" : "12.5"; }

        var constants = WorkbenchSampleData.Rows("orders.csv", new("Constant", "C-CONST", "Constant", "7"), true, Default);
        Assert.All(constants, row => Assert.Equal(("C-CONST", 7m, "Ready"), (row.CustomerId, row.Amount, row.Status)));
        Assert.Empty(requested);

        var defaults = WorkbenchSampleData.Rows("orders.csv", new("Declared default", "", "Declared default", ""), true, Default);
        Assert.All(defaults, row => Assert.Equal(("C-DEFAULT", 12.5m), (row.CustomerId, row.Amount)));
        Assert.Equal(["CustomerId", "Amount", "CustomerId", "Amount", "CustomerId", "Amount"], requested);

        var swapped = WorkbenchSampleData.Rows("orders.csv", new("CSV column", "Amount", "CSV column", "CustomerId"), true, Unexpected);
        Assert.Equal(new SampleRow(1, "149.90", null, "Expected number, got C1001"), swapped[0]);

        var blank = WorkbenchSampleData.Rows("orders.csv", new("Constant", " ", "CSV column", "Amount"), true, Unexpected);
        Assert.Equal(new SampleRow(1, " ", 149.90m, "CustomerId required"), blank[0]);
    }

    [Fact]
    public void Amounts_are_read_the_same_in_every_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            Assert.Equal(149.90m, WorkbenchSampleData.Rows("orders.csv", Columns, true, Unexpected)[0].Amount);
            Assert.Null(WorkbenchSampleData.InputProblem(false, "", false, "", false, "C1001", "149.90"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(false, "", false, "", false, "C1001", "149.90", null)]
    [InlineData(true, "5", false, "", false, "C1001", "149.90", null)]
    [InlineData(true, "0", false, "", false, "C1001", "149.90", "ttl")]
    [InlineData(true, "-3", false, "", false, "C1001", "149.90", "ttl")]
    [InlineData(true, "soon", false, "", true, "", "", "ttl")]
    [InlineData(false, "", true, "my-id", false, "C1001", "149.90", null)]
    [InlineData(false, "", true, " ", false, "C1001", "149.90", "ttl")]
    [InlineData(false, "", true, "my-id", true, "", "", "ttl")]
    [InlineData(false, "", false, "", false, " ", "149.90", "inputs")]
    [InlineData(false, "", false, "", false, "C1001", "abc", "inputs")]
    [InlineData(false, "", false, "", false, "C1001", "1,000.50", null)]
    [InlineData(false, "", false, "", true, "", "abc", null)]
    public void Prepare_inputs_need_a_positive_ttl_a_single_message_custom_id_and_valid_single_values(bool ttl, string ttlMinutes,
        bool customId, string customIdValue, bool csv, string customer, string amount, string? expected)
    {
        var problem = WorkbenchSampleData.InputProblem(ttl, ttlMinutes, customId, customIdValue, csv, customer, amount);

        Assert.Equal(expected switch
        {
            "ttl" => new PrepareInputProblem("Enter a positive TTL; custom message IDs require a nonempty value and single-message mode.", false),
            "inputs" => new PrepareInputProblem("Enter a CustomerId and a numeric Amount to preview.", true),
            _ => null
        }, problem);
    }

    private static string Unexpected(string name) => throw new InvalidOperationException($"The default of {name} was read.");
}
