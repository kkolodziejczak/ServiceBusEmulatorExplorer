using System.Text;
using ICSharpCode.AvalonEdit.Document;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationDraftTests
{
    [Fact]
    public void Same_message_id_and_sequence_from_different_sources_have_separate_documents()
    {
        MessageDelivery first = CreateDelivery("billing", MessageBucket.DeadLetter);
        MessageDelivery second = CreateDelivery("audit", MessageBucket.DeadLetter);
        var inspector = new DeliveryInspector();

        inspector.Select(first);
        inspector.Document.Insert(inspector.Document.TextLength, " ");
        TextDocument firstDocument = inspector.Document;

        inspector.Select(second);
        Assert.NotSame(firstDocument, inspector.Document);
        Assert.False(inspector.Document.Text.EndsWith(" ", StringComparison.Ordinal));

        inspector.Select(first);
        Assert.Same(firstDocument, inspector.Document);
        Assert.EndsWith(" ", inspector.Document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Draft_and_undo_stack_survive_selection_changes()
    {
        MessageDelivery first = CreateDelivery("billing", MessageBucket.DeadLetter);
        MessageDelivery second = CreateDelivery("audit", MessageBucket.DeadLetter);
        var inspector = new DeliveryInspector();

        inspector.Select(first);
        string original = inspector.Document.Text;
        inspector.Document.Insert(0, " ");
        Assert.True(inspector.IsDirty);
        Assert.True(inspector.Document.UndoStack.CanUndo);

        inspector.Select(second);
        inspector.Select(first);

        Assert.Equal(" " + original, inspector.Document.Text);
        Assert.True(inspector.Document.UndoStack.CanUndo);
        inspector.Document.UndoStack.Undo();
        Assert.Equal(original, inspector.Document.Text);
        Assert.False(inspector.IsDirty);
    }

    [Fact]
    public void DiscardCurrent_restores_the_initial_presented_document_and_clears_dirty_state()
    {
        MessageDelivery delivery = CreateDelivery("billing", MessageBucket.DeadLetter);
        var inspector = new DeliveryInspector();
        inspector.Select(delivery);
        string original = inspector.Document.Text;

        inspector.Document.Insert(0, " ");
        inspector.DiscardCurrent();

        Assert.Equal(original, inspector.Document.Text);
        Assert.False(inspector.IsDirty);
        Assert.False(inspector.HasDrafts);
        Assert.False(inspector.Document.UndoStack.CanUndo);
    }

    [Fact]
    public void Clear_releases_current_selection_and_cached_drafts()
    {
        MessageDelivery delivery = CreateDelivery("billing", MessageBucket.DeadLetter);
        var inspector = new DeliveryInspector();
        inspector.Select(delivery);
        string original = inspector.Document.Text;
        inspector.Document.Insert(0, " ");
        TextDocument releasedDocument = inspector.Document;

        inspector.Clear();

        Assert.Null(inspector.Current);
        Assert.False(inspector.HasDrafts);
        Assert.False(inspector.IsDirty);
        Assert.True(inspector.IsReadOnly);
        Assert.Empty(inspector.RawText);
        Assert.Empty(inspector.PropertiesText);

        releasedDocument.Insert(0, "changed after clear");
        Assert.False(inspector.HasDrafts);

        inspector.Select(delivery);
        Assert.Equal(original, inspector.Document.Text);
    }

    [Fact]
    public void Active_documents_are_read_only_but_dead_letter_documents_are_editable()
    {
        var inspector = new DeliveryInspector();

        inspector.Select(CreateDelivery("billing", MessageBucket.Active));
        Assert.True(inspector.IsReadOnly);

        inspector.Select(CreateDelivery("billing", MessageBucket.DeadLetter));
        Assert.False(inspector.IsReadOnly);
    }

    [Fact]
    public void Invalid_utf8_is_shown_as_exact_base64_with_an_honest_description()
    {
        byte[] bytes = [0xC3, 0x28, 0x00, 0xFF];
        MessageDelivery delivery = CreateDelivery("billing", MessageBucket.DeadLetter, "replacement", bytes);
        var inspector = new DeliveryInspector();

        inspector.Select(delivery);

        string expectedBase64 = Convert.ToBase64String(bytes);
        Assert.Equal(expectedBase64, inspector.RawText);
        Assert.Equal(expectedBase64, inspector.Document.Text);
        Assert.Contains("not valid UTF-8", inspector.RawDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(bytes, delivery.Message.RawBody!.ToArray());
        Assert.False(inspector.IsValidJson);
    }

    [Fact]
    public void Non_json_text_is_preserved_and_remains_allowed_until_replay_validation()
    {
        MessageDelivery delivery = CreateDelivery("billing", MessageBucket.DeadLetter, "Audit service started.");
        var inspector = new DeliveryInspector();

        inspector.Select(delivery);

        Assert.Equal("Audit service started.", inspector.RawText);
        Assert.Equal("Audit service started.", inspector.Document.Text);
        Assert.Equal("Body (not JSON)", inspector.JsonLabel);
        Assert.False(inspector.IsValidJson);
        Assert.False(inspector.IsDirty);
    }

    [Fact]
    public void Drafts_are_session_only_and_are_not_shared_with_a_new_inspector()
    {
        MessageDelivery delivery = CreateDelivery("billing", MessageBucket.DeadLetter);
        var firstInspector = new DeliveryInspector();
        firstInspector.Select(delivery);
        firstInspector.Document.Insert(0, " ");

        var newInspector = new DeliveryInspector();
        newInspector.Select(delivery);

        Assert.False(newInspector.IsDirty);
        Assert.False(newInspector.Document.Text.StartsWith(" ", StringComparison.Ordinal));
    }

    private static MessageDelivery CreateDelivery(
        string source,
        MessageBucket bucket,
        string body = "{\"name\":\"Ada\"}",
        byte[]? rawBody = null)
    {
        ExplorerMessage message = new(
            "same-message-id",
            12,
            body,
            body,
            rawBody?.Length ?? Encoding.UTF8.GetByteCount(body),
            null,
            null,
            0,
            "application/json",
            null,
            null,
            null,
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>())
        {
            RawBody = BinaryData.FromBytes(rawBody ?? Encoding.UTF8.GetBytes(body))
        };

        return new MessageDelivery(
            new DeliveryIdentity(1, new EntityAddress(EntityKind.Subscription, source, "orders"), bucket, 12),
            message);
    }
}
