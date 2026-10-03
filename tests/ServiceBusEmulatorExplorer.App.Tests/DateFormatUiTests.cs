using Calendar = System.Windows.Controls.Calendar;
using System.IO;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.ReadmeScreenshot;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class DateFormatUiTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Settings_offers_five_date_formats_saves_selection_and_restores_it_when_reopened()
        => OnSta((dispatcher) =>
        {
            WorkspacePreferences persisted = new();
            var window = new SettingsWindow(persisted, preferences =>
            {
                persisted = preferences;
                return Task.CompletedTask;
            });
            try
            {
                window.Show();
                window.UpdateLayout();
                var selector = (ComboBox)window.FindName("DateFormatSelector")!;
                var choices = selector.Items.Cast<DateFormatChoice>().ToArray();

                Assert.Equal(5, choices.Length);
                Assert.Equal(new[]
                {
                    DateDisplayFormat.Windows,
                    DateDisplayFormat.Iso,
                    DateDisplayFormat.DayFirst,
                    DateDisplayFormat.MonthFirst,
                    DateDisplayFormat.NamedMonth
                }, choices.Select(choice => choice.Format));
                Assert.Collection(choices,
                    choice => Assert.StartsWith("Follow Windows", choice.Label, StringComparison.Ordinal),
                    choice => Assert.StartsWith("yyyy-MM-dd", choice.Label, StringComparison.Ordinal),
                    choice => Assert.StartsWith("dd/MM/yyyy", choice.Label, StringComparison.Ordinal),
                    choice => Assert.StartsWith("MM/dd/yyyy", choice.Label, StringComparison.Ordinal),
                    choice => Assert.StartsWith("dd MMM yyyy", choice.Label, StringComparison.Ordinal));
                Assert.All(choices, choice => Assert.Contains(DateDisplay.Date(new DateTime(2026, 10, 4), choice.Format), choice.Label));

                selector.SelectedValue = DateDisplayFormat.MonthFirst;
                WaitUntil(dispatcher, () => persisted.DateFormat == DateDisplayFormat.MonthFirst,
                    "The selected date format was not persisted.");

                window.Close();
                var reopened = new SettingsWindow(persisted, _ => Task.CompletedTask);
                try
                {
                    reopened.Show();
                    reopened.UpdateLayout();
                    var restoredSelector = (ComboBox)reopened.FindName("DateFormatSelector")!;
                    Assert.Equal(DateDisplayFormat.MonthFirst, restoredSelector.SelectedValue);

                    foreach (double width in new[] { 460d, 520d, 980d })
                    {
                        reopened.Width = width;
                        reopened.UpdateLayout();
                        dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                        var settingsContent = Assert.IsAssignableFrom<FrameworkElement>(reopened.Content);
                        var dateSelector = (FrameworkElement)reopened.FindName("DateFormatSelector")!;
                        Rect selectorBounds = Bounds(dateSelector, settingsContent);
                        Assert.True(selectorBounds.Left >= 0 && selectorBounds.Right <= settingsContent.ActualWidth + 1,
                            $"Date format selector is clipped at the {width}-DIP Settings width: {selectorBounds}, content width={settingsContent.ActualWidth}.");
                        Assert.True(selectorBounds.Height > 0 && selectorBounds.Bottom <= settingsContent.ActualHeight + 1,
                            $"Date format selector is outside the visible Settings content at the {width}-DIP width: {selectorBounds}.");
                        CaptureIfEnabled(reopened, $"settings-{(int)width}.png");
                    }

                    reopened.Width = 460;
                    reopened.UpdateLayout();
                    dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    restoredSelector.ApplyTemplate();
                    var selectionPresenter = (ContentPresenter)restoredSelector.Template.FindName("Selection", restoredSelector)!;
                    foreach (DateFormatChoice choice in choices)
                    {
                        restoredSelector.SelectedValue = choice.Format;
                        WaitUntil(dispatcher, () => reopened.CurrentPreferences.DateFormat == choice.Format,
                            $"Settings did not apply the {choice.Format} choice at the narrow width.");
                        reopened.UpdateLayout();
                        var text = new FormattedText(choice.Label, CultureInfo.CurrentCulture,
                            restoredSelector.FlowDirection,
                            new Typeface(restoredSelector.FontFamily, restoredSelector.FontStyle,
                                restoredSelector.FontWeight, restoredSelector.FontStretch),
                            restoredSelector.FontSize, Brushes.Transparent,
                            VisualTreeHelper.GetDpi(selectionPresenter).PixelsPerDip);
                        Assert.True(text.WidthIncludingTrailingWhitespace <= selectionPresenter.ActualWidth + 1,
                            $"The {choice.Format} label is clipped at 460 DIPs: text width={text.WidthIncludingTrailingWhitespace}, available={selectionPresenter.ActualWidth}.");
                    }

                    var dropdown = (Popup)restoredSelector.Template.FindName("PART_Popup", restoredSelector)!;
                    restoredSelector.IsDropDownOpen = true;
                    dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.True(dropdown.IsOpen);
                    CaptureIfEnabled(reopened, dropdown, "settings-date-format-dropdown-460.png");
                    restoredSelector.IsDropDownOpen = false;
                }
                finally
                {
                    reopened.Close();
                }
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Schedule_date_format_updates_without_changing_scheduled_instant_and_clears_text_highlight_after_selection()
        => OnSta(dispatcher =>
        {
            var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Review, "Local emulator", "order-events", 1)
            {
                Height = 820
            };
            try
            {
                dialog.Show();
                ((RadioButton)dialog.FindName("ReviewSchedule")!).IsChecked = true;
                dialog.UpdateLayout();

                var date = (CalendarOnlyDatePicker)dialog.FindName("ScheduleDateInput")!;
                var hour = (ComboBox)dialog.FindName("ScheduleHourInput")!;
                var minute = (ComboBox)dialog.FindName("ScheduleMinuteInput")!;
                DateTime selectedDate = new(2026, 10, 4);
                date.SelectedDate = selectedDate;
                hour.SelectedItem = "14";
                minute.SelectedItem = "30";
                dialog.UpdateLayout();
                date.ApplyTemplate();

                var dateText = Descendants(date).OfType<DatePickerTextBox>().Single();
                var peer = UIElementAutomationPeer.CreatePeerForElement(date)!;
                var value = Assert.IsAssignableFrom<IValueProvider>(peer.GetPattern(PatternInterface.Value));
                Assert.True(value.IsReadOnly);

                foreach (DateDisplayFormat format in Enum.GetValues<DateDisplayFormat>())
                {
                    DatePresentation.SetFormat(dialog, format);
                    dialog.UpdateLayout();
                    string expectedDate = DateDisplay.Date(selectedDate, format);
                    Assert.Equal(expectedDate, date.DisplayDateText);
                    Assert.Equal(date.DisplayDateText, value.Value);
                    Assert.Equal(selectedDate, date.SelectedDate!.Value.Date);
                    Assert.Equal("14", hour.SelectedItem);
                    Assert.Equal("30", minute.SelectedItem);
                    Assert.Contains(expectedDate + " 14:30", ((TextBlock)dialog.FindName("ResolvedSchedule")!).Text);
                    CaptureIfEnabled(dialog, $"schedule-{format}.png");
                }

                var popup = (Popup)date.Template.FindName("PART_Popup", date)!;
                date.IsDropDownOpen = true;
                dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(popup.IsOpen);
                CaptureIfEnabled(dialog, popup, "schedule-calendar-open.png");
                var calendar = popup.Child as Calendar ?? Descendants(popup.Child!).OfType<Calendar>().Single();
                var nextDate = selectedDate.AddDays(1);
                var day = Descendants(calendar).OfType<CalendarDayButton>().Single(button =>
                    button.DataContext is DateTime dateContext && dateContext.Date == nextDate);
                day.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = Mouse.MouseDownEvent
                });
                day.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = Mouse.MouseUpEvent
                });
                dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.Equal(nextDate, date.SelectedDate!.Value.Date);
                Assert.Equal(0, dateText.SelectionLength);
                Assert.Equal(DateDisplay.Date(nextDate, DateDisplayFormat.NamedMonth), value.Value);
                Assert.False(date.IsDropDownOpen);
                Assert.Equal("14", hour.SelectedItem);
                Assert.Equal("30", minute.SelectedItem);
                CaptureIfEnabled(dialog, "schedule-after-day-selection.png");
            }
            finally
            {
                dialog.Close();
            }
        });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static void CaptureIfEnabled(Window window, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("SBE_DATE_FORMAT_PROOF_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
        var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        WpfScreenshot.SaveWindowContent(window, Path.Combine(directory, name),
            (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight));
    }

    private static void CaptureIfEnabled(Window window, Popup popup, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("SBE_DATE_FORMAT_PROOF_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
        var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        WpfScreenshot.SaveWindowContentWithPopup(window, popup, Path.Combine(directory, name),
            (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight));
    }

    private static void WaitUntil(Dispatcher dispatcher, Func<bool> condition, string message)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, message);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
    }

    private static void OnSta(Action<Dispatcher> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                action(dispatcher);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Date format UI proof exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
