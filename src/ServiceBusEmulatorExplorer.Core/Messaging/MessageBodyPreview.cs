namespace ServiceBusEmulatorExplorer.Core.Messaging;

public static class MessageBodyPreview
{
    public static string Create(string body, int maxLength = 160)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);

        string normalized = NormalizeWhitespace(body);

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        if (maxLength <= 3)
        {
            return normalized[..maxLength];
        }

        return $"{normalized[..(maxLength - 3)]}...";
    }

    private static string NormalizeWhitespace(string body)
    {
        return string.Join(
            " ",
            body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
