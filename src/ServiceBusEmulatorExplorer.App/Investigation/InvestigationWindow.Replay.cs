using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private bool replayPending;

    private ReplaySelectionState CurrentReplaySelection() => ReplaySelection.Evaluate(workspace.IsConnected,
        workspace.Surface.Messages, workspace.Surface.FocusedMessage, workspace.Inspector);

    private void UpdateReplaySurface()
    {
        if (!ready) return;
        var selection = CurrentReplaySelection();
        bool visible = workspace.Surface.FocusedMessage?.IsDeadLetter == true
            || selection.Targets.Any(delivery => delivery.Identity.Bucket == MessageBucket.DeadLetter);
        ReplayButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        string label = replayPending ? "Replaying…" : workspace.Inspector.IsDirty ? "Edit and Replay"
            : selection.Targets.Count > 1 ? $"Replay ({selection.Targets.Count})" : "Replay";
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new Path { Data = Geometry.Parse("M3,1 L15,8 L3,15 Z"), Width = 16, Height = 16,
            Stretch = Stretch.Uniform, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        ReplayButton.Content = content;
        AutomationProperties.SetName(ReplayButton, label);
        ReplayButton.IsEnabled = !replayPending && selection.CanReplay;
        ReplayButton.ToolTip = "Send a new active copy. The original DLQ message remains until separately deleted.";
        EditError.Text = selection.Problem ?? "";
        EditError.Visibility = visible && selection.Problem is not null ? Visibility.Visible : Visibility.Collapsed;
        ReplayActions.Visibility = visible && (workspace.Inspector.IsDirty || selection.Problem is not null)
            ? Visibility.Visible : Visibility.Collapsed;
        NextReplayIdText.Text = "A new ID includes the saved attempt number and a unique suffix.";
        NextReplayIdText.ToolTip = NextReplayIdText.Text;
        DiscardButton.IsEnabled = !replayPending;
    }

    private async void Replay_Click(object sender, RoutedEventArgs e)
    {
        if (replayPending) return;
        var selection = CurrentReplaySelection();
        if (!selection.CanReplay) return;
        string profileId = workspace.SelectedProfile.Id;
        long generation = workspace.ConnectionGeneration;
        int confirmed = 0, uncertain = 0, notSent = 0, attempted = 0;
        bool currentSession() => workspace.SelectedProfile.Id == profileId && workspace.ConnectionGeneration == generation;
        replayPending = true;
        UpdateInspector();
        try
        {
            foreach (var delivery in selection.Targets)
            {
                if (closing || closePending || !currentSession() || !workspace.IsConnected) break;
                var source = delivery.Identity.Source;
                if (source.Kind == EntityKind.Subscription && new ProfileWarningWindow(workspace.SelectedProfile.Connection.Name,
                    $"Replay message {delivery.Message.MessageId} from {source.TopicName}/{source.Name}?\n\nThis publishes a new copy to topic {source.TopicName}. Other matching subscriptions can receive it. The original DLQ message will remain.",
                    workspace.SelectedProfile.ColorHex, "Replay to parent topic", "Replay") { Owner = this }.ShowDialog() != true)
                    break;
                ReplayCopyOutcome outcome;
                attempted++;
                try
                {
                    outcome = await workspace.ReplayAsync(delivery, selection.EditedBody);
                }
                catch (Exception)
                {
                    if (!currentSession()) break;
                    notSent++;
                    workspace.Log($"Replay of {delivery.Message.MessageId} was not sent: its reservation could not be saved or the delivery is no longer current.", true);
                    continue;
                }
                if (!currentSession()) break;
                if (outcome.Status == ReplaySendStatus.Confirmed) confirmed++;
                else if (outcome.Status == ReplaySendStatus.Uncertain) uncertain++;
                else notSent++;
                string result = outcome.Status switch
                {
                    ReplaySendStatus.Confirmed => "sent; original DLQ message retained",
                    ReplaySendStatus.Uncertain => "send outcome uncertain; inspect before manually retrying",
                    _ => "not sent; reserved attempt retained"
                };
                workspace.Log($"Replay {outcome.Reservation.MessageId}: {result}.", outcome.Status != ReplaySendStatus.Confirmed);
                if (outcome.Status == ReplaySendStatus.Confirmed && selection.EditedBody is not null)
                    workspace.Inspector.DiscardReplayedDraft(delivery.Identity, selection.EditedBody);
            }
        }
        finally
        {
            if (currentSession() && !closing && selection.Targets.Count > 1)
                workspace.Log($"Replay batch: {confirmed} sent, {uncertain} uncertain, {notSent} not sent, {selection.Targets.Count - attempted} canceled.",
                    uncertain > 0 || notSent > 0);
            replayPending = false;
            if (!closing) UpdateInspector();
        }
    }
}
