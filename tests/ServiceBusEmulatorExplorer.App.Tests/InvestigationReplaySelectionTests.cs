using System.Text;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationReplaySelectionTests
{
    [Fact]
    public void Disconnected_selection_has_no_replay_targets()
    {
        MessageRow row = Row("orders", selected: true);
        ReplaySelectionState result = ReplaySelection.Evaluate(false, [row], row, new DeliveryInspector());
        Assert.Empty(result.Targets);
        Assert.False(result.CanReplay);
        Assert.Null(result.EditedBody);
    }

    [Fact]
    public void No_checked_or_focused_row_has_no_replay_targets()
    {
        ReplaySelectionState result = ReplaySelection.Evaluate(true, [Row("orders")], null, new DeliveryInspector());
        Assert.Empty(result.Targets);
        Assert.False(result.CanReplay);
    }

    [Fact]
    public void Focused_dlq_row_is_replayed_when_nothing_is_checked()
    {
        MessageRow row = Row("orders");
        ReplaySelectionState result = ReplaySelection.Evaluate(true, [row], row, new DeliveryInspector());
        Assert.True(result.CanReplay);
        Assert.Equal([row.Delivery], result.Targets);
        Assert.Null(result.EditedBody);
    }

    [Fact]
    public void Checked_rows_take_precedence_over_focus_and_preserve_display_order()
    {
        MessageRow first = Row("first", selected: true);
        MessageRow focused = Row("focused");
        MessageRow last = Row("last", selected: true);
        ReplaySelectionState result = ReplaySelection.Evaluate(true, [first, focused, last], focused, new DeliveryInspector());
        Assert.True(result.CanReplay);
        Assert.Equal([first.Delivery, last.Delivery], result.Targets);
        Assert.Null(result.EditedBody);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Active_delivery_cannot_be_replayed_whether_focused_or_checked(bool selected)
    {
        MessageRow row = Row("active", MessageBucket.Active, selected);
        AssertBlocked(ReplaySelection.Evaluate(true, [row], row, new DeliveryInspector()));
    }

    [Fact]
    public void Mixed_active_and_dlq_selection_blocks_the_entire_batch()
    {
        MessageRow dlq = Row("dead-letter", selected: true);
        MessageRow active = Row("active", MessageBucket.Active, selected: true);
        AssertBlocked(ReplaySelection.Evaluate(true, [dlq, active], dlq, new DeliveryInspector()));
    }

    [Fact]
    public void Checked_active_row_does_not_fall_back_to_focused_dlq()
    {
        MessageRow dlq = Row("dead-letter");
        MessageRow active = Row("active", MessageBucket.Active, selected: true);
        AssertBlocked(ReplaySelection.Evaluate(true, [dlq, active], dlq, new DeliveryInspector()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sole_focused_target_uses_its_valid_json_draft(bool selected)
    {
        MessageRow row = Row("orders", selected: selected);
        var inspector = new DeliveryInspector();
        inspector.Select(row.Delivery);
        inspector.Document.Text = "{\"edited\":true}";
        ReplaySelectionState result = ReplaySelection.Evaluate(true, [row], row, inspector);
        Assert.True(result.CanReplay);
        Assert.Equal(inspector.Document.Text, result.EditedBody);
        Assert.Equal([row.Delivery], result.Targets);
    }

    [Fact]
    public void Invalid_focused_draft_is_blocked()
    {
        MessageRow row = Row("orders");
        var inspector = new DeliveryInspector();
        inspector.Select(row.Delivery);
        inspector.Document.Text = "{invalid";
        AssertBlocked(ReplaySelection.Evaluate(true, [row], row, inspector));
    }

    [Fact]
    public void Focused_draft_cannot_be_silently_ignored_in_a_batch()
    {
        MessageRow row = Row("orders", selected: true);
        MessageRow second = Row("audit", selected: true);
        var inspector = new DeliveryInspector();
        inspector.Select(row.Delivery);
        inspector.Document.Text = "{\"edited\":true}";
        AssertBlocked(ReplaySelection.Evaluate(true, [row, second], row, inspector));
    }

    [Fact]
    public void Hidden_selected_draft_blocks_replaying_its_original_bytes()
    {
        MessageRow edited = Row("orders", selected: true);
        MessageRow focused = Row("audit");
        var inspector = new DeliveryInspector();
        inspector.Select(edited.Delivery);
        inspector.Document.Text = "{\"edited\":true}";
        inspector.Select(focused.Delivery);
        Assert.True(inspector.HasDraft(edited.Key));
        Assert.False(inspector.IsDirty);
        AssertBlocked(ReplaySelection.Evaluate(true, [edited, focused], focused, inspector));
    }

    [Fact]
    public void Hidden_draft_for_an_unselected_delivery_does_not_block_clean_focused_target()
    {
        MessageRow edited = Row("orders");
        MessageRow clean = Row("audit", selected: true);
        var inspector = new DeliveryInspector();
        inspector.Select(edited.Delivery);
        inspector.Document.Text = "{\"edited\":true}";
        inspector.Select(clean.Delivery);
        Assert.True(inspector.HasDraft(edited.Key));
        Assert.False(inspector.IsDirty);
        ReplaySelectionState result = ReplaySelection.Evaluate(true, [edited, clean], clean, inspector);
        Assert.True(result.CanReplay);
        Assert.Equal([clean.Delivery], result.Targets);
        Assert.Null(result.EditedBody);
    }

    [Fact]
    public void Visible_focused_draft_blocks_replay_of_another_checked_delivery()
    {
        MessageRow edited = Row("orders");
        MessageRow clean = Row("audit", selected: true);
        var inspector = new DeliveryInspector();
        inspector.Select(edited.Delivery);
        inspector.Document.Text = "{\"edited\":true}";
        AssertBlocked(ReplaySelection.Evaluate(true, [edited, clean], edited, inspector));
    }

    [Fact]
    public void Inspector_for_another_delivery_cannot_supply_target_draft()
    {
        MessageRow target = Row("orders", selected: true);
        MessageRow other = Row("audit");
        var inspector = new DeliveryInspector();
        inspector.Select(target.Delivery);
        inspector.Document.Text = "{\"target\":true}";
        inspector.Select(other.Delivery);
        inspector.Document.Text = "{\"other\":true}";
        AssertBlocked(ReplaySelection.Evaluate(true, [target, other], target, inspector));
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("")]
    public void Unedited_non_json_body_can_be_replayed_as_original_bytes(string body)
    {
        MessageRow row = Row("orders", body: body);
        var inspector = new DeliveryInspector();
        inspector.Select(row.Delivery);
        Assert.False(inspector.IsValidJson);
        ReplaySelectionState result = ReplaySelection.Evaluate(true, [row], row, inspector);
        Assert.True(result.CanReplay);
        Assert.Null(result.EditedBody);
    }

    [Fact]
    public void Discarded_hidden_draft_no_longer_blocks_selected_target()
    {
        MessageRow row = Row("orders", selected: true);
        MessageRow focused = Row("audit");
        var inspector = new DeliveryInspector();
        inspector.Select(row.Delivery);
        inspector.Document.Text = "{\"edited\":true}";
        inspector.DiscardCurrent();
        inspector.Select(focused.Delivery);
        Assert.False(inspector.HasDraft(row.Key));
        Assert.True(ReplaySelection.Evaluate(true, [row, focused], focused, inspector).CanReplay);
    }

    [Fact]
    public void Confirmed_replay_discards_hidden_sent_draft_without_changing_current_selection()
    {
        MessageRow sent = Row("orders");
        MessageRow focused = Row("audit");
        var inspector = new DeliveryInspector();
        inspector.Select(sent.Delivery);
        string original = inspector.Document.Text;
        inspector.Document.Text = "{\"sent\":true}";
        string sentText = inspector.Document.Text;
        var sentDocument = inspector.Document;
        inspector.Select(focused.Delivery);
        var focusedDocument = inspector.Document;

        inspector.DiscardReplayedDraft(sent.Key, sentText);

        Assert.Same(focused.Delivery, inspector.Current);
        Assert.Same(focusedDocument, inspector.Document);
        Assert.False(inspector.HasDraft(sent.Key));
        Assert.Equal(original, sentDocument.Text);
        Assert.False(sentDocument.UndoStack.CanUndo);
    }

    [Fact]
    public void Confirmed_replay_preserves_a_draft_edited_again_after_send_started()
    {
        MessageRow row = Row("orders");
        var inspector = new DeliveryInspector();
        inspector.Select(row.Delivery);
        inspector.Document.Text = "{\"version\":1}";
        string sentText = inspector.Document.Text;
        inspector.Document.Text = "{\"version\":2}";

        inspector.DiscardReplayedDraft(row.Key, sentText);

        Assert.Equal("{\"version\":2}", inspector.Document.Text);
        Assert.True(inspector.IsDirty);
        Assert.True(inspector.HasDraft(row.Key));
    }

    [Fact]
    public void Confirmed_replay_preserves_unrelated_draft_even_when_its_text_matches()
    {
        MessageRow sent = Row("orders");
        MessageRow unrelated = Row("audit");
        var inspector = new DeliveryInspector();
        inspector.Select(sent.Delivery);
        inspector.Document.Text = "{\"edited\":true}";
        string sentText = inspector.Document.Text;
        inspector.Select(unrelated.Delivery);
        inspector.Document.Text = sentText;

        inspector.DiscardReplayedDraft(sent.Key, sentText);

        Assert.False(inspector.HasDraft(sent.Key));
        Assert.True(inspector.HasDraft(unrelated.Key));
        Assert.Same(unrelated.Delivery, inspector.Current);
        Assert.Equal(sentText, inspector.Document.Text);
        Assert.True(inspector.IsDirty);
    }

    private static void AssertBlocked(ReplaySelectionState result)
    {
        Assert.False(result.CanReplay);
        Assert.False(string.IsNullOrWhiteSpace(result.Problem));
        Assert.Null(result.EditedBody);
    }

    private static MessageRow Row(string source, MessageBucket bucket = MessageBucket.DeadLetter,
        bool selected = false, string body = "{\"original\":true}")
    {
        ExplorerMessage message = new("same-message-id", 12, body, body, Encoding.UTF8.GetByteCount(body),
            null, null, 0, "application/json", null, null, null,
            new Dictionary<string, object?>(), new Dictionary<string, object?>())
        {
            RawBody = BinaryData.FromString(body)
        };
        var delivery = new MessageDelivery(new DeliveryIdentity(1,
            new EntityAddress(EntityKind.Queue, source), bucket, 12), message);
        return new MessageRow(delivery, TimestampDisplay.Utc) { IsSelected = selected };
    }
}
