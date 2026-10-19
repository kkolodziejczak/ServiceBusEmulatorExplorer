using System.Windows;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Presentation.Settings;

namespace ServiceBusEmulatorExplorer.App.Investigation.Settings;

/// <summary>Asks whether to save, discard or keep editing unsaved profile edits when Settings closes (Settings 70).</summary>
public partial class SaveChangesWindow : Window
{
    public SaveChangesWindow(string question, string colorHex)
    {
        InitializeComponent();
        ProfileTheme.Apply(this, colorHex);
        QuestionText.Text = question;
        Loaded += (_, _) => SaveButton.Focus();
    }

    /// <summary>The answer; Cancel when the window is closed any other way.</summary>
    public UnsavedChangesChoice Choice { get; private set; } = UnsavedChangesChoice.Cancel;

    private void Save_Click(object sender, RoutedEventArgs e) => Answer(UnsavedChangesChoice.Save);

    private void Discard_Click(object sender, RoutedEventArgs e) => Answer(UnsavedChangesChoice.Discard);

    private void Answer(UnsavedChangesChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
