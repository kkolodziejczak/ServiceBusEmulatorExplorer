using System.Runtime.InteropServices;
using System.Windows;

namespace ServiceBusEmulatorExplorer.App.Tests;

/// <summary>
/// The system clipboard is shared with other processes, so copy tests keep whatever was there, start from a known
/// sentinel, and treat a moment of foreign locking as "not yet" rather than as a failure.
/// </summary>
internal static class TestClipboard
{
    public const string Sentinel = "test-clipboard-sentinel";

    /// <summary>Snapshots every format on the clipboard and puts it back when disposed.</summary>
    public static IDisposable Preserve() => new Snapshot(Copy(Clipboard.GetDataObject()));

    public static void WriteSentinel()
    {
        for (int attempt = 1; ; attempt++)
        {
            try { Clipboard.SetText(Sentinel); return; }
            catch (ExternalException) when (attempt < 10) { Thread.Sleep(50); }
        }
    }

    /// <summary>The clipboard text, or null while it holds no text or another process has it locked.</summary>
    public static string? TryReadText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch (ExternalException) { return null; }
    }

    private static DataObject? Copy(IDataObject? source)
    {
        if (source is null) return null;
        var copy = new DataObject();
        foreach (string format in source.GetFormats(autoConvert: false))
        {
            try { copy.SetData(format, source.GetData(format, autoConvert: false)); }
            catch (ExternalException) { } // Formats another process cannot render now are left out.
        }

        return copy;
    }

    private sealed class Snapshot(DataObject? previous) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                if (previous is null) Clipboard.Clear();
                else Clipboard.SetDataObject(previous, copy: true);
            }
            catch (ExternalException) { } // Restoring is best effort; another process may own the clipboard now.
        }
    }
}
