using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class InspectorPanelViewModelTests
{
    private const string EditHelp = "Edit this body, then replay a new copy. Original stays unchanged.";

    [Fact]
    public async Task Requires_a_workspace_and_a_clipboard()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        Assert.Equal("workspace", Assert.Throws<ArgumentNullException>(() => new InspectorPanelViewModel(null!, _ => { })).ParamName);
        Assert.Equal("copyText", Assert.Throws<ArgumentNullException>(() => new InspectorPanelViewModel(fixture.Workspace, null!)).ParamName);
    }

    [Fact]
    public async Task Without_an_inspected_message_nothing_shows()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var panel = new InspectorPanelViewModel(fixture.Workspace, _ => { });
        fixture.Workspace.Browse.FocusedMessage = null;
        fixture.Workspace.Inspector.Select(fixture.Rows[0].Delivery);
        fixture.Workspace.Inspector.Document.Text = "edited";

        var body = panel.StateFor(replayPending: false);
        panel.SetMode(InspectorMode.Properties);
        var properties = panel.StateFor(replayPending: false);

        Assert.All([body, properties], state =>
        {
            Assert.False(state.HasMessage);
            Assert.False(state.IsDeadLetter);
            Assert.False(state.ShowsBody || state.ShowsProperties || state.IsModified);
        });
    }

    [Fact]
    public async Task A_dead_lettered_json_body_can_be_edited_until_a_replay_runs()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var panel = new InspectorPanelViewModel(fixture.Workspace, _ => { });
        fixture.Focus(0);

        Assert.Equal(new InspectorPanelState(true, true, false, true, "Editable", EditHelp, true, false, "Formatted JSON", false),
            panel.StateFor(replayPending: false));

        var replaying = panel.StateFor(replayPending: true);
        Assert.True(replaying.IsReadOnly);
        Assert.False(replaying.IsEditable);
        Assert.Equal("Read-only", replaying.Editability);
        Assert.Equal("Read-only while replay is in progress.", replaying.EditabilityHelp);
    }

    [Fact]
    public async Task Properties_are_read_only_and_the_mode_changes_only_once()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var panel = new InspectorPanelViewModel(fixture.Workspace, _ => { });
        fixture.Focus(0);

        Assert.False(panel.SetMode(InspectorMode.Body));
        Assert.True(panel.SetMode(InspectorMode.Properties));
        Assert.False(panel.SetMode(InspectorMode.Properties));
        var state = panel.StateFor(false);

        Assert.Equal(InspectorMode.Properties, panel.Mode);
        Assert.False(state.IsEditable);
        Assert.False(state.IsReadOnly);
        Assert.Equal("Message properties are read-only. You can select and copy them.", state.EditabilityHelp);
        Assert.False(state.ShowsBody);
        Assert.True(state.ShowsProperties);
    }

    [Fact]
    public async Task An_active_message_body_is_read_only()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var panel = new InspectorPanelViewModel(fixture.Workspace, _ => { });
        fixture.Focus(0);
        var deadLetter = fixture.Rows[0].Delivery;
        fixture.Workspace.Inspector.Select(new MessageDelivery(deadLetter.Identity with { Bucket = MessageBucket.Active }, deadLetter.Message));

        var state = panel.StateFor(false);

        Assert.True(state.IsReadOnly);
        Assert.Equal("Read-only", state.Editability);
        Assert.Equal("This message body is read-only. You can select, find, fold and copy its content.", state.EditabilityHelp);
    }

    [Fact]
    public async Task An_edited_body_is_modified_and_explains_why_it_cannot_be_formatted()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var panel = new InspectorPanelViewModel(fixture.Workspace, _ => { });
        fixture.Focus(0);

        fixture.Workspace.Inspector.Document.Text = "not json";

        var state = panel.StateFor(false);
        Assert.True(state.IsModified);
        Assert.Equal("Plain text or invalid JSON cannot be formatted; the body is shown as received.", state.JsonTabToolTip);
    }

    [Fact]
    public async Task A_binary_body_explains_that_it_is_shown_as_base64()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var panel = new InspectorPanelViewModel(fixture.Workspace, _ => { });
        fixture.Focus(0);
        var delivery = fixture.Rows[0].Delivery;
        fixture.Workspace.Inspector.Select(new MessageDelivery(delivery.Identity with { Source = new EntityAddress(EntityKind.Queue, "binary") },
            delivery.Message with { RawBody = BinaryData.FromBytes([0xC3, 0x28]) }));

        Assert.Equal(fixture.Workspace.Inspector.BodyDescription, panel.StateFor(false).JsonTabToolTip);
        Assert.Contains("Base64", panel.StateFor(false).JsonTabToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copy_takes_the_shown_body_or_properties_and_logs_the_outcome()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var copied = new List<string>();
        var panel = new InspectorPanelViewModel(fixture.Workspace, copied.Add);
        fixture.Focus(0);

        panel.CopyShown();
        panel.SetMode(InspectorMode.Properties);
        panel.CopyShown();

        Assert.Equal([fixture.Workspace.Inspector.Document.Text, fixture.Workspace.Inspector.PropertiesText], copied);
        Assert.Equal(2, fixture.Log.Count(message => message == "Copied displayed text to clipboard."));
        fixture.Workspace.Inspector.Select(null);
        panel.CopyShown();
        Assert.Equal(2, copied.Count);
    }

    [Fact]
    public async Task A_correlation_id_is_copied_only_when_the_message_has_one()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var copied = new List<string>();
        var panel = new InspectorPanelViewModel(fixture.Workspace, copied.Add);

        panel.CopyCorrelation(fixture.Rows[1]);
        panel.CopyCorrelation(null);
        panel.CopyCorrelation(fixture.Rows[0]);

        Assert.Equal(["order-80341"], copied);
    }

    [Fact]
    public async Task A_clipboard_that_cannot_be_used_is_reported()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var panel = new InspectorPanelViewModel(fixture.Workspace, _ => throw new InvalidOperationException("clipboard busy"));

        panel.Copy("text");

        Assert.Contains(fixture.Workspace.Activity, entry => entry.Warning && entry.Message == "Clipboard could not be accessed. Try Copy again.");
        Assert.DoesNotContain("Copied displayed text to clipboard.", fixture.Log);
    }
}
