using System.Windows;
using System.Windows.Input;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private void InspectorDocumentActivity(object sender, MouseEventArgs e) => UpdateInspectorCopy();

    private void InspectorDocumentFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdateInspectorCopy();

    private void UpdateInspectorCopy()
    {
        if (CopyButton is null || InspectorDocumentSurface is null) return;
        double top = inspectorMode == "Body" ? BodyEditor.Padding.Top : BodyViewer.Padding.Top;
        double lineHeight = BodyEditor.TextArea.TextView.DefaultLineHeight;
        double buttonHeight = CopyButton.ActualHeight > 0 ? CopyButton.ActualHeight : 32;
        CopyButton.Margin = new Thickness(0, Math.Max(0, top + (lineHeight - buttonHeight) / 2), 24, 0);
        bool visible = InspectorDocumentSurface.IsMouseOver || InspectorDocumentSurface.IsKeyboardFocusWithin;
        CopyButton.Opacity = visible ? 1 : 0;
        CopyButton.IsHitTestVisible = visible;
    }
}
