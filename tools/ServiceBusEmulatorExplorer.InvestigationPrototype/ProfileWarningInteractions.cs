using System.Windows;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class PrototypeWindow
{
    private string? acknowledgedProfileWarning;

    private static string WarningIdentity(PrototypeConnectionProfile profile) => profile.Id + "\n" + profile.WarningMessage;

    private bool ConfirmProfileWarning(PrototypeConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.WarningMessage)) return true;
        var dialog = new ProfileWarningWindow(profile.Name, profile.WarningMessage, profile.ColorHex) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    private bool ConfirmConnection()
    {
        var profile = connectionSettings.SelectedProfile;
        if (acknowledgedProfileWarning == WarningIdentity(profile))
        {
            acknowledgedProfileWarning = null;
            return true;
        }
        return ConfirmProfileWarning(profile);
    }
}
