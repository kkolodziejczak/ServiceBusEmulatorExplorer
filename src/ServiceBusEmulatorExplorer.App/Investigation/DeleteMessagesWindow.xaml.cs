using System.Windows;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class DeleteMessagesWindow : Window
{
    public IReadOnlyList<MessageDelivery> Targets { get; }

    public DeleteMessagesWindow(IReadOnlyList<MessageDelivery> targets, string colorHex)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0 || targets.Any(target => target.Identity.Bucket != MessageBucket.DeadLetter)
            || targets.Select(target => target.Identity).Distinct().Count() != targets.Count)
            throw new ArgumentException("Select distinct dead-letter deliveries before confirming deletion.", nameof(targets));
        Targets = Array.AsReadOnly(targets.ToArray());
        InitializeComponent();
        ProfileTheme.Apply(this, colorHex);
        string countLabel = $"{Targets.Count} message{(Targets.Count == 1 ? "" : "s")}";
        DeleteHeading.Text = $"Delete {countLabel}?";
        DeleteSummary.Text = $"Selected: {Targets.Count} dead-letter. Only these selected deliveries will be removed, including any unsaved edits to them.";
        DeleteButtonLabel.Text = $"Delete {countLabel}";
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Confirmation_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (ConfirmDeleteButton is not null) ConfirmDeleteButton.IsEnabled = DeleteConfirmationInput.Text == "DELETE";
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (DeleteConfirmationInput.Text == "DELETE") DialogResult = true;
    }
}
