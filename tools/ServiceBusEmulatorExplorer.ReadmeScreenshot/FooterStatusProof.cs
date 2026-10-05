using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Azure;
using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.App.Investigation;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class FooterStatusProof
{
    private const string ExpectedFooter = "The operation failed. See Activity log for details.";

    public static async Task RunAsync(InvestigationWindow window, InvestigationWorkspace workspace, string outputDirectory)
    {
        Directory.CreateDirectory(Path.GetFullPath(outputDirectory));
        await workspace.RunReadAsync(() => Task.FromException(CreateWarmupFailure()));
        await SettleAsync(window);

        string[] diagnosticTerms = [
            "ServiceBusException",
            "Service is warming up. Please try after some time.",
            "Status: 503 (Service Unavailable)",
            "Content:",
            "<Error><Code>503</Code>",
            "Headers:",
            "Content-Type: application/xml"
        ];
        string activityMessage = workspace.Activity.LastOrDefault()?.Message
            ?? throw new InvalidOperationException("The synthetic warmup failure did not create an Activity log entry.");
        RequireContainsAll(activityMessage, diagnosticTerms, "workspace Activity entry");

        foreach ((double width, double height, string suffix) in new[]
        {
            (1500d, 1000d, "1500"),
            (1100d, 800d, "1100"),
            (980d, 640d, "980")
        })
        {
            window.Width = width;
            window.Height = height;
            await workspace.UpdateDisplayPreferencesAsync(logExpanded: true);
            await SettleAsync(window);
            AssertFooter(window, suffix);
            AssertActivityLogContainsDetails(window, diagnosticTerms, suffix);

            if (width == 980)
            {
                string imagePath = Path.Combine(Path.GetFullPath(outputDirectory), $"footer-status-{suffix}.png");
                WpfScreenshot.SaveWindowContent(window, imagePath, (int)width, (int)height);
            }

            await workspace.UpdateDisplayPreferencesAsync(logExpanded: false);
            await SettleAsync(window);
            if (Require<FrameworkElement>(window, "LogPanel").Visibility != Visibility.Collapsed)
                throw new InvalidOperationException($"At {suffix}px, the collapsed Activity log remained visible.");
            AssertFooter(window, $"{suffix}px with log collapsed");
            AssertInvestigationPanesRemainSideBySide(window, suffix);
            Console.WriteLine($"Footer status passed at {width:F0}px with Activity log expanded and collapsed.");
        }

        await AssertWorkbenchResponsiveLayoutAsync(window, outputDirectory);
        await AssertAuthorizationFooterFitsAt980Async(window, workspace);
    }

    private static ServiceBusException CreateWarmupFailure()
    {
        const string diagnostic = "Service is warming up. Please try after some time. TrackingId: 4a4efaf9-3302-4c54-9a22-4cdaea54f590, SystemTracker:localhost:$Resources/queues, Timestamp:2026-10-03T08:16:42Z";
        string message = $"{diagnostic}\r\nStatus: 503 (Service Unavailable)\r\n\r\n" +
            "Content:\r\n<Error><Code>503</Code><Detail>" + diagnostic + "</Detail></Error>\r\n\r\n" +
            "Headers:\r\nConnection: keep-alive\r\nDate: Sat, 03 Oct 2026 08:16:42 GMT\r\n" +
            "Server: Kestrel\r\nContent-Length: 220\r\nContent-Type: application/xml; charset=utf-8\r\n" +
            "(ServiceBus). For troubleshooting information, see https://aka.ms/azsdk/net/servicebus/exceptions/troubleshoot.";
        return new ServiceBusException(message, ServiceBusFailureReason.ServiceCommunicationProblem, "orders");
    }

    private static void AssertFooter(InvestigationWindow window, string viewport)
    {
        TextBlock footer = Require<TextBlock>(window, "LastOperation");
        if (!string.Equals(footer.Text, ExpectedFooter, StringComparison.Ordinal)
            || footer.ActualHeight > 40
            || footer.Text.Contains('\n')
            || footer.Text.Contains('\r'))
        {
            throw new InvalidOperationException(
                $"At {viewport}, footer was '{footer.Text}' with height {footer.ActualHeight:F1}px; " +
                $"expected one line no taller than 40px: '{ExpectedFooter}'.");
        }
    }

    private static void AssertActivityLogContainsDetails(InvestigationWindow window, IReadOnlyList<string> terms, string viewport)
    {
        RichTextBox log = Require<RichTextBox>(window, "LogText");
        string renderedText = new TextRange(log.Document.ContentStart, log.Document.ContentEnd).Text;
        RequireContainsAll(renderedText, terms, $"rendered Activity log at {viewport}px");
    }

    private static void AssertInvestigationPanesRemainSideBySide(InvestigationWindow window, string viewport)
    {
        Grid list = Require<Grid>(window, "ListPane");
        Grid inspector = Require<Grid>(window, "InspectorPane");
        if (Grid.GetRow(list) != 0 || Grid.GetRow(inspector) != 0
            || Grid.GetColumn(inspector) <= Grid.GetColumn(list)
            || list.ActualWidth <= 0 || inspector.ActualWidth <= 0)
        {
            throw new InvalidOperationException(
                $"Investigation panes were not side by side at {viewport}px: " +
                $"list row/column {Grid.GetRow(list)}/{Grid.GetColumn(list)}, " +
                $"inspector row/column {Grid.GetRow(inspector)}/{Grid.GetColumn(inspector)}.");
        }
    }

    private static async Task AssertWorkbenchResponsiveLayoutAsync(InvestigationWindow window, string outputDirectory)
    {
        ToggleButton workbenchTab = Require<ToggleButton>(window, "MessageLibraryTab");
        workbenchTab.IsChecked = true;
        await SettleAsync(window);

        var prototype = (MessageLibraryPrototypeView)Require<FrameworkElement>(window, "MessageLibraryPrototype");
        prototype.SelectEntityContext("topic:order-events");
        Button prepare = (Button)prototype.FindName("ContinueToPrepareButton")!;
        prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, prepare));
        await SettleAsync(window);

        foreach ((double width, double height, bool expectedSideBySide) in new[]
        {
            (1642d, 958d, true),
            (980d, 640d, false)
        })
        {
            window.Width = width;
            window.Height = height;
            await SettleAsync(window);
            Grid body = (Grid)prototype.FindName("PrepareBody")!;
            FrameworkElement preview = (FrameworkElement)prototype.FindName("PreviewSurface")!;
            FrameworkElement preparePane = (FrameworkElement)prototype.FindName("PreparePane")!;
            ScrollViewer bodyScroll = (ScrollViewer)prototype.FindName("PrepareBodyScroll")!;
            if (preparePane.Visibility != Visibility.Visible || !preparePane.IsVisible || !prototype.IsVisible)
                throw new InvalidOperationException($"Message Workbench Prepare was not visible at {width:F0}px.");
            if (expectedSideBySide)
            {
                if (bodyScroll.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
                    throw new InvalidOperationException("Message Workbench wide layout unexpectedly enables outer body scrolling.");
            }
            else
            {
                if (bodyScroll.VerticalScrollBarVisibility != ScrollBarVisibility.Auto)
                    throw new InvalidOperationException("Message Workbench compact layout does not enable outer body scrolling.");
                bodyScroll.ScrollToBottom();
                await SettleAsync(window);
            }
            int expectedColumn = expectedSideBySide ? 1 : 0;
            int expectedRow = expectedSideBySide ? 0 : 1;
            bool sideBySide = Grid.GetColumn(preview) == 1 && Grid.GetRow(preview) == 0
                && body.ColumnDefinitions[1].ActualWidth > 0;
            bool stacked = Grid.GetColumn(preview) == 0 && Grid.GetRow(preview) == 1
                && body.RowDefinitions[0].ActualHeight >= 300
                && preview.ActualHeight > 0;
            Rect previewInScroller = preview.TransformToAncestor(bodyScroll).TransformBounds(new Rect(new Point(), preview.RenderSize));
            Rect viewport = new(0, 0, bodyScroll.ViewportWidth, bodyScroll.ViewportHeight);
            Rect visiblePreview = Rect.Intersect(previewInScroller, viewport);
            bool previewReachable = visiblePreview.Width > 0 && visiblePreview.Height >= Math.Min(155, preview.ActualHeight);
            WpfScreenshot.SaveWindowContent(window,
                Path.Combine(Path.GetFullPath(outputDirectory), $"message-workbench-{(expectedSideBySide ? "wide" : "compact")}.png"),
                (int)width, (int)height);
            if (Grid.GetColumn(preview) != expectedColumn || Grid.GetRow(preview) != expectedRow
                || (expectedSideBySide ? !sideBySide : !stacked) || !previewReachable)
            {
                throw new InvalidOperationException(
                    $"Message Workbench Prepare did not rearrange as expected at {width:F0}px: " +
                    $"preview row/column {Grid.GetRow(preview)}/{Grid.GetColumn(preview)}, " +
                    $"body columns {body.ColumnDefinitions[0].ActualWidth:F0}/" +
                    $"{body.ColumnDefinitions[1].ActualWidth:F0}, rows " +
                    $"{body.RowDefinitions[0].ActualHeight:F0}/{body.RowDefinitions[1].ActualHeight:F0}, " +
                    $"body {body.ActualWidth:F0}x{body.ActualHeight:F0}px, " +
                    $"preview {preview.ActualWidth:F0}x{preview.ActualHeight:F0}px, " +
                    $"scroller {bodyScroll.ViewportWidth:F0}x{bodyScroll.ViewportHeight:F0}px, " +
                    $"extent {bodyScroll.ExtentWidth:F0}x{bodyScroll.ExtentHeight:F0}px, " +
                    $"visible preview {visiblePreview.Width:F0}x{visiblePreview.Height:F0}px.");
            }
            Console.WriteLine($"Message Workbench Prepare layout passed at {width:F0}px: " +
                (expectedSideBySide ? "side by side" : "stacked"));
        }

        Require<ToggleButton>(window, "InvestigationWorkspaceTab").IsChecked = true;
        await SettleAsync(window);
        if (Require<FrameworkElement>(window, "ContentGrid").Visibility != Visibility.Visible)
            throw new InvalidOperationException("Investigation did not return to the visible workspace after Message Workbench proof.");
        AssertInvestigationPanesRemainSideBySide(window, "after returning from Message Workbench");
    }

    private static async Task AssertAuthorizationFooterFitsAt980Async(InvestigationWindow window, InvestigationWorkspace workspace)
    {
        window.Width = 980;
        window.Height = 640;
        await workspace.RunReadAsync(() => Task.FromException(
            new RequestFailedException(403, "Forbidden", "AuthorizationFailed", null)));
        await SettleAsync(window);

        TextBlock footer = Require<TextBlock>(window, "LastOperation");
        DpiScale dpi = VisualTreeHelper.GetDpi(footer);
        var formatted = new FormattedText(
            footer.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(footer.FontFamily, footer.FontStyle, footer.FontWeight, footer.FontStretch),
            footer.FontSize,
            Brushes.Black,
            dpi.PixelsPerDip);
        Console.WriteLine($"Authorization guidance at 980px: {formatted.Width:F0}px text in {footer.ActualWidth:F0}px footer; " +
            $"text='{footer.Text}'.");
        if (footer.ActualHeight > 40 || footer.Text.Contains('\n') || footer.Text.Contains('\r'))
            throw new InvalidOperationException("Authorization guidance expanded the footer beyond one line at 980px.");
        Console.WriteLine(formatted.Width > footer.ActualWidth + 1
            ? "Authorization guidance will be ellipsized by the current one-line footer."
            : "Authorization guidance fits the current one-line footer.");
    }

    private static async Task SettleAsync(InvestigationWindow window)
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    private static void RequireContainsAll(string text, IReadOnlyList<string> terms, string surface)
    {
        foreach (string term in terms)
        {
            if (!text.Contains(term, StringComparison.Ordinal))
                throw new InvalidOperationException($"The {surface} did not contain diagnostic text '{term}'.");
        }
    }

    private static T Require<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        root.FindName(name) as T
        ?? throw new InvalidOperationException($"The rendered window is missing {name}.");
}
