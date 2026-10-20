using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private void UpdateReplaySurface()
    {
        if (!ready) return;
        UpdateDeleteSurface();
        var replay = actions.Replay;
        ReplayButton.Visibility = replay.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new Path { Data = Geometry.Parse("M3,1 L15,8 L3,15 Z"), Width = 16, Height = 16,
            Stretch = Stretch.Uniform, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = replay.Label, VerticalAlignment = VerticalAlignment.Center });
        ReplayButton.Content = content;
        AutomationProperties.SetName(ReplayButton, replay.Label);
        ReplayButton.IsEnabled = replay.IsEnabled;
        ReplayButton.ToolTip = "Send a new active copy. The original DLQ message remains until separately deleted.";
        EditError.Text = replay.Problem ?? "";
        EditError.Visibility = replay.IsVisible && replay.Problem is not null ? Visibility.Visible : Visibility.Collapsed;
        ReplayActions.Visibility = replay.ShowsActions ? Visibility.Visible : Visibility.Collapsed;
        NextReplayIdText.Text = "Review the exact outgoing message ID before sending.";
        NextReplayIdText.ToolTip = NextReplayIdText.Text;
        DiscardButton.IsEnabled = !actions.IsReplayPending;
    }

    private async void Replay_Click(object sender, RoutedEventArgs e) => await actions.ReplayAsync();

    IReadOnlyList<string>? IMessageActionsHost.ReviewReplays(IReadOnlyList<PreparedReplay> candidates)
    {
        var review = new ReplayReviewWindow(workspace.SelectedProfile, candidates) { Owner = this };
        return review.ShowDialog() == true ? review.ReviewedIds : null;
    }

    bool IMessageActionsHost.IsClosing => closing;

    bool IMessageActionsHost.IsClosePending => closePending;

    void IMessageActionsHost.ShowReplayHistory() => ShowReplayHistory();
}
