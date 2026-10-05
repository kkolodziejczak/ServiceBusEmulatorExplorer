using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

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

/// <summary>Flows the workspace's date preference into hosted review controls without global state.</summary>
public static class DatePresentation
{
    public static readonly DependencyProperty FormatProperty = DependencyProperty.RegisterAttached(
        "Format", typeof(DateDisplayFormat), typeof(DatePresentation),
        new FrameworkPropertyMetadata(DateDisplayFormat.Windows, FrameworkPropertyMetadataOptions.Inherits, FormatChanged));

    public static DateDisplayFormat GetFormat(DependencyObject target) => (DateDisplayFormat)target.GetValue(FormatProperty);
    public static void SetFormat(DependencyObject target, DateDisplayFormat value) => target.SetValue(FormatProperty, value);

    private static void FormatChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is CalendarOnlyDatePicker picker) picker.RefreshDateDisplay();
        if (target is MessageLibraryPrototypeReviewSurface review) review.RefreshDatePresentation();
    }
}

internal sealed class DateTimestampConverter(DateDisplayFormat format) : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DateTimeOffset instant => DateDisplay.Timestamp(instant, format),
        _ => string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
