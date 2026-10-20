using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using ServiceBusEmulatorExplorer.App.Tests.VisualBaselines;

namespace ServiceBusEmulatorExplorer.App.Tests;

/// <summary>
/// The Message Workbench screens, rendered by the screenshot harness with its scripted states, match their baselines
/// pixel for pixel (DEC-050, DEC-057). The harness fixes the Workbench's clock and generated IDs, so every run renders
/// the same pixels.
/// </summary>
public sealed class MessageWorkbenchVisualBaselineTests
{
    public static TheoryData<string> Screens => new()
    {
        "--message-library-prepare", "--message-library-prepare-1500", "--message-library-prepare-compact",
        "--message-library-prepare-minimum", "--message-library-prepare-log",
        "--message-library-properties", "--message-library-properties-bottom", "--message-library-properties-selected",
        "--message-library-properties-minimum", "--message-library-variables", "--message-library-author-minimum",
        "--message-library-single", "--message-library-single-minimum",
        "--message-library-wizard-review", "--message-library-wizard-review-minimum", "--message-library-wizard-review-queue",
        "--message-library-wizard-review-large-batch",
        "--message-library-map", "--message-library-review", "--message-library-results",
        "--message-library-schedule", "--message-library-schedule-past-date", "--message-library-schedule-past-time",
        "--message-library-scheduled-results", "--message-library-cancellation-history",
        "--organization", "--responsive-compose"
    };

    [Theory]
    [MemberData(nameof(Screens))]
    [Trait("TestCategory", "UiRender")]
    public void Workbench_screen_matches_its_baseline(string screen)
    {
        string output = Path.Combine(Path.GetTempPath(), "sbe-workbench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            string target = Path.Combine(output, "screen.png");
            RunHarness(target, screen);
            string name = "workbench-" + screen.TrimStart('-');
            if (Directory.Exists(target))
            {
                string[] images = Directory.GetFiles(target, "*.png", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
                Assert.NotEmpty(images);
                foreach (string image in images)
                {
                    string relative = Path.ChangeExtension(Path.GetRelativePath(target, image), null)
                        .Replace(Path.DirectorySeparatorChar, '-');
                    VisualBaseline.AssertPngMatches(name + "-" + relative, File.ReadAllBytes(image));
                }
            }
            else
            {
                VisualBaseline.AssertPngMatches(name, File.ReadAllBytes(target));
            }
        }
        finally { Directory.Delete(output, recursive: true); }
    }

    private static void RunHarness(string target, string screen)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(HarnessPath());
        start.ArgumentList.Add(target);
        start.ArgumentList.Add(screen);
        using var harness = Process.Start(start)!;
        Task<string> output = harness.StandardOutput.ReadToEndAsync();
        Task<string> error = harness.StandardError.ReadToEndAsync();
        if (!harness.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            harness.Kill(entireProcessTree: true);
            Assert.Fail($"The screenshot harness did not finish {screen} within 120 seconds.");
        }
        Assert.True(harness.ExitCode == 0 && (File.Exists(target) || Directory.Exists(target)),
            $"The screenshot harness failed for {screen} with exit code {harness.ExitCode}: {error.Result}{output.Result}");
    }

    /// <summary>The harness built next to these tests, in the same configuration.</summary>
    private static string HarnessPath([CallerFilePath] string sourcePath = "")
    {
        string repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
        string framework = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Name;
        string configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
        string harness = Path.Combine(repository, "tools", "ServiceBusEmulatorExplorer.ReadmeScreenshot", "bin", configuration, framework,
            "ServiceBusEmulatorExplorer.ReadmeScreenshot.dll");
        Assert.True(File.Exists(harness), $"Build the screenshot harness first; it was not found at {harness}.");
        return harness;
    }
}
