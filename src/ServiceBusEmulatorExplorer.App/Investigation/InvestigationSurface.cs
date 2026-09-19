using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>The visible investigation list; browsing remains intact while a search is displayed.</summary>
public sealed class InvestigationSurface : ObservableObject, IDisposable
{
    private readonly MessageBrowseWorkflow browse;
    private readonly MessageSearchWorkflow search;

    public InvestigationSurface(MessageBrowseWorkflow browse, MessageSearchWorkflow search)
    {
        this.browse = browse;
        this.search = search;
        browse.PropertyChanged += SourceChanged;
        search.PropertyChanged += SourceChanged;
    }

    public bool IsSearch => search.IsActive;
    public ObservableCollection<EntityNode> Roots => IsSearch ? search.Roots : browse.Roots;
    public ObservableCollection<MessageRow> Messages => IsSearch ? search.Messages : browse.Messages;
    public MessageRow? FocusedMessage
    {
        get => IsSearch ? search.FocusedMessage : browse.FocusedMessage;
        set { if (IsSearch) search.FocusedMessage = value; else browse.FocusedMessage = value; }
    }
    public bool IsConnected => browse.IsConnected;
    public bool IsBusy => IsSearch ? search.IsBusy : browse.IsBusy;
    public bool IsDeadLetter => !IsSearch && browse.IsDeadLetter;
    public bool CanLoadMore => IsSearch ? search.CanContinue : browse.CanLoadMore;
    public bool ShowsSource => IsSearch || browse.ShowsSource;
    public int SelectedCount => IsSearch ? search.SelectedCount : browse.SelectedCount;
    public string CountSummary => IsSearch ? search.CountSummary : browse.CountSummary;
    public string EntityPath => IsSearch ? "Search / Related messages" : browse.EntityPath;

    public void SetAllChecked(bool selected)
    {
        if (IsSearch) search.SetAllChecked(selected);
        else browse.SetAllChecked(selected);
    }

    private void SourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, search) || !IsSearch)
            OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        browse.PropertyChanged -= SourceChanged;
        search.PropertyChanged -= SourceChanged;
    }
}
