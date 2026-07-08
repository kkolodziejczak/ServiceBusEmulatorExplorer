using System.Windows;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App;

public partial class ReplayDeadLetterDialog : Window
{
    private readonly ExplorerMessage _message;
    private readonly string _initialBodyText;
    private readonly string _initialApplicationPropertiesText;

    public ReplayMessageEdits? Result { get; private set; }

    public ReplayDeadLetterDialog(ServiceBusEntityNode entity, ExplorerMessage message)
    {
        _message = message;
        InitializeComponent();
        ReplaySummaryText.Text = $"Source: {entity.Metadata.Path} DLQ | Sequence: {message.SequenceNumber}";
        _initialBodyText = message.Body;
        BodyTextBox.Text = _initialBodyText;
        ContentTypeTextBox.Text = message.ContentType ?? "";
        SubjectTextBox.Text = message.Subject ?? "";
        CorrelationIdTextBox.Text = message.CorrelationId ?? "";
        SessionIdTextBox.Text = message.SessionId ?? "";
        _initialApplicationPropertiesText = FormatApplicationProperties(message.ApplicationProperties);
        ApplicationPropertiesTextBox.Text = _initialApplicationPropertiesText;
        ValidateFields();
    }

    public static IReadOnlyDictionary<string, object?> CreateEditedApplicationProperties(
        IReadOnlyDictionary<string, object?> originalProperties,
        string initialText,
        string editedText)
    {
        return string.Equals(initialText, editedText, StringComparison.Ordinal)
            ? originalProperties
            : SendMessageDialog.ParseApplicationProperties(editedText);
    }

    private static string FormatApplicationProperties(IReadOnlyDictionary<string, object?> properties)
    {
        return string.Join(Environment.NewLine, properties.Select(property => $"{property.Key}={property.Value}"));
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
        ReplayButton.IsEnabled = error is null;
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
            SendMessageDialog.ParseApplicationProperties(ApplicationPropertiesTextBox.Text);
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }

        return null;
    }

    private void ReplayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateFields())
        {
            return;
        }

        Result = new ReplayMessageEdits(
            CreateEditedBody(),
            NullIfWhiteSpace(ContentTypeTextBox.Text),
            NullIfWhiteSpace(CorrelationIdTextBox.Text),
            NullIfWhiteSpace(SessionIdTextBox.Text),
            NullIfWhiteSpace(SubjectTextBox.Text),
            CreateApplicationProperties());
        DialogResult = true;
    }

    private string? CreateEditedBody()
    {
        return string.Equals(_initialBodyText, BodyTextBox.Text, StringComparison.Ordinal)
            ? null
            : BodyTextBox.Text;
    }

    private IReadOnlyDictionary<string, object?>? CreateApplicationProperties()
    {
        return CreateEditedApplicationProperties(
            _message.ApplicationProperties,
            _initialApplicationPropertiesText,
            ApplicationPropertiesTextBox.Text);
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
