using System.Windows;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class TemplateLocationDialog : Window
{
    private readonly Func<string, string, string?> validate;
    public string TemplateName => NameInput.Text.Trim();
    public string FolderPath => (FolderInput.SelectedItem as FolderChoice)?.Path ?? "";

    public TemplateLocationDialog(string title, string name, IEnumerable<string> folders, string selectedFolder,
        Func<string, string, string?> validate, bool moving = false)
    {
        InitializeComponent();
        this.validate = validate;
        Title = Heading.Text = title;
        NameInput.Text = name;
        var choices = new[] { new FolderChoice("", "Root") }.Concat(folders
            .Where(path => path.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new FolderChoice(path, path.Replace("/", " / ")))).ToArray();
        FolderInput.ItemsSource = choices;
        FolderInput.SelectedItem = choices.FirstOrDefault(choice => choice.Path.Equals(selectedFolder, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
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
        string? error = TemplateName.Length == 0 ? "Enter a name." : validate(TemplateName, FolderPath);
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }

    private sealed record FolderChoice(string Path, string Label);
}
