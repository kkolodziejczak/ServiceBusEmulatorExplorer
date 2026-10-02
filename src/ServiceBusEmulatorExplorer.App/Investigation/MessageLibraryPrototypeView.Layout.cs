using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeView
{
    private const double WideEditorBreakpoint = 1050;
    private const double CompactPropertiesBreakpoint = 700;

    private EditorSurface activeTabSurface = EditorSurface.Body;
    private EditorSurface lastFocusedSurface = EditorSurface.Body;
    private EditorSurface inspectorSurface = EditorSurface.Properties;
    private bool isWideEditorLayout;

    private enum EditorSurface { Body, Properties, Variables }

    private void EditorArea_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyEditorResponsiveLayout();

    private void WideInspectorToggle_Click(object sender, RoutedEventArgs e)
    {
        inspectorSurface = inspectorSurface == EditorSurface.Variables
            ? EditorSurface.Properties
            : EditorSurface.Variables;
        lastFocusedSurface = inspectorSurface;
        ApplyEditorResponsiveLayout();
    }

    private void BodySurface_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        RememberFocusedEditorSurface(EditorSurface.Body);

    private void PropertiesSurface_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        RememberFocusedEditorSurface(EditorSurface.Properties);

    private void VariablesSurface_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        RememberFocusedEditorSurface(EditorSurface.Variables);

    private void RememberFocusedEditorSurface(EditorSurface surface)
    {
        lastFocusedSurface = surface;
        if (surface != EditorSurface.Body)
            inspectorSurface = surface;
        if (!isWideEditorLayout)
            activeTabSurface = surface;
        UpdateEditorTabSelection();
    }

    private void ApplicationPropertiesGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ApplicationPropertiesGrid is null || ApplicationPropertiesGrid.Columns.Count < 4) return;

        double available = Math.Max(0, ApplicationPropertiesGrid.ActualWidth - 2);
        double nameMaxWidth = Math.Clamp(available - 32 - 80 - 90, 100, 200);
        double typeMaxWidth = Math.Clamp(available - 32 - nameMaxWidth - 90, 80, 120);
        ApplicationPropertiesGrid.Columns[0].Width = DataGridLength.Auto;
        ApplicationPropertiesGrid.Columns[0].MinWidth = 100;
        ApplicationPropertiesGrid.Columns[0].MaxWidth = nameMaxWidth;
        ApplicationPropertiesGrid.Columns[1].Width = DataGridLength.Auto;
        ApplicationPropertiesGrid.Columns[1].MinWidth = 80;
        ApplicationPropertiesGrid.Columns[1].MaxWidth = typeMaxWidth;
        ApplicationPropertiesGrid.Columns[2].Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        ApplicationPropertiesGrid.Columns[3].Width = new DataGridLength(32);
    }

    private void ApplyEditorResponsiveLayout()
    {
        if (EditorArea is null || EditorArea.ActualWidth <= 0) return;

        bool wide = EditorArea.ActualWidth >= WideEditorBreakpoint;
        bool compact = EditorArea.ActualWidth < CompactPropertiesBreakpoint;
        if (isWideEditorLayout && !wide)
            activeTabSurface = lastFocusedSurface;
        else if (!isWideEditorLayout && wide && activeTabSurface != EditorSurface.Body)
            inspectorSurface = activeTabSurface;
        isWideEditorLayout = wide;

        EditorSurfaces.ColumnDefinitions[0].Width = wide
            ? new GridLength(0.36, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        EditorSurfaces.ColumnDefinitions[1].Width = wide
            ? new GridLength(0.64, GridUnitType.Star)
            : new GridLength(0, GridUnitType.Pixel);

        Grid.SetColumn(BodyEditorSurface, 0);
        Grid.SetColumn(PropertiesEditorSurface, wide ? 1 : 0);
        Grid.SetColumn(VariablesEditorSurface, wide ? 1 : 0);
        Grid.SetColumnSpan(BodyEditorSurface, 1);
        Grid.SetColumnSpan(PropertiesEditorSurface, 1);
        Grid.SetColumnSpan(VariablesEditorSurface, 1);

        BodyEditorSurface.Visibility = wide || activeTabSurface == EditorSurface.Body
            ? Visibility.Visible : Visibility.Collapsed;
        PropertiesEditorSurface.Visibility = wide && inspectorSurface == EditorSurface.Properties ||
                                               !wide && activeTabSurface == EditorSurface.Properties
            ? Visibility.Visible : Visibility.Collapsed;
        VariablesEditorSurface.Visibility = wide && inspectorSurface == EditorSurface.Variables ||
                                             !wide && activeTabSurface == EditorSurface.Variables
            ? Visibility.Visible : Visibility.Collapsed;

        EditorTabsPanel.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
        EditorWideHeader.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;

        PropertyGroupsGrid.ColumnDefinitions[1].Width = compact
            ? new GridLength(0, GridUnitType.Pixel)
            : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(RoutingPropertiesGroup, compact ? 0 : 1);
        Grid.SetRow(RoutingPropertiesGroup, compact ? 1 : 0);
        MessagePropertiesGroup.Margin = compact
            ? new Thickness(0, 0, 0, 16)
            : new Thickness(0, 0, 8, 0);
        RoutingPropertiesGroup.Margin = compact
            ? new Thickness(0)
            : new Thickness(8, 0, 0, 0);
        UpdateEditorTabSelection();
    }

    private void UpdateEditorTabSelection()
    {
        if (EditorBodyTab is null) return;

        EditorBodyTab.Style = (Style)FindResource(activeTabSurface == EditorSurface.Body
            ? "PreviewTabActive" : "PreviewTab");
        EditorPropertiesTab.Style = (Style)FindResource(activeTabSurface == EditorSurface.Properties
            ? "PreviewTabActive" : "PreviewTab");
        EditorVariablesTab.Style = (Style)FindResource(activeTabSurface == EditorSurface.Variables
            ? "PreviewTabActive" : "PreviewTab");

        if (WideVariablesAction is not null)
        {
            bool variablesShown = inspectorSurface == EditorSurface.Variables;
            WidePropertiesHeading.Text = variablesShown ? "Variables" : "Properties";
            WideVariablesAction.Content = variablesShown ? "Properties" : "Variables";
            WideVariablesAction.Style = (Style)FindResource(variablesShown
                ? "PreviewTabActive" : "PreviewTab");
            AutomationProperties.SetName(WideVariablesAction, variablesShown
                ? "Show template properties" : "Show template variables");
        }
    }
}
