using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class CheckboxSelectionProof
{
    public static async Task<int> RunAsync(PrototypeWindow window, string output)
    {
        Directory.CreateDirectory(output);
        var report = new List<string> { "# Checkbox selection regression", "" };
        try
        {
            await Exercise(window, report);
            ProofCapture.Save(window, output, "selection");
            File.WriteAllLines(Path.Combine(output, "selection-report.md"), report);
            return 0;
        }
        catch (Exception exception)
        {
            report.Add("FAIL: " + exception.Message);
            ProofCapture.Save(window, output, "selection-failure");
            File.WriteAllLines(Path.Combine(output, "selection-report.md"), report);
            return 1;
        }
    }

    public static async Task Exercise(PrototypeWindow window, List<string> report)
    {
        await Settle();
        var workspace = window.Workspace;
        var grid = ProofCapture.Control<DataGrid>(window, "MessageGrid");
        var header = ProofCapture.Control<CheckBox>(window, "SelectAllBox");
        var original = workspace.Messages.Where(row => row.IsSelected).ToArray();
        var originalFocus = workspace.FocusedMessage;
        grid.SelectedItem = workspace.Messages[4];
        await Settle();
        var errors = new List<string>();
        if (original.Any(row => !row.IsSelected)) errors.Add("Previewing another row cleared existing checkbox selections.");
        var rowCheck = RowCheck(grid, workspace.Messages[0]);
        var headerX = header.TranslatePoint(new Point(header.ActualWidth / 2, 0), grid).X;
        var rowX = rowCheck.TranslatePoint(new Point(rowCheck.ActualWidth / 2, 0), grid).X;
        if (Math.Abs(headerX - rowX) > 1) errors.Add($"Header checkbox center differs from row checkbox center by {Math.Abs(headerX - rowX):F1} pixels.");
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        report.Add("PASS: Preview changes preserve checked messages; header and row checkbox centers align within 1 pixel.");

        ClearAll(header, workspace);
        grid.SelectedItem = workspace.Messages[0];
        Click(RowCheck(grid, workspace.Messages[0]));
        grid.SelectedItem = workspace.Messages[1];
        Click(RowCheck(grid, workspace.Messages[1]));
        await Settle();
        Require(workspace.SelectedCount == 2 && workspace.Messages[0].IsSelected && workspace.Messages[1].IsSelected,
            "Two ordinary checkbox clicks keep both messages checked without modifiers.", report);
        grid.SelectedItem = workspace.Messages[3];
        Require(workspace.SelectedCount == 2, "Row preview does not alter the checked set.", report);

        int notifications = 0;
        void CountNotifications(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => notifications++;
        workspace.PropertyChanged += CountNotifications;
        Click(header);
        workspace.PropertyChanged -= CountNotifications;
        Require(workspace.SelectedCount == workspace.Messages.Count && header.IsChecked == true,
            "Header checkbox selects all loaded messages from a partial selection.", report);
        Require(notifications <= 4, "Select all batches selection notifications instead of notifying once per row.", report);
        Click(header);
        Require(workspace.SelectedCount == 0 && header.IsChecked == false, "Header checkbox clears all loaded messages.", report);

        // Exercise the preview-key route, then native checkbox activation when not intercepted.
        PreviewSpace(header);
        Require(workspace.SelectedCount == workspace.Messages.Count, "Space on the header selects all, not a current row.", report);
        Click(header);
        grid.CurrentCell = new DataGridCellInfo(workspace.Messages[0], grid.Columns[1]);
        PreviewSpace(RowCheck(grid, workspace.Messages[1]));
        Require(workspace.Messages[1].IsSelected && !workspace.Messages[0].IsSelected,
            "Space on a row checkbox toggles that checkbox once.", report);
        ClearAll(header, workspace);
        foreach (var row in original) Click(RowCheck(grid, row));
        if (originalFocus is not null) grid.SelectedItem = originalFocus;
        await Settle();
    }

    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Require(bool condition, string text, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(text);
        report.Add("PASS: " + text);
    }

    internal static CheckBox RowCheck(DataGrid grid, MessageRow row)
    {
        grid.ScrollIntoView(row);
        grid.UpdateLayout();
        return ProofCapture.Descendants(grid).OfType<CheckBox>().Single(box => ReferenceEquals(box.DataContext, row));
    }

    internal static void Click(CheckBox box) => typeof(ToggleButton).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(box, null);

    private static void ClearAll(CheckBox header, Workspace workspace)
    {
        if (workspace.SelectedCount == 0) return;
        Click(header);
        if (workspace.SelectedCount != 0) Click(header);
    }

    private static void PreviewSpace(CheckBox box)
    {
        box.Focus();
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box), Environment.TickCount, Key.Space)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        box.RaiseEvent(args);
        if (!args.Handled) Click(box);
    }
}
