using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal sealed class NativeWindowSizeOverride : IDisposable
{
    private const int WindowProcedureIndex = -4;
    private const int GetMinimumMaximumInfoMessage = 0x0024;
    private const uint DefaultDpi = 96;
    private static readonly List<NativeWindowSizeOverride> FailedRestores = [];

    private readonly IntPtr windowHandle;
    private readonly WindowProcedure windowProcedure;
    private readonly int minimumMaximumWidth;
    private readonly int minimumMaximumHeight;
    private IntPtr originalWindowProcedure;
    private bool disposed;

    private NativeWindowSizeOverride(Window window, int width, int height)
    {
        windowHandle = new WindowInteropHelper(window).Handle;
        uint dpi = GetDpiForWindow(windowHandle);
        if (dpi == 0) dpi = DefaultDpi;
        minimumMaximumWidth = (int)Math.Ceiling(width * dpi / (double)DefaultDpi);
        minimumMaximumHeight = (int)Math.Ceiling(height * dpi / (double)DefaultDpi);
        windowProcedure = ProcessWindowMessage;
    }

    public static NativeWindowSizeOverride Install(Window window, int width, int height)
    {
        var sizeOverride = new NativeWindowSizeOverride(window, width, height);
        sizeOverride.Install();
        try
        {
            sizeOverride.RefreshWpfSizeLimits();
            return sizeOverride;
        }
        catch
        {
            sizeOverride.Dispose();
            throw;
        }
    }

    private void Install()
    {
        Marshal.SetLastPInvokeError(0);
        originalWindowProcedure = SetWindowProcedure(
            windowHandle,
            WindowProcedureIndex,
            Marshal.GetFunctionPointerForDelegate(windowProcedure));
        int error = Marshal.GetLastPInvokeError();
        if (originalWindowProcedure == IntPtr.Zero && error != 0)
        {
            throw new InvalidOperationException(
                $"Could not install the screenshot window size override. Win32 error: {error}.");
        }
    }

    private void RefreshWpfSizeLimits()
    {
        IntPtr minimumMaximumInfo = Marshal.AllocHGlobal(Marshal.SizeOf<MinimumMaximumInfo>());
        try
        {
            Marshal.StructureToPtr(new MinimumMaximumInfo(), minimumMaximumInfo, false);
            _ = SendMessage(windowHandle, GetMinimumMaximumInfoMessage, IntPtr.Zero, minimumMaximumInfo);
        }
        finally
        {
            Marshal.FreeHGlobal(minimumMaximumInfo);
        }
    }

    private IntPtr ProcessWindowMessage(
        IntPtr window,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter)
    {
        if (message == GetMinimumMaximumInfoMessage)
        {
            var limits = Marshal.PtrToStructure<MinimumMaximumInfo>(longParameter);
            limits.MaximumSize.X = Math.Max(limits.MaximumSize.X, minimumMaximumWidth);
            limits.MaximumSize.Y = Math.Max(limits.MaximumSize.Y, minimumMaximumHeight);
            limits.MaximumTrackSize.X = Math.Max(limits.MaximumTrackSize.X, minimumMaximumWidth);
            limits.MaximumTrackSize.Y = Math.Max(limits.MaximumTrackSize.Y, minimumMaximumHeight);
            Marshal.StructureToPtr(limits, longParameter, false);
        }

        return CallWindowProc(originalWindowProcedure, window, message, wordParameter, longParameter);
    }

    public void Dispose()
    {
        if (disposed) return;
        if (windowHandle != IntPtr.Zero && originalWindowProcedure != IntPtr.Zero)
        {
            Marshal.SetLastPInvokeError(0);
            IntPtr replacedWindowProcedure = SetWindowProcedure(
                windowHandle,
                WindowProcedureIndex,
                originalWindowProcedure);
            int error = Marshal.GetLastPInvokeError();
            if (replacedWindowProcedure == IntPtr.Zero && error != 0)
            {
                Console.Error.WriteLine(
                    $"Could not restore the screenshot window procedure. Win32 error: {error}.");
                FailedRestores.Add(this);
                return;
            }
        }

        disposed = true;
        GC.KeepAlive(windowProcedure);
    }

    private static IntPtr SetWindowProcedure(IntPtr window, int index, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(window, index, value)
            : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinimumMaximumInfo
    {
        public NativePoint Reserved;
        public NativePoint MaximumSize;
        public NativePoint MaximumPosition;
        public NativePoint MinimumTrackSize;
        public NativePoint MaximumTrackSize;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(
        IntPtr window,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr window, int index, int value);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(
        IntPtr previousWindowProcedure,
        IntPtr window,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr window,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}
