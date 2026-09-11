using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

// A desktop window is intentional: it persists until an explicit response.
public sealed class WatchNotificationWindow : Window
{
    private readonly TextBlock summary = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock source = new() { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(59, 87, 146)), TextWrapping = TextWrapping.Wrap, Margin = new(0, 7, 0, 10) };

    public WatchNotificationWindow(Action investigate, Action dismiss)
    {
        Title = "Service Bus Explorer notification";
        Width = 360; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Background = Brushes.Transparent; AllowsTransparency = true;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/AppIcon.png"));
        var panel = new StackPanel { Margin = new Thickness(16) };
        var header = new DockPanel { Margin = new(0, 0, 0, 12) };
        var close = new Button { Content = "×", FontSize = 21, Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(5, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(close, "Dismiss notification");
        close.Click += (_, _) => dismiss();
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(new Image { Source = Icon, Width = 26, Height = 26, Margin = new(0, 0, 9, 0) });
        header.Children.Add(new TextBlock { Text = "Service Bus Emulator Explorer", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(header); panel.Children.Add(summary); panel.Children.Add(source);
        var buttons = new Grid(); buttons.ColumnDefinitions.Add(new()); buttons.ColumnDefinitions.Add(new());
        var open = new Button { Content = "Investigate", Height = 38, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(0, 103, 255)), BorderThickness = new(0), Margin = new(0, 0, 5, 0) };
        open.Click += (_, _) => investigate();
        var dismissButton = new Button { Content = "Dismiss", Height = 38, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(202, 218, 239)), Margin = new(5, 0, 0, 0) };
        dismissButton.Click += (_, _) => dismiss(); Grid.SetColumn(dismissButton, 1);
        buttons.Children.Add(open); buttons.Children.Add(dismissButton); panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock { Text = "Remains until you respond", FontSize = 11, Margin = new(0, 10, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(90, 110, 150)) });
        Content = new Border { Background = new SolidColorBrush(Color.FromRgb(248, 251, 255)), BorderBrush = new SolidColorBrush(Color.FromRgb(204, 220, 240)), BorderThickness = new(1), CornerRadius = new(9), Child = panel };
        Loaded += (_, _) => Position(); SizeChanged += (_, _) => Position();
    }

    private void Position()
    {
        var desktop = SystemParameters.WorkArea;
        Left = desktop.Right - ActualWidth - 16; Top = desktop.Bottom - ActualHeight - 16;
    }

    public void Update(MessageRow message, int count, int otherGroups)
    {
        summary.Text = $"{count} new {(message.IsDeadLetter ? "dead-letter" : "active")} message{(count == 1 ? "" : "s")}";
        source.Text = $"Local emulator / {message.Source.Replace("/", " / ")}" + (otherGroups > 0 ? $"\n+ {otherGroups} other watched location{(otherGroups == 1 ? "" : "s")}" : "");
    }
}
