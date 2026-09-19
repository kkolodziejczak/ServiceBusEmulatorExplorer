using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ServiceBusEmulatorExplorer.App.Investigation.Inspection;

/// <summary>Provides best-effort lexical syntax colors and an AvalonEdit transformer for the inspector editor.</summary>
public static class JsonSyntaxColorizer
{
    public static readonly Brush TextBrush = CreateBrush("#DCE7F3");
    public static readonly Brush PropertyNameBrush = CreateBrush("#D9A0F5");
    public static readonly Brush StringBrush = CreateBrush("#56E5E5");
    public static readonly Brush ScalarBrush = CreateBrush("#F4D071");

    private static readonly Regex Tokens = new(
        "\\\"(?:\\\\.|[^\\\"\\\\])*\\\"(?=\\s*:)|\\\"(?:\\\\.|[^\\\"\\\\])*\\\"|(?<![\\w.])-?\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?\\b|\\b(?:true|false|null)\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<JsonTokenSpan> FindTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<JsonTokenSpan> spans = [];
        foreach (Match match in Tokens.Matches(text))
        {
            ReadOnlySpan<char> after = text.AsSpan(match.Index + match.Length).TrimStart();
            JsonTokenKind kind = match.Value.StartsWith('"')
                ? after.StartsWith(":") ? JsonTokenKind.PropertyName : JsonTokenKind.String
                : JsonTokenKind.Scalar;
            spans.Add(new JsonTokenSpan(match.Index, match.Length, kind));
        }

        return spans;
    }

    public static Brush GetBrush(JsonTokenKind kind)
    {
        return kind switch
        {
            JsonTokenKind.PropertyName => PropertyNameBrush,
            JsonTokenKind.String => StringBrush,
            JsonTokenKind.Scalar => ScalarBrush,
            _ => TextBrush
        };
    }

    private static Brush CreateBrush(string color)
    {
        Brush brush = (Brush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}

/// <summary>Colorizes AvalonEdit lines while leaving the editable document and undo stack unchanged.</summary>
public sealed class JsonColorizingTransformer : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        foreach (JsonTokenSpan token in JsonSyntaxColorizer.FindTokens(text))
        {
            ChangeLinePart(
                line.Offset + token.Start,
                line.Offset + token.Start + token.Length,
                element => element.TextRunProperties.SetForegroundBrush(JsonSyntaxColorizer.GetBrush(token.Kind)));
        }
    }
}

/// <summary>Configured editor used for the formatted JSON inspection tab.</summary>
public sealed class JsonEditor : TextEditor
{
    public JsonEditor()
    {
        WordWrap = true;
        ShowLineNumbers = true;
        Foreground = JsonSyntaxColorizer.TextBrush;
        TextArea.TextView.LineTransformers.Add(new JsonColorizingTransformer());
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.IndentationSize = 2;
        Options.HighlightCurrentLine = true;
        TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromRgb(38, 58, 83));
    }
}
