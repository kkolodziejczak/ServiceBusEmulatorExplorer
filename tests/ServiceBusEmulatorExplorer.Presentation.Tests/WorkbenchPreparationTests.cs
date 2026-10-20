using System.Text.Json;
using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class WorkbenchPreparationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 30, 0, TimeSpan.FromHours(2));

    private static readonly TemplateVariable[] BuiltIn =
    [
        new("CustomerId", "string", "Input", false, ""),
        new("Amount", "number", "Input", false, ""),
        new("EventId", "string", "New GUID", false, ""),
        new("OccurredAt", "string", "Current UTC", false, "")
    ];

    [Fact]
    public void Each_row_gets_its_own_event_id_and_the_row_values()
    {
        var result = Prepare("""{"customerId":"$(CustomerId)","amount":$(Amount),"eventId":"$(EventId)","occurredAt":"$(OccurredAt)"}""",
            rows: [new(1, "C1001", 149.90m), new(3, "C1003", 5m)]);

        Assert.Null(result.Error);
        var messages = result.Messages!;
        Assert.Equal([1, 3], messages.Select(message => message.Row));
        Assert.Equal([Id(1), Id(2)], messages.Select(message => message.EventId));
        Assert.Equal([Id(1), Id(2)], messages.Select(message => message.MessageId));
        Assert.All(messages, message => Assert.Equal("2026-10-18T07:30:00Z", message.OccurredAt));
        using var first = JsonDocument.Parse(messages[0].Body);
        Assert.Equal("C1001", first.RootElement.GetProperty("customerId").GetString());
        Assert.Equal(149.90m, first.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal(Id(1), first.RootElement.GetProperty("eventId").GetString());
        Assert.Equal("2026-10-18T07:30:00Z", first.RootElement.GetProperty("occurredAt").GetString());
        Assert.Contains(Environment.NewLine + "  \"customerId\": \"C1001\"", messages[0].Body);
        Assert.Equal("5", messages[1].VariableValues["amount"]);
        Assert.Equal("C1003", messages[1].VariableValues["CUSTOMERID"]);
    }

    [Fact]
    public void Values_are_json_encoded_inside_strings_and_numbers_are_inserted_as_they_are()
    {
        var variables = BuiltIn.Append(new("Count", "number", "Input", true, "42")).Append(new("Note", "string", "Input", true, "a \"b\"\nc"));

        var result = Prepare("""{"quoted":"$(customerid)","inside":"id: $(CustomerId)!","count":"$(count)","note":"$(Note)","amount":"$(Amount)"}""",
            variables, rows: [new(1, "C\"1\\", 2.5m)]);

        using var body = JsonDocument.Parse(result.Messages![0].Body);
        Assert.Equal("C\"1\\", body.RootElement.GetProperty("quoted").GetString());
        Assert.Equal("id: C\"1\\!", body.RootElement.GetProperty("inside").GetString());
        Assert.Equal(42, body.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("a \"b\"\nc", body.RootElement.GetProperty("note").GetString());
        Assert.Equal(JsonValueKind.Number, body.RootElement.GetProperty("amount").ValueKind);

        var amountAsText = BuiltIn.Select(variable => variable.Name == "Amount" ? variable with { Type = "string" } : variable);
        using var amount = JsonDocument.Parse(Prepare("""{"amount":"$(Amount)"}""", amountAsText).Messages![0].Body);
        Assert.Equal(149.90m, amount.RootElement.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public void A_custom_message_id_is_used_for_every_message()
    {
        Assert.All(Prepare("""{"id":"$(EventId)"}""", customMessageId: "my-id", rows: [new(1, "C1", 1m), new(2, "C2", 2m)]).Messages!,
            message => Assert.Equal("my-id", message.MessageId));
        Assert.Equal("", Assert.Single(Prepare("""{"id":"$(EventId)"}""", customMessageId: "").Messages!).MessageId);
    }

    [Fact]
    public void A_declared_variable_does_not_replace_a_row_value_of_the_same_name()
    {
        var variables = BuiltIn.Append(new("customerid", "string", "Input", true, "default"));

        var result = Prepare("""{"customerId":"$(CustomerId)"}""", variables);

        using var body = JsonDocument.Parse(result.Messages![0].Body);
        Assert.Equal("C1001", body.RootElement.GetProperty("customerId").GetString());
    }

    [Fact]
    public void A_referenced_variable_without_a_value_stops_preparation()
    {
        const string required = "Variable 'Region' requires a default value before this message can be reviewed.";
        var region = new TemplateVariable("Region", "string", "Input", false, "");
        var generated = new TemplateVariable("Region", "string", "New GUID", true, "ignored");

        Assert.Equal(new PreparationResult(null, required), Prepare("""{"region":"$(region)"}""", BuiltIn.Append(region)));
        Assert.Equal(new PreparationResult(null, required), Prepare("{}", BuiltIn.Append(region), properties: ["", "eu-$(Region)"]));
        Assert.Equal(new PreparationResult(null, required), Prepare("""{"region":"$(Region)"}""", BuiltIn.Append(generated)));
        Assert.Equal("{}", Assert.Single(Prepare("{}", BuiltIn.Append(region), properties: ["Region", "$(Regions)"]).Messages!).Body);
    }

    [Fact]
    public void An_undeclared_variable_or_a_body_that_is_not_json_prepares_nothing()
    {
        Assert.Equal(new PreparationResult(null, "Variable 'Missing' is not declared."), Prepare("""{"x":"$(Missing)"}"""));
        Assert.Equal(new PreparationResult(null, null), Prepare("""{"x": $(CustomerId)}"""));
        Assert.Equal(new PreparationResult(null, null), Prepare("not json"));
    }

    private static PreparationResult Prepare(string body, IEnumerable<TemplateVariable>? variables = null, string? customMessageId = null,
        IReadOnlyList<string>? properties = null, IReadOnlyList<PreparationRow>? rows = null)
    {
        int next = 0;
        return WorkbenchPreparation.Prepare(rows ?? [new(1, "C1001", 149.90m)],
            new(body, (variables ?? BuiltIn).ToArray(), properties ?? [], customMessageId), Now, () => new Guid(Id(++next)));
    }

    private static string Id(int number) => $"00000000-0000-4000-8000-{number:D12}";
}
