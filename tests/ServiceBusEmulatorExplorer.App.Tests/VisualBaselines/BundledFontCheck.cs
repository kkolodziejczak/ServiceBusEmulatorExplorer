using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;

namespace ServiceBusEmulatorExplorer.App.Tests.VisualBaselines;

/// <summary>
/// Baselines compare only what renders the same on every machine (DEC-051): visible text must use a bundled font,
/// and every character must exist in it, because WPF silently substitutes an installed system font otherwise.
/// </summary>
internal static class BundledFontCheck
{
    private static readonly Dictionary<Typeface, IDictionary<int, ushort>> GlyphMaps = new();

    public static void AssertBundledOnly(FrameworkElement element, Popup? popup, string name)
    {
        element.UpdateLayout();
        var problems = new SortedSet<string>(StringComparer.Ordinal);
        foreach (DependencyObject root in new DependencyObject?[] { element, popup?.Child }.OfType<DependencyObject>())
            foreach (DependencyObject visible in VisibleDescendants(root))
                foreach ((Typeface typeface, string text) in RenderedText(visible))
                    Check(typeface, text, problems);
        Assert.True(problems.Count == 0,
            $"Visual baseline '{name}' renders text that would depend on installed fonts: {string.Join("; ", problems)}");
    }

    private static IEnumerable<DependencyObject> VisibleDescendants(DependencyObject root)
    {
        if (root is UIElement { Visibility: not Visibility.Visible }) yield break;
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (DependencyObject child in VisibleDescendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }

    private static IEnumerable<(Typeface Typeface, string Text)> RenderedText(DependencyObject visible) => visible switch
    {
        TextBlock block => Runs(block.Inlines),
        TextBox box => [(TypefaceOf(box), box.Text)],
        RichTextBox rich => Runs(rich.Document),
        TextEditor editor => [(TypefaceOf(editor), editor.Text)],
        _ => []
    };

    private static IEnumerable<(Typeface, string)> Runs(DependencyObject container)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(container))
        {
            if (child is Run run) yield return (new Typeface(run.FontFamily, run.FontStyle, run.FontWeight, run.FontStretch), run.Text);
            else if (child is TextElement or FlowDocument)
                foreach ((Typeface, string) nested in Runs((DependencyObject)child)) yield return nested;
        }
    }

    private static IEnumerable<(Typeface, string)> Runs(InlineCollection inlines) =>
        inlines.SelectMany(inline => inline is Run run
            ? [(new Typeface(run.FontFamily, run.FontStyle, run.FontWeight, run.FontStretch), run.Text)]
            : Runs(inline));

    private static Typeface TypefaceOf(Control control) =>
        new(control.FontFamily, control.FontStyle, control.FontWeight, control.FontStretch);

    private static void Check(Typeface typeface, string text, ISet<string> problems)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string snippet = text.Length <= 40 ? text : text[..40] + "…";
        if (!Equals(typeface.FontFamily, AppFonts.Ui) && !Equals(typeface.FontFamily, AppFonts.Code))
        {
            problems.Add($"\"{snippet}\" uses {typeface.FontFamily.Source}");
            return;
        }

        IDictionary<int, ushort> glyphs = GlyphMap(typeface);
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.IsControl(rune) || glyphs.ContainsKey(rune.Value)) continue;
            problems.Add($"U+{rune.Value:X4} '{rune}' in \"{snippet}\" is missing from {typeface.FontFamily.Source}");
        }
    }

    private static IDictionary<int, ushort> GlyphMap(Typeface typeface)
    {
        lock (GlyphMaps)
        {
            if (GlyphMaps.TryGetValue(typeface, out IDictionary<int, ushort>? map)) return map;
            Assert.True(typeface.TryGetGlyphTypeface(out GlyphTypeface glyphTypeface),
                $"{typeface.FontFamily.Source} {typeface.Weight} did not resolve to a bundled font file.");
            return GlyphMaps[typeface] = glyphTypeface.CharacterToGlyphMap;
        }
    }
}
