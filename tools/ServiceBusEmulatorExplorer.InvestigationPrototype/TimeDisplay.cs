using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public enum TimeDisplayMode { Utc, Local, Server }

public static class TimeDisplay
{
    public static string Label(TimeDisplayMode mode) => mode == TimeDisplayMode.Utc ? "UTC" : mode.ToString();

    public static string Enqueued(DateTime utc, TimeDisplayMode mode) =>
        InZone(utc, mode).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string LogTime(DateTime utc, TimeDisplayMode mode) =>
        InZone(utc, mode).ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + Label(mode);

    public static DateTimeOffset InZone(DateTime utc, TimeDisplayMode mode)
    {
        var instant = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        return mode switch
        {
            TimeDisplayMode.Local => TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.Local),
            TimeDisplayMode.Server => instant.ToOffset(TimeSpan.FromHours(-5)),
            _ => instant
        };
    }

    public static string Description(TimeDisplayMode mode, DateTime utc)
    {
        var offset = InZone(utc, mode).ToString("zzz", CultureInfo.InvariantCulture);
        return mode switch
        {
            TimeDisplayMode.Local => $"Local: {TimeZoneInfo.Local.DisplayName}. UTC{offset} at this time; daylight-saving rules apply.",
            TimeDisplayMode.Server => "Server: fixed sample UTC-05:00 for this prototype.",
            _ => "Coordinated Universal Time (UTC+00:00)."
        };
    }
}

internal sealed class EnqueuedTimeConverter(TimeDisplayMode mode) : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTime utc ? TimeDisplay.Enqueued(utc, mode) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public partial class PrototypeWindow
{
    private TimeDisplayMode timeDisplayMode;
    private DateTime? lastOperationUtc;

    private void TimeDisplay_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TimeDisplaySelector is null || EnqueuedColumn is null || LogText is null || LastOperationTime is null) return;
        UpdateTimeDisplay();
        QueuePreferencesSave();
    }

    private void UpdateTimeDisplay()
    {
        timeDisplayMode = TimeDisplaySelector.SelectedIndex switch
        {
            1 => TimeDisplayMode.Local,
            2 => TimeDisplayMode.Server,
            _ => TimeDisplayMode.Utc
        };
        EnqueuedColumn.Header = $"Enqueued ({TimeDisplay.Label(timeDisplayMode)})";
        EnqueuedColumn.Binding = new Binding(nameof(MessageRow.Enqueued))
        {
            Converter = new EnqueuedTimeConverter(timeDisplayMode),
            Mode = BindingMode.OneWay
        };
        TimeDisplaySelector.ToolTip = TimeDisplay.Description(timeDisplayMode, DateTime.UtcNow);
        foreach (var paragraph in LogText.Document.Blocks.OfType<Paragraph>().ToArray())
        {
            if (paragraph.Tag is not DateTime utc || paragraph.Inlines.FirstInline is not Run timestamp) continue;
            timestamp.Text = TimeDisplay.LogTime(utc, timeDisplayMode) + "   ";
            timestamp.ToolTip = TimeDisplay.Description(timeDisplayMode, utc);
        }
        if (lastOperationUtc is { } lastUtc)
        {
            LastOperationTime.Text = TimeDisplay.LogTime(lastUtc, timeDisplayMode);
            LastOperationTime.ToolTip = TimeDisplay.Description(timeDisplayMode, lastUtc);
        }
    }
}
