using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ServiceBusEmulatorExplorer.Avalonia.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Avalonia.Tests;

/// <summary>
/// The spike's question (DEC-058): does the Delete dialog render the same on Windows, Linux and macOS, and how
/// close is it to the WPF baseline of the same state? DeleteMessagesDialogBehaviourTests covers its behaviour.
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

    // The text tolerance absorbs each OS's glyph anti-aliasing, not a moved text: the heading 1 px off must fail.
    [AvaloniaTheory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void Text_moved_by_one_pixel_fails_the_comparison(double x, double y)
    {
        var dialog = new DeleteMessagesDialog(new DeleteMessagesViewModel(SampleDeliveries.DeadLetters(1)), "#2563EB") { Width = 454 };
        dialog.Show();
        try
        {
            dialog.FindControl<TextBlock>("DeleteHeading")!.RenderTransform = new TranslateTransform(x, y);
            Dispatcher.UIThread.RunJobs();
            using var frame = dialog.CaptureRenderedFrame()!;
            string path = Path.Combine(Path.GetTempPath(), $"sbe-moved-heading-{Guid.NewGuid():N}.png");
            frame.Save(path, PngBitmapEncoderOptions.Default);
            try
            {
                var (moved, size) = PixelBaseline.Decode(path);
                var (baseline, _) = PixelBaseline.Decode(PixelBaseline.BaselinePath("avalonia-delete-dialog-470-cancel-initial"));
                var (outsideText, textDelta) = PixelBaseline.Compare(baseline, moved, PixelBaseline.TextMask(dialog, size), size);
                Assert.Equal(0, outsideText);
                Assert.True(textDelta > PixelBaseline.TextTolerance, $"A 1 px move changed text by only {textDelta}.");
            }
            finally { File.Delete(path); }
        }
        finally { dialog.Close(); }
    }

    private static void Capture(Window dialog, string wpfName)
    {
        using var frame = dialog.CaptureRenderedFrame() ?? throw new InvalidOperationException("The headless platform rendered no frame.");
        string name = "avalonia-" + wpfName;
        PixelBaseline.CompareWithWpf(frame, wpfName, name);
        PixelBaseline.AssertMatches(frame, dialog, name);
    }
}
