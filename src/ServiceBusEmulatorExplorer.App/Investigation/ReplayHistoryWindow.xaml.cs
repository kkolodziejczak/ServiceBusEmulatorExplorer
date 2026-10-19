using System.Windows;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>
/// The WPF view of <see cref="ReplayHistoryViewModel"/>; it only applies the profile theme, brings workspace changes
/// onto the UI thread and asks the remove question.
/// </summary>
public partial class ReplayHistoryWindow : Window
{
    private readonly ReplayHistoryViewModel viewModel;

    public ReplayHistoryWindow(InvestigationWorkspace workspace, Func<IReadOnlyList<string>, Task> find,
        Func<ReplayAttempt, Task> findOriginal, Func<ReplayAttempt, Task> deleteOriginal, Func<Task> checkStatus,
        Action<string>? copyText = null)
    {
        viewModel = new ReplayHistoryViewModel(workspace, find, findOriginal, deleteOriginal, checkStatus,
            copyText ?? Clipboard.SetText, OnUiThread) { ConfirmRemove = message => ConfirmRemove(workspace.SelectedProfile, message) };
        InitializeComponent();
        // Bind only once the grid has its columns: a selection bound earlier selects the row but none of its cells,
        // so the selected row would lose its highlight.
        DataContext = viewModel;
        ProfileTheme.Apply(this, viewModel.ProfileColorHex);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReplayHistoryViewModel.ProfileColorHex)) ProfileTheme.Apply(this, viewModel.ProfileColorHex);
        };
        Closed += (_, _) => viewModel.Dispose();
    }

    private void OnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.Invoke(action);
    }

    private bool ConfirmRemove(InvestigationProfile profile, string question) =>
        new ProfileWarningWindow(profile.Connection.Name, question, profile.ColorHex, "Remove from history", "Remove from history")
        { Owner = this }.ShowDialog() == true;
}
