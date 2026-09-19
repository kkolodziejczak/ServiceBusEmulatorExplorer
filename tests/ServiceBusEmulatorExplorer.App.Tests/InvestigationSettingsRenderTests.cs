using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationSettingsRenderTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Settings_window_renders_supported_sizes_and_persists_independent_preferences()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RenderAndInteract();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The Settings render proof exceeded its 30-second bound.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void RenderAndInteract()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        WorkspacePreferences initial = CreatePreferences();
        var saved = new List<WorkspacePreferences>();
        bool holdNextSave = true;
        bool failNextSave = false;
        TaskCompletionSource<bool>? releaseSave = null;

        async Task SaveAsync(WorkspacePreferences preferences)
        {
            if (holdNextSave)
            {
                holdNextSave = false;
                releaseSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                await releaseSave.Task;
            }

            await Task.Delay(5);
            if (failNextSave)
            {
                failNextSave = false;
                throw new InvalidOperationException("synthetic persistence failure");
            }

            saved.Add(preferences);
        }

        var window = new SettingsWindow(initial, SaveAsync);
        try
        {
            window.Show();
            PumpUntil(dispatcher, () => window.IsVisible && window.ActualWidth > 0 && window.ActualHeight > 0,
                TimeSpan.FromSeconds(5));

            ComboBox queue = (ComboBox)window.FindName("QueuePageSizeSelector")!;
            ComboBox topic = (ComboBox)window.FindName("TopicPageSizeSelector")!;
            ComboBox subscription = (ComboBox)window.FindName("SubscriptionPageSizeSelector")!;
            ComboBox searchTime = (ComboBox)window.FindName("SearchTimeBudgetSelector")!;
            ComboBox searchDeliveries = (ComboBox)window.FindName("SearchDeliveryBudgetSelector")!;
            ComboBox authentication = (ComboBox)window.FindName("AuthenticationModeSelector")!;
            TabControl tabs = (TabControl)window.FindName("SettingsTabs")!;
            TabItem general = (TabItem)window.FindName("GeneralTab")!;
            TabItem connections = (TabItem)window.FindName("ConnectionsTab")!;
            Button done = (Button)window.FindName("DoneButton")!;
            TextBlock generalStatus = (TextBlock)window.FindName("GeneralStatus")!;
            AssertPageTypography(general);

            queue.SelectedItem = 100;
            Assert.False(done.IsEnabled, "Done must be disabled while the first general save is pending.");
            Assert.NotNull(releaseSave);
            releaseSave!.SetResult(true);
            WaitForSave(dispatcher, saved, 1, done, generalStatus);

            SetGeneralSelector(dispatcher, saved, queue, 200, done, generalStatus);
            SetGeneralSelector(dispatcher, saved, topic, 25, done, generalStatus);
            SetGeneralSelector(dispatcher, saved, subscription, 200, done, generalStatus);
            SetGeneralSelector(dispatcher, saved, searchTime, 120, done, generalStatus);
            SetGeneralSelector(dispatcher, saved, searchDeliveries, 50000, done, generalStatus);

            WorkspacePreferences current = saved[^1];
            Assert.Equal(200, current.QueuePageSize);
            Assert.Equal(25, current.TopicPageSize);
            Assert.Equal(200, current.SubscriptionPageSize);
            Assert.Equal(120, current.SearchTimeBudgetSeconds);
            Assert.Equal(50000, current.SearchDeliveryBudget);
            Assert.Equal(initial.TopicPageSize, saved[0].TopicPageSize);
            Assert.Equal(initial.SearchTimeBudgetSeconds, saved[0].SearchTimeBudgetSeconds);

            tabs.SelectedItem = connections;
            window.UpdateLayout();
            AssertPageTypography(connections);
            ListBox profiles = (ListBox)window.FindName("ProfilesList")!;
            Assert.Equal(2, profiles.Items.Count);
            authentication.SelectedIndex = 1;
            TextBox namespaceBox = (TextBox)window.FindName("FullyQualifiedNamespace")!;
            namespaceBox.Text = "orders.servicebus.windows.net";
            TextBox warning = (TextBox)window.FindName("ConnectionWarning")!;
            warning.Text = "Use the staging tenant before connecting.";
            Button saveProfile = (Button)window.FindName("SaveProfileButton")!;
            TextBlock profileStatus = (TextBlock)window.FindName("ProfileStatus")!;
            saveProfile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(dispatcher, () => profileStatus.Text == "Profile saved. No connection was attempted.",
                TimeSpan.FromSeconds(5));

            InvestigationProfile persistedProfile = saved[^1].Profiles.Single(profile => profile.Id == "primary");
            Assert.Equal(ConnectionAuthenticationMode.AzureCli, persistedProfile.Connection.AuthenticationMode);
            Assert.Equal("orders.servicebus.windows.net", persistedProfile.Connection.FullyQualifiedNamespace);
            Assert.Equal("Use the staging tenant before connecting.", persistedProfile.WarningMessage);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("ConnectionStringsPanel")!).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("AzureCliPanel")!).Visibility);

            profiles.SelectedIndex = 1;
            window.UpdateLayout();
            Assert.Equal(0, authentication.SelectedIndex);
            profiles.SelectedIndex = 0;
            window.UpdateLayout();
            Assert.Equal(1, authentication.SelectedIndex);

            failNextSave = true;
            warning.Text = "This edit should fail to persist.";
            saveProfile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(dispatcher, () => profileStatus.Text.StartsWith("Could not save profile:", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));
            Assert.Equal("Use the staging tenant before connecting.", saved[^1].Profiles.Single(profile => profile.Id == "primary").WarningMessage);

            tabs.SelectedItem = general;
            window.Width = 520;
            window.Height = 820;
            window.UpdateLayout();
            FrameworkElement settingsTabs = (FrameworkElement)window.FindName("SettingsTabs")!;
            AssertVisibleBounds(window, settingsTabs, done, queue, topic, subscription, searchTime, searchDeliveries);
            CaptureIfEnabled(window, "settings-default-general");

            tabs.SelectedItem = connections;
            window.UpdateLayout();
            AssertVisibleBounds(window, settingsTabs, done, profiles, authentication, namespaceBox);
            CaptureIfEnabled(window, "settings-default-connections");

            tabs.SelectedItem = general;
            window.Width = 460;
            window.Height = 520;
            window.UpdateLayout();
            AssertVisibleBounds(window, settingsTabs, done);
            Assert.True(settingsTabs.ActualWidth <= window.ActualWidth + 1,
                "The Settings tab surface is wider than the minimum supported dialog.");
            CaptureIfEnabled(window, "settings-minimum-general");
        }
        finally
        {
            releaseSave?.TrySetResult(true);
            if (window.IsVisible)
            {
                window.Close();
                PumpUntil(dispatcher, () => !window.IsVisible, TimeSpan.FromSeconds(5));
            }

            if (window.IsVisible)
            {
                window.Hide();
            }
        }
    }

    private static WorkspacePreferences CreatePreferences() => new()
    {
        Profiles =
        [
            new InvestigationProfile(
                "primary",
                new ConnectionProfile(
                    "Primary workspace",
                    "Endpoint=sb://runtime.example/;SharedAccessKeyName=Root;SharedAccessKey=secret",
                    "Endpoint=sb://admin.example/;SharedAccessKeyName=Root;SharedAccessKey=secret"),
                "#0069FA",
                "Review this profile before connecting."),
            new InvestigationProfile(
                "secondary",
                new ConnectionProfile(
                    "Secondary workspace",
                    "Endpoint=sb://secondary-runtime.example/;SharedAccessKeyName=Root;SharedAccessKey=secret",
                    "Endpoint=sb://secondary-admin.example/;SharedAccessKeyName=Root;SharedAccessKey=secret"),
                "#7540BF")
        ],
        SelectedProfileId = "primary",
        QueuePageSize = 25,
        TopicPageSize = 50,
        SubscriptionPageSize = 100,
        SearchTimeBudgetSeconds = 10,
        SearchDeliveryBudget = 1000,
        WindowWidth = 520,
        WindowHeight = 820
    };

    private static void AssertPageTypography(TabItem tab)
    {
        var content = (ScrollViewer)tab.Content;
        Assert.Equal(Color.FromRgb(0x17, 0x21, 0x3D), ((SolidColorBrush)content.Foreground).Color);
        Assert.Equal(FontWeights.Normal, content.FontWeight);
    }

    private static void SetGeneralSelector(
        Dispatcher dispatcher,
        List<WorkspacePreferences> saved,
        ComboBox selector,
        int value,
        Button done,
        TextBlock status)
    {
        int saveCount = saved.Count;
        selector.SelectedItem = value;
        WaitForSave(dispatcher, saved, saveCount + 1, done, status);
    }

    private static void WaitForSave(
        Dispatcher dispatcher,
        List<WorkspacePreferences> saved,
        int expectedCount,
        Button done,
        TextBlock status)
    {
        WaitUntil(dispatcher, () => saved.Count >= expectedCount && done.IsEnabled &&
            status.Text == "General preferences saved.", TimeSpan.FromSeconds(5));
    }

    private static void AssertVisibleBounds(Window window, params FrameworkElement[] elements)
    {
        foreach (FrameworkElement element in elements)
        {
            Assert.NotEqual(Visibility.Collapsed, element.Visibility);
            Rect bounds = element.TransformToAncestor(window).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            Assert.True(bounds.Left >= -1 && bounds.Top >= -1,
                $"{element.Name} starts outside the Settings window at {bounds}.");
            Assert.True(bounds.Right <= window.ActualWidth + 1 && bounds.Bottom <= window.ActualHeight + 1,
                $"{element.Name} is clipped by the Settings window at {bounds}.");
        }
    }

    private static void CaptureIfEnabled(Window window, string name)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_INVESTIGATION_UI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "artifacts", "investigation-ui");
        Directory.CreateDirectory(directory);
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream output = File.Create(Path.Combine(directory, $"{name}.png"));
        encoder.Save(output);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static void WaitUntil(Dispatcher dispatcher, Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The Settings render proof did not reach its expected state.");
            }

            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
    }

    private static void PumpUntil(Dispatcher dispatcher, Func<bool> condition, TimeSpan timeout) =>
        WaitUntil(dispatcher, condition, timeout);
}
