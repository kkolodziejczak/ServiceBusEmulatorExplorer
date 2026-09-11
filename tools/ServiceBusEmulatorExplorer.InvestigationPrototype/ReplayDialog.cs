using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

// Local sample editor: accepting this dialog does not send or persist anything.
public sealed class ReplayDialog : Window
{
    private readonly TextBox messageId;
    private readonly JsonEditor body;
    private readonly TextBlock error;
    private readonly string originalId;
    private readonly Func<string, string?>? validateId;
    public string EditedBody => body.Text;
    public string NewMessageId => messageId.Text;

    public ReplayDialog(MessageRow source, string destination, Func<string, string?>? validateId = null)
    {
        originalId = source.MessageId;
        this.validateId = validateId;
        Title = "Edit and Replay · Sample data";
        Width = 760;
        Height = 640;
        MinWidth = 560;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        Background = Brushes.WhiteSmoke;
        var layout = new Grid();
        for (var index = 0; index < 6; index++)
            layout.RowDefinitions.Add(new RowDefinition { Height = index == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        var explanation = new TextBlock
        {
            Text = $"Original ID: {source.MessageId}\nReplay destination: {destination}\nA new active copy is simulated. The original remains in DLQ.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
        };
        layout.Children.Add(explanation);
        messageId = new TextBox { Name = "ReplayId", Text = Guid.NewGuid().ToString(), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetAutomationId(messageId, "ReplayId");
        AutomationProperties.SetName(messageId, "New message ID");
        var idPanel = new StackPanel();
        idPanel.Children.Add(new Label { Content = "New message _ID", Target = messageId, Padding = new Thickness(0, 0, 0, 4) });
        idPanel.Children.Add(messageId);
        Grid.SetRow(idPanel, 1);
        layout.Children.Add(idPanel);

        body = new JsonEditor
        {
            Name = "ReplayBody", Text = JsonPresentation.Format(source.Body),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"),
            FontSize = 14, Background = new SolidColorBrush(Color.FromRgb(23, 33, 51)),
            Padding = new Thickness(12), BorderThickness = new Thickness(0)
        };
        AutomationProperties.SetAutomationId(body, "ReplayBody");
        AutomationProperties.SetName(body, "Message body");
        var bodyLabel = new Label { Content = "Message _body", Target = body, Padding = new Thickness(0, 0, 0, 6) };
        Grid.SetRow(bodyLabel, 2);
        layout.Children.Add(bodyLabel);
        Grid.SetRow(body, 3);
        layout.Children.Add(body);
        error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        messageId.TextChanged += (_, _) => error.Text = "";
        Grid.SetRow(error, 4);
        layout.Children.Add(error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Name = "CancelReplay", Content = "Cancel", IsCancel = true, MinWidth = 90, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetAutomationId(cancel, "CancelReplay");
        cancel.Click += (_, _) => DialogResult = false;
        var confirm = new Button { Name = "ConfirmReplay", Content = "Replay", MinWidth = 90, Padding = new Thickness(12, 7, 12, 7) };
        AutomationProperties.SetAutomationId(confirm, "ConfirmReplay");
        confirm.Click += ConfirmReplay;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        Grid.SetRow(buttons, 5);
        layout.Children.Add(buttons);
        Content = new Border { Background = Brushes.WhiteSmoke, Padding = new Thickness(24), Child = layout };
    }

    private void ConfirmReplay(object sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NewMessageId) || NewMessageId == originalId)
        {
            error.Text = "Enter a new message ID different from the original.";
            messageId.Focus();
            return;
        }
        if (validateId?.Invoke(NewMessageId) is { } validationError)
        {
            error.Text = validationError;
            messageId.Focus();
            return;
        }
        DialogResult = true;
    }
}
