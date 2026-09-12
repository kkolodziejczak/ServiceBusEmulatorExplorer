using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private string inspectorMode = "JSON";
    private int inspectorFindOffset;
    private bool inspectorSynchronizingSelection;
    private bool inspectorRendering;
    private string? inspectorRenderedText;
    private string? inspectorRenderedMode;
    private DeliveryIdentity? inspectorRenderedIdentity;

    private void Messages_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (inspectorSynchronizingSelection)
        {
            return;
        }

        MessageRow? focused = e.AddedItems.OfType<MessageRow>().LastOrDefault()
            ?? MessageGrid.CurrentItem as MessageRow;
        workspace.Surface.FocusedMessage = focused;
        UpdateInspector();
    }

    private void Messages_CurrentCellChanged(object? sender, EventArgs e)
    {
        if (!inspectorSynchronizingSelection && MessageGrid.CurrentItem is MessageRow row)
        {
            workspace.Surface.FocusedMessage = row;
            UpdateInspector();
        }
    }

    private void Messages_KeyDown(object sender, KeyEventArgs e)
    {
        if (IsInsideCheckBox(e.OriginalSource as DependencyObject)
            || e.Key != Key.Space
            || MessageGrid.CurrentItem is not MessageRow row)
        {
            return;
        }

        row.IsSelected = !row.IsSelected;
        workspace.Surface.FocusedMessage = row;
        SynchronizeInspectorSelection();
        e.Handled = true;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool select = workspace.Surface.SelectedCount < workspace.Surface.Messages.Count;
        workspace.Surface.SetAllChecked(select);
        e.Handled = true;
    }

    private void RowCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: MessageRow row })
        {
            workspace.Surface.FocusedMessage = row;
            SynchronizeInspectorSelection();
            e.Handled = true;
        }
    }

    private void CopyRowCorrelation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MessageRow row } && !string.IsNullOrEmpty(row.CorrelationId))
        {
            CopyToClipboard(row.CorrelationId);
        }
    }

    private void CopyCorrelation_Click(object sender, RoutedEventArgs e)
    {
        if (workspace.Surface.FocusedMessage is { CorrelationId.Length: > 0 } row)
        {
            CopyToClipboard(row.CorrelationId);
        }
    }

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        FindPanel.Visibility = Visibility.Visible;
        inspectorFindOffset = 0;
        FindBox.Focus();
    }

    private void Json_Click(object sender, RoutedEventArgs e) => SetInspectorMode("JSON");

    private void Properties_Click(object sender, RoutedEventArgs e) => SetInspectorMode("Properties");

    private void Raw_Click(object sender, RoutedEventArgs e) => SetInspectorMode("Raw");

    private void CloseFind_Click(object sender, RoutedEventArgs e)
    {
        FindPanel.Visibility = Visibility.Collapsed;
        if (inspectorMode == "JSON") BodyEditor.Focus();
        else BodyViewer.Focus();
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNextInspectorMatch();

    private void Find_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindNextInspectorMatch();
            e.Handled = true;
        }
    }

    private void BodyEditor_Changed(object? sender, EventArgs e)
    {
        if (!inspectorRendering)
        {
            UpdateInspector();
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (workspace.Inspector.Current is null)
        {
            return;
        }

        string text = inspectorMode switch
        {
            "Properties" => workspace.Inspector.PropertiesText,
            "Raw" => workspace.Inspector.RawText,
            _ => workspace.Inspector.Document.Text
        };
        CopyToClipboard(text);
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        workspace.Inspector.DiscardCurrent();
        UpdateInspector();
    }

    private void UpdateInspector()
    {
        if (!ready)
        {
            return;
        }

        DeliveryInspector inspector = workspace.Inspector;
        MessageRow? focused = workspace.Surface.FocusedMessage;
        bool hasMessage = focused is not null && inspector.Current is not null;
        DeliveryIdentity? identity = inspector.Current?.Identity;

        bool inspectorContentChanged = !EqualityComparer<DeliveryIdentity?>.Default.Equals(inspectorRenderedIdentity, identity)
            || !string.Equals(inspectorRenderedMode, inspectorMode, StringComparison.Ordinal);
        if (inspectorContentChanged)
        {
            inspectorFindOffset = 0;
            inspectorRenderedText = null;
        }

        inspectorRenderedIdentity = identity;
        inspectorRenderedMode = inspectorMode;
        InspectorHeading.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        InspectorTabs.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        EmptyInspector.Visibility = hasMessage ? Visibility.Collapsed : Visibility.Visible;
        CopyButton.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        DlqReason.Visibility = focused?.IsDeadLetter == true ? Visibility.Visible : Visibility.Collapsed;
        ReplayActions.Visibility = hasMessage && focused?.IsDeadLetter == true ? Visibility.Visible : Visibility.Collapsed;
        RawTab.ToolTip = inspector.RawDescription;
        RawTab.Content = inspector.RawDescription.Contains("Base64", StringComparison.Ordinal) ? "Raw (Base64)" : "Raw";
        ApplyInspectorStateBadge(focused);
        if (!hasMessage)
        {
            FindPanel.Visibility = Visibility.Collapsed;
        }

        inspectorRendering = true;
        try
        {
            if (!ReferenceEquals(BodyEditor.Document, inspector.Document))
            {
                BodyEditor.Document = inspector.Document;
            }

            BodyEditor.IsReadOnly = inspector.IsReadOnly;
            BodyEditor.Visibility = hasMessage && inspectorMode == "JSON"
                ? Visibility.Visible
                : Visibility.Collapsed;
            BodyViewer.Visibility = hasMessage && inspectorMode != "JSON"
                ? Visibility.Visible
                : Visibility.Collapsed;
            JsonTab.Content = inspector.JsonLabel;
            JsonTab.ToolTip = inspector.IsValidJson
                ? "Formatted JSON"
                : "Plain text or invalid JSON cannot be formatted; the body is shown as received.";
            JsonTab.IsChecked = inspectorMode == "JSON";
            PropertiesTab.IsChecked = inspectorMode == "Properties";
            RawTab.IsChecked = inspectorMode == "Raw";
            ModifiedBadge.Visibility = hasMessage && inspectorMode == "JSON" && inspector.IsDirty
                ? Visibility.Visible
                : Visibility.Collapsed;
            DiscardButton.Visibility = hasMessage && inspector.IsDirty
                ? Visibility.Visible
                : Visibility.Collapsed;

            RenderViewer(inspector, hasMessage);
        }
        finally
        {
            inspectorRendering = false;
        }
    }

    private void ApplyInspectorStateBadge(MessageRow? focused)
    {
        bool deadLetter = focused?.IsDeadLetter == true;
        InspectorState.Background = deadLetter
            ? new SolidColorBrush(Color.FromRgb(255, 230, 200))
            : new SolidColorBrush(Color.FromRgb(239, 246, 255));
        InspectorState.BorderBrush = deadLetter
            ? new SolidColorBrush(Color.FromRgb(166, 91, 0))
            : new SolidColorBrush(Color.FromRgb(108, 155, 210));
        InspectorState.BorderThickness = new Thickness(1);
        if (InspectorState.Child is TextBlock state)
        {
            state.Foreground = deadLetter
                ? new SolidColorBrush(Color.FromRgb(166, 91, 0))
                : new SolidColorBrush(Color.FromRgb(23, 74, 126));
        }
    }

    private void SetInspectorMode(string mode)
    {
        if (inspectorMode == mode)
        {
            return;
        }

        inspectorMode = mode;
        UpdateInspector();
    }

    private void RenderViewer(DeliveryInspector inspector, bool hasMessage)
    {
        if (!hasMessage || inspectorMode == "JSON")
        {
            return;
        }

        string text = inspectorMode == "Properties" ? inspector.PropertiesText : inspector.RawText;
        if (string.Equals(inspectorRenderedText, text, StringComparison.Ordinal)
            && BodyViewer.Document is not null)
        {
            return;
        }

        inspectorRenderedText = text;
        var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 24 };
        if (inspectorMode == "Properties")
        {
            AddHighlightedJson(paragraph, text);
        }
        else
        {
            paragraph.Inlines.Add(new Run(text));
        }

        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Foreground = JsonSyntaxColorizer.TextBrush,
            PageWidth = double.NaN
        };
        document.Blocks.Add(paragraph);
        BodyViewer.Document = document;
    }

    private static void AddHighlightedJson(Paragraph paragraph, string text)
    {
        int position = 0;
        foreach (JsonTokenSpan token in JsonSyntaxColorizer.FindTokens(text))
        {
            if (token.Start > position)
            {
                paragraph.Inlines.Add(new Run(text[position..token.Start]));
            }

            paragraph.Inlines.Add(new Run(text.Substring(token.Start, token.Length))
            {
                Foreground = JsonSyntaxColorizer.GetBrush(token.Kind)
            });
            position = token.Start + token.Length;
        }

        if (position < text.Length)
        {
            paragraph.Inlines.Add(new Run(text[position..]));
        }
    }

    private void FindNextInspectorMatch()
    {
        string query = FindBox.Text;
        if (string.IsNullOrEmpty(query) || workspace.Inspector.Current is null)
        {
            return;
        }

        string full = inspectorMode == "JSON"
            ? BodyEditor.Text
            : new TextRange(BodyViewer.Document.ContentStart, BodyViewer.Document.ContentEnd).Text;
        int startOffset = Math.Min(inspectorFindOffset, full.Length);
        int index = full.IndexOf(query, startOffset, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            index = full.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        }

        if (index < 0)
        {
            workspace.Log("No matches in the displayed message.");
            return;
        }

        if (inspectorMode == "JSON")
        {
            BodyEditor.Select(index, query.Length);
            BodyEditor.ScrollToLine(BodyEditor.Document.GetLineByOffset(index).LineNumber);
            BodyEditor.Focus();
        }
        else
        {
            TextPointer start = TextPosition(index);
            TextPointer end = TextPosition(index + query.Length);
            BodyViewer.Selection.Select(start, end);
            BodyViewer.Focus();
            start.Paragraph?.BringIntoView();
        }

        inspectorFindOffset = index + query.Length;
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

    private void SynchronizeInspectorSelection()
    {
        inspectorSynchronizingSelection = true;
        try
        {
            MessageGrid.SelectedItem = workspace.Surface.FocusedMessage;
        }
        finally
        {
            inspectorSynchronizingSelection = false;
        }

        UpdateInspector();
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            workspace.Log("Copied displayed text to clipboard.");
        }
        catch (Exception)
        {
            workspace.Log("Clipboard could not be accessed. Try Copy again.", true);
        }
    }

    private static bool IsInsideCheckBox(DependencyObject? source)
    {
        for (DependencyObject? current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is CheckBox)
            {
                return true;
            }
        }

        return false;
    }
}
