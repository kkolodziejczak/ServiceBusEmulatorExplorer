using System.Text.Json;
using ICSharpCode.AvalonEdit.Folding;

namespace ServiceBusEmulatorExplorer.App.Investigation.Inspection;

/// <summary>Validated JSON ranges in UTF-16 document offsets, ignoring quoted delimiters.</summary>
internal static class JsonFoldRanges
{
    public static IEnumerable<NewFolding> Create(string text)
    {
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return [];
        }

        List<NewFolding> ranges = [];
        Stack<(int Offset, int Line)> opened = [];
        bool quoted = false;
        int line = 0;
        for (int offset = 0; offset < text.Length; offset++)
        {
            char character = text[offset];
            if (quoted)
            {
                if (character == '\\') offset++;
                else if (character == '"') quoted = false;
                continue;
            }
            if (character == '"') quoted = true;
            else if (character == '\r' || (character == '\n' && (offset == 0 || text[offset - 1] != '\r'))) line++;
            else if (character is '{' or '[') opened.Push((offset, line));
            else if (character is '}' or ']')
            {
                (int start, int startLine) = opened.Pop();
                if (line > startLine)
                    ranges.Add(new NewFolding(start + 1, offset) { Name = " … " });
            }
        }
        return ranges.OrderBy(range => range.StartOffset);
    }
}
