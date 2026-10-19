using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Settings;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

/// <summary>Settings 70: unsaved profile edits are marked, and can all be saved at once when closing.</summary>
public sealed partial class ConnectionProfilesViewModelTests
{
    [Fact]
    public void Showing_profiles_marks_nothing()
    {
        var (connections, secrets, _, _) = Create();
        connections.ProfileLoaded += secrets.Show;

        connections.Start();
        connections.SelectedProfile = connections.Profiles[0];

        Assert.All(connections.Profiles, profile => Assert.False(profile.IsModified));
        Assert.False(connections.HasUnsavedChanges);
    }

    [Fact]
    public void Editing_marks_the_shown_profile_and_typing_the_saved_value_back_clears_the_mark()
    {
        var (connections, secrets, _, _) = Create();
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        var told = new List<string?>();
        connections.PropertyChanged += (_, e) => told.Add(e.PropertyName);

        connections.ProfileName = "Secondary edited";

        Assert.True(connections.Profiles[1].IsModified);
        Assert.True(connections.HasUnsavedChanges);
        Assert.Contains(nameof(ConnectionProfilesViewModel.HasUnsavedChanges), told);

        connections.ProfileName = " Secondary ";

        Assert.False(connections.Profiles[1].IsModified);
        Assert.False(connections.HasUnsavedChanges);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("namespace")]
    [InlineData("colour")]
    [InlineData("warning")]
    public void Every_editor_field_can_mark_the_profile(string field)
    {
        var (connections, secrets, _, _) = Create();
        connections.ProfileLoaded += secrets.Show;
        connections.Start();

        switch (field)
        {
            case "mode": connections.AuthenticationModeIndex = 0; break;
            case "namespace": connections.FullyQualifiedNamespace = "payments.servicebus.windows.net"; break;
            case "colour": connections.ColorHex = "#007F80"; break;
            default: connections.WarningMessage = "Production"; break;
        }

        Assert.True(connections.Profiles[1].IsModified);
    }

    [Fact]
    public void A_changed_connection_string_marks_the_profile_when_the_view_reports_it()
    {
        var (connections, secrets, _, _) = Create(Start with { SelectedProfileId = "primary" });
        connections.ProfileLoaded += secrets.Show;
        connections.Start();

        secrets.Administration = SecondaryRuntime;
        Assert.False(connections.Profiles[0].IsModified);
        connections.ConnectionStringsChanged();

        Assert.True(connections.Profiles[0].IsModified);
    }

    [Fact]
    public void Switching_profiles_keeps_the_mark_on_the_edited_one()
    {
        var (connections, secrets, _, _) = Create();
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        connections.WarningMessage = "Production";

        connections.SelectedProfile = connections.Profiles[0];

        Assert.True(connections.Profiles[1].IsModified);
        Assert.False(connections.Profiles[0].IsModified);
        Assert.True(connections.HasUnsavedChanges);

        connections.SelectedProfile = connections.Profiles[1];
        Assert.True(connections.Profiles[1].IsModified);
        Assert.Equal("Production", connections.WarningMessage);
    }

    [Fact]
    public async Task A_new_connection_is_marked_until_it_is_saved()
    {
        var (connections, secrets, saves, _) = Create();
        connections.ProfileLoaded += secrets.Show;
        connections.Start();

        connections.AddConnectionCommand.Execute(null);

        Assert.True(connections.SelectedProfile!.IsModified);
        Assert.True(connections.HasUnsavedChanges);

        secrets.Runtime = PrimaryRuntime;
        secrets.Administration = PrimaryAdmin;
        connections.SaveProfileCommand.Execute(null);
        await saves.CompleteNext();

        Assert.False(connections.SelectedProfile.IsModified);
        Assert.False(connections.HasUnsavedChanges);
    }

    [Fact]
    public async Task Saving_the_shown_profile_clears_its_mark_and_a_failed_save_keeps_it()
    {
        var (connections, secrets, saves, _) = Create();
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        connections.WarningMessage = "Production";

        connections.SaveProfileCommand.Execute(null);
        await saves.FailNext("disk full");
        Assert.True(connections.Profiles[1].IsModified);

        connections.SaveProfileCommand.Execute(null);
        await saves.CompleteNext();
        Assert.False(connections.Profiles[1].IsModified);
        Assert.False(connections.HasUnsavedChanges);
    }

