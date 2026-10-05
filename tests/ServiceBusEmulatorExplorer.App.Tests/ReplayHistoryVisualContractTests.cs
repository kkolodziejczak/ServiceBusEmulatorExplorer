using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed partial class ReplayWindowsTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Replay47_visual_contract_keeps_readable_states_and_copy_next_to_identity()
    {
        OnSta(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var preferences = CreateStatusStatePreferences();
            var now = DateTimeOffset.UtcNow;
            var source = new EntityAddress(EntityKind.Subscription, "billing", "retail-order-events-1005-190340");
            var topic = preferences.ReplayAttempts[4] with
            {
                RequestedAtUtc = now,
                OriginalSource = source,
                OriginalMessageId = "retail-1005-190340-order-events-000",
                Reservation = preferences.ReplayAttempts[4].Reservation with
                { MessageId = "retail-1005-190340-order-events-000-replay-1-d18617dd" },
                Observation = new ReplayObservation(now, true, 3, 30,
                    [new(source, MessageBucket.Active, "Active"), new(source with { Name = "notifications" }, MessageBucket.Active, "Active"), new(source with { Name = "fulfillment" }, MessageBucket.Active, "Active")])
            };
            preferences = preferences with
            {
                Profiles = [preferences.Profiles[0] with { Connection = preferences.Profiles[0].Connection with { Name = "Local emulator" } }],
                ReplayAttempts = [topic,
                    preferences.ReplayAttempts[3] with { RequestedAtUtc = now.AddMinutes(-1) },
                    preferences.ReplayAttempts[0] with { RequestedAtUtc = now.AddMinutes(-2), Observation = null },
                    preferences.ReplayAttempts[1] with { RequestedAtUtc = now.AddMinutes(-3), SendStatus = ReplaySendStatus.Confirmed, SentAtUtc = now.AddMinutes(-3), Observation = new ReplayObservation(now, true, 3, 30, []) }]
            };
            var workspace = CreateWorkspace(new CapturingReplaySender(), initialPreferences: preferences);
            ReplayHistoryWindow? window = null;
            try
            {
                Complete(dispatcher, workspace.InitializeAsync());
                Complete(dispatcher, workspace.ConnectAsync());
                window = CreateHistoryWindow(workspace);
                window.Width = 1200;
                window.Height = 720;
                window.Show();
                var grid = (DataGrid)window.FindName("HistoryGrid");
                grid.SelectedItem = grid.Items.Cast<ReplayHistoryRow>().Single(row => row.MessageId == topic.Reservation.MessageId);
                SettleHistoryLayout(dispatcher, window);
                Capture(window, "Replay-47-visual-contract.png");
                var id = (TextBox)SelectedDetailElement(window, grid, "DetailOriginalId");
                var copy = SelectedDetailButton(window, grid, "CopyOriginalIdButton");
                Assert.True(id.FontSize >= 14, "Replay 47 identities use readable body typography, not native 12px defaults.");
                double gap = copy.TranslatePoint(new Point(), grid).X - id.TranslatePoint(new Point(id.ActualWidth, 0), grid).X;
                Assert.InRange(gap, 0, 24);
                var active = Descendants(grid).OfType<TextBlock>().First(text => text.Text == "3 Active");
                Assert.True(active.FontSize >= 14, "Observed counts are prominent labels, not 11px metadata pills.");
                Assert.Equal(((SolidColorBrush)window.FindResource("RaisedBrush")).Color, ((SolidColorBrush)active.Foreground).Color);
                foreach (var dlq in Descendants(grid).OfType<TextBlock>().Where(text => text.Text is "DLQ" or "1 DLQ"))
                {
                    Assert.Equal(((SolidColorBrush)window.FindResource("DeadLetterTextBrush")).Color, ((SolidColorBrush)dlq.Foreground).Color);
                    var badge = Assert.IsType<Border>(VisualTreeHelper.GetParent(dlq));
                    Assert.Equal(((SolidColorBrush)window.FindResource("DeadLetterBrush")).Color, ((SolidColorBrush)badge.Background).Color);
                }
                Assert.DoesNotContain(Descendants(SelectedRow(grid)).OfType<TextBlock>(), text => text.Text == "Destination");
                window.Width = 780;
                window.Height = 480;
                SettleHistoryLayout(dispatcher, window);
                AssertHistoryColumnsFit(grid);
                ScrollSelectedDetailsIntoView(dispatcher, window, grid, "CopyReplayIdButton");
                AssertVisibleInside(SelectedDetailButton(window, grid, "CopyReplayIdButton"), grid);
                Capture(window, "Replay-47-visual-contract-compact.png");
            }
            finally
            {
                window?.Close();
                workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }
}
