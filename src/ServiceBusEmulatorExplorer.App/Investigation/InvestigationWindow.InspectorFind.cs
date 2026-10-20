using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ServiceBusEmulatorExplorer.Presentation.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private readonly TextFindViewModel textFind = new();

    private void InspectorFind_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = workspace?.Inspector.Current is not null && InspectorPane.IsVisible;
        e.Handled = true;
    }

    private void InspectorFind_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        string selected = inspectorPanel.Mode == InspectorMode.Body ? BodyEditor.SelectedText : BodyViewer.Selection.Text;
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
        textFind.Forget();
        if (!restoreFocus) return;
        if (inspectorPanel.Mode == InspectorMode.Body) BodyEditor.Focus();
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
        string text = inspectorPanel.Mode == InspectorMode.Body ? BodyEditor.Text
            : new TextRange(BodyViewer.Document.ContentStart, BodyViewer.Document.ContentEnd).Text;
        if (!textFind.Search(text, FindBox.Text, force)) return;
        bool preserveEditingSelection = !force && BodyEditor.IsKeyboardFocusWithin && inspectorPanel.Mode == InspectorMode.Body;
        FindPrevious.IsEnabled = FindNext.IsEnabled = textFind.Matches.Count > 0;
        if (textFind.Current < 0)
        {
            FindMatchCount.Text = textFind.CountText;
            if (!preserveEditingSelection) ClearInspectorMatchSelection();
        }
        else if (preserveEditingSelection)
        {
            textFind.Unselect();
            FindMatchCount.Text = textFind.CountText;
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
        if (textFind.Move(direction)) SelectInspectorMatch();
    }

    private void SelectInspectorMatch()
    {
        int index = textFind.Matches[textFind.Current];
        int length = textFind.QueryLength;
        FindMatchCount.Text = textFind.CountText;
        if (inspectorPanel.Mode == InspectorMode.Body)
        {
            BodyEditor.RevealRange(index, length);
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
