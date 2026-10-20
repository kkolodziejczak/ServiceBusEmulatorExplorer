using System.Windows;
using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class TemplateLocationDialog : Window
{
    private readonly TemplateLocationViewModel location;
    public string TemplateName => NameInput.Text.Trim();
    public string FolderPath => (FolderInput.SelectedItem as FolderChoice)?.Path ?? "";

    public TemplateLocationDialog(string title, string name, IEnumerable<string> folders, string selectedFolder,
        Func<string, string, string?> validate, bool moving = false)
    {
        InitializeComponent();
        location = new TemplateLocationViewModel(folders, selectedFolder, validate);
        Title = Heading.Text = title;
        NameInput.Text = name;
        FolderInput.ItemsSource = location.Choices;
        FolderInput.SelectedItem = location.InitialChoice;
        if (moving)
        {
            NameRow.Visibility = Visibility.Collapsed;
            ConfirmButton.Content = "Move";
        }
        Loaded += (_, _) =>
        {
            if (moving) FolderInput.Focus();
            else { NameInput.Focus(); NameInput.SelectAll(); }
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (location.Problem(NameInput.Text, FolderInput.SelectedItem as FolderChoice) is { } error)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }
}
