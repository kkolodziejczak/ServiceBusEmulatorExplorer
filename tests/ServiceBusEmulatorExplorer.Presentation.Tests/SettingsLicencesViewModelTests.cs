using System.Runtime.InteropServices;
using ServiceBusEmulatorExplorer.Presentation.Settings;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class SettingsLicencesViewModelTests
{
    private static readonly Component Font = new("Selawik", "SIL OFL 1.1", "Includes: selawk.ttf\n\nOFL text");
    private static readonly Component Library = new("AvalonEdit", "MIT", "Includes: AvalonEdit\n\nMIT text");

    [Fact]
    public void Opens_on_the_first_component_and_shows_its_notice()
    {
        var licences = new SettingsLicencesViewModel([Font, Library], _ => { });

        Assert.Same(Font, licences.SelectedComponent);
        Assert.Equal(Font.Notice, licences.NoticeText);
        Assert.Equal(string.Empty, licences.Status);
    }

    [Fact]
    public void Selecting_another_component_shows_its_notice_clears_the_status_and_asks_to_scroll_to_the_start()
    {
        var copied = new List<string>();
        var licences = new SettingsLicencesViewModel([Font, Library], copied.Add);
        int shown = 0;
        licences.NoticeShown += () => shown++;
        licences.CopyCommand.Execute(null);

        licences.SelectedComponent = Library;

        Assert.Equal(Library.Notice, licences.NoticeText);
        Assert.Equal(string.Empty, licences.Status);
        Assert.Equal(1, shown);
    }

    [Fact]
    public void Copying_puts_the_shown_notice_on_the_clipboard_exactly()
    {
        var copied = new List<string>();
        var licences = new SettingsLicencesViewModel([Font, Library], copied.Add) { SelectedComponent = Library };

        licences.CopyCommand.Execute(null);

        Assert.Equal([Library.Notice], copied);
        Assert.Equal("AvalonEdit licence copied.", licences.Status);
    }

    [Fact]
    public void Copying_while_the_clipboard_is_unavailable_says_so()
    {
        var licences = new SettingsLicencesViewModel([Font], _ => throw new ExternalException("held"));

        licences.CopyCommand.Execute(null);

        Assert.Equal("Clipboard is unavailable. Try copying again.", licences.Status);
    }

    [Fact]
    public void Without_a_selection_nothing_is_shown_or_copied()
    {
        var copied = new List<string>();
        var licences = new SettingsLicencesViewModel([Font], copied.Add) { SelectedComponent = null };

        licences.CopyCommand.Execute(null);

        Assert.Equal(string.Empty, licences.NoticeText);
        Assert.Empty(copied);
        Assert.Equal(string.Empty, licences.Status);
    }

    [Fact]
    public void A_notice_is_read_only_when_its_component_is_selected()
    {
        var lazy = new CountingComponent();

        var licences = new SettingsLicencesViewModel([Font, lazy], _ => { });
        Assert.Equal(0, lazy.Reads);
        licences.SelectedComponent = lazy;

        Assert.Equal(1, lazy.Reads);
    }

    private sealed record Component(string Name, string Licence, string Notice) : ILicensedComponent;

    private sealed class CountingComponent : ILicensedComponent
    {
        public int Reads { get; private set; }

        public string Name => "Counted";

        public string Licence => "MIT";

        public string Notice
        {
            get
            {
                Reads++;
                return "text";
            }
        }
    }
}
