using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>The WPF view of <see cref="ReplayReviewViewModel"/>; it only applies the theme, focus and dialog result.</summary>
public partial class ReplayReviewWindow : Window
{
    private readonly ReplayReviewViewModel viewModel;

    public ReplayReviewWindow(InvestigationProfile profile, IReadOnlyList<PreparedReplay> candidates)
    {
        viewModel = new ReplayReviewViewModel(profile.Connection.Name, candidates);
        DataContext = viewModel;
        InitializeComponent();
        ProfileTheme.Apply(this, profile.ColorHex);
        viewModel.Completed += accepted => DialogResult = accepted;
        viewModel.IdFocusRequested += FocusId;
        Loaded += (_, _) => CancelButton.Focus();
    }

    public IReadOnlyList<ReplayReviewRow> Rows => viewModel.Rows;

    public IReadOnlyList<string> ReviewedIds => viewModel.ReviewedIds;

    private void FocusId(ReplayReviewRow row) => FindIdBox(ReplayRows.ItemContainerGenerator.ContainerFromItem(row))?.Focus();

    private static TextBox? FindIdBox(DependencyObject? element)
    {
        if (element is null) return null;
        if (element is TextBox box && box.Name == "MessageIdBox") return box;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindIdBox(VisualTreeHelper.GetChild(element, i)) is { } match) return match;
        return null;
    }
}
