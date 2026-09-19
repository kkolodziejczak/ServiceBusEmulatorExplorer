using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.UiSmoke.Tests;

/// <summary>
/// Bounded proof that keyboard behavior is reachable through Windows input dispatch.
/// This test intentionally does not use UIA Invoke, Selection, Toggle, or Value patterns
/// for the keyboard assertions. It also records, but does not change, monitor DPI state.
/// </summary>
public sealed class InvestigationKeyboardEndToEndTests
{
    private static readonly TimeSpan UiTimeout = TimeSpan.FromSeconds(15);

    private readonly Xunit.Abstractions.ITestOutputHelper output;

    public InvestigationKeyboardEndToEndTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        this.output = output;
    }

    [UiSmokeFact(Timeout = 120_000)]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Windows_input_navigates_settings_and_restores_focus()
    {
        await Task.Yield();
        string runId = Guid.NewGuid().ToString("N");
        string profilePath = Path.Combine(
            AppContext.BaseDirectory,
            "ui-smoke-profiles",
            runId,
            "connection-profiles.json");
        string executablePath = WpfAppPath.Resolve();
        Assert.True(
            File.Exists(executablePath),
            $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

        Application? application = null;
        UIA3Automation? automation = null;

        try
        {
            application = Application.Launch(executablePath, CreateProfileStoreArguments(profilePath));
            automation = new UIA3Automation();

            Window window = WaitForMainWindowWithAutomationId(
                application,
                automation,
                "SearchBox",
                UiTimeout);
            Assert.Equal(
                "Disconnected",
                WaitForElement(UiTimeout, () => window.FindFirstDescendant(cf => cf.ByText("Disconnected"))).Name);

            AutomationElement settingsButton = WaitForElement(
                UiTimeout,
                () => window.FindAllDescendants(cf => cf.ByName("Settings"))
                    .FirstOrDefault(element => element.ControlType == ControlType.Button));
            window.Focus();
            settingsButton.Focus();
            WaitForFocus(settingsButton, UiTimeout);

            SendKey(VirtualKeyShort.RETURN);
            Window settings = WaitForWindowWithAutomationId(
                application,
                automation,
                "GeneralTab",
                UiTimeout);
            AutomationElement settingsTab = WaitForAutomationId(settings, "GeneralTab", UiTimeout);
            output.WriteLine($"Settings opened by Enter; initial focused descendant: {DescribeFocusedElement(settings)}.");

            // Establish a known starting point through UIA only. Every movement and action
            // asserted below is then driven by actual Windows keyboard input.
            settingsTab.Focus();
            WaitForFocus(settingsTab, UiTimeout);

            MoveFocusWithTab(settings, "NotificationsToggle", reverse: false);
            AutomationElement notifications = WaitForAutomationId(settings, "NotificationsToggle", UiTimeout);
            ToggleState initialNotificationState = notifications.Patterns.Toggle.Pattern.ToggleState.Value;
            Assert.NotEqual(ToggleState.Indeterminate, initialNotificationState);

            SendKey(VirtualKeyShort.SHIFT, VirtualKeyShort.TAB);
            Assert.False(
                IsFocused(notifications),
                "Shift+Tab was dispatched but focus did not leave NotificationsToggle.");
            SendKey(VirtualKeyShort.TAB);
            WaitForFocus(notifications, UiTimeout);

            SendKey(VirtualKeyShort.SPACE);
            WaitForToggleState(notifications, Opposite(initialNotificationState), UiTimeout);
            WaitForAutomationNameContains(settings, "GeneralStatus", "saved", UiTimeout);

            // Restore the temporary preference before closing. This is still an OS-level
            // Space action, and the isolated profile file is removed during cleanup.
            SendKey(VirtualKeyShort.SPACE);
            WaitForToggleState(notifications, initialNotificationState, UiTimeout);
            WaitForEnabled(settings, "DoneButton", UiTimeout);

            MoveFocusWithTab(settings, "DoneButton", reverse: false);
            SendKey(VirtualKeyShort.ESCAPE);
            WaitForWindowToClose(application, automation, settings, UiTimeout);
            WaitForFocus(settingsButton, UiTimeout);
        }
        catch (Exception exception) when (IsKeyboardDispatchFailure(exception))
        {
            throw new InvalidOperationException(
                "Physical Windows keyboard dispatch through FlaUI Keyboard.SendInput was unavailable. " +
                "This proof does not fall back to UIA actions; investigate desktop input permissions or focus.",
                exception);
        }
        finally
        {
            try
            {
                automation?.Dispose();
            }
            finally
            {
                try
                {
                    if (application is not null)
                    {
                        CloseApplication(application);
                    }
                }
                finally
                {
                    DeleteProfileArtifacts(profilePath);
                }
            }
        }
    }

    [UiSmokeFact(Timeout = 120_000)]
    [Trait("TestCategory", "UiSmoke")]
    public async Task Window_moves_to_each_available_monitor_and_records_dpi_without_display_changes()
    {
        await Task.Yield();
        string runId = Guid.NewGuid().ToString("N");
        string profilePath = Path.Combine(
            AppContext.BaseDirectory,
            "ui-smoke-profiles",
            runId,
            "connection-profiles.json");
        string executablePath = WpfAppPath.Resolve();
        Assert.True(
            File.Exists(executablePath),
            $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

        Application? application = null;
        UIA3Automation? automation = null;
        try
        {
            application = Application.Launch(executablePath, CreateProfileStoreArguments(profilePath));
            automation = new UIA3Automation();
            Window window = WaitForMainWindowWithAutomationId(application, automation, "SearchBox", UiTimeout);
            RecordAndVisitAvailableMonitors(window);
        }
        finally
        {
            try
            {
                automation?.Dispose();
            }
            finally
            {
                try
                {
                    if (application is not null)
                    {
                        CloseApplication(application);
                    }
                }
                finally
                {
                    DeleteProfileArtifacts(profilePath);
                }
            }
        }
    }

    private void RecordAndVisitAvailableMonitors(Window window)
    {
        IReadOnlyList<MonitorSnapshot> monitors = ReadMonitors();
        nint windowHandle = (nint)window.Properties.NativeWindowHandle.Value;
        uint initialDpi = GetDpiForWindow(windowHandle);
        Assert.NotEqual(0u, initialDpi);
        output.WriteLine($"Initial window bounds={window.BoundingRectangle}; dpi={initialDpi}; monitors={monitors.Count}.");

        foreach (MonitorSnapshot monitor in monitors)
        {
            int targetLeft = monitor.WorkArea.Left + 8;
            int targetTop = monitor.WorkArea.Top + 8;
            window.Move(targetLeft, targetTop);

            WaitForElement(UiTimeout, () =>
            {
                var bounds = window.BoundingRectangle;
                return bounds.Left == targetLeft && bounds.Top == targetTop ? window : null;
            });

            nint actualMonitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
            uint actualDpi = GetDpiForWindow(windowHandle);
            Assert.Equal(monitor.Handle, actualMonitor);
            Assert.NotEqual(0u, actualDpi);
            output.WriteLine(
                $"Visited {monitor.DeviceName}: monitor={monitor.Monitor}; workArea={monitor.WorkArea}; " +
                $"window={window.BoundingRectangle}; dpi={actualDpi}; handle={monitor.Handle}.");
        }
    }

    private void MoveFocusWithTab(Window settings, string automationId, bool reverse)
    {
        for (int attempt = 0; attempt < 80; attempt++)
        {
            if (IsFocused(settings, automationId))
            {
                return;
            }

            if (reverse)
            {
                SendKey(VirtualKeyShort.SHIFT, VirtualKeyShort.TAB);
            }
            else
            {
                SendKey(VirtualKeyShort.TAB);
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Physical {(reverse ? "Shift+Tab" : "Tab")} did not reach '{automationId}'. " +
            $"Focused element: {DescribeFocusedElement(settings)}.");
    }

    private static void SendKey(params VirtualKeyShort[] keys)
    {
        try
        {
            if (keys.Length == 1)
            {
                Keyboard.TypeVirtualKeyCode((ushort)keys[0]);
            }
            else
            {
                Keyboard.TypeSimultaneously(keys);
            }

            Wait.UntilInputIsProcessed();
        }
        catch (Exception exception)
        {
            throw new KeyboardDispatchException(exception);
        }
    }

    private static bool IsKeyboardDispatchFailure(Exception exception) =>
        exception is KeyboardDispatchException ||
        exception.InnerException is KeyboardDispatchException;

    private static ToggleState Opposite(ToggleState state) =>
        state == ToggleState.On ? ToggleState.Off : ToggleState.On;

    private static bool IsFocused(Window window, string automationId)
    {
        AutomationElement? element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
        return element?.Properties.HasKeyboardFocus.Value == true;
    }

    private static bool IsFocused(AutomationElement element) => element.Properties.HasKeyboardFocus.Value;

    private static void WaitForFocus(AutomationElement element, TimeSpan timeout) =>
        WaitForElement(timeout, () => element.Properties.HasKeyboardFocus.Value ? element : null);

    private static void WaitForToggleState(
        AutomationElement element,
        ToggleState expected,
        TimeSpan timeout) =>
        WaitForElement(timeout, () =>
            element.Patterns.Toggle.Pattern.ToggleState.Value == expected ? element : null);

    private static AutomationElement WaitForEnabled(
        Window window,
        string automationId,
        TimeSpan timeout) =>
        WaitForElement(timeout, () =>
        {
            AutomationElement? element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            return element is { IsEnabled: true } ? element : null;
        });

    private static AutomationElement WaitForAutomationNameContains(
        Window window,
        string automationId,
        string expected,
        TimeSpan timeout) =>
        WaitForElement(timeout, () =>
        {
            AutomationElement? element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            return element?.Name.Contains(expected, StringComparison.OrdinalIgnoreCase) == true ? element : null;
        });

    private static string DescribeFocusedElement(Window window)
    {
        AutomationElement? focused = window.FindAllDescendants()
            .FirstOrDefault(element => element.Properties.HasKeyboardFocus.Value);
        return focused is null
            ? "(none in settings window)"
            : $"Name='{focused.Name}', AutomationId='{focused.AutomationId}', ControlType={focused.ControlType}";
    }

    private static Window WaitForMainWindowWithAutomationId(
        Application application,
        UIA3Automation automation,
        string automationId,
        TimeSpan timeout) =>
        WaitForElement(timeout, () => application.GetAllTopLevelWindows(automation)
            .FirstOrDefault(candidate => candidate.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is not null));

    private static Window WaitForWindowWithAutomationId(
        Application application,
        UIA3Automation automation,
        string automationId,
        TimeSpan timeout) =>
        WaitForElement(timeout, () =>
        {
            foreach (Window topLevelWindow in application.GetAllTopLevelWindows(automation))
            {
                AutomationElement? element = topLevelWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
                if (element is null)
                {
                    continue;
                }

                for (AutomationElement? current = element; current is not null; current = current.Parent)
                {
                    if (current.ControlType == ControlType.Window)
                    {
                        return current.AsWindow();
                    }
                }
            }

            return null;
        });

    private static AutomationElement WaitForAutomationId(
        Window window,
        string automationId,
        TimeSpan timeout) =>
        WaitForElement(timeout, () => window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)));

    private static T WaitForElement<T>(TimeSpan timeout, Func<T?> findElement)
        where T : class
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            T? element = findElement();
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(100);
        }

        throw new Xunit.Sdk.XunitException($"UI element did not appear within {timeout.TotalSeconds:0} seconds.");
    }

    private static void WaitForWindowToClose(
        Application application,
        UIA3Automation automation,
        Window window,
        TimeSpan timeout)
    {
        nint nativeHandle = (nint)window.Properties.NativeWindowHandle.Value;
        WaitForElement(timeout, () =>
            application.GetAllTopLevelWindows(automation)
                .Any(candidate => (nint)candidate.Properties.NativeWindowHandle.Value == nativeHandle) ? null : window);
    }

    private static string CreateProfileStoreArguments(string profilePath) =>
        $"--profile-store-path {QuoteProcessArgument(profilePath)}";

    private static string QuoteProcessArgument(string argument) =>
        argument.Contains(' ', StringComparison.Ordinal)
            ? "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : argument;

    private static void CloseApplication(Application application)
    {
        if (application.HasExited)
        {
            return;
        }

        try
        {
            application.Kill();
            SpinWait.SpinUntil(() => application.HasExited, TimeSpan.FromSeconds(5));
        }
        catch when (application.HasExited)
        {
        }
    }

    private static void DeleteProfileArtifacts(string profilePath)
    {
        if (File.Exists(profilePath))
        {
            File.Delete(profilePath);
        }

        string? directory = Path.GetDirectoryName(profilePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IReadOnlyList<MonitorSnapshot> ReadMonitors()
    {
        var monitors = new List<MonitorSnapshot>();
        EnumDisplayMonitors(0, 0, (handle, _, _, _) =>
        {
            var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
            if (GetMonitorInfo(handle, ref info))
            {
                monitors.Add(new MonitorSnapshot(handle, info.Monitor, info.WorkArea, info.Device));
            }

            return true;
        }, 0);

        Assert.NotEmpty(monitors);
        return monitors;
    }

    private sealed class KeyboardDispatchException : Exception
    {
        public KeyboardDispatchException(Exception innerException)
            : base("FlaUI Keyboard.SendInput failed.", innerException)
        {
        }
    }

    private sealed record MonitorSnapshot(nint Handle, NativeRect Monitor, NativeRect WorkArea, string DeviceName);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public override string ToString() => $"({Left},{Top})-({Right},{Bottom})";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(
        nint hdc,
        nint clippingRectangle,
        MonitorEnumProc callback,
        nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(nint monitor, ref NativeMonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetDpiForWindow(nint window);

    private delegate bool MonitorEnumProc(
        nint monitor,
        nint hdc,
        nint monitorRectangle,
        nint data);
}
