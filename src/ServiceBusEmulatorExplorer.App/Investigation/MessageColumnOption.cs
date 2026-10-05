using CommunityToolkit.Mvvm.ComponentModel;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed class MessageColumnOption(MessageColumnDefinition definition) : ObservableObject
{
    public string Id => definition.Id;
    public string Label => definition.Label;
    public string Section => IsSelected ? "Visible columns" : definition.Section;
    private bool selected;
    private bool canToggle = true;
    public bool IsSelected { get => selected; set { if (SetProperty(ref selected, value)) OnPropertyChanged(nameof(Section)); } }
    public bool CanToggle { get => canToggle; set => SetProperty(ref canToggle, value); }
    private bool canMoveUp;
    private bool canMoveDown;
    public bool CanMoveUp { get => canMoveUp; set => SetProperty(ref canMoveUp, value); }
    public bool CanMoveDown { get => canMoveDown; set => SetProperty(ref canMoveDown, value); }
}
