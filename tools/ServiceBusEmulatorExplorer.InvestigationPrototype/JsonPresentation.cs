using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class JsonPresentation
{
    public static readonly Brush TextBrush = Brush("#DCE7F3");
    private static readonly Brush KeyBrush = Brush("#D9A0F5");
    private static readonly Brush StringBrush = Brush("#56E5E5");
    private static readonly Brush ScalarBrush = Brush("#F4D071");
    private static readonly Regex Tokens = new("\"(?:\\\\.|[^\"\\\\])*\"(?=\\s*:)|\"(?:\\\\.|[^\"\\\\])*\"|(?<![\\w.])-?\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?\\b|\\b(?:true|false|null)\\b");

    public static string Format(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException) { return text; }
    }

    public static IEnumerable<(int Start, int Length, Brush Color)> Highlights(string text)
    {
        foreach (Match match in Tokens.Matches(text))
        {
            var after = text.AsSpan(match.Index + match.Length).TrimStart();
            yield return (match.Index, match.Length, match.Value.StartsWith('"')
                ? after.StartsWith(":" ) ? KeyBrush : StringBrush : ScalarBrush);
        }
    }

    private static Brush Brush(string color)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}

// Coloring changes the rendered lines, never the editable document or undo history.
internal sealed class JsonEditor : TextEditor
{
    public JsonEditor()
    {
        WordWrap = true;
        Foreground = JsonPresentation.TextBrush;
        TextArea.TextView.LineTransformers.Add(new JsonColors());
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.IndentationSize = 2;
    }

    private sealed class JsonColors : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.GetText(line);
            foreach (var token in JsonPresentation.Highlights(text))
                ChangeLinePart(line.Offset + token.Start, line.Offset + token.Start + token.Length,
                    element => element.TextRunProperties.SetForegroundBrush(token.Color));
        }
    }
}
