using ServiceBusEmulatorExplorer.Core.ServiceBus;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private bool inspectorSynchronizingSelection;
    private bool inspectorRendering;
    private string? inspectorRenderedText;
    private InspectorMode? inspectorRenderedMode;
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

    private void CopyCorrelation_Click(object sender, RoutedEventArgs e) =>
        inspectorPanel.CopyCorrelation(workspace.Surface.FocusedMessage);

    private void CopyRowCorrelation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MessageRow row }) inspectorPanel.CopyCorrelation(row);
    }

    private void Json_Checked(object sender, RoutedEventArgs e)
    {
        if (ready && !inspectorRendering) SetInspectorMode(InspectorMode.Body);
    }

    private void Properties_Checked(object sender, RoutedEventArgs e)
    {
        if (ready && !inspectorRendering) SetInspectorMode(InspectorMode.Properties);
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

    private void Copy_Click(object sender, RoutedEventArgs e) => inspectorPanel.CopyShown();

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
        var state = inspectorPanel.StateFor(actions.IsReplayPending);
        bool hasMessage = state.HasMessage;
        DeliveryIdentity? identity = inspector.Current?.Identity;

        bool inspectorContentChanged = !EqualityComparer<DeliveryIdentity?>.Default.Equals(inspectorRenderedIdentity, identity)
            || inspectorRenderedMode != inspectorPanel.Mode;
        if (inspectorContentChanged)
        {
            inspectorRenderedText = null;
        }

        inspectorRenderedIdentity = identity;
        inspectorRenderedMode = inspectorPanel.Mode;
        InspectorHeading.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        InspectorTabs.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        EmptyInspector.Visibility = hasMessage ? Visibility.Collapsed : Visibility.Visible;
        CopyButton.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        DlqReason.Visibility = state.IsDeadLetter ? Visibility.Visible : Visibility.Collapsed;
        ReplayActions.Visibility = hasMessage && state.IsDeadLetter ? Visibility.Visible : Visibility.Collapsed;
        ApplyInspectorStateBadge(state.IsDeadLetter);
        if (!hasMessage)
        {
            CloseInspectorFind(restoreFocus: false);
        }

        inspectorRendering = true;
        try
        {
            var editorDocument = EditorDocuments.For(inspector.Document);
            if (!ReferenceEquals(BodyEditor.Document, editorDocument))
            {
                BodyEditor.SetInspectionDocument(editorDocument);
            }

            BodyEditor.IsReadOnly = state.IsReadOnly;
            InspectorEditabilityStatus.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
            InspectorEditabilityText.Text = state.Editability;
            InspectorEditabilityIcon.SetResourceReference(System.Windows.Shapes.Path.DataProperty,
                state.IsEditable ? "PencilGeometry" : "LockGeometry");
            InspectorEditabilityStatus.ToolTip = state.EditabilityHelp;
            System.Windows.Automation.AutomationProperties.SetName(InspectorEditabilityStatus, state.Editability);
            System.Windows.Automation.AutomationProperties.SetHelpText(InspectorEditabilityStatus, state.EditabilityHelp);
            System.Windows.Automation.AutomationProperties.SetHelpText(BodyEditor, state.EditabilityHelp);
            BodyEditor.Visibility = state.ShowsBody ? Visibility.Visible : Visibility.Collapsed;
            BodyViewer.Visibility = state.ShowsProperties ? Visibility.Visible : Visibility.Collapsed;
            JsonTab.ToolTip = state.JsonTabToolTip;
            JsonTab.IsChecked = inspectorPanel.Mode == InspectorMode.Body;
            PropertiesTab.IsChecked = inspectorPanel.Mode == InspectorMode.Properties;
            ModifiedBadge.Visibility = state.IsModified ? Visibility.Visible : Visibility.Collapsed;
            DiscardButton.Visibility = state.IsModified ? Visibility.Visible : Visibility.Collapsed;

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

    private void ApplyInspectorStateBadge(bool deadLetter)
    {
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

    private void SetInspectorMode(InspectorMode mode)
    {
        if (inspectorPanel.SetMode(mode)) UpdateInspector();
    }

    private void RenderViewer(DeliveryInspector inspector, bool hasMessage)
    {
        if (!hasMessage || inspectorPanel.Mode == InspectorMode.Body)
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
            FontFamily = AppFonts.Code,
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
