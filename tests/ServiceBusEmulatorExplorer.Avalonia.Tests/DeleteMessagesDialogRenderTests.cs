using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ServiceBusEmulatorExplorer.Avalonia.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Avalonia.Tests;

/// <summary>
/// The spike's question (DEC-058): does the Delete dialog render the same pixels on Windows, Linux and macOS, and how
/// close is it to the WPF baseline of the same state? The same assertions as the WPF dialog test pin its behaviour.
/// </summary>
public sealed class DeleteMessagesDialogRenderTests
{
    [AvaloniaTheory]
    [InlineData(454, 1)]
    [InlineData(384, 125)]
    public void The_dialog_renders_its_initial_and_ready_states(int width, int targets)
    {
        var viewModel = new DeleteMessagesViewModel(SampleDeliveries.DeadLetters(targets));
        var dialog = new DeleteMessagesDialog(viewModel, "#2563EB") { Width = width };
        dialog.Show();
        try
        {
            var input = dialog.FindControl<TextBox>("DeleteConfirmationInput")!;
            var confirm = dialog.FindControl<Button>("ConfirmDeleteButton")!;
            var summary = dialog.FindControl<TextBlock>("DeleteSummary")!;
            Dispatcher.UIThread.RunJobs();
            // Avalonia keeps IsEnabled; a command that cannot execute disables the button through IsEffectivelyEnabled.
            Assert.False(confirm.IsEffectivelyEnabled);
            Assert.Contains(targets.ToString(), summary.Text);
            Assert.DoesNotContain("WWWW", summary.Text);
            Capture(dialog, $"delete-dialog-{width + 16}-cancel-initial");

            input.Text = "DELETE";
            Dispatcher.UIThread.RunJobs();
            Assert.True(confirm.IsEffectivelyEnabled);
            Capture(dialog, $"delete-dialog-{width + 16}-cancel-ready");
        }
        finally { dialog.Close(); }
    }

    private static void Capture(Window dialog, string wpfName)
    {
        using var frame = dialog.CaptureRenderedFrame() ?? throw new InvalidOperationException("The headless platform rendered no frame.");
        string name = "avalonia-" + wpfName;
        PixelBaseline.CompareWithWpf(frame, wpfName, name);
        PixelBaseline.AssertMatches(frame, name);
    }
}
