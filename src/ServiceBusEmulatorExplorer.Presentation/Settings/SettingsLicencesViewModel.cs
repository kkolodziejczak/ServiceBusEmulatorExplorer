using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ServiceBusEmulatorExplorer.Presentation.Settings;

/// <summary>A bundled component and its licence; the app loads the notice text from its own resources (DEC-052).</summary>
public interface ILicensedComponent
{
    string Name { get; }

    string Licence { get; }

    /// <summary>What the component covers, then its full licence text.</summary>
    string Notice { get; }
}

/// <summary>The Licences tab of Settings: pick a bundled component, read its notice, copy it exactly (DEC-052, DEC-053).</summary>
public sealed class SettingsLicencesViewModel : ObservableObject
{
    private readonly Action<string> copyText;
    private ILicensedComponent? selectedComponent;
    private string noticeText = string.Empty;
    private string status = string.Empty;

    public SettingsLicencesViewModel(IReadOnlyList<ILicensedComponent> components, Action<string> copyText)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(copyText);
        Components = components;
        this.copyText = copyText;
        CopyCommand = new RelayCommand(Copy);
        SelectedComponent = components.FirstOrDefault();
    }

    /// <summary>Raised after a new notice is shown, so the view can scroll back to its start.</summary>
    public event Action? NoticeShown;

    public IReadOnlyList<ILicensedComponent> Components { get; }

    public ILicensedComponent? SelectedComponent
    {
        get => selectedComponent;
        set
        {
            if (!SetProperty(ref selectedComponent, value)) return;
            Status = string.Empty;
            NoticeText = value?.Notice ?? string.Empty;
            NoticeShown?.Invoke();
        }
    }

    public string NoticeText
    {
        get => noticeText;
        private set => SetProperty(ref noticeText, value);
    }

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public RelayCommand CopyCommand { get; }

    private void Copy()
    {
        if (SelectedComponent is not { } component) return;
        try
        {
            copyText(NoticeText);
            Status = $"{component.Name} licence copied.";
        }
        catch (ExternalException)
        {
            Status = ConnectionProfilesViewModel.ClipboardUnavailable;
        }
    }
}
