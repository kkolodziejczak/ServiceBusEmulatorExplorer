using System.Windows;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class PrototypeWindow
{
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var targets = Workspace.ReplayTargets.ToArray();
        if (!Workspace.IsConnected || targets.Length == 0) return;
        var confirmation = new DeleteMessagesWindow(targets) { Owner = this };
        ProfileTheme.Apply(confirmation, connectionSettings.SelectedProfile.ColorHex);
        if (confirmation.ShowDialog() != true) return;
        DeleteConfirmedMessages(targets);
    }

    private void DeleteConfirmedMessages(IReadOnlyList<MessageRow> targets)
    {
        var removed = 0;
        ChangeScope(() => removed = Workspace.DeleteMessages(targets));
        if (removed == 0) return;
        var keys = targets.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            drafts.Remove(key);
            loadedSearchMessages.Remove(key);
        }
        foreach (var location in pendingWatchMessages.Keys.ToArray())
        {
            pendingWatchMessages[location].RemoveAll(row => keys.Contains(row.Key));
            if (pendingWatchMessages[location].Count == 0) pendingWatchMessages.Remove(location);
        }
        AdvanceWatchNotification();
        UpdateEmpty();
        AddLog($"Deleted {removed} selected sample message{(removed == 1 ? "" : "s")}. This cannot be undone.");
    }
}
