using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class JsonFoldingStyleTests
{
    private const string NestedJson = """
        {
          "outer": {
            "items": [
              {
                "value": "deep"
              }
            ]
          }
        }
        """;

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Custom_gutter_and_placeholder_render_expanded_collapsed_and_keyboard_focus_states()
        => OnSta(() =>
        {
            var document = new TextDocument(NestedJson);
            var editor = new JsonEditor { FontSize = 14, ShowLineNumbers = true };
            editor.SetInspectionDocument(document);
            var window = CreateWindow(editor);
            try
            {
                window.Show();
                Layout(window, editor);

                JsonFoldingMargin margin = Assert.Single(editor.TextArea.LeftMargins.OfType<JsonFoldingMargin>());
                Assert.Empty(editor.TextArea.LeftMargins.OfType<FoldingMargin>());
                Assert.Single(PlaceholderGenerators(editor));
                Assert.Equal(4, editor.JsonFoldings.Count);
                Assert.Equal(NestedJson, document.Text);
                Assert.False(document.UndoStack.CanUndo);

                Button[] expandedButtons = MarginButtons(margin);
                Assert.Equal(4, expandedButtons.Length);
                BitmapSource expanded = Capture(window, "inspect36-expanded.png");

                FoldingSection innermostFold = editor.JsonFoldings.MinBy(fold => fold.EndOffset - fold.StartOffset)!;
                Button innerButton = ButtonForFold(editor, margin, innermostFold);
                Assert.True(Keyboard.Focus(innerButton) is not null);
                Layout(window, editor);
                Assert.Same(innerButton, Keyboard.FocusedElement);
                Assert.True(innerButton.IsKeyboardFocused);
                BitmapSource focused = Capture(window, "inspect36-keyboard-focus.png");
                Assert.True(PixelDifferenceCount(expanded, focused, BoundsInWindow(innerButton, window)) > 0,
                    "The keyboard-focus render should visibly change the focused chevron surface.");

                innerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, innerButton));
                Layout(window, editor);
                Assert.True(innermostFold.IsFolded);
                Assert.Equal(4, MarginButtons(margin).Length);
                BitmapSource nestedCollapsed = Capture(window, "inspect36-nested-collapsed.png");
                Assert.True(PixelDifferenceCount(expanded, nestedCollapsed) > 0,
                    "The collapsed placeholder must change the rendered document.");

                FoldingSection outerFold = editor.JsonFoldings.MaxBy(fold => fold.EndOffset - fold.StartOffset)!;
                Button outerButton = ButtonForFold(editor, margin, outerFold);
                outerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, outerButton));
                Layout(window, editor);
                Assert.True(outerFold.IsFolded);
                Assert.Single(MarginButtons(margin));
                BitmapSource outerCollapsed = Capture(window, "inspect36-outer-collapsed.png");
                Assert.True(PixelDifferenceCount(expanded, outerCollapsed) > 0);

                editor.RevealRange(NestedJson.IndexOf("deep", StringComparison.Ordinal), "deep".Length);
                Layout(window, editor);
                Assert.All(editor.JsonFoldings, fold => Assert.False(fold.IsFolded));
                Assert.Equal(NestedJson, document.Text);
                Assert.False(document.UndoStack.CanUndo);
            }
            finally
            {
                if (window.IsVisible) window.Close();
            }
        });

    [Fact]
    public void Invalid_document_replacement_and_unload_reload_keep_one_owned_margin_and_generator()
        => OnSta(() =>
        {
            var editor = new JsonEditor();
            var first = new TextDocument(NestedJson);
            editor.SetInspectionDocument(first);
            var window = CreateWindow(editor);
            Window? reloadedWindow = null;
            try
            {
                window.Show();
                Layout(window, editor);
                AssertSingleOwnedVisuals(editor);

                editor.SetInspectionDocument(first);
                AssertSingleOwnedVisuals(editor);

                const string invalidJson = "{\n  \"outer\": [\n";
                first.Text = invalidJson;
                Assert.Empty(editor.JsonFoldings);
                Assert.Equal(invalidJson, first.Text);
                AssertSingleOwnedVisuals(editor);

                var replacement = new TextDocument(NestedJson);
                editor.SetInspectionDocument(replacement);
                Assert.Same(replacement, editor.Document);
                Assert.Equal(4, editor.JsonFoldings.Count);
                first.Text = "stale document edit";
                Assert.Equal(4, editor.JsonFoldings.Count);
                Assert.Equal(NestedJson, replacement.Text);
                AssertSingleOwnedVisuals(editor);

                window.Content = null;
                Layout(window);
                Assert.Empty(editor.TextArea.LeftMargins.OfType<JsonFoldingMargin>());
                Assert.Empty(editor.TextArea.LeftMargins.OfType<FoldingMargin>());
                Assert.Empty(PlaceholderGenerators(editor));
                Assert.Null(editor.TextArea.GetService(typeof(FoldingManager)));
                Assert.Empty(editor.JsonFoldings);

                reloadedWindow = CreateWindow(editor);
                reloadedWindow.Show();
                Layout(reloadedWindow, editor);
                Assert.Equal(4, editor.JsonFoldings.Count);
                AssertSingleOwnedVisuals(editor);
            }
            finally
            {
                if (window.IsVisible) window.Close();
                if (reloadedWindow?.IsVisible == true) reloadedWindow.Close();
            }
        });

    private static Window CreateWindow(JsonEditor editor)
    {
        var window = new Window
        {
            Width = 640,
            Height = 440,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Left = -1200,
            Top = -1200,
            Background = Brushes.Black,
            Content = editor
        };
        window.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
            new Uri("/ServiceBusEmulatorExplorer.App;component/Investigation/Resources/SharedStyles.xaml", UriKind.Relative)));
        editor.Background = (Brush)window.Resources["EditorBackgroundBrush"];
        editor.Foreground = (Brush)window.Resources["EditorForegroundBrush"];
        return window;
    }

    private static void AssertSingleOwnedVisuals(JsonEditor editor)
    {
        Assert.Single(editor.TextArea.LeftMargins.OfType<JsonFoldingMargin>());
        Assert.Empty(editor.TextArea.LeftMargins.OfType<FoldingMargin>());
        Assert.Single(PlaceholderGenerators(editor));
        Assert.IsAssignableFrom<FoldingManager>(editor.TextArea.GetService(typeof(FoldingManager)));
    }

    private static VisualLineElementGenerator[] PlaceholderGenerators(JsonEditor editor)
        => editor.TextArea.TextView.ElementGenerators
            .Where(generator => generator.GetType().Name == "JsonFoldPlaceholder")
            .ToArray();

    private static Button[] MarginButtons(JsonFoldingMargin margin) => Descendants(margin).OfType<Button>().ToArray();

    private static Button ButtonForFold(JsonEditor editor, JsonFoldingMargin margin, FoldingSection fold)
    {
        int line = editor.Document!.GetLineByOffset(fold.StartOffset).LineNumber;
        return Assert.Single(MarginButtons(margin), button =>
            AutomationProperties.GetName(button)?.EndsWith($"line {line}", StringComparison.Ordinal) == true);
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

    private static Rect BoundsInWindow(FrameworkElement element, Window window)
        => element.TransformToAncestor(window).TransformBounds(new Rect(new Point(), element.RenderSize));

    private static void Layout(params FrameworkElement[] elements)
    {
        foreach (FrameworkElement element in elements) element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        foreach (FrameworkElement element in elements) element.UpdateLayout();
    }

    private static BitmapSource Capture(Window window, string name)
    {
        int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);

        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "artifacts", "inspect36-proof");
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream output = File.Create(Path.Combine(directory, name));
        encoder.Save(output);
        return bitmap;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ServiceBusEmulatorExplorer.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static int PixelDifferenceCount(BitmapSource left, BitmapSource right, Rect? region = null)
    {
        Assert.Equal(left.PixelWidth, right.PixelWidth);
        Assert.Equal(left.PixelHeight, right.PixelHeight);
        int stride = left.PixelWidth * 4;
        byte[] leftPixels = new byte[stride * left.PixelHeight];
        byte[] rightPixels = new byte[stride * right.PixelHeight];
        left.CopyPixels(leftPixels, stride, 0);
        right.CopyPixels(rightPixels, stride, 0);

        Rect bounds = region ?? new Rect(0, 0, left.PixelWidth, left.PixelHeight);
        int leftX = Math.Clamp((int)Math.Floor(bounds.Left), 0, left.PixelWidth);
        int topY = Math.Clamp((int)Math.Floor(bounds.Top), 0, left.PixelHeight);
        int rightX = Math.Clamp((int)Math.Ceiling(bounds.Right), 0, left.PixelWidth);
        int bottomY = Math.Clamp((int)Math.Ceiling(bounds.Bottom), 0, left.PixelHeight);
        int changed = 0;
        for (int y = topY; y < bottomY; y++)
        for (int x = leftX; x < rightX; x++)
        {
            int offset = y * stride + x * 4;
            if (leftPixels[offset] != rightPixels[offset]
                || leftPixels[offset + 1] != rightPixels[offset + 1]
                || leftPixels[offset + 2] != rightPixels[offset + 2]
                || leftPixels[offset + 3] != rightPixels[offset + 3]) changed++;
        }
        return changed;
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The JSON folding style proof exceeded its 30-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
