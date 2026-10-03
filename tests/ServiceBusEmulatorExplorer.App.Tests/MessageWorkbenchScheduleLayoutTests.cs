using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class MessageWorkbenchScheduleLayoutTests
{
    [Theory]
    [InlineData(980)]
    [InlineData(1100)]
    [InlineData(1500)]
    [Trait("TestCategory", "UiRender")]
    public void Review_schedule_fields_are_compact_and_unclipped(int width) => OnSta(() =>
    {
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Review, "Local emulator", "order-events", 1)
        {
            Width = width,
            Height = 850
        };
        try
        {
            dialog.Show();
            dialog.UpdateLayout();
            ((RadioButton)dialog.FindName("ReviewSchedule")!).IsChecked = true;
            dialog.UpdateLayout();

            var schedule = (Grid)dialog.FindName("ScheduleInputs")!;
            var date = (DatePicker)dialog.FindName("ScheduleDateInput")!;
            var hour = (ComboBox)dialog.FindName("ScheduleHourInput")!;
            var minute = (ComboBox)dialog.FindName("ScheduleMinuteInput")!;
            var zone = schedule.Children.OfType<WrapPanel>().Single();
            var resolved = (TextBlock)dialog.FindName("ResolvedSchedule")!;
            var reviewSurface = (FrameworkElement)dialog.FindName("ReviewSurface")!;
            var card = Descendants(reviewSurface).OfType<FrameworkElement>().Single(element => element.Name == "ReviewTargetCard");

            Assert.Equal(4, schedule.RowDefinitions.Count);
            Assert.Equal(new[] { "Date", "Time", "Time zone" }, schedule.Children.OfType<TextBlock>()
                .Where(label => Grid.GetColumn(label) == 0)
                .Select(label => label.Text)
                .ToArray());
            Assert.Equal(32, date.Height);
            Assert.Equal(32, hour.Height);
            Assert.Equal(32, minute.Height);
            Assert.False(hour.IsEditable);
            Assert.False(minute.IsEditable);
            var timeFields = Descendants(schedule).OfType<FrameworkElement>().Single(element => element.Name == "ScheduleTimeFields");
            Assert.InRange(Math.Abs(date.MaxWidth - timeFields.ActualWidth), 0, 1);
            Assert.True(date.ActualWidth > 0 && date.ActualWidth <= timeFields.ActualWidth);
            Assert.Equal(Enumerable.Range(0, 24).Select(value => value.ToString("D2")), hour.Items.Cast<string>());
            Assert.Equal(Enumerable.Range(0, 60).Select(value => value.ToString("D2")), minute.Items.Cast<string>());
            Assert.Equal("14", hour.SelectedItem);
            Assert.Equal("30", minute.SelectedItem);
            Assert.Equal("ScheduleDateInput", AutomationProperties.GetAutomationId(date));
            Assert.Equal("ScheduleHourInput", AutomationProperties.GetAutomationId(hour));
            Assert.Equal("ScheduleMinuteInput", AutomationProperties.GetAutomationId(minute));
            Assert.Equal("Date", AutomationProperties.GetName(date));
            Assert.Equal("Hour", AutomationProperties.GetName(hour));
            Assert.Equal("Minute", AutomationProperties.GetName(minute));
            date.Focus();
            Assert.True(date.IsKeyboardFocusWithin, $"Date focus: focusable={date.Focusable}, keyboard={System.Windows.Input.Keyboard.FocusedElement}");
            Assert.True(hour.Focus() && hour.IsKeyboardFocusWithin);
            Assert.True(minute.Focus() && minute.IsKeyboardFocusWithin);
            Assert.Equal(TextWrapping.Wrap, resolved.TextWrapping);
            Assert.True(zone.Children.OfType<RadioButton>().All(button => button.IsVisible));
            Assert.All(zone.Children.OfType<RadioButton>(), button =>
            {
                Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
                AssertRadioGlyphAndLabelAreVerticallyCentered(button);
            });
            foreach (string radioName in new[] { "ReviewSendNow", "ReviewSchedule", "ScheduleUtc", "ScheduleLocal" })
            {
                var button = (RadioButton)dialog.FindName(radioName)!;
                Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
                AssertRadioGlyphAndLabelAreVerticallyCentered(button);
            }

            date.ApplyTemplate();
            dialog.UpdateLayout();
            var dateText = Descendants(date).OfType<DatePickerTextBox>().Single();
            Assert.True(dateText.VerticalContentAlignment == VerticalAlignment.Center && dateText.ActualHeight >= 28,
                $"DatePicker template text part needs centered text inside a 32-DIP control: alignment={dateText.VerticalContentAlignment}, height={dateText.ActualHeight}, minHeight={dateText.MinHeight}.");
            double editableTextHeight = Descendants(dateText).OfType<ScrollContentPresenter>().Single().ActualHeight;
            Assert.True(editableTextHeight >= 16,
                $"Date text viewport should leave room for the date at {width} DIPs; inner text viewport was {editableTextHeight} DIPs.");

            Rect dateBounds = Bounds(date, dialog);
            Rect hourBounds = Bounds(hour, dialog);
            Rect minuteBounds = Bounds(minute, dialog);

            Rect dateTextLine = dateText.TransformToAncestor(dialog).TransformBounds(
                dateText.GetRectFromCharacterIndex(0));
            double dateCenter = dateBounds.Top + dateBounds.Height / 2;
            double textCenter = dateTextLine.Top + dateTextLine.Height / 2;
            Assert.True(Math.Abs(textCenter - dateCenter) <= 0.5,
                $"Visible date text should be centered vertically in the field at {width} DIPs: date={dateBounds}, text={dateTextLine}.");

            Assert.InRange(Math.Abs(dateBounds.Left - hourBounds.Left), 0, 1);
            Assert.InRange(Math.Abs(dateBounds.Right - minuteBounds.Right), 0, 1);

            var pickerButton = Descendants(date).OfType<Button>().Single(button => button.Name == "PART_Button");
            Rect pickerButtonBounds = Bounds(pickerButton, dialog);
            Assert.True(pickerButtonBounds.Left >= dateBounds.Left && pickerButtonBounds.Right <= dateBounds.Right
                && pickerButtonBounds.Top >= dateBounds.Top && pickerButtonBounds.Bottom <= dateBounds.Bottom,
                $"Calendar action must remain contained in compact date field at {width} DIPs: button={pickerButtonBounds}, date={dateBounds}.");

            Rect cardBounds = Bounds(card, dialog);
            foreach (FrameworkElement field in new FrameworkElement[] { date, hour, minute, zone, resolved })
            {
                Rect bounds = Bounds(field, dialog);
                Assert.True(bounds.Width > 0 && bounds.Height > 0, $"{field.Name} did not render at {width} DIPs: {bounds}.");
                Assert.True(bounds.Left >= cardBounds.Left && bounds.Right <= cardBounds.Right
                    && bounds.Top >= cardBounds.Top && bounds.Bottom <= cardBounds.Bottom,
                    $"{field.Name} clipped by target card at {width} DIPs: {bounds}; card {cardBounds}.");
            }
        }
        finally
        {
            dialog.Close();
        }
    });

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static void AssertRadioGlyphAndLabelAreVerticallyCentered(RadioButton button)
    {
        button.ApplyTemplate();
        var decorator = Descendants(button).OfType<BulletDecorator>().Single();
        var glyph = Assert.IsAssignableFrom<FrameworkElement>(decorator.Bullet);
        var label = Assert.IsAssignableFrom<FrameworkElement>(decorator.Child);
        Rect glyphBounds = Bounds(glyph, button);
        Rect labelBounds = Bounds(label, button);
        double glyphCenter = glyphBounds.Top + glyphBounds.Height / 2;
        double labelCenter = labelBounds.Top + labelBounds.Height / 2;
        Assert.InRange(Math.Abs(glyphCenter - labelCenter), 0, 1.5);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Schedule layout proof exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
