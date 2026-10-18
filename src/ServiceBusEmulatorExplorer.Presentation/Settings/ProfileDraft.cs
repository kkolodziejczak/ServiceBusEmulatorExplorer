using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Settings;

/// <summary>
/// One connection profile as edited in Settings. It changes only when the editor leaves it or saves it, so its row in
/// the list keeps the last captured name and colour while the user types.
/// </summary>
public sealed class ProfileDraft : ObservableObject
{
    public const string DefaultColorHex = "#0069FA";

    private string name;
    private string runtimeConnectionString;
    private string administrationConnectionString;
    private ConnectionAuthenticationMode authenticationMode;
    private string fullyQualifiedNamespace;
    private string colorHex = DefaultColorHex;
    private string warningMessage = string.Empty;

    public ProfileDraft(string id, string name, ConnectionProfile connection)
    {
        Id = id;
        this.name = name;
        runtimeConnectionString = connection.RuntimeConnectionString;
        administrationConnectionString = connection.AdministrationConnectionString;
        authenticationMode = connection.AuthenticationMode;
        fullyQualifiedNamespace = connection.FullyQualifiedNamespace;
    }

    public string Id { get; }

    public string Name => name;

    public string RuntimeConnectionString => runtimeConnectionString;

    public string AdministrationConnectionString => administrationConnectionString;

    public ConnectionAuthenticationMode AuthenticationMode => authenticationMode;

    public string FullyQualifiedNamespace => fullyQualifiedNamespace;

    public string ColorHex => colorHex;

    public string WarningMessage => warningMessage;

    public static ProfileDraft From(InvestigationProfile profile) =>
        new(profile.Id, profile.Connection.Name, profile.Connection)
        {
            colorHex = profile.ColorHex,
            warningMessage = profile.WarningMessage
        };

    public void Apply(InvestigationProfile profile) => ApplyDraft(profile.Connection, profile.ColorHex, profile.WarningMessage);

    public void ApplyDraft(ConnectionProfile connection, string colorHex, string warningMessage)
    {
        SetProperty(ref name, connection.Name, nameof(Name));
        SetProperty(ref runtimeConnectionString, connection.RuntimeConnectionString, nameof(RuntimeConnectionString));
        SetProperty(ref administrationConnectionString, connection.AdministrationConnectionString, nameof(AdministrationConnectionString));
        SetProperty(ref authenticationMode, connection.AuthenticationMode, nameof(AuthenticationMode));
        SetProperty(ref fullyQualifiedNamespace, connection.FullyQualifiedNamespace, nameof(FullyQualifiedNamespace));
        SetProperty(ref this.colorHex, colorHex, nameof(ColorHex));
        SetProperty(ref this.warningMessage, warningMessage, nameof(WarningMessage));
    }
}
