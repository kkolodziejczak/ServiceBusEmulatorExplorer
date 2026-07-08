using System.Windows;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App;

public partial class DeleteDeadLetterMessagesConfirmationDialog : Window
{
    private const string VisibleDeleteConfirmationPhrase = "DELETE VISIBLE";
    private readonly bool _visiblePage;

    public DeleteDeadLetterMessagesConfirmationDialog(
        ServiceBusEntityNode entity,
        IReadOnlyList<ExplorerMessage> messages,
        bool visiblePage)
    {
        _visiblePage = visiblePage;
        InitializeComponent();
        TitleText.Text = visiblePage ? "Delete Visible DLQ Messages" : "Delete Selected DLQ Message";
        MessageSummaryText.Text = $"{entity.Metadata.Path} DLQ | {messages.Count} message(s)";
        WarningText.Text = visiblePage
            ? "This deletes every DLQ message currently visible in the grid."
            : "This deletes only the selected DLQ message.";
        ConfirmDeleteCheckBox.Visibility = visiblePage ? Visibility.Collapsed : Visibility.Visible;
        ConfirmPhrasePanel.Visibility = visiblePage ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConfirmDeleteCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateDeleteButtonState();
    }

    private void ConfirmPhraseTextBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateDeleteButtonState();
    }

    private void UpdateDeleteButtonState()
    {
        DeleteButton.IsEnabled = _visiblePage
            ? string.Equals(ConfirmPhraseTextBox.Text, VisibleDeleteConfirmationPhrase, StringComparison.Ordinal)
            : ConfirmDeleteCheckBox.IsChecked == true;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
