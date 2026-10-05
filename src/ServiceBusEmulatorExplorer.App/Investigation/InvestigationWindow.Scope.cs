using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private EntityAddress? namespaceSelectionAddress;
    private bool restoringNamespaceSelection;
    private bool namespaceTreeReconciliation;
    private int namespaceSelectionRestoreRevision;

    private void UpdateBrowseScopeHeader()
    {
        if (ScopeHeader is null) return;

        if (workspace.Search.IsActive || MessageLibraryPrototype.Visibility == Visibility.Visible)
        {
            BrowseScopeHeader.Visibility = Visibility.Collapsed;
            ListBreadcrumb.Visibility = Visibility.Visible;
            AutomationProperties.SetName(ScopeHeader, workspace.Surface.EntityPath);
            return;
        }

        EntityNode? scope = workspace.Browse.SelectedEntity;
        if (scope?.Address is not { } address)
        {
            BrowseScopeHeader.Visibility = Visibility.Collapsed;
            ListBreadcrumb.Visibility = Visibility.Visible;
            AutomationProperties.SetName(ScopeHeader, workspace.Surface.EntityPath);
            return;
        }

        BrowseScopeHeader.Visibility = Visibility.Visible;
        ListBreadcrumb.Visibility = Visibility.Collapsed;
        ScopeTopicLine.Visibility = Visibility.Collapsed;
        ScopeEntityLine.Visibility = Visibility.Collapsed;
        ScopeAllSubscriptions.Visibility = Visibility.Collapsed;

        if (scope.Kind == nameof(EntityKind.Subscription))
        {
            EntityNode? topic = workspace.Browse.AllEntities().FirstOrDefault(node =>
                node.Kind == nameof(EntityKind.Topic) &&
                string.Equals(node.Name, address.TopicName, StringComparison.OrdinalIgnoreCase));
            if (topic is not null)
            {
                SetScopeLine(ScopeTopicIcon, ScopeTopicName, topic);
                ScopeTopicLine.Visibility = Visibility.Visible;
            }

            SetScopeLine(ScopeEntityIcon, ScopeEntityName, scope);
            ScopeEntityLine.Margin = new Thickness(8, 0, 0, 0);
            ScopeEntityLine.Visibility = Visibility.Visible;
            AutomationProperties.SetName(ScopeHeader, $"Topic: {address.TopicName}; Subscription: {scope.Name}");
            return;
        }

        if (scope.Kind == nameof(EntityKind.Topic))
        {
            SetScopeLine(ScopeTopicIcon, ScopeTopicName, scope);
            ScopeTopicLine.Visibility = Visibility.Visible;
            ScopeAllSubscriptions.Visibility = Visibility.Visible;
            ScopeEntityLine.Margin = new Thickness(0);
            AutomationProperties.SetName(ScopeHeader, $"Topic: {scope.Name}; All subscriptions");
            return;
        }

        SetScopeLine(ScopeEntityIcon, ScopeEntityName, scope);
        ScopeEntityLine.Margin = new Thickness(0);
        ScopeEntityLine.Visibility = Visibility.Visible;
        AutomationProperties.SetName(ScopeHeader, $"Queue: {scope.Name}");
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

        EntityAddress? address = workspace.Search.IsActive
            ? namespaceSelectionAddress ?? workspace.Browse.SelectedEntity?.Address
            : workspace.Browse.SelectedEntity?.Address;
        if (address is null) return;

        TreeViewItem? item = FindNamespaceItem(NamespaceTree, workspace.Surface.Roots, address);
        if (item is null) return;

        if (!workspace.Search.IsActive) namespaceSelectionAddress = address;
        if (item.IsSelected) return;

        restoringNamespaceSelection = true;
        try { item.IsSelected = true; }
        finally { restoringNamespaceSelection = false; }
    }

    private static TreeViewItem? FindNamespaceItem(TreeView tree, IEnumerable<EntityNode> roots, EntityAddress address)
    {
        var path = new List<EntityNode>();
        if (!FindNamespacePath(roots, address, path)) return null;

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

    private static bool FindNamespacePath(IEnumerable<EntityNode> nodes, EntityAddress address, List<EntityNode> path)
    {
        foreach (EntityNode node in nodes)
        {
            path.Add(node);
            if (node.Address == address || FindNamespacePath(node.Children, address, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }
}
