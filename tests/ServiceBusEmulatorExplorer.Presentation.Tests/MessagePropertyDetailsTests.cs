using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class MessagePropertyDetailsTests
{
    private static readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CustomerId"] = "C1001",
        ["Amount"] = "149.90"
    };

    [Fact]
    public void Details_list_every_property_with_variables_filled_in()
    {
        var properties = new MessagePropertyTexts("Order $(CustomerId)", "application/json", "$(customerid)", "session-$(Amount)",
            "$(CustomerId)-replies", "", "", [("amount", "decimal", "$(Amount)"), ("note", "string", "$(Missing)")]);

        Assert.Equal(string.Join("\n",
                "Subject: Order C1001",
                "Content type: application/json",
                "Correlation ID: C1001",
                "Session ID: session-149.90",
                "Reply to: C1001-replies",
                "Reply session ID: ",
                "Partition key: none",
                "Application properties: amount (decimal) = 149.90, note (string) = $(Missing)"),
            MessagePropertyDetails.Describe(properties, Values));
    }

    [Fact]
    public void A_partition_key_is_reported_as_unsupported_and_an_empty_property_list_stays_empty()
    {
        var details = MessagePropertyDetails.Describe(
            new MessagePropertyTexts("", "text/plain", "", "", "", "$(Amount)", " key ", []), Values);

        Assert.EndsWith("Reply session ID: 149.90\nPartition key: unsupported by emulator\nApplication properties: ", details);
        Assert.Equal("Partition key: none", MessagePropertyDetails.Describe(
            new MessagePropertyTexts("", "", "", "", "", "", "  ", []), Values).Split('\n')[6]);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("$(CustomerId)-$(Amount)", "C1001-149.90")]
    [InlineData("a $(Unknown) b", "a $(Unknown) b")]
    [InlineData("open $(CustomerId", "open $(CustomerId")]
    [InlineData("$()$(CustomerId)", "$()C1001")]
    [InlineData("$$(CustomerId))", "$C1001)")]
    [InlineData("", "")]
    public void Expansion_replaces_known_references_and_keeps_the_rest(string value, string expected) =>
        Assert.Equal(expected, MessagePropertyDetails.Expand(value, Values));
}
