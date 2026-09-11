using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class NamespaceSearchProof
{
    public static async Task Exercise(PrototypeWindow window, List<string> report, string output)
    {
        var width = window.Width;
        var height = window.Height;
        var original = window.Workspace.SelectedEntity!;
        await Viewport(window, report, output, "desktop");
        window.Width = 980;
        window.Height = 640;
        await Settle();
        await Viewport(window, report, output, "compact");
        window.Width = width;
        window.Height = height;
        window.Workspace.SelectEntity(original);
        await Settle();
    }

    private static async Task Viewport(PrototypeWindow window, List<string> report, string output, string viewport)
    {
        var workspace = window.Workspace;
        var nodes = workspace.Roots.SelectMany(PrototypeData.Flatten).ToArray();
        var totals = nodes.Select(node => (node.Path, node.MessageCount, node.DlqCount)).ToArray();
        var box = (TextBox)window.FindName("SearchBox");
        var list = (ListBox)window.FindName("EntitySuggestionsList");
        var popup = (Popup)window.FindName("EntitySuggestionsPopup");
        var clear = (Button)window.FindName("ClearEntitySearchButton");
        var empty = (TextBlock)window.FindName("NamespaceEmpty");
        box.Focus();
        box.Text = "order";
        await Settle();
        Check(popup.IsOpen && list.IsVisible && list.Items.Count >= 2, $"{viewport}: namespace typing renders entity suggestions", report);
        Check(list.Items.Cast<EntityNode>().All(node => !node.IsGroup && node.Path.Contains("order", StringComparison.OrdinalIgnoreCase)), $"{viewport}: namespace suggestions match complete entity paths", report);
        Check(nodes.Any(node => !node.IsVisible) && nodes.Any(node => node.IsVisible), $"{viewport}: namespace typing filters the tree live", report);
        Check(clear.IsVisible && clear.IsEnabled && !string.IsNullOrEmpty(AutomationProperties.GetName(clear)), $"{viewport}: namespace clear action is visible and accessible", report);
        ProofCapture.CheckBounds(window, [box, clear]);
        ProofCapture.Save(window, output, $"namespace-{viewport}-suggestions");
        SavePopup(list, output, $"namespace-{viewport}-popup");
        Key(box, System.Windows.Input.Key.Down);
        var first = list.SelectedItem;
        Key(box, System.Windows.Input.Key.Down);
        Check(list.SelectedIndex == 1, $"{viewport}: repeated Down advances namespace suggestions", report);
        Key(box, System.Windows.Input.Key.Up);
        Check(Equals(first, list.SelectedItem), $"{viewport}: Up returns to the first namespace suggestion", report);
        Key(box, System.Windows.Input.Key.Escape);
        Check(!popup.IsOpen && box.Text == "order", $"{viewport}: Escape dismisses suggestions without clearing the namespace filter", report);
        box.Text = "billing";
        await Settle();
        Key(box, System.Windows.Input.Key.Down);
        Key(box, System.Windows.Input.Key.Enter);
        await Settle();
        Check(workspace.EntityPath == "order-events/billing" && box.Text == workspace.EntityPath && !popup.IsOpen, $"{viewport}: Enter fills the full subscription path and opens its messages", report);
        box.Text = "webhook";
        await Settle();
        list.SelectedIndex = 0;
        list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        await Settle();
        Check(workspace.EntityPath == "webhook-delivery" && box.Text == workspace.EntityPath && !popup.IsOpen, $"{viewport}: clicking a queue suggestion opens that queue", report);
        var selected = workspace.SelectedEntity;
        box.Text = "no-such-namespace-entity";
        await Settle();
        Check(empty.IsVisible && !popup.IsOpen && nodes.All(node => !node.IsVisible), $"{viewport}: unmatched namespace search shows its empty state", report);
        ProofCapture.CheckBounds(window, [box, clear, empty]);
        ProofCapture.Save(window, output, $"namespace-{viewport}-empty");
        clear.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Settle();
        Check(box.Text == "" && !popup.IsOpen && !empty.IsVisible && nodes.All(node => node.IsVisible), $"{viewport}: clear restores the entire namespace tree", report);
        Check(ReferenceEquals(selected, workspace.SelectedEntity), $"{viewport}: clearing namespace search preserves the open entity", report);
        Check(totals.SequenceEqual(nodes.Select(node => (node.Path, node.MessageCount, node.DlqCount))), $"{viewport}: namespace search preserves MessageCount and DLQ totals", report);
    }

    private static void Check(bool condition, string description, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(description);
        report.Add("- PASS: " + description);
    }

    private static void Key(UIElement control, Key key) => control.RaiseEvent(new KeyEventArgs(
        Keyboard.PrimaryDevice, PresentationSource.FromVisual(control), Environment.TickCount, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    private static void SavePopup(FrameworkElement list, string output, string name)
    {
        var root = PresentationSource.FromVisual(list)?.RootVisual as FrameworkElement
            ?? throw new InvalidOperationException("Namespace suggestions have no rendered popup root.");
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(file);
    }
}
