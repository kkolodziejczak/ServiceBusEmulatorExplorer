using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

/// <summary>View models must stay usable by any UI (DEC-053), so the Presentation assembly may not reference WPF or Windows Forms.</summary>
public sealed class PresentationBoundaryTests
{
    private static readonly string[] WindowsUiAssemblies =
        ["PresentationFramework", "PresentationCore", "WindowsBase", "System.Xaml", "System.Windows.Forms", "UIAutomationTypes"];

    [Fact]
    public void Presentation_references_no_windows_ui_framework()
    {
        string[] referenced = typeof(DeleteMessagesViewModel).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToArray();

        Assert.Empty(referenced.Intersect(WindowsUiAssemblies, StringComparer.OrdinalIgnoreCase));
    }
}