    [Fact]
    public void The_question_names_every_edited_profile_by_its_typed_name()
    {
        var (connections, secrets, _, _) = Create();
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        connections.ProfileName = "Payments";

        Assert.Equal("You have unsaved changes to Payments. What do you want to do?", connections.UnsavedChangesQuestion());

        connections.SelectedProfile = connections.Profiles[0];
        connections.WarningMessage = "Production";
        Assert.Equal("You have unsaved changes to Primary and Payments. What do you want to do?",
            connections.UnsavedChangesQuestion());

        connections.AddConnectionCommand.Execute(null);
        connections.ProfileName = "  ";
        Assert.Equal("You have unsaved changes to Primary, Payments and an unnamed profile. What do you want to do?",
            connections.UnsavedChangesQuestion());
    }

    [Fact]
    public async Task Saving_all_writes_every_edited_profile_once_and_clears_the_marks()
    {
        var (connections, secrets, saves, persistence) = Create();
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        connections.WarningMessage = "Production";
        connections.SelectedProfile = connections.Profiles[0];
        connections.ProfileName = "Primary edited";

        Task<bool> saving = connections.SaveAllAsync();
        Assert.False(connections.IsEditable);
        await saves.CompleteNext();

        Assert.True(await saving);
        InvestigationProfile[] written = Assert.Single(saves.Requested).Profiles.ToArray();
        Assert.Equal(["Primary edited", "Secondary"], written.Select(profile => profile.Connection.Name));
        Assert.Equal("Production", written[1].WarningMessage);
        Assert.Equal(written, persistence.Saved.Profiles);
        Assert.False(connections.HasUnsavedChanges);
        Assert.True(connections.IsEditable);
    }

    [Fact]
    public async Task Saving_all_keeps_an_invalid_profile_open_with_its_errors_and_saves_the_valid_ones()
    {
        var (connections, secrets, saves, _) = Create();
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        connections.FullyQualifiedNamespace = string.Empty;
        connections.SelectedProfile = connections.Profiles[0];
        connections.WarningMessage = "Production";
        var raised = new List<string>();
        connections.AttentionRequested += () => raised.Add("attention");
        connections.FieldFocusRequested += field => raised.Add(field.ToString());

        Task<bool> saving = connections.SaveAllAsync();
        await saves.CompleteNext();

        Assert.False(await saving);
        Assert.Equal("Production", Assert.Single(saves.Requested).Profiles[0].WarningMessage);
        Assert.False(connections.Profiles[0].IsModified);
        Assert.Same(connections.Profiles[1], connections.SelectedProfile);
        Assert.True(connections.Profiles[1].IsModified);
        string errors = string.Join(" ", ConnectionProfileValidator.Validate(new ConnectionProfile("Secondary", SecondaryRuntime,
            SecondaryRuntime, ConnectionAuthenticationMode.AzureCli)).Errors);
        Assert.Equal(errors, connections.Status);
        Assert.Equal(["attention", nameof(ProfileField.FullyQualifiedNamespace)], raised);
    }

    [Theory]
    [InlineData("name", ProfileField.Name)]
    [InlineData("runtime", ProfileField.RuntimeConnection)]
    [InlineData("administration", ProfileField.AdministrationConnection)]
    [InlineData("namespace", ProfileField.FullyQualifiedNamespace)]
    public void Saving_focuses_the_first_invalid_field(string broken, ProfileField expected)
    {
        var (connections, secrets, _, _) = Create(Start with { SelectedProfileId = broken == "namespace" ? "secondary" : "primary" });
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        var focus = new List<ProfileField>();
        connections.FieldFocusRequested += focus.Add;

        switch (broken)
        {
            case "name": connections.ProfileName = " "; break;
            case "runtime": secrets.Runtime = string.Empty; break;
            case "administration": secrets.Administration = "not a connection string"; break;
            default: connections.FullyQualifiedNamespace = "https://orders"; break;
        }
        connections.SaveProfileCommand.Execute(null);

        Assert.Equal([expected], focus);
    }

    [Fact]
    public async Task A_failed_save_of_all_keeps_the_marks_and_says_why()
    {
        var (connections, secrets, saves, _) = Create();
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        connections.WarningMessage = "Production";
        int attention = 0;
        connections.AttentionRequested += () => attention++;

        Task<bool> saving = connections.SaveAllAsync();
        await saves.FailNext("disk full");

        Assert.False(await saving);
        Assert.True(connections.HasUnsavedChanges);
        Assert.Equal("Could not save profiles: disk full", connections.Status);
        Assert.Equal(1, attention);
        Assert.True(connections.IsEditable);
    }

    [Fact]
    public async Task Saving_all_with_nothing_edited_writes_nothing()
    {
        var (connections, secrets, saves, _) = Create();
        connections.ProfileLoaded += secrets.Show;
        connections.Start();

        Assert.True(await connections.SaveAllAsync());
        Assert.Empty(saves.Requested);
    }
}
