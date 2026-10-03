using System.Globalization;
using System.Windows.Data;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeReviewSurface
{
    internal void RefreshDatePresentation()
    {
        if (ScheduledGrid is null || HistoryGrid is null) return;
        DateDisplayFormat format = DatePresentation.GetFormat(this);
        foreach (var row in scheduledResults) row.DateFormat = format;
        ScheduledGrid.Items.Refresh();
        AttemptDateColumn.Binding = new Binding(nameof(PrototypeCancellationAttempt.RequestedAtUtc))
        {
            Converter = new DateTimestampConverter(format)
        };
        CancellationTarget.Text = $"{ReviewProfile.Text} · localhost · {ReviewTarget.Text} · " +
            string.Join("; ", scheduledResults.Select(row => row.DueTimeDisplay).Distinct());
        UpdateReviewTiming();
    }

    private sealed class PrototypeScheduledResult(int row, string receipt, string payload, string outcome, string dueTime)
    {
        public int Row { get; } = row;
        public string Receipt { get; } = receipt;
        public string Payload { get; } = payload;
        public string Outcome { get; } = outcome;
        // New runs capture ISO UTC; older session fixtures are parsed once at this boundary.
        public string DueTime { get; } = dueTime;
        public DateTimeOffset? DueAtUtc { get; } = ReadDueInstant(dueTime);
        public DateDisplayFormat DateFormat { get; set; }
        public string DueTimeDisplay => DueAtUtc is { } instant
            ? DateDisplay.Timestamp(instant, DateFormat, seconds: false) + " UTC" : DueTime;
        public bool Selected { get; set; }
        public string CancellationStatus { get; set; } = "Eligible";
        public string AttemptTime { get; set; } = "—";

        private static DateTimeOffset? ReadDueInstant(string text)
        {
            string value = text.Split('·')[0].Replace("UTC", "", StringComparison.Ordinal).Trim();
            const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, styles, out var instant)
                || DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, styles, out instant) ? instant : null;
        }
    }
}
