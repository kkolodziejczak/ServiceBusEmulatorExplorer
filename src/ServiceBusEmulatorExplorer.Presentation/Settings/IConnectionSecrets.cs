namespace ServiceBusEmulatorExplorer.Presentation.Settings;

/// <summary>
/// The connection strings typed into the window, read only when a draft is captured, saved or copied, so the view model
/// never keeps a copy of what the user is typing (DEC-053).
/// </summary>
public interface IConnectionSecrets
{
    string Runtime { get; }

    string Administration { get; }
}
