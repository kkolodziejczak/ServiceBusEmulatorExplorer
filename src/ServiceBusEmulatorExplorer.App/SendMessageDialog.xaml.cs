using System.Windows;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App;

public partial class SendMessageDialog : Window
{
    private readonly EntityAddress _destination;

    public SendMessageCommand? Result { get; private set; }

    public SendMessageDialog(ServiceBusEntityNode entity)
    {
        _destination = CreateDestination(entity);
        InitializeComponent();
        DialogTitleText.Text = "Send Message";
        DestinationText.Text = $"Destination: {entity.Kind} {entity.Metadata.Path}";
        ValidateFields();
    }

    public static IReadOnlyDictionary<string, object?> ParseApplicationProperties(string text)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (string line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            int separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                throw new FormatException("Application properties must use key=value lines.");
            }

            string key = trimmed[..separatorIndex].Trim();
            string value = trimmed[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new FormatException("Application property keys are required.");
            }

            properties[key] = value;
        }

        return properties;
    }

    private static EntityAddress CreateDestination(ServiceBusEntityNode entity)
    {
        return entity.Kind switch
        {
            EntityKind.Queue => new EntityAddress(EntityKind.Queue, entity.Name),
            EntityKind.Topic => new EntityAddress(EntityKind.Topic, entity.Name),
            _ => throw new ArgumentException("Messages can be sent to queues and topics, not subscriptions.", nameof(entity))
        };
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
        {
            ValidateFields();
        }
    }

    private bool ValidateFields()
    {
        string? error = GetValidationError();
        ValidationMessageText.Text = error ?? "";
        SendButton.IsEnabled = error is null;
        return error is null;
    }

    private string? GetValidationError()
    {
        if (string.IsNullOrWhiteSpace(BodyTextBox.Text))
        {
            return "Message body is required.";
        }

        try
        {
            ParseApplicationProperties(ApplicationPropertiesTextBox.Text);
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }

        return null;
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateFields())
        {
            return;
        }

        Result = new SendMessageCommand(
            _destination,
            BodyTextBox.Text,
            NullIfWhiteSpace(ContentTypeTextBox.Text),
            NullIfWhiteSpace(CorrelationIdTextBox.Text),
            NullIfWhiteSpace(SessionIdTextBox.Text),
            NullIfWhiteSpace(SubjectTextBox.Text),
            ParseApplicationProperties(ApplicationPropertiesTextBox.Text));
        DialogResult = true;
    }

    private static string? NullIfWhiteSpace(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
