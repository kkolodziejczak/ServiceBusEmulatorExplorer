using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private readonly List<int> inspectorMatches = [];
    private int inspectorMatchIndex = -1;
    private string? inspectorSearchText;
    private string? inspectorSearchQuery;

    private void InspectorFind_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = workspace?.Inspector.Current is not null && InspectorPane.IsVisible;
        e.Handled = true;
    }

    private void InspectorFind_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        string selected = inspectorMode == "JSON" ? BodyEditor.SelectedText : BodyViewer.Selection.Text;
        FindPanel.Visibility = Visibility.Visible;
        BodyEditor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromRgb(113, 80, 0));
        BodyEditor.TextArea.SelectionForeground = (Brush)FindResource("ModifiedTextBrush");
        BodyViewer.SelectionBrush = (Brush)FindResource("ModifiedBrush");
        BodyViewer.SelectionOpacity = 0.65;
        if (!string.IsNullOrEmpty(selected)) FindBox.Text = selected;
        RefreshInspectorFind(force: true);
        FindBox.Focus();
        FindBox.SelectAll();
        e.Handled = true;
    }

    private void Inspector_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || FindPanel.Visibility != Visibility.Visible) return;
        CloseInspectorFind();
        e.Handled = true;
    }

    private void CloseFind_Click(object sender, RoutedEventArgs e) => CloseInspectorFind();

    private void CloseInspectorFind(bool restoreFocus = true)
    {
        if (FindPanel.Visibility != Visibility.Visible) return;
        FindPanel.Visibility = Visibility.Collapsed;
        BodyEditor.TextArea.ClearValue(TextArea.SelectionBrushProperty);
        BodyEditor.TextArea.ClearValue(TextArea.SelectionForegroundProperty);
        BodyViewer.ClearValue(RichTextBox.SelectionBrushProperty);
        BodyViewer.ClearValue(RichTextBox.SelectionOpacityProperty);
        ClearInspectorMatchSelection();
        inspectorSearchText = null;
        if (!restoreFocus) return;
        if (inspectorMode == "JSON") BodyEditor.Focus();
        else BodyViewer.Focus();
    }

    private void Find_Changed(object sender, TextChangedEventArgs e) => RefreshInspectorFind(force: true);
    private void FindPrevious_Click(object sender, RoutedEventArgs e) => MoveInspectorMatch(-1);
    private void FindNext_Click(object sender, RoutedEventArgs e) => MoveInspectorMatch(1);

    private void Find_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        MoveInspectorMatch((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
        e.Handled = true;
    }

    private void RefreshInspectorFind(bool force = false)
    {
        if (FindPanel?.Visibility != Visibility.Visible || FindBox is null || FindMatchCount is null) return;
        string text = inspectorMode == "JSON" ? BodyEditor.Text
            : new TextRange(BodyViewer.Document.ContentStart, BodyViewer.Document.ContentEnd).Text;
        string query = FindBox.Text;
        if (!force && text == inspectorSearchText && query == inspectorSearchQuery) return;
        inspectorSearchText = text;
        inspectorSearchQuery = query;
        inspectorMatches.Clear();
        if (query.Length > 0)
        {
            int offset = 0;
            while (offset <= text.Length - query.Length)
            {
                int match = text.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
                if (match < 0) break;
                inspectorMatches.Add(match);
                offset = match + query.Length;
            }
        }
        bool preserveEditingSelection = !force && BodyEditor.IsKeyboardFocusWithin && inspectorMode == "JSON";
        inspectorMatchIndex = inspectorMatches.Count == 0 ? -1 : 0;
        FindPrevious.IsEnabled = FindNext.IsEnabled = inspectorMatches.Count > 0;
        if (inspectorMatchIndex < 0)
        {
            FindMatchCount.Text = query.Length == 0 ? "" : "No matches";
            if (!preserveEditingSelection) ClearInspectorMatchSelection();
        }
        else if (preserveEditingSelection)
        {
            inspectorMatchIndex = -1;
            FindMatchCount.Text = $"{inspectorMatches.Count} matches";
        }
        else SelectInspectorMatch();
    }

    private void ClearInspectorMatchSelection()
    {
        BodyEditor.Select(BodyEditor.CaretOffset, 0);
        BodyViewer.Selection.Select(BodyViewer.Document.ContentStart, BodyViewer.Document.ContentStart);
    }

    private void MoveInspectorMatch(int direction)
    {
        RefreshInspectorFind();
        if (inspectorMatches.Count == 0) return;
        inspectorMatchIndex = inspectorMatchIndex < 0 ? (direction > 0 ? 0 : inspectorMatches.Count - 1)
            : (inspectorMatchIndex + direction + inspectorMatches.Count) % inspectorMatches.Count;
        SelectInspectorMatch();
    }

    private void SelectInspectorMatch()
    {
        int index = inspectorMatches[inspectorMatchIndex];
        int length = FindBox.Text.Length;
        FindMatchCount.Text = $"{inspectorMatchIndex + 1} of {inspectorMatches.Count}";
        if (inspectorMode == "JSON")
        {
            BodyEditor.Select(index, length);
            BodyEditor.ScrollToLine(BodyEditor.Document.GetLineByOffset(index).LineNumber);
        }
        else
        {
            TextPointer start = TextPosition(index);
            BodyViewer.Selection.Select(start, TextPosition(index + length));
            start.Paragraph?.BringIntoView();
        }
    }

    private TextPointer TextPosition(int offset)
    {
        TextPointer pointer = BodyViewer.Document.ContentStart;
        while (true)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                string run = pointer.GetTextInRun(LogicalDirection.Forward);
                if (offset <= run.Length)
                {
                    return pointer.GetPositionAtOffset(offset) ?? BodyViewer.Document.ContentEnd;
                }

                offset -= run.Length;
            }

            TextPointer? next = pointer.GetNextContextPosition(LogicalDirection.Forward);
            if (next is null)
            {
                return BodyViewer.Document.ContentEnd;
            }

            pointer = next;
        }
    }

}
