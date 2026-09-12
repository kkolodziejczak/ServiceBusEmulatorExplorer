using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class ChromePolishProof
{
    public static async Task Exercise(PrototypeWindow window, List<string> report, string output)
    {
        var accent = (Border)window.FindName("ProfileAccentBorder");
        Check(accent.BorderThickness.Left == 0 && accent.BorderThickness.Top == 0 && accent.BorderThickness.Right == 0 && accent.BorderThickness.Bottom > 0,
            "Profile accent is confined to the header bottom edge", report);
        ProofCapture.Save(window, output, "header-bottom-accent");
        ProofCapture.Descendants(window).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Settings")
            .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Settle();
        var settings = Application.Current.Windows.OfType<PrototypeSettingsWindow>().Single();
        try
        {
            Check(!ProofCapture.Descendants(settings).OfType<TextBlock>().Any(text => text.Text == "Settings"),
                "Settings content does not repeat the native window title", report);
            ((TabItem)settings.FindName("ConnectionsTab")).IsSelected = true;
            await Settle();
            var runtime = (PasswordBox)settings.FindName("RuntimeConnection");
            var admin = (PasswordBox)settings.FindName("AdministrationConnection");
            var runtimeCopy = (Button)settings.FindName("CopyRuntimeConnectionButton");
            var adminCopy = (Button)settings.FindName("CopyAdministrationConnectionButton");
            var originalRuntime = runtime.Password; var originalAdmin = admin.Password;
            var clipboard = Clipboard.GetDataObject();
            try
            {
                runtime.Password = ""; admin.Password = "";
                await Settle();
                Check(!runtimeCopy.IsEnabled && !adminCopy.IsEnabled, "Blank connection fields disable their copy actions", report);
                runtime.Password = "unsaved-runtime-proof"; admin.Password = "unsaved-admin-proof";
                await Settle();
                await Copy(runtimeCopy, "unsaved-runtime-proof");
                await Copy(adminCopy, "unsaved-admin-proof");
                Check(true, "Copy actions use the exact unsaved runtime and administration field values", report);
            }
            finally
            {
                runtime.Password = originalRuntime; admin.Password = originalAdmin;
                await RestoreClipboard(clipboard);
            }
            ProofCapture.Save(settings, output, "settings-chrome");
            settings.Width = 460; settings.Height = 520;
            await Settle();
            await ScrollVertical(settings, report, "Compact Settings");
            ProofCapture.CheckBounds(settings, [ProofCapture.Control<Button>(settings, "DoneButton"), ProofCapture.Control<Button>(settings, "SaveProfileButton")]);
            ProofCapture.Save(settings, output, "settings-chrome-compact");
            adminCopy.BringIntoView();
            await Settle();
            ProofCapture.CheckBounds(settings, [runtimeCopy, adminCopy]);
            ProofCapture.Save(settings, output, "settings-chrome-compact-copy-actions");
        }
        finally { settings.Close(); }
        await ScrollVertical(window, report, "Main investigation workspace");
        var roots = PrototypeData.CreateTree();
        var group = roots.First();
        for (var index = 0; index < 45; index++) group.Children.Add(new EntityNode { Name = "regional-processing-queue-" + index, Path = "regional-processing-queue-" + index, Kind = "Queue" });
        var watch = new GlobalWatchWindow(roots, new WatchRules(), () => { }) { Owner = window, Width = 460, Height = 520 };
        try
        {
            watch.Show();
            await Settle();
            await ScrollVertical(watch, report, "Watch tree with many entities");
            ProofCapture.Save(watch, output, "watch-scroll-chrome");
        }
        finally { watch.Close(); }
        await ScrollbarHarness(window, report, output);
    }

    private static async Task Copy(Button button, string expected)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Settle();
            try { if (Clipboard.GetText() == expected) return; }
            catch (System.Runtime.InteropServices.COMException) { }
            await Task.Delay(60);
        }
        throw new InvalidOperationException("Copy did not place the exact unsaved connection field on the clipboard.");
    }

    private static async Task RestoreClipboard(IDataObject? original)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { if (original is null) Clipboard.Clear(); else Clipboard.SetDataObject(original, true); return; }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 2) { await Task.Delay(60); }
        }
    }

    private static async Task ScrollVertical(Window window, List<string> report, string label)
    {
        var scroller = ProofCapture.Descendants(window).OfType<ScrollViewer>().Where(scroll => scroll.IsVisible && scroll.ScrollableHeight > 0)
            .OrderByDescending(scroll => scroll.ActualHeight).FirstOrDefault()
            ?? throw new InvalidOperationException(label + " has no rendered scrollable content.");
        var original = scroller.VerticalOffset;
        scroller.ScrollToEnd();
        await Settle();
        Check(scroller.VerticalOffset > 0 && ProofCapture.Descendants(scroller).OfType<ScrollBar>().Any(bar => bar.IsVisible && bar.Orientation == Orientation.Vertical && bar.Template is not null),
            label + " scrolls long content with a rendered vertical scrollbar", report);
        scroller.ScrollToVerticalOffset(original);
        await Settle();
    }

    private static async Task ScrollbarHarness(Window owner, List<string> report, string output)
    {
        var scroller = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = string.Join("\n", Enumerable.Range(0, 60).Select(index => "2026-09-12T10:23:45Z INFO regional-processing queue event " + index + " " + new string('x', 140))) } };
        var harness = new Window { Owner = owner, Width = 500, Height = 280, Content = scroller, Title = "Scrollbar style proof" };
        harness.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("SharedStyles.xaml", UriKind.Relative) });
        scroller.SetResourceReference(Control.BackgroundProperty, "CanvasBrush");
        scroller.SetResourceReference(Control.ForegroundProperty, "InkBrush");
        try
        {
            harness.Show();
            await Settle();
            scroller.ScrollToHorizontalOffset(100); scroller.ScrollToVerticalOffset(100);
            await Settle();
            Check(scroller.HorizontalOffset > 0 && scroller.VerticalOffset > 0 && ProofCapture.Descendants(scroller).OfType<ScrollBar>()
                .Count(bar => bar.IsVisible) == 2, "Shared scrollbar style harness supports both horizontal and vertical scrolling", report);
            await ExerciseScrollbar(scroller, Orientation.Horizontal, report);
            await ExerciseScrollbar(scroller, Orientation.Vertical, report);
            ProofCapture.Save(harness, output, "shared-scrollbars-both-orientations");
        }
        finally { harness.Close(); }
    }

    private static async Task ExerciseScrollbar(ScrollViewer scroller, Orientation orientation, List<string> report)
    {
        if (orientation == Orientation.Horizontal) scroller.ScrollToHorizontalOffset(0);
        else scroller.ScrollToVerticalOffset(0);
        await Settle();
        var bar = ProofCapture.Descendants(scroller).OfType<ScrollBar>().Single(control => control.IsVisible && control.Orientation == orientation);
        var track = (Track)bar.Template.FindName("PART_Track", bar);
        var surface = track.Thumb.Template.FindName("ThumbSurface", track.Thumb) as Border;
        Check(surface is { IsVisible: true } && surface.ActualWidth > 0 && surface.ActualHeight > 0,
            orientation + " scrollbar renders the shared ThumbSurface", report);
        var before = orientation == Orientation.Horizontal ? scroller.HorizontalOffset : scroller.VerticalOffset;
        var peer = UIElementAutomationPeer.CreatePeerForElement(track.IncreaseRepeatButton);
        if (peer?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
            throw new InvalidOperationException(orientation + " scrollbar page button has no Invoke provider.");
        invoke.Invoke();
        await Settle();
        var paged = orientation == Orientation.Horizontal ? scroller.HorizontalOffset : scroller.VerticalOffset;
        Check(paged > before, orientation + " scrollbar page button advances its ScrollViewer", report);
        var horizontal = orientation == Orientation.Horizontal ? 20d : 0d;
        var vertical = orientation == Orientation.Vertical ? 20d : 0d;
        track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        track.Thumb.RaiseEvent(new DragDeltaEventArgs(horizontal, vertical) { RoutedEvent = Thumb.DragDeltaEvent });
        track.Thumb.RaiseEvent(new DragCompletedEventArgs(horizontal, vertical, false) { RoutedEvent = Thumb.DragCompletedEvent });
        await Settle();
        var dragged = orientation == Orientation.Horizontal ? scroller.HorizontalOffset : scroller.VerticalOffset;
        Check(dragged > paged, orientation + " scrollbar Thumb drag advances its ScrollViewer", report);
    }
    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
