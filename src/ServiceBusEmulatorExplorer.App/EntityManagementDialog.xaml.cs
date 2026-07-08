using System.Windows;
using System.Windows.Controls;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App;

public enum EntityDialogMode
{
    CreateQueue,
    CreateTopic,
    CreateSubscription,
    UpdateQueue,
    UpdateTopic,
    UpdateSubscription
}

public sealed record EntityManagementDialogResult(
    string Name,
    string TopicName,
    TimeSpan? LockDuration,
    int? MaxDeliveryCount,
    TimeSpan? DefaultMessageTimeToLive,
    bool RequiresSession,
    bool RequiresDuplicateDetection);

public partial class EntityManagementDialog : Window
{
    private readonly EntityDialogMode _mode;

    public EntityManagementDialogResult? Result { get; private set; }

    public EntityManagementDialog(EntityDialogMode mode, ServiceBusEntityNode? entity)
    {
        _mode = mode;
        InitializeComponent();
        ConfigureMode(entity);
        ValidateFields();
    }

    private void ConfigureMode(ServiceBusEntityNode? entity)
    {
        Title = GetTitle();
        DialogTitleText.Text = GetTitle();
        DialogSubtitleText.Text = IsCreateMode()
            ? "Create a Service Bus entity in the connected namespace."
            : "Update supported metadata for the selected entity.";
        CreateOrUpdateButton.Content = IsCreateMode() ? "Create" : "Update";

        TopicNamePanel.Visibility = UsesTopicName() ? Visibility.Visible : Visibility.Collapsed;
        LockDurationPanel.Visibility = SupportsLockDuration() ? Visibility.Visible : Visibility.Collapsed;
        MaxDeliveryPanel.Visibility = SupportsLockDuration() ? Visibility.Visible : Visibility.Collapsed;
        RequiresSessionCheckBox.Visibility = SupportsRequiresSession() ? Visibility.Visible : Visibility.Collapsed;
        RequiresDuplicateDetectionCheckBox.Visibility = SupportsRequiresDuplicateDetection() ? Visibility.Visible : Visibility.Collapsed;
        NameLabelText.Text = UsesTopicName() ? "Subscription name" : "Name";

        if (entity is not null)
        {
            TopicNameTextBox.Text = entity.TopicName ?? "";
            NameTextBox.Text = entity.Name;
            LockDurationSecondsTextBox.Text = FormatSeconds(entity.Metadata.LockDuration);
            MaxDeliveryCountTextBox.Text = entity.Metadata.MaxDeliveryCount?.ToString() ?? "";
            DefaultTtlDaysTextBox.Text = FormatDays(entity.Metadata.DefaultMessageTimeToLive);
            RequiresSessionCheckBox.IsChecked = entity.Metadata.RequiresSession == true;
            RequiresDuplicateDetectionCheckBox.IsChecked = entity.Metadata.RequiresDuplicateDetection == true;
        }

        TopicNameTextBox.IsEnabled = IsCreateMode();
        NameTextBox.IsEnabled = IsCreateMode();
        RequiresSessionCheckBox.IsEnabled = IsCreateMode();
        RequiresDuplicateDetectionCheckBox.IsEnabled = IsCreateMode();
    }

    private string GetTitle()
    {
        return _mode switch
        {
            EntityDialogMode.CreateQueue => "New Queue",
            EntityDialogMode.CreateTopic => "New Topic",
            EntityDialogMode.CreateSubscription => "New Subscription",
            EntityDialogMode.UpdateQueue => "Update Queue",
            EntityDialogMode.UpdateTopic => "Update Topic",
            EntityDialogMode.UpdateSubscription => "Update Subscription",
            _ => "Manage Entity"
        };
    }

    private bool IsCreateMode()
    {
        return _mode is EntityDialogMode.CreateQueue
            or EntityDialogMode.CreateTopic
            or EntityDialogMode.CreateSubscription;
    }

    private bool UsesTopicName()
    {
        return _mode is EntityDialogMode.CreateSubscription or EntityDialogMode.UpdateSubscription;
    }

    private bool SupportsLockDuration()
    {
        return _mode is EntityDialogMode.CreateQueue
            or EntityDialogMode.UpdateQueue
            or EntityDialogMode.CreateSubscription
            or EntityDialogMode.UpdateSubscription;
    }

    private bool SupportsRequiresSession()
    {
        return _mode is EntityDialogMode.CreateQueue or EntityDialogMode.CreateSubscription;
    }

    private bool SupportsRequiresDuplicateDetection()
    {
        return _mode is EntityDialogMode.CreateQueue or EntityDialogMode.CreateTopic;
    }

    private static string FormatSeconds(TimeSpan? value)
    {
        return value is null ? "" : value.Value.TotalSeconds.ToString("0.###");
    }

    private static string FormatDays(TimeSpan? value)
    {
        return value is null ? "" : value.Value.TotalDays.ToString("0.###");
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
        CreateOrUpdateButton.IsEnabled = error is null;
        return error is null;
    }

    private string? GetValidationError()
    {
        if (UsesTopicName() && string.IsNullOrWhiteSpace(TopicNameTextBox.Text))
        {
            return "Topic name is required.";
        }

        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            return UsesTopicName() ? "Subscription name is required." : "Name is required.";
        }

        if (!TryParsePositiveSeconds(LockDurationSecondsTextBox.Text, out _, out string? lockError))
        {
            return lockError;
        }

        if (!TryParsePositiveInteger(MaxDeliveryCountTextBox.Text, out _, out string? maxDeliveryError))
        {
            return maxDeliveryError;
        }

        if (!TryParsePositiveDays(DefaultTtlDaysTextBox.Text, out _, out string? ttlError))
        {
            return ttlError;
        }

        return null;
    }

    private static bool TryParsePositiveSeconds(string text, out TimeSpan? value, out string? error)
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!double.TryParse(text, out double seconds) || seconds <= 0)
        {
            error = "Lock duration seconds must be greater than zero.";
            return false;
        }

        value = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static bool TryParsePositiveInteger(string text, out int? value, out string? error)
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text, out int number) || number <= 0)
        {
            error = "Max delivery count must be greater than zero.";
            return false;
        }

        value = number;
        return true;
    }

    private static bool TryParsePositiveDays(string text, out TimeSpan? value, out string? error)
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!double.TryParse(text, out double days) || days <= 0)
        {
            error = "Default message TTL days must be greater than zero.";
            return false;
        }

        value = TimeSpan.FromDays(days);
        return true;
    }

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateFields())
        {
            return;
        }

        TryParsePositiveSeconds(LockDurationSecondsTextBox.Text, out TimeSpan? lockDuration, out _);
        TryParsePositiveInteger(MaxDeliveryCountTextBox.Text, out int? maxDeliveryCount, out _);
        TryParsePositiveDays(DefaultTtlDaysTextBox.Text, out TimeSpan? defaultTtl, out _);

        Result = new EntityManagementDialogResult(
            NameTextBox.Text.Trim(),
            UsesTopicName() ? TopicNameTextBox.Text.Trim() : "",
            SupportsLockDuration() ? lockDuration : null,
            SupportsLockDuration() ? maxDeliveryCount : null,
            defaultTtl,
            RequiresSessionCheckBox.IsChecked == true,
            RequiresDuplicateDetectionCheckBox.IsChecked == true);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
