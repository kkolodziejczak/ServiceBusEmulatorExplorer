using System.Windows;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private void UpdateNamespaceIndicators(WatchRuleEditor? editor = null)
    {
        if (!ready) return;
        var targets = (editor ?? CreateWatchEditor()).Targets.Select(target => (target.Address, target.Bucket)).ToHashSet();
        bool showRefresh = MessageLibraryPrototype.Visibility != Visibility.Visible
            && !workspace.Search.IsActive && workspace.IsConnected
            && workspace.Preferences.AutoRefreshSeconds > 0;
        string bucket = workspace.Browse.IsDeadLetter ? "Dead letter" : "Active";
        var nodes = workspace.Browse.Roots.Concat(workspace.Search.Roots)
            .SelectMany(WatchRuleEditor.Flatten).Distinct();
        foreach (var node in nodes)
        {
            bool watchable = node.Address is { Kind: EntityKind.Queue or EntityKind.Subscription };
            bool active = watchable && targets.Contains((node.Address!, MessageBucket.Active));
            bool deadLetter = watchable && targets.Contains((node.Address!, MessageBucket.DeadLetter));
            bool current = showRefresh && node.Address is not null && node.Address == workspace.Browse.SelectedEntity?.Address;
            string refresh = current ? paused ? "Paused" : "Running" : "None";
            string detail = !current ? "" : paused
                ? $"Auto-refresh paused: Every {workspace.Preferences.AutoRefreshSeconds} seconds\nCurrent {bucket} view. Watch remains independent."
                : $"Refresh: Every {workspace.Preferences.AutoRefreshSeconds} seconds\nCurrent {bucket} view. Follows the open Investigation view.";
            node.SetIndicators(active, deadLetter, workspace.IsConnected, refresh, detail);
        }
    }
}
