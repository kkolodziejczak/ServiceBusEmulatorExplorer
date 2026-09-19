using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

// A desktop window is intentional: it persists until an explicit response.
public sealed class WatchNotificationWindow : Window
{
    private readonly TextBlock summary = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock source = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) };

    public WatchNotificationWindow(Action investigate, Action dismiss)
    {
        Title = "Service Bus Explorer notification";
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("SharedStyles.xaml", UriKind.Relative) });
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        SetResourceReference(ForegroundProperty, "InkBrush");
        source.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        Width = 360; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Background = Brushes.Transparent; AllowsTransparency = true;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/AppIcon.png"));
        var panel = new StackPanel { Margin = new Thickness(16) };
        var header = new DockPanel { Margin = new(0, 0, 0, 12) };
        var close = CreateCloseButton();
        System.Windows.Automation.AutomationProperties.SetName(close, "Dismiss notification");
        close.Click += (_, _) => dismiss();
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(new Image { Source = Icon, Width = 26, Height = 26, Margin = new(0, 0, 9, 0) });
        header.Children.Add(new TextBlock { Text = "Service Bus Emulator Explorer", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(header); panel.Children.Add(summary); panel.Children.Add(source);
        var buttons = new Grid(); buttons.ColumnDefinitions.Add(new()); buttons.ColumnDefinitions.Add(new());
        var open = new Button { Content = ActionContent("Investigate", "M10,5 A5,5 0 1 1 0,5 A5,5 0 1 1 10,5 M9,9 L14,14"), Style = (Style)FindResource("PrimaryButton"), Height = 38, Padding = new(8, 4, 8, 4), Margin = new(0, 0, 4, 0) };
        System.Windows.Automation.AutomationProperties.SetName(open, "Investigate notification");
        open.Click += (_, _) => investigate();
        var dismissButton = new Button { Content = ActionContent("Dismiss", "M2,2 L14,14 M14,2 L2,14"), Height = 38, Padding = new(8, 4, 8, 4), Margin = new(4, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(dismissButton, "Dismiss notification");
        dismissButton.Click += (_, _) => dismiss(); Grid.SetColumn(dismissButton, 1);
        buttons.Children.Add(open); buttons.Children.Add(dismissButton); panel.Children.Add(buttons);
        var persistenceHint = new TextBlock { Text = "Remains until you respond", FontSize = 12, Margin = new(0, 12, 0, 0) };
        persistenceHint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        panel.Children.Add(persistenceHint);
        Content = new Border { Background = (Brush)FindResource("CanvasBrush"), BorderBrush = (Brush)FindResource("ControlBorderBrush"), BorderThickness = new(1), CornerRadius = new(9), Child = panel };
        ((Border)Content).SetResourceReference(Border.BackgroundProperty, "CanvasBrush");
        ((Border)Content).SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        Loaded += (_, _) => Position(); SizeChanged += (_, _) => Position();
    }

    private static StackPanel ActionContent(string label, string geometry)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), Width = 16, Height = 16, Stretch = Stretch.Uniform, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Margin = new(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return content;
    }

    private static Button CreateCloseButton()
    {
        var close = new Button
        {
            Width = 28, Height = 28, MinHeight = 28, VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, BorderThickness = new(0),
            ToolTip = "Dismiss notification", Cursor = System.Windows.Input.Cursors.Hand
        };
        close.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Border x:Name="Surface" Background="Transparent" CornerRadius="4" BorderThickness="1" BorderBrush="Transparent">
                <Path Data="M0,0 L16,16 M16,0 L0,16" Width="16" Height="16" Stroke="{DynamicResource SecondaryBrush}" StrokeThickness="1.5"
                      StrokeStartLineCap="Round" StrokeEndLineCap="Round" HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="{DynamicResource NeutralHoverBrush}"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource PrimaryBrush}"/></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter TargetName="Surface" Property="Background" Value="{DynamicResource SelectionBrush}"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        return close;
    }

    private void Position()
    {
        var desktop = SystemParameters.WorkArea;
        Left = desktop.Right - ActualWidth - 16; Top = desktop.Bottom - ActualHeight - 16;
    }

    public void Update(MessageRow message, int count, int otherGroups, string connectionName)
    {
        summary.Text = $"{count} new {(message.IsDeadLetter ? "dead-letter" : "active")} message{(count == 1 ? "" : "s")}";
        source.Text = $"{connectionName} / {message.Source.Replace("/", " / ")}" + (otherGroups > 0 ? $"\n+ {otherGroups} other watched location{(otherGroups == 1 ? "" : "s")}" : "");
    }
}
