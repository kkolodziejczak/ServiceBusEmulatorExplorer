using System.Windows;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private void UpdateNamespaceIndicators(WatchRuleEditor? editor = null)
    {
        if (!ready) return;
        bool showsCurrent = MessageLibraryPrototype.Visibility != Visibility.Visible && !workspace.Search.IsActive;
        NamespaceIndicators.Apply(
            workspace.Browse.Roots.Concat(workspace.Search.Roots).SelectMany(WatchRuleEditor.Flatten).Distinct(),
            (editor ?? CreateWatchEditor()).Targets,
            new(workspace.IsConnected, workspace.Preferences.AutoRefreshSeconds, paused, workspace.Browse.IsDeadLetter,
                showsCurrent ? workspace.Browse.SelectedEntity?.Address : null));
    }
}
