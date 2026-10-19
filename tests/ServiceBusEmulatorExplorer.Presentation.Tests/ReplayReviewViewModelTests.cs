using System.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class ReplayReviewViewModelTests
{
    private static readonly EntityAddress Queue = new(EntityKind.Queue, "orders");
    private static readonly EntityAddress Subscription = new(EntityKind.Subscription, "audit", "payments");

    [Fact]
    public void Rejects_missing_or_empty_candidates()
    {
        Assert.Equal("profileName", Assert.Throws<ArgumentNullException>(() => new ReplayReviewViewModel(null!, [Prepared(1)])).ParamName);
        Assert.Equal("candidates", Assert.Throws<ArgumentNullException>(() => new ReplayReviewViewModel("Local", null!)).ParamName);
        Assert.Throws<ArgumentException>(() => new ReplayReviewViewModel("Local", []));
        Assert.Throws<ArgumentException>(() => new ReplayReviewViewModel("Local", [Prepared(1), null!]));
    }

    [Theory]
    [InlineData(1, "Replay 1 message")]
    [InlineData(3, "Replay 3 messages")]
    public void Heading_names_the_count_with_the_right_plural(int count, string heading)
    {
        var review = new ReplayReviewViewModel("Local", Enumerable.Range(1, count).Select(sequence => Prepared(sequence)).ToArray());

        Assert.Equal(heading, review.Heading);
        Assert.Equal("Local", review.ProfileName);
    }

    [Fact]
    public void Rows_keep_the_candidate_order_and_start_with_the_generated_ids()
    {
        PreparedReplay first = Prepared(1), second = Prepared(2);
        var caller = new List<PreparedReplay> { second, first };

        var review = new ReplayReviewViewModel("Local", caller);
        caller.Clear();

        Assert.Equal([second, first], review.Rows.Select(row => row.Prepared));
        Assert.Equal([second.Reservation.MessageId, first.Reservation.MessageId], review.ReviewedIds);
        Assert.All(review.Rows, row => Assert.False(row.HasProblem));
        Assert.True(review.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void A_queue_copy_goes_back_to_its_queue()
    {
        ReplayReviewRow row = new ReplayReviewViewModel("Local", [Prepared(1, messageId: "order-1")]).Rows[0];

        Assert.Equal("orders · Dead letter", row.Source);
        Assert.Equal("orders (Queue)", row.Destination);
        Assert.Equal("order-1", row.OriginalId);
        Assert.Equal("A new active copy will be sent. The original DLQ message stays.", row.Notice);
        Assert.Equal("Original body", row.BodySummary);
    }

    [Fact]
    public void A_subscription_copy_goes_to_its_topic_and_warns_about_other_subscriptions()
    {
        ReplayReviewRow row = new ReplayReviewViewModel("Local", [Prepared(1, Subscription, editedBody: "{\"fixed\":true}")]).Rows[0];

        Assert.Equal("payments / audit · Dead letter", row.Source);
        Assert.Equal("payments (Topic)", row.Destination);
        Assert.Equal("Other matching subscriptions may receive this copy. The original DLQ message stays.", row.Notice);
        Assert.Equal("Modified JSON", row.BodySummary);
    }

    [Theory]
    [InlineData("", "Enter a message ID.")]
    [InlineData("   ", "Enter a message ID.")]
    [InlineData(null, "Enter a message ID.")]
    public void An_invalid_id_shows_its_problem_and_blocks_confirming(string? typed, string problem)
    {
        var review = new ReplayReviewViewModel("Local", [Prepared(1), Prepared(2)]);
        var results = new List<bool>();
        review.Completed += results.Add;

        review.Rows[1].MessageId = typed!;
        review.ConfirmCommand.Execute(null);

        Assert.Equal(problem, review.Rows[1].Problem);
        Assert.True(review.Rows[1].HasProblem);
        Assert.False(review.ConfirmCommand.CanExecute(null));
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(128, null)]
    [InlineData(129, "Use a message ID of at most 128 characters.")]
    public void An_id_may_have_at_most_128_characters(int length, string? problem)
    {
        var review = new ReplayReviewViewModel("Local", [Prepared(1)]);

        review.Rows[0].MessageId = new string('x', length);

        Assert.Equal(problem, review.Rows[0].Problem);
        Assert.Equal(problem is null, review.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Editing_an_id_updates_its_problem_and_confirm_availability_at_once()
    {
        var review = new ReplayReviewViewModel("Local", [Prepared(1)]);
        ReplayReviewRow row = review.Rows[0];
        var changed = new List<string?>();
        var availability = new List<bool>();
        ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        review.ConfirmCommand.CanExecuteChanged += (_, _) => availability.Add(review.ConfirmCommand.CanExecute(null));

        row.MessageId = "";
        row.MessageId = "order-1-reviewed";

        Assert.Equal([nameof(row.MessageId), nameof(row.Problem), nameof(row.HasProblem),
            nameof(row.MessageId), nameof(row.Problem), nameof(row.HasProblem)], changed);
        Assert.Equal([false, true], availability);
        Assert.Equal(["order-1-reviewed"], review.ReviewedIds);
    }

    [Fact]
    public void Reset_puts_back_the_generated_id()
    {
        PreparedReplay candidate = Prepared(1);
        var review = new ReplayReviewViewModel("Local", [candidate]);
        ReplayReviewRow row = review.Rows[0];
        row.MessageId = "";

        row.ResetCommand.Execute(null);

        Assert.Equal(candidate.Reservation.MessageId, row.MessageId);
        Assert.Null(row.Problem);
        Assert.True(review.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Edit_id_asks_the_view_to_focus_that_rows_id()
    {
        var review = new ReplayReviewViewModel("Local", [Prepared(1), Prepared(2)]);
        var focused = new List<ReplayReviewRow>();
        review.IdFocusRequested += focused.Add;

        review.Rows[1].EditIdCommand.Execute(null);

        Assert.Equal([review.Rows[1]], focused);
    }

    [Fact]
    public void Confirming_valid_ids_completes_with_acceptance_once_and_keeps_the_reviewed_ids()
    {
        var review = new ReplayReviewViewModel("Local", [Prepared(1), Prepared(2)]);
        var results = new List<bool>();
        review.Completed += results.Add;
        review.Rows[0].MessageId = "order-1-reviewed";

        review.ConfirmCommand.Execute(null);

        Assert.Equal([true], results);
        Assert.Equal(["order-1-reviewed", review.Rows[1].Prepared.Reservation.MessageId], review.ReviewedIds);
    }

    [Fact]
    public void Cancelling_is_always_possible_and_completes_with_rejection()
    {
        var review = new ReplayReviewViewModel("Local", [Prepared(1)]);
        var results = new List<bool>();
        review.Completed += results.Add;
        review.Rows[0].MessageId = "";

        Assert.True(review.CancelCommand.CanExecute(null));
        review.CancelCommand.Execute(null);

        Assert.Equal([false], results);
    }

    private static PreparedReplay Prepared(int sequence, EntityAddress? source = null, string? messageId = null, string? editedBody = null)
    {
        var message = new ExplorerMessage(messageId ?? $"message-{sequence}", sequence, "{}", "{}", 2, null, null, 0, "application/json",
            null, null, null, new Dictionary<string, object?>(), new Dictionary<string, object?>());
        MessageDelivery delivery = new(new(1, source ?? Queue, MessageBucket.DeadLetter, sequence), message);
        return new PreparedReplay("local", "namespace", delivery, ReplayLineage.Reserve(delivery, []), editedBody);
    }
}
