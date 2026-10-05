using ServiceBusEmulatorExplorer.Core.ServiceBus;
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
    private string inspectorMode = "Body";
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

    private void CopyCorrelation_Click(object sender, RoutedEventArgs e)
    {
        if (workspace.Surface.FocusedMessage is { CorrelationId.Length: > 0 } row)
        {
            CopyToClipboard(row.CorrelationId);
        }
    }

    private void CopyRowCorrelation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MessageRow row } && !string.IsNullOrEmpty(row.CorrelationId))
            CopyToClipboard(row.CorrelationId);
    }

    private void Json_Checked(object sender, RoutedEventArgs e)
    {
        if (ready && !inspectorRendering) SetInspectorMode("Body");
    }

    private void Properties_Checked(object sender, RoutedEventArgs e)
    {
        if (ready && !inspectorRendering) SetInspectorMode("Properties");
    }

    private void InspectorTab_Unchecked(object sender, RoutedEventArgs e)
    {
        if (ready && !inspectorRendering) UpdateInspector();
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
        ApplyInspectorStateBadge(focused);
        if (!hasMessage)
        {
            CloseInspectorFind(restoreFocus: false);
        }

        inspectorRendering = true;
        try
        {
            if (!ReferenceEquals(BodyEditor.Document, inspector.Document))
            {
                BodyEditor.SetInspectionDocument(inspector.Document);
            }

            BodyEditor.IsReadOnly = inspector.IsReadOnly || replayPending;
            bool editable = inspectorMode == "Body" && !BodyEditor.IsReadOnly;
            string editability = editable ? "Editable" : "Read-only";
            string editabilityHelp = editable
                ? "Edit this body, then replay a new copy. Original stays unchanged."
                : replayPending ? "Read-only while replay is in progress."
                : inspectorMode != "Body" ? "Message properties are read-only. You can select and copy them."
                : "This message body is read-only. You can select, find, fold and copy its content.";
            InspectorEditabilityStatus.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
            InspectorEditabilityText.Text = editability;
            InspectorEditabilityIcon.SetResourceReference(System.Windows.Shapes.Path.DataProperty,
                editable ? "PencilGeometry" : "LockGeometry");
            InspectorEditabilityStatus.ToolTip = editabilityHelp;
            System.Windows.Automation.AutomationProperties.SetName(InspectorEditabilityStatus, editability);
            System.Windows.Automation.AutomationProperties.SetHelpText(InspectorEditabilityStatus, editabilityHelp);
            System.Windows.Automation.AutomationProperties.SetHelpText(BodyEditor, editabilityHelp);
            BodyEditor.Visibility = hasMessage && inspectorMode == "Body"
                ? Visibility.Visible
                : Visibility.Collapsed;
            BodyViewer.Visibility = hasMessage && inspectorMode != "Body"
                ? Visibility.Visible
                : Visibility.Collapsed;
            JsonTab.ToolTip = inspector.IsValidJson
                ? "Formatted JSON"
                : inspector.BodyDescription.Contains("Base64", StringComparison.Ordinal) ? inspector.BodyDescription
                : "Plain text or invalid JSON cannot be formatted; the body is shown as received.";
            JsonTab.IsChecked = inspectorMode == "Body";
            PropertiesTab.IsChecked = inspectorMode == "Properties";
            ModifiedBadge.Visibility = hasMessage && inspector.IsDirty
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
        RefreshInspectorFind();
        UpdateInspectorCopy();
        UpdateReplaySurface();
    }

    private void SaveInspectedTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (workspace.Inspector.Current is not { } current) return;
        var source = current.Identity.Source;
        string topic = source.TopicName ?? source.Name;
        string sourceLabel = $"{source.Name} · {current.Identity.Bucket}";
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Capture,
            workspace.SelectedProfile.Connection.Name, topic, 1) { Owner = this };
        MessageLibraryPrototype.ConfigureCaptureCollections(dialog);
        dialog.SetCaptureSource(sourceLabel, workspace.Inspector.RawText,
            workspace.Inspector.Document.Text, topic, current.Message,
            source.TopicName is null ? EntityKind.Queue : EntityKind.Topic);
        if (dialog.ShowDialog() != true) return;
        MessageLibraryPrototype.OpenCapturedDraft(dialog.CaptureTemplateName,
            dialog.CaptureTemplateBody, dialog.CaptureTopic, dialog.CaptureCollectionName, dialog.CaptureProperties,
            destinationKind: dialog.CaptureDestinationKind);
        SelectWorkspaceTab(true);
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
        if (!hasMessage || inspectorMode == "Body")
        {
            return;
        }

        string text = inspector.PropertiesText;
        if (string.Equals(inspectorRenderedText, text, StringComparison.Ordinal)
            && BodyViewer.Document is not null)
        {
            return;
        }

        inspectorRenderedText = text;
        var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 24 };
        AddHighlightedJson(paragraph, text);

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
