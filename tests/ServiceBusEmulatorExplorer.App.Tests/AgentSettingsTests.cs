using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Agent;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class AgentSettingsTests
{
    [Fact]
    public void Turning_agent_access_on_creates_and_saves_a_token() => OnSta(() =>
    {
        var saves = new List<WorkspacePreferences>();
        var window = new SettingsWindow(new WorkspacePreferences(), preferences => { saves.Add(preferences); return Task.CompletedTask; });
        try
        {
            ((ToggleButton)window.FindName("AgentAccessToggle")!).IsChecked = true;
            Pump();

            var saved = Assert.Single(saves);
            Assert.True(saved.AgentAccessEnabled);
            Assert.True(saved.AgentAccessToken.Length >= 32);
            Assert.DoesNotContain(saved.AgentAccessToken, ((TextBox)window.FindName("AgentToken")!).Text);
            Assert.DoesNotContain(saved.AgentAccessToken, ((TextBox)window.FindName("AgentSnippet")!).Text);
            Assert.True(((Button)window.FindName("RegenerateAgentTokenButton")!).IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Invalid_port_warning_survives_status_refreshes_until_a_valid_port_is_saved() => OnSta(() =>
    {
        var saves = new List<WorkspacePreferences>();
        var status = new FakeStatus();
        var initial = new WorkspacePreferences { AgentAccessEnabled = true, AgentAccessToken = "token-value" };
        var window = new SettingsWindow(initial, preferences => { saves.Add(preferences); return Task.CompletedTask; }, agentStatus: status);
        try
        {
            var port = (TextBox)window.FindName("AgentPort")!;
            var warning = (Border)window.FindName("AgentPortWarning")!;
            port.Text = "80";
            Wait(window.ApplyAgentPortAsync());
            Assert.Equal(Visibility.Visible, warning.Visibility);

            status.Publish(new AgentAccessStatus(AgentAccessState.Listening, AgentAccessDefaults.Port, LastRequestUtc: DateTimeOffset.UtcNow));
            Pump();
            Assert.Equal(Visibility.Visible, warning.Visibility);
            Assert.Equal("80", port.Text);
            Assert.Empty(saves);

            port.Text = "50123";
            Wait(window.ApplyAgentPortAsync());
            Assert.Equal(Visibility.Collapsed, warning.Visibility);
            Assert.Equal(50123, Assert.Single(saves).AgentAccessPort);
            Assert.Equal("http://127.0.0.1:50123/mcp", ((TextBox)window.FindName("AgentEndpoint")!).Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Port_in_use_shows_the_listener_failure_with_a_remedy() => OnSta(() =>
    {
        var status = new FakeStatus();
        var window = new SettingsWindow(new WorkspacePreferences { AgentAccessEnabled = true, AgentAccessToken = "token-value" },
            _ => Task.CompletedTask, agentStatus: status);
        try
        {
            status.Publish(new AgentAccessStatus(AgentAccessState.Failed, AgentAccessDefaults.Port, "Port 47811 is in use by another program or another copy of this app."));
            Pump();
            Assert.Equal(Visibility.Visible, ((Border)window.FindName("AgentPortWarning")!).Visibility);
            Assert.Contains("in use", ((TextBlock)window.FindName("AgentPortWarningText")!).Text);
            Assert.Equal("Not listening.", ((TextBlock)window.FindName("AgentStatusText")!).Text);
        }
        finally { window.Close(); }
    });

    private static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The Settings agent test exceeded its 30-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class FakeStatus : IAgentAccessStatusSource
    {
        public AgentAccessStatus Status { get; private set; } = new(AgentAccessState.Listening, AgentAccessDefaults.Port);
        public event Action<AgentAccessStatus>? StatusChanged;

        public void Publish(AgentAccessStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(status);
        }
    }
}
