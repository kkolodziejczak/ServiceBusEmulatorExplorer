using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ServiceBusEmulatorExplorer.Avalonia.Investigation;
using ServiceBusEmulatorExplorer.Presentation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Avalonia.Tests;

/// <summary>
/// The Avalonia dialog keeps the WPF dialog's behaviour (InvestigationDeleteDialogTests): Cancel is focused and default,
/// only typing DELETE enables deleting, and the dialog closes with the user's answer. Input is simulated mouse and
/// keyboard on the headless platform.
/// </summary>
public sealed class DeleteMessagesDialogBehaviourTests
{
    [AvaloniaFact]
    public async Task Typing_delete_and_clicking_confirm_answers_yes()
    {
        var (owner, dialog, result) = Open();
        Click(dialog, "DeleteConfirmationInput");
        dialog.KeyTextInput("DELETE");
        Dispatcher.UIThread.RunJobs();
        Click(dialog, "ConfirmDeleteButton");

        Assert.True(await result.WaitAsync(TimeSpan.FromSeconds(5)));
        owner.Close();
    }

    [AvaloniaTheory]
    [InlineData("click")]
    [InlineData("escape")]
    [InlineData("enter")]
    public async Task Cancel_clicked_escape_or_enter_answers_no(string how)
    {
        var (owner, dialog, result) = Open();
        Click(dialog, "DeleteConfirmationInput");
        dialog.KeyTextInput("DELETE");
        Dispatcher.UIThread.RunJobs();
        if (how == "click") Click(dialog, "CancelButton");
        else
        {
            // Enter goes to the default button, Cancel, even with DELETE typed.
            dialog.KeyPressQwerty(how == "escape" ? PhysicalKey.Escape : PhysicalKey.Enter, RawInputModifiers.None);
        }

        Assert.False(await result.WaitAsync(TimeSpan.FromSeconds(5)));
        owner.Close();
    }

    [AvaloniaFact]
    public void Cancel_has_focus_and_controls_keep_the_wpf_automation_names()
    {
        var (owner, dialog, _) = Open();
        var cancel = dialog.FindControl<Button>("CancelButton")!;
        var input = dialog.FindControl<TextBox>("DeleteConfirmationInput")!;
        var confirm = dialog.FindControl<Button>("ConfirmDeleteButton")!;

        Assert.True(cancel.IsFocused);
        Assert.True(cancel.IsCancel && cancel.IsDefault);
        Assert.False(confirm.IsDefault);
        Assert.Equal("Type DELETE to confirm deletion", AutomationProperties.GetName(input));
        Assert.Equal("Confirm delete messages", AutomationProperties.GetName(confirm));
        Assert.Equal("DeleteConfirmationInput", AutomationProperties.GetAutomationId(input));
        Assert.Equal("ConfirmDeleteButton", AutomationProperties.GetAutomationId(confirm));
        Assert.Equal("CancelDeleteButton", AutomationProperties.GetAutomationId(cancel));
        foreach (string text in new[] { "wrong", "delete", "Delete", "DELETE ", " DELETE", "DELETE\n", "" })
        {
            input.Text = text;
            Dispatcher.UIThread.RunJobs();
            Assert.False(confirm.IsEffectivelyEnabled, $"'{text}' must not enable deletion.");
        }
        owner.Close();
    }

    [AvaloniaFact]
    public void Buttons_hover_and_press_in_the_wpf_colours()
    {
        var (owner, dialog, _) = Open();
        dialog.FindControl<TextBox>("DeleteConfirmationInput")!.Text = "DELETE";
        Dispatcher.UIThread.RunJobs();
        var palette = ProfilePalette.For(new(0x25, 0x63, 0xEB));

        Assert.Equal(palette["NeutralHoverBrush"].ToString(), Hover(dialog, "CancelButton"));
        Assert.Equal(palette["SelectionBrush"].ToString(), Press(dialog, "CancelButton"));
        Assert.Equal("#B31222", Hover(dialog, "ConfirmDeleteButton"));
        Assert.Equal("#940D1B", Press(dialog, "ConfirmDeleteButton"));
        owner.Close();
    }

    private static (Window Owner, DeleteMessagesDialog Dialog, Task<bool> Result) Open()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new DeleteMessagesDialog(new DeleteMessagesViewModel(SampleDeliveries.DeadLetters(1)), "#2563EB");
        var result = dialog.ShowDialog<bool>(owner);
        Dispatcher.UIThread.RunJobs();
        return (owner, dialog, result);
    }

    private static Point Centre(Window window, string name)
    {
        var control = window.FindControl<Control>(name)!;
        return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    }

    private static void Click(Window window, string name)
    {
        var centre = Centre(window, name);
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static string Hover(Window window, string name)
    {
        window.MouseMove(Centre(window, name));
        Dispatcher.UIThread.RunJobs();
        return Fill(window, name);
    }

    private static string Press(Window window, string name)
    {
        var centre = Centre(window, name);
        window.MouseMove(centre);
        window.MouseDown(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        string fill = Fill(window, name);
        // Release away from the button, so the press does not click it.
        window.MouseUp(new Point(1, 1), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        return fill;
    }

    private static string Fill(Window window, string name)
    {
        var presenter = window.FindControl<Button>(name)!.GetVisualDescendants().OfType<ContentPresenter>()
            .First(candidate => candidate.Name == "PART_ContentPresenter");
        var colour = Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color;
        return $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
    }
}
