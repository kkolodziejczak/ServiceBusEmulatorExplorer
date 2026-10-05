using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.ReadmeScreenshot;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class CalendarPointerInteractionTests
{
    [PointerInputTheory]
    [InlineData(760, false)]
    [InlineData(760, true)]
    [InlineData(980, false)]
    [InlineData(980, true)]
    [InlineData(1100, false)]
    [InlineData(1100, true)]
    [InlineData(1500, false)]
    [InlineData(1500, true)]
    [Trait("TestCategory", "UiSmoke")]
    public void Complete_pointer_click_opens_calendar_without_changing_the_date(int width, bool clickCalendarButton) => OnSta(() =>
    {
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Review, "Local emulator", "order-events", 1)
        {
            Width = width,
            Height = 850,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        try
        {
            dialog.Show();
            ((RadioButton)dialog.FindName("ReviewSchedule")!).IsChecked = true;
            dialog.UpdateLayout();

            var date = (CalendarOnlyDatePicker)dialog.FindName("ScheduleDateInput")!;
            date.ApplyTemplate();
            dialog.UpdateLayout();
            var popup = (Popup)date.Template.FindName("PART_Popup", date)!;
            var pickerButton = Descendants(date).OfType<Button>().Single(button => button.Name == "PART_Button");
            FrameworkElement clickTarget = clickCalendarButton ? pickerButton : date;
            DateTime originalDate = date.SelectedDate!.Value.Date;
            int calendarOpened = 0;
            int calendarClosed = 0;
            date.CalendarOpened += (_, _) => calendarOpened++;
            date.CalendarClosed += (_, _) => calendarClosed++;

            PointerClickTrace trace = SendCompleteWindowClick(dialog, clickTarget, date, popup,
                () => calendarOpened, () => calendarClosed);
            DrainPopupInput(dialog.Dispatcher);

            Assert.True(date.IsDropDownOpen && popup.IsOpen,
                $"A complete click on {(clickCalendarButton ? "the calendar icon" : "the displayed date")} should leave the calendar open; afterDown={trace.DropdownAfterDown}/{trace.PopupAfterDown}, eventsAfterDown={trace.OpenedAfterDown}/{trace.ClosedAfterDown}, selected={date.SelectedDate:O}, original={originalDate:O}, eventsAtEnd={calendarOpened}/{calendarClosed}.");
            Assert.False(trace.DropdownAfterDown || trace.PopupAfterDown,
                "The calendar must wait until the opening gesture is complete.");
            Assert.Equal((0, 0), (trace.OpenedAfterDown, trace.ClosedAfterDown));
            Assert.Equal((1, 0), (calendarOpened, calendarClosed));
            Assert.Equal(originalDate, date.SelectedDate!.Value.Date);

            string? proofDirectory = Environment.GetEnvironmentVariable("SBE_POINTER_PROOF_DIR");
            if (!string.IsNullOrWhiteSpace(proofDirectory))
            {
                string captureName = $"calendar-{width}-{(clickCalendarButton ? "icon" : "date")}.png";
                WpfScreenshot.SaveWindowContentWithPopup(dialog, popup, Path.Combine(proofDirectory, captureName),
                    (int)Math.Ceiling(((FrameworkElement)dialog.Content).ActualWidth),
                    (int)Math.Ceiling(((FrameworkElement)dialog.Content).ActualHeight));
            }

            try { SendKey(0x1b, keyUp: false); PumpInput(dialog.Dispatcher); }
            finally { SendKey(0x1b, keyUp: true); PumpInput(dialog.Dispatcher); }
            Assert.False(date.IsDropDownOpen || popup.IsOpen, "Escape should dismiss the open calendar.");

            _ = SendCompleteWindowClick(dialog, clickTarget, date, popup,
                () => calendarOpened, () => calendarClosed);
            DrainPopupInput(dialog.Dispatcher);
            Assert.True(date.IsDropDownOpen && popup.IsOpen,
                $"A fresh click on {(clickCalendarButton ? "the calendar icon" : "the displayed date")} should reopen the dismissed calendar at {width} DIPs.");
            Assert.Equal((2, 1), (calendarOpened, calendarClosed));
            Assert.Equal(originalDate, date.SelectedDate!.Value.Date);
        }
        finally
        {
            dialog.Close();
        }
    });

    [PointerInputTheory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("TestCategory", "UiSmoke")]
    public void Cancelled_pointer_gesture_does_not_open_calendar(bool loseCapture) => OnSta(() =>
    {
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Review, "Local emulator", "order-events", 1)
        {
            Width = 760, Height = 850, Topmost = true, WindowStartupLocation = WindowStartupLocation.Manual
        };
        try
        {
            dialog.Show();
            ((RadioButton)dialog.FindName("ReviewSchedule")!).IsChecked = true;
            dialog.UpdateLayout();
            var date = (CalendarOnlyDatePicker)dialog.FindName("ScheduleDateInput")!;
            var popup = (Popup)date.Template.FindName("PART_Popup", date)!;
            DateTime? originalDate = date.SelectedDate;
            SendCompleteWindowClick(dialog, date, date, popup, () => 0, () => 0, () =>
            {
                Assert.True(date.IsMouseCaptured);
                if (loseCapture) Mouse.Capture(null);
                else dialog.Left += date.ActualWidth + 16;
            });
            Assert.False(date.IsDropDownOpen || popup.IsOpen);
            Assert.False(date.IsMouseCaptured);
            Assert.Equal(originalDate, date.SelectedDate);
        }
        finally { dialog.Close(); }
    });

    private static PointerClickTrace SendCompleteWindowClick(Window window, FrameworkElement target,
        CalendarOnlyDatePicker date, Popup popup, Func<int> openedCount, Func<int> closedCount, Action? beforeRelease = null)
    {
        var windowHandle = new WindowInteropHelper(window).Handle;
        Assert.NotEqual(IntPtr.Zero, windowHandle);
        Point targetCenter = new(target.ActualWidth / 2, target.ActualHeight / 2);
        Assert.True(GetCursorPos(out NativePoint cursorPoint), "Could not read the current pointer position for the isolated window input proof.");
        window.Activate();
        // Moving across monitors can update WPF's DPI asynchronously; settle before input.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Point screenPoint = target.PointToScreen(targetCenter);
            DpiScale dpi = VisualTreeHelper.GetDpi(window);
            window.Left += (cursorPoint.X - screenPoint.X) / dpi.DpiScaleX;
            window.Top += (cursorPoint.Y - screenPoint.Y) / dpi.DpiScaleY;
            window.UpdateLayout();
            PumpInput(window.Dispatcher);
        }
        Point settledPoint = target.PointToScreen(targetCenter);
        Assert.InRange(Math.Abs(settledPoint.X - cursorPoint.X), 0, 1);
        Assert.InRange(Math.Abs(settledPoint.Y - cursorPoint.Y), 0, 1);
        Assert.Equal(windowHandle, WindowFromPoint(cursorPoint));
        Assert.True(GetCursorPos(out NativePoint current) && current.X == cursorPoint.X && current.Y == cursorPoint.Y,
            "Pointer moved before the guarded test click.");
        bool dropdownAfterDown;
        bool popupAfterDown;
        int openedAfterDown;
        int closedAfterDown;
        try
        {
            SendButton(0x0002);
            PumpInput(window.Dispatcher);
            dropdownAfterDown = date.IsDropDownOpen;
            popupAfterDown = popup.IsOpen;
            openedAfterDown = openedCount();
            closedAfterDown = closedCount();
            beforeRelease?.Invoke();
        }
        finally
        {
            SendButton(0x0004);
            PumpInput(window.Dispatcher);
        }
        return new PointerClickTrace(dropdownAfterDown, popupAfterDown, openedAfterDown, closedAfterDown);
    }

    private sealed record PointerClickTrace(bool DropdownAfterDown, bool PopupAfterDown,
        int OpenedAfterDown, int ClosedAfterDown);

    private static void SendButton(uint flags)
    {
        var input = new NativeInput { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = flags } } };
        Assert.Equal(1u, SendInput(1, [input], Marshal.SizeOf<NativeInput>()));
    }

    private static void SendKey(ushort virtualKey, bool keyUp)
    {
        var input = new NativeInput
        {
            Type = 1,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = keyUp ? 0x0002u : 0u }
            }
        };
        Assert.Equal(1u, SendInput(1, [input], Marshal.SizeOf<NativeInput>()));
    }

    private static void PumpInput(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, NativeInput[] inputs, int size);

    private static void DrainPopupInput(Dispatcher dispatcher)
    {
        foreach (DispatcherPriority priority in new[]
                 {
                     DispatcherPriority.Input,
                     DispatcherPriority.Background,
                     DispatcherPriority.ContextIdle,
                     DispatcherPriority.ApplicationIdle
                 })
        {
            dispatcher.Invoke(() => { }, priority);
        }
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Calendar pointer interaction proof exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y) => (X, Y) = (x, y);
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}

public sealed class PointerInputTheoryAttribute : TheoryAttribute
{
    public PointerInputTheoryAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_RUN_POINTER_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            Skip = "Set SBE_RUN_POINTER_TESTS=true to allow the guarded native pointer proof.";
    }
}
