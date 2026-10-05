namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed partial class EntityNode
{
    private bool activeWatched;
    private bool dlqWatched;
    private bool watchConnected;
    private string refreshIndicator = "None";
    private string refreshDetail = "";

    public bool IsActiveWatched => activeWatched;
    public bool IsDlqWatched => dlqWatched;
    public string RefreshIndicator => refreshIndicator;
    public string RefreshDetail => refreshDetail;

    internal void SetIndicators(bool active, bool deadLetter, bool connected, string refresh, string detail)
    {
        bool connectionChanged = watchConnected != connected;
        watchConnected = connected;
        bool activeChanged = SetProperty(ref activeWatched, active, nameof(IsActiveWatched));
        bool dlqChanged = SetProperty(ref dlqWatched, deadLetter, nameof(IsDlqWatched));
        if (activeChanged || connectionChanged) OnPropertyChanged(nameof(ActiveCountDetail));
        if (dlqChanged || connectionChanged) OnPropertyChanged(nameof(DlqCountDetail));
        SetProperty(ref refreshIndicator, refresh, nameof(RefreshIndicator));
        SetProperty(ref refreshDetail, detail, nameof(RefreshDetail));
    }

    private string WatchDetail(bool watched, string bucket) => !watched ? "" :
        $"\n\nWatch enabled: {bucket}." + (watchConnected ? "" : "\nDisconnected; Watch resumes after reconnecting.");
}
