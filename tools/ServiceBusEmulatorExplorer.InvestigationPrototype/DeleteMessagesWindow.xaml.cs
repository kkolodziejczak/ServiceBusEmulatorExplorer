using System.Windows;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class DeleteMessagesWindow : Window
{
    private readonly bool hasTargets;
    public DeleteMessagesWindow(IReadOnlyList<MessageRow> targets)
    {
        InitializeComponent();
        var count = targets.Count;
        hasTargets = count > 0;
        var deadLetters = targets.Count(message => message.IsDeadLetter);
        DeleteHeading.Text = $"Delete {count} message{(count == 1 ? "" : "s")}?";
        DeleteSummary.Text = $"Selected: {count - deadLetters} active · {deadLetters} dead-letter. Only these selected messages will be removed.";
        DeleteButtonLabel.Text = $"Delete {count} message{(count == 1 ? "" : "s")}";
        ConfirmDeleteButton.IsEnabled = false;
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Confirmation_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (ConfirmDeleteButton is not null) ConfirmDeleteButton.IsEnabled = hasTargets && DeleteConfirmationInput.Text == "DELETE";
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (hasTargets && DeleteConfirmationInput.Text == "DELETE") DialogResult = true;
    }
}
