using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Settings;

/// <summary>
/// One connection profile as edited in Settings. It changes only when the editor leaves it or saves it, so its row in
/// the list keeps the last captured name and colour while the user types. <see cref="IsModified"/> says whether it
/// differs from what is saved (Settings 70).
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
    private bool isModified;

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

    /// <summary>Whether this profile has edits that are not saved, including a new profile never saved.</summary>
    public bool IsModified
    {
        get => isModified;
        internal set => SetProperty(ref isModified, value);
    }

    public static ProfileDraft From(InvestigationProfile profile) =>
        new(profile.Id, profile.Connection.Name, profile.Connection)
        {
            colorHex = profile.ColorHex,
            warningMessage = profile.WarningMessage
        };

    public InvestigationProfile ToProfile() =>
        new(Id, new ConnectionProfile(Name, RuntimeConnectionString, AdministrationConnectionString, AuthenticationMode,
            FullyQualifiedNamespace), ColorHex, WarningMessage);

    public void Apply(InvestigationProfile profile)
    {
        SetProperty(ref name, profile.Connection.Name, nameof(Name));
        SetProperty(ref runtimeConnectionString, profile.Connection.RuntimeConnectionString, nameof(RuntimeConnectionString));
        SetProperty(ref administrationConnectionString, profile.Connection.AdministrationConnectionString, nameof(AdministrationConnectionString));
        SetProperty(ref authenticationMode, profile.Connection.AuthenticationMode, nameof(AuthenticationMode));
        SetProperty(ref fullyQualifiedNamespace, profile.Connection.FullyQualifiedNamespace, nameof(FullyQualifiedNamespace));
        SetProperty(ref colorHex, profile.ColorHex, nameof(ColorHex));
        SetProperty(ref warningMessage, profile.WarningMessage, nameof(WarningMessage));
    }
}
