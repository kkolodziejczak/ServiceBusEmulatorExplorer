using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
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
    public void Review_schedule_fields_are_aligned_centered_and_unclipped(int width) => OnSta(() =>
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
            var time = (TextBox)dialog.FindName("ScheduleTimeInput")!;
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
            Assert.Equal(32, time.Height);
            Assert.Equal(VerticalAlignment.Center, time.VerticalContentAlignment);
            Assert.Equal(TextWrapping.Wrap, resolved.TextWrapping);
            Assert.True(zone.Children.OfType<RadioButton>().All(button => button.IsVisible));

            date.ApplyTemplate();
            dialog.UpdateLayout();
            var dateText = Descendants(date).OfType<DatePickerTextBox>().Single();
            Assert.True(dateText.VerticalContentAlignment == VerticalAlignment.Center && dateText.ActualHeight >= 32,
                $"DatePicker template text part needs centered text in a 32-DIP target: alignment={dateText.VerticalContentAlignment}, height={dateText.ActualHeight}, minHeight={dateText.MinHeight}.");
            double editableTextHeight = Descendants(dateText).OfType<ScrollContentPresenter>().Single().ActualHeight;
            Assert.True(editableTextHeight >= 16,
                $"Date text viewport should leave room for the date at {width} DIPs; inner text viewport was {editableTextHeight} DIPs.");

            Rect cardBounds = Bounds(card, dialog);
            foreach (FrameworkElement field in new FrameworkElement[] { date, time, zone, resolved })
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
