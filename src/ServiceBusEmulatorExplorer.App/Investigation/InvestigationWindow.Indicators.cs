using System.Windows;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private void UpdateNamespaceIndicators(WatchRuleEditor? editor = null)
    {
        if (!ready) return;
        var targets = (editor ?? CreateWatchEditor()).Targets.Select(target => (target.Address, target.Bucket)).ToHashSet();
        bool showRefresh = workspace.IsConnected
            && workspace.Preferences.AutoRefreshSeconds > 0;
        string bucket = workspace.Browse.IsDeadLetter ? "Dead letter" : "Active";
        var nodes = workspace.Browse.Roots.Concat(workspace.Search.Roots)
            .SelectMany(WatchRuleEditor.Flatten).Distinct();
        foreach (var node in nodes)
        {
            bool watchable = node.Address is { Kind: EntityKind.Queue or EntityKind.Subscription };
            bool active = watchable && targets.Contains((node.Address!, MessageBucket.Active));
            bool deadLetter = watchable && targets.Contains((node.Address!, MessageBucket.DeadLetter));
            bool topicActive = node.Kind == nameof(EntityKind.Topic) && targets.Any(target => target.Address.TopicName == node.Name && target.Bucket == MessageBucket.Active);
            bool topicDlq = node.Kind == nameof(EntityKind.Topic) && targets.Any(target => target.Address.TopicName == node.Name && target.Bucket == MessageBucket.DeadLetter);
            bool background = active || deadLetter || topicActive || topicDlq;
            bool current = MessageLibraryPrototype.Visibility != Visibility.Visible && !workspace.Search.IsActive
                && node.Address is not null && node.Address == workspace.Browse.SelectedEntity?.Address;
            bool refreshing = showRefresh && (background || current);
            string refresh = refreshing ? paused ? "Paused" : "Running" : "None";
            string scope = background
                ? $"Background: {((active || topicActive) && (deadLetter || topicDlq) ? "Active and Dead letter" : active || topicActive ? "Active" : "Dead letter")}" + (node.Kind == nameof(EntityKind.Topic) ? " watched subscriptions." : ".")
                : $"Current {bucket} view.";
            string detail = !refreshing ? "" :
                $"{(paused ? "Auto-refresh paused" : "Refresh")}: Every {workspace.Preferences.AutoRefreshSeconds} seconds\n{scope} Watch remains independent.";
            node.SetIndicators(active, deadLetter, workspace.IsConnected, refresh, detail);
        }
    }
}
