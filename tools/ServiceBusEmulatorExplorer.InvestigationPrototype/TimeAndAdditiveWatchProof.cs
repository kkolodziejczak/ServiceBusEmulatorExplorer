using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class TimeAndAdditiveWatchProof
{
    public static async Task Exercise(PrototypeWindow window, List<string> report, string output)
    {
        await ExerciseTime(window, report, output);
        await ExerciseAdditiveWatch(window, report, output);
    }

    private static async Task ExerciseTime(PrototypeWindow window, List<string> report, string output)
    {
        var selector = (ComboBox)window.FindName("TimeDisplaySelector");
        var column = (DataGridTextColumn)window.FindName("EnqueuedColumn");
        var row = window.Workspace.Messages.First();
        var originalBody = row.Body;
        var originalProperties = row.Properties;
        var originalEnqueued = row.Enqueued;
        var log = (RichTextBox)window.FindName("LogText");
        var paragraphs = log.Document.Blocks.OfType<Paragraph>().ToArray();
        Check(paragraphs.Length > 0, "Time proof starts with existing activity history", report);
        var footer = (TextBlock)window.FindName("LastOperationTime");
        for (var index = 0; index < 3; index++)
        {
            selector.SelectedIndex = index;
            await Settle();
            var label = new[] { "UTC", "Local", "Server" }[index];
            var expected = Convert(row.Enqueued, index).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var cell = column.GetCellContent(row) as TextBlock;
            Check(column.Header?.ToString() == $"Enqueued ({label})" && cell?.Text == expected,
                $"{label} selection changes the rendered Enqueued header and numerical timestamp", report);
            Check(paragraphs.All(paragraph => paragraph.Tag is DateTime utc && paragraph.Inlines.FirstInline is Run run
                && run.Text.StartsWith(Convert(utc, index).ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + label, StringComparison.Ordinal)),
                $"{label} selection reformats existing console timestamps from their original UTC instants", report);
            var lastUtc = (DateTime)paragraphs[^1].Tag;
            Check(footer.Text == Convert(lastUtc, index).ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + label,
                $"{label} selection reformats the footer's existing last-operation timestamp", report);
        }
        Check(row.Body == originalBody && row.Properties == originalProperties && row.Enqueued == originalEnqueued,
            "Time display preferences preserve original message JSON, metadata and UTC instant", report);
        ProofCapture.Save(window, output, "time-server");
        selector.SelectedIndex = 0;
        await Settle();
    }

    private static DateTime Convert(DateTime utc, int mode) => mode switch
    {
        1 => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local),
        2 => utc.AddHours(-5),
        _ => utc
    };

    private static async Task ExerciseAdditiveWatch(PrototypeWindow window, List<string> report, string output)
    {
        var workspace = window.Workspace;
        var original = workspace.SnapshotMessages().First();
        var box = (TextBox)window.FindName("SearchBox");
        box.Text = MessageSearchQuery.QuoteLiteral(original.MessageId);
        typeof(PrototypeWindow).GetMethod("BeginGlobalSearch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [true]);
        await Complete(window);
        var applied = workspace.CorrelationQuery;
        box.Text = "unapplied-draft-must-not-be-searched";
        window.SetWatched(original.Source, true, true);
        window.SimulateWatchedArrivals();
        await Settle();
        var notification = Application.Current.Windows.OfType<WatchNotificationWindow>().Single();
        var arrival = workspace.SnapshotMessages().Where(row => row.Source == original.Source && row.IsDeadLetter)
            .OrderByDescending(row => row.Enqueued).First();
        ProofCapture.Descendants(notification).OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Investigate notification")
            .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Complete(window);
        var expected = workspace.SnapshotMessages().Where(row => row.MessageId == original.MessageId || row.CorrelationId == arrival.CorrelationId)
            .Select(row => row.Key).Order().ToArray();
        Check(workspace.Messages.Select(row => row.Key).Order().SequenceEqual(expected),
            "Investigate unions the applied message-ID search with the notified correlation across the connection", report);
        Check(box.Text.Contains(original.MessageId, StringComparison.Ordinal) && !box.Text.Contains("unapplied-draft", StringComparison.Ordinal)
            && workspace.FocusedMessage?.Key == arrival.Key,
            "Investigate preserves applied search meaning, replaces unrelated draft text and focuses the arrival", report);
        var combined = workspace.CorrelationQuery;
        InvokeBatch(window, [arrival]);
        await Complete(window);
        Check(workspace.CorrelationQuery == combined, "Investigating the same notification again does not duplicate criteria", report);
        var snapshot = workspace.Messages.Select(row => row.Key).ToArray();
        InvokeBatch(window, []);
        await Settle();
        Check(workspace.CorrelationQuery == combined && workspace.Messages.Select(row => row.Key).SequenceEqual(snapshot),
            "An empty notification batch preserves the applied query and result snapshot", report);
        var fallbackSource = workspace.SnapshotMessages().First(row => !snapshot.Contains(row.Key));
        var noCorrelation = new MessageRow
        {
            Key = fallbackSource.Key, MessageId = fallbackSource.MessageId, EventName = fallbackSource.EventName,
            Source = fallbackSource.Source, Body = fallbackSource.Body, Properties = fallbackSource.Properties, CorrelationId = ""
        };
        InvokeBatch(window, [noCorrelation]);
        await Complete(window);
        Check(workspace.Messages.Select(row => row.Key).Order().SequenceEqual(workspace.SnapshotMessages()
            .Where(row => expected.Contains(row.Key) || row.MessageId == fallbackSource.MessageId).Select(row => row.Key).Order()),
            "A notified message without correlation appends its message-ID criterion without dropping prior cases", report);
        ProofCapture.Save(window, output, "additive-watch-search");
        window.SetWatched(original.Source, true, false);
        ((Button)window.FindName("ClearSearchButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Settle();
    }

    private static void InvokeBatch(PrototypeWindow window, IReadOnlyList<MessageRow> messages) =>
        typeof(PrototypeWindow).GetMethod("InvestigateWatchedCases", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [messages]);

    private static async Task Complete(PrototypeWindow window)
    {
        for (var page = 0; window.Workspace.IsSearching && page < 1000; page++) window.Workspace.ScanNext();
        if (window.Workspace.IsSearching) throw new InvalidOperationException("Additive search exceeded bounded scan.");
        await Settle();
    }

    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
