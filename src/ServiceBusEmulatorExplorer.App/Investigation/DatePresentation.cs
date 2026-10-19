using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

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
