using System.Globalization;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class WorkbenchRunTests
{
    private static readonly DateTimeOffset Due = new(2099, 9, 23, 14, 30, 0, TimeSpan.Zero);
    private DateTimeOffset now = new(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Sending_acknowledges_each_message_in_turn()
    {
        var run = new WorkbenchRun(() => now);
        Assert.Null(run.Capture("Local emulator", "orders"));
        run.Start(["id-1", "id-2"], null, "orders");

        Assert.False(run.SendNext(DateDisplayFormat.Iso));
        Assert.Equal(1, run.Sent);
        Assert.True(run.SendNext(DateDisplayFormat.Iso));

        Assert.False(run.Scheduled);
        Assert.Equal([new WorkbenchSendResult(1, "id-1", "Confirmed sent", "Sample acknowledgement"),
            new WorkbenchSendResult(2, "id-2", "Confirmed sent", "Sample acknowledgement")], run.Results);
        Assert.Empty(run.ScheduledReceipts);
        var snapshot = run.Capture("Local emulator", "orders")!;
        Assert.Equal(("Local emulator", "orders", false), (snapshot.Profile, snapshot.Target, snapshot.Scheduled));
        Assert.Equal([(1, "id-1", "Confirmed sent", "Sample acknowledgement"), (2, "id-2", "Confirmed sent", "Sample acknowledgement")],
            snapshot.Results);
    }

    [Fact]
    public void Scheduling_keeps_a_selected_receipt_due_at_the_one_instant()
    {
        using var culture = new UseCulture("");
        var run = new WorkbenchRun(() => now);
        run.Start(["id-1", "id-2"], Due, "order-events");
        run.SendNext(DateDisplayFormat.Iso);
        run.SendNext(DateDisplayFormat.DayFirst);

        Assert.True(run.Scheduled);
        Assert.Equal(new WorkbenchSendResult(2, "id-2", "Confirmed scheduled", "Sample receipt retained"), run.Results[1]);
        var receipt = run.ScheduledReceipts[1];
        Assert.Equal((2, "702", "order-events-2.json", "Confirmed scheduled", "2099-09-23T14:30:00.0000000+00:00", true, "Eligible", "—"),
            (receipt.Row, receipt.Receipt, receipt.Payload, receipt.Outcome, receipt.DueTime, receipt.Selected, receipt.CancellationStatus, receipt.AttemptTime));
        Assert.Equal("2099-09-23 14:30 UTC", run.ScheduledReceipts[0].DueTimeDisplay);
        Assert.Equal("23/09/2099 14:30 UTC", receipt.DueTimeDisplay);
        Assert.Equal("2099-09-23 14:30 UTC; 23/09/2099 14:30 UTC", run.DueTimes);

        run.UseDateFormat(DateDisplayFormat.Iso);
        Assert.Equal("2099-09-23 14:30 UTC", run.DueTimes);
    }

    [Fact]
    public void Stopping_marks_the_message_in_flight_unknown_and_the_rest_not_attempted()
    {
        var run = new WorkbenchRun(() => now);
        run.Start(["id-1", "id-2", "id-3"], null, "orders");
        run.SendNext(DateDisplayFormat.Iso);

        run.Stop();

        Assert.Equal([new WorkbenchSendResult(1, "id-1", "Confirmed sent", "Sample acknowledgement"),
            new WorkbenchSendResult(2, "id-2", "Outcome unknown", "In flight when stopped"),
            new WorkbenchSendResult(3, "id-3", "Not attempted", "Not submitted")], run.Results);

        run.Start(["id-4"], null, "orders");
        Assert.Empty(run.Results);
        run.SendNext(DateDisplayFormat.Iso);
        run.Stop();
        Assert.Equal([new WorkbenchSendResult(1, "id-4", "Confirmed sent", "Sample acknowledgement")], run.Results);
    }

    [Fact]
    public void Starting_again_clears_the_previous_run()
    {
        var run = Restored(Receipt(1, "701"));
        run.Cancel(run.CancellableSelection());

        run.Start(["id-1"], null, "orders");

        Assert.Empty(run.Results);
        Assert.Empty(run.ScheduledReceipts);
        Assert.Empty(run.CancellationAttempts);
        Assert.False(run.Scheduled);
    }

    [Fact]
    public void A_restored_run_captures_as_it_was_kept()
    {
        var attempt = new PrototypeCancellationAttempt(2, "702", "second.json", "Outcome unknown", now.AddMinutes(-1));
        var kept = Snapshot([Receipt(1, "701", selected: false, status: "Cancellation acknowledged", attemptTime: "09:29 UTC"),
            Receipt(2, "702", payload: "second.json", status: "Outcome unknown")], attempt);
        var run = new WorkbenchRun(() => now);

        run.Restore(kept, DateDisplayFormat.Iso);

        Assert.True(run.Scheduled);
        Assert.All(run.ScheduledReceipts, receipt => Assert.Equal(DateDisplayFormat.Iso, receipt.DateFormat));
        var captured = run.Capture("Local emulator", "order-events")!;
        Assert.Equal(kept.Results, captured.Results);
        Assert.Equal(kept.ScheduledResults, captured.ScheduledResults);
        Assert.Equal(kept.CancellationAttempts, captured.CancellationAttempts);
    }

    [Fact]
    public void Cancelling_acknowledges_odd_rows_and_stops_after_an_unknown_outcome()
    {
        var run = Restored(Receipt(1, "701"), Receipt(2, "702"), Receipt(3, "703"), Receipt(5, "705"));

        run.Cancel(run.CancellableSelection());

        Assert.Equal(["Cancellation acknowledged", "Outcome unknown", "Not attempted", "Not attempted"],
            run.ScheduledReceipts.Select(row => row.CancellationStatus));
        Assert.Equal([false, true, true, true], run.ScheduledReceipts.Select(row => row.Selected));
        Assert.All(run.ScheduledReceipts, row => Assert.Equal("09:30 UTC", row.AttemptTime));
        Assert.Equal([new PrototypeCancellationAttempt(1, "701", "order-events-1.json", "Cancellation acknowledged", now),
                new PrototypeCancellationAttempt(2, "702", "order-events-2.json", "Outcome unknown", now),
                new PrototypeCancellationAttempt(3, "703", "order-events-3.json", "Not attempted", now),
                new PrototypeCancellationAttempt(5, "705", "order-events-5.json", "Not attempted", now)],
            run.CancellationAttempts);
        Assert.All(run.Results, result => Assert.Equal("Confirmed scheduled", result.Outcome));
    }

    [Fact]
    public void Only_selected_future_scheduled_receipts_without_acknowledgement_can_be_cancelled()
    {
        var run = Restored(
            Receipt(1, "701"),
            Receipt(2, "702", outcome: "Scheduled"),
            Receipt(3, "703", selected: false),
            Receipt(4, "704", outcome: "Outcome unknown"),
            Receipt(5, "705", status: "Cancellation acknowledged"),
            Receipt(6, "receipt-6"),
            Receipt(7, "-707"),
            Receipt(8, "708", dueTime: "2026-10-18T09:30:00.0000000+00:00"),
            Receipt(9, "709", dueTime: "soon"),
            Receipt(10, "710", dueTime: "23 Sep 2099 14:30 UTC"));

        Assert.Equal([1, 2, 10], run.CancellableSelection().Select(row => row.Row));
        Assert.True(run.CanRetryCancellation);

        now = Due;
        Assert.Empty(run.CancellableSelection());
        Assert.False(run.CanRetryCancellation);

        now = new(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);
        Assert.False(Restored(Receipt(1, "701", selected: false)).CanRetryCancellation);
    }

    [Fact]
    public void Cancelling_skips_receipts_that_fell_due_after_they_were_selected()
    {
        var run = Restored(Receipt(1, "701"), Receipt(3, "703", dueTime: "2026-10-18T09:45:00Z"));
        var selected = run.CancellableSelection();
        Assert.Equal([1, 3], selected.Select(row => row.Row));

        now = now.AddMinutes(20);
        run.Cancel(selected);

        Assert.Equal([1], run.CancellationAttempts.Select(attempt => attempt.Row));
        Assert.Equal("Eligible", run.ScheduledReceipts[1].CancellationStatus);
    }

    [Fact]
    public void A_receipt_with_ten_attempts_can_no_longer_be_cancelled()
    {
        const string open = "Dispatch results remain unchanged. Retry requires fresh confirmation; only future, unacknowledged receipts are eligible.";
        var attempts = Enumerable.Range(1, 9)
            .Select(index => new PrototypeCancellationAttempt(2, "702", "order-events-2.json", "Outcome unknown", now.AddMinutes(-index)))
            .ToArray();
        var run = new WorkbenchRun(() => now);
        run.Restore(Snapshot([Receipt(2, "702"), Receipt(4, "704")],
            [.. attempts, .. Enumerable.Repeat(new PrototypeCancellationAttempt(4, "other", "x", "Outcome unknown", now), 10)]), DateDisplayFormat.Iso);
        Assert.Equal(open, run.CancellationNotice);

        run.Cancel(run.CancellableSelection().Take(1));

        Assert.Equal(20, run.CancellationAttempts.Count);
        Assert.Equal([4], run.CancellableSelection().Select(row => row.Row));
        Assert.Equal("10 attempts reached for a receipt; further attempts for that receipt are disabled. Dispatch results remain unchanged.",
            run.CancellationNotice);
    }

    [Fact]
    public void Due_times_are_read_from_iso_or_older_display_text()
    {
        using (new UseCulture("de-DE"))
            Assert.Equal(Due, new WorkbenchScheduledReceipt(1, "701", "p", "Scheduled", "23.09.2099 14:30 UTC").DueAtUtc);
        using var culture = new UseCulture("");
        Assert.Equal(Due, new WorkbenchScheduledReceipt(1, "701", "p", "Scheduled", "23 Sep 2099 14:30 UTC").DueAtUtc);
        Assert.Equal(Due, new WorkbenchScheduledReceipt(1, "701", "p", "Scheduled", "2099-09-23 14:30 UTC · 16:30 local").DueAtUtc);
        var unreadable = new WorkbenchScheduledReceipt(1, "701", "p", "Scheduled", "soon");
        Assert.Null(unreadable.DueAtUtc);
        Assert.Equal("soon", unreadable.DueTimeDisplay);
    }

    private WorkbenchRun Restored(params ScheduledRow[] receipts)
    {
        var run = new WorkbenchRun(() => now);
        run.Restore(Snapshot(receipts), DateDisplayFormat.Iso);
        return run;
    }

    private static PrototypeRunSnapshot Snapshot(IReadOnlyList<ScheduledRow> receipts, params PrototypeCancellationAttempt[] attempts) =>
        new("Local emulator", "order-events", true,
            receipts.Select(row => (row.Row, $"id-{row.Row}", "Confirmed scheduled", "Sample receipt retained")).ToArray(),
            receipts.Select(row => (row.Row, row.Receipt, row.Payload, row.Outcome, row.DueTime, row.Selected, row.CancellationStatus, row.AttemptTime)).ToArray())
        { CancellationAttempts = attempts };

    private static ScheduledRow Receipt(int row, string receipt, string outcome = "Confirmed scheduled", bool selected = true,
        string status = "Eligible", string dueTime = "2099-09-23T14:30:00.0000000+00:00", string? payload = null, string attemptTime = "—") =>
        new(row, receipt, payload ?? $"order-events-{row}.json", outcome, dueTime, selected, status, attemptTime);

    private sealed record ScheduledRow(int Row, string Receipt, string Payload, string Outcome, string DueTime, bool Selected,
        string CancellationStatus, string AttemptTime);

    /// <summary>
    /// Formats and reads dates in the named culture (the invariant one for "") while a test runs, so the expected texts
    /// hold on any machine.
    /// </summary>
    private sealed class UseCulture : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;

        public UseCulture(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = previous;
    }
}
