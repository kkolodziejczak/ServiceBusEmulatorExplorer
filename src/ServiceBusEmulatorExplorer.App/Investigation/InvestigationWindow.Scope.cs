using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private bool restoringNamespaceSelection;
    private bool namespaceTreeReconciliation;
    private int namespaceSelectionRestoreRevision;

    private void UpdateBrowseScopeHeader()
    {
        if (ScopeHeader is null) return;

        var header = namespaceScope.HeaderFor(MessageLibraryPrototype.Visibility == Visibility.Visible);
        AutomationProperties.SetName(ScopeHeader, header.AutomationName);
        BrowseScopeHeader.Visibility = header.IsShown ? Visibility.Visible : Visibility.Collapsed;
        ListBreadcrumb.Visibility = header.IsShown ? Visibility.Collapsed : Visibility.Visible;
        if (!header.IsShown) return;

        ScopeTopicLine.Visibility = header.Topic is null ? Visibility.Collapsed : Visibility.Visible;
        if (header.Topic is { } topic) SetScopeLine(ScopeTopicIcon, ScopeTopicName, topic);
        ScopeEntityLine.Visibility = header.Entity is null ? Visibility.Collapsed : Visibility.Visible;
        if (header.Entity is { } entity) SetScopeLine(ScopeEntityIcon, ScopeEntityName, entity);
        ScopeEntityLine.Margin = new Thickness(header.IndentsEntity ? 8 : 0, 0, 0, 0);
        ScopeAllSubscriptions.Visibility = header.ShowsAllSubscriptions ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetScopeLine(System.Windows.Shapes.Path icon, TextBlock name, EntityNode node)
    {
        icon.DataContext = node;
        name.Text = node.Name;
        name.ToolTip = node.Path;
        AutomationProperties.SetName(name, node.Path);
    }

    private void ScheduleNamespaceSelectionRestore()
    {
        int revision = ++namespaceSelectionRestoreRevision;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (revision != namespaceSelectionRestoreRevision) return;
            RestoreNamespaceSelection();
            namespaceTreeReconciliation = false;
        }));
    }

    private void RestoreNamespaceSelection()
    {
        if (!ready || MessageLibraryPrototype.Visibility == Visibility.Visible) return;

        if (namespaceScope.SelectionToRestore is not { } address) return;
        TreeViewItem? item = FindNamespaceItem(NamespaceTree, workspace.Surface.Roots, address);
        if (item is null) return;

        namespaceScope.Restored(address);
        if (item.IsSelected) return;

        restoringNamespaceSelection = true;
        try { item.IsSelected = true; }
        finally { restoringNamespaceSelection = false; }
    }

    private static TreeViewItem? FindNamespaceItem(TreeView tree, IEnumerable<EntityNode> roots, EntityAddress address)
    {
        if (NamespaceScopeViewModel.PathTo(roots, address) is not { } path) return null;

        ItemsControl parent = tree;
        TreeViewItem? selected = null;
        foreach (EntityNode expected in path)
        {
            parent.UpdateLayout();
            selected = null;
            for (int index = 0; index < parent.Items.Count; index++)
            {
                if (parent.ItemContainerGenerator.ContainerFromIndex(index) is TreeViewItem item
                    && ReferenceEquals(item.DataContext, expected))
                {
                    selected = item;
                    break;
                }
            }
            if (selected is null) return null;
            if (ReferenceEquals(expected, path[^1])) continue;
            selected.IsExpanded = true;
            parent = selected;
        }
        return selected;
    }
}
