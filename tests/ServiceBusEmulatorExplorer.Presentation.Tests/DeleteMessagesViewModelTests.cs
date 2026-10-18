using System.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class DeleteMessagesViewModelTests
{
    [Fact]
    public void Rejects_missing_empty_active_and_duplicate_targets()
    {
        MessageDelivery target = Delivery(1);
        MessageDelivery active = new(target.Identity with { Bucket = MessageBucket.Active }, target.Message);

        Assert.Throws<ArgumentNullException>(() => new DeleteMessagesViewModel(null!));
        Assert.Throws<ArgumentException>(() => new DeleteMessagesViewModel([]));
        Assert.Throws<ArgumentException>(() => new DeleteMessagesViewModel([active]));
        Assert.Throws<ArgumentException>(() => new DeleteMessagesViewModel([target, Delivery(2), active]));
        Assert.Throws<ArgumentException>(() => new DeleteMessagesViewModel([target, target]));
    }

    [Fact]
    public void Distinct_deliveries_count_as_different_targets_even_with_the_same_message_id()
    {
        MessageDelivery first = Delivery(1, messageId: "same-id");
        MessageDelivery second = Delivery(2, messageId: "same-id");

        var viewModel = new DeleteMessagesViewModel([first, second]);

        Assert.Equal([first, second], viewModel.Targets);
    }

    [Fact]
    public void Targets_keep_their_order_and_ignore_later_changes_to_the_callers_list()
    {
        MessageDelivery first = Delivery(1), second = Delivery(2);
        var caller = new List<MessageDelivery> { second, first };

        var viewModel = new DeleteMessagesViewModel(caller);
        caller.Clear();
        caller.Add(Delivery(3));

        Assert.Equal([second, first], viewModel.Targets);
    }

    [Theory]
    [InlineData(1, "Delete 1 message?", "Delete 1 message", "Selected: 1 dead-letter.")]
    [InlineData(3, "Delete 3 messages?", "Delete 3 messages", "Selected: 3 dead-letter.")]
    public void Texts_name_the_count_with_the_right_plural(int count, string heading, string confirmLabel, string summaryStart)
    {
        var viewModel = new DeleteMessagesViewModel(Enumerable.Range(1, count).Select(sequence => Delivery(sequence)).ToArray());

        Assert.Equal(heading, viewModel.Heading);
        Assert.Equal(confirmLabel, viewModel.ConfirmLabel);
        Assert.Equal($"{summaryStart} Only these selected deliveries will be removed, including any unsaved edits to them.", viewModel.Summary);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("delete")]
    [InlineData("Delete")]
    [InlineData("DELETE ")]
    [InlineData(" DELETE")]
    [InlineData("DELETE\n")]
    public void Only_the_exact_word_allows_confirming(string typed)
    {
        var viewModel = new DeleteMessagesViewModel([Delivery(1)]) { ConfirmationText = typed };
        bool? completed = null;
        viewModel.Completed += accepted => completed = accepted;

        Assert.False(viewModel.ConfirmCommand.CanExecute(null));
        viewModel.ConfirmCommand.Execute(null);

        Assert.Null(completed);
    }

    [Fact]
    public void Typing_the_word_enables_confirming_at_once_and_editing_it_disables_it_again()
    {
        var viewModel = new DeleteMessagesViewModel([Delivery(1)]);
        var availability = new List<bool>();
        var results = new List<bool>();
        viewModel.ConfirmCommand.CanExecuteChanged += (_, _) => availability.Add(viewModel.ConfirmCommand.CanExecute(null));
        viewModel.Completed += results.Add;

        Assert.False(viewModel.ConfirmCommand.CanExecute(null));
        viewModel.ConfirmationText = DeleteMessagesViewModel.ConfirmationWord;
        viewModel.ConfirmationText = "DELET";

        Assert.Equal([true, false], availability);
        Assert.Empty(results);
    }

    [Fact]
    public void Typing_raises_property_change_for_the_bound_text()
    {
        var viewModel = new DeleteMessagesViewModel([Delivery(1)]);
        var changed = new List<string?>();
        ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.ConfirmationText = "D";

        Assert.Equal([nameof(DeleteMessagesViewModel.ConfirmationText)], changed);
    }

    [Fact]
    public void Confirming_with_the_word_completes_with_acceptance_once()
    {
        var viewModel = new DeleteMessagesViewModel([Delivery(1)]) { ConfirmationText = DeleteMessagesViewModel.ConfirmationWord };
        var results = new List<bool>();
        viewModel.Completed += results.Add;

        viewModel.ConfirmCommand.Execute(null);

        Assert.Equal([true], results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("DELETE")]
    public void Cancelling_is_always_possible_completes_with_rejection_and_keeps_the_targets(string typed)
    {
        MessageDelivery target = Delivery(1);
        var viewModel = new DeleteMessagesViewModel([target]) { ConfirmationText = typed };
        var results = new List<bool>();
        viewModel.Completed += results.Add;

        Assert.True(viewModel.CancelCommand.CanExecute(null));
        viewModel.CancelCommand.Execute(null);

        Assert.Equal([false], results);
        Assert.Equal([target], viewModel.Targets);
    }

    private static MessageDelivery Delivery(int sequence, string? messageId = null)
    {
        var message = new ExplorerMessage(messageId ?? $"message-{sequence}", sequence, "{}", "{}", 2, null, null, 0, "application/json",
            null, null, null, new Dictionary<string, object?>(), new Dictionary<string, object?>());
        return new(new(1, new(EntityKind.Queue, "orders"), MessageBucket.DeadLetter, sequence), message);
    }
}
