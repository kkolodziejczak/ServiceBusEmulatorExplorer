using System.Globalization;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation;

/// <summary>Formats UI dates only. Payloads, persisted instants and exports remain invariant.</summary>
public static class DateDisplay
{
    public static string Date(DateTime value, DateDisplayFormat format, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string pattern = format switch
        {
            DateDisplayFormat.Iso => "yyyy-MM-dd",
            DateDisplayFormat.DayFirst => "dd'/'MM'/'yyyy",
            DateDisplayFormat.MonthFirst => "MM'/'dd'/'yyyy",
            DateDisplayFormat.NamedMonth => "dd MMM yyyy",
            _ => culture.DateTimeFormat.ShortDatePattern
        };
        return value.ToString(pattern, culture);
    }

    public static string Timestamp(DateTimeOffset value, DateDisplayFormat format, bool seconds = true) =>
        Date(value.DateTime, format) + value.ToString(seconds ? " HH:mm:ss" : " HH:mm", CultureInfo.CurrentCulture);
}
