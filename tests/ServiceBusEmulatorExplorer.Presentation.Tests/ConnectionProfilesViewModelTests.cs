using System.Runtime.InteropServices;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Settings;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class ConnectionProfilesViewModelTests
{
    private const string PrimaryRuntime = "Endpoint=sb://primary-runtime.example/;SharedAccessKeyName=Root;SharedAccessKey=secret";
    private const string PrimaryAdmin = "Endpoint=sb://primary-admin.example/;SharedAccessKeyName=Root;SharedAccessKey=secret";
    private const string SecondaryRuntime = "Endpoint=sb://secondary-runtime.example/;SharedAccessKeyName=Root;SharedAccessKey=secret";

    private static readonly WorkspacePreferences Start = new()
    {
        Profiles =
        [
            new InvestigationProfile("primary", new ConnectionProfile("Primary", PrimaryRuntime, PrimaryAdmin), "#0069FA", "Check first"),
            new InvestigationProfile("secondary", new ConnectionProfile("Secondary", SecondaryRuntime, SecondaryRuntime,
                ConnectionAuthenticationMode.AzureCli, "orders.servicebus.windows.net"), "#7540BF")
        ],
        SelectedProfileId = "secondary"
    };

    [Fact]
    public void Starting_shows_the_saved_selected_profile_and_saves_nothing()
    {
        var (connections, secrets, saves, _) = Create();
        var loaded = new List<string>();
        connections.ProfileLoaded += draft => { loaded.Add(draft.Id); secrets.Show(draft); };

        Assert.Null(connections.SelectedProfile);
        connections.Start();

        Assert.Equal(["secondary"], loaded);
        Assert.Equal("secondary", connections.SelectedProfile!.Id);
        Assert.Equal(("Secondary", 1, "orders.servicebus.windows.net", "#7540BF"),
            (connections.ProfileName, connections.AuthenticationModeIndex, connections.FullyQualifiedNamespace, connections.ColorHex));
        Assert.True(connections.UsesAzureCli);
        Assert.False(connections.UsesConnectionStrings);
        Assert.Empty(saves.Requested);
    }

    [Fact]
    public void Starting_falls_back_to_the_first_profile_and_shows_nothing_without_profiles()
    {
        var (first, _, _, _) = Create(Start with { SelectedProfileId = "missing" });
        first.Start();
        Assert.Equal("primary", first.SelectedProfile!.Id);

        var (empty, _, _, _) = Create(new WorkspacePreferences { Profiles = [] });
        empty.Start();
        Assert.Null(empty.SelectedProfile);
        Assert.True(empty.UsesConnectionStrings);
    }

    [Fact]
    public void Switching_profiles_keeps_the_trimmed_draft_and_exact_secrets_of_the_profile_you_left()
    {
        var (connections, secrets, saves, _) = Create(Start with { SelectedProfileId = "primary" });
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        ProfileDraft primary = connections.SelectedProfile!;

        connections.ProfileName = "  Primary renamed  ";
        secrets.Runtime = "  Endpoint=sb://edited.example/;SharedAccessKeyName=Root;SharedAccessKey=edited ";
        connections.WarningMessage = "  Edited  ";
        connections.ColorHex = null;
        connections.SelectedProfile = connections.Profiles[1];

        Assert.Equal("Primary renamed", primary.Name);
        Assert.Equal("  Endpoint=sb://edited.example/;SharedAccessKeyName=Root;SharedAccessKey=edited ", primary.RuntimeConnectionString);
        Assert.Equal(PrimaryAdmin, primary.AdministrationConnectionString);
        Assert.Equal("Edited", primary.WarningMessage);
        Assert.Equal("#0069FA", primary.ColorHex);
        Assert.Equal("Secondary", connections.ProfileName);
        Assert.Equal(SecondaryRuntime, secrets.Runtime);

        connections.SelectedProfile = primary;
        Assert.Equal("Primary renamed", connections.ProfileName);
        Assert.Equal("Edited", connections.WarningMessage);
        Assert.Empty(saves.Requested);
    }

    [Fact]
    public void Switching_authentication_mode_changes_which_fields_show_and_keeps_the_hidden_ones()
    {
        var (connections, secrets, _, _) = Create(Start with { SelectedProfileId = "primary" });
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        connections.FullyQualifiedNamespace = "kept.servicebus.windows.net";

        connections.AuthenticationModeIndex = 1;
        Assert.True(connections.UsesAzureCli);
        connections.AuthenticationModeIndex = 0;

        Assert.True(connections.UsesConnectionStrings);
        Assert.Equal("kept.servicebus.windows.net", connections.FullyQualifiedNamespace);
        Assert.Equal(PrimaryRuntime, secrets.Runtime);
    }

    [Fact]
    public void Adding_a_connection_picks_the_first_free_name_ignoring_case_and_selects_it_without_saving()
    {
        var preferences = Start with
        {
            Profiles = [.. Start.Profiles, new InvestigationProfile("third", new ConnectionProfile("connection 1", "", ""), "#0069FA")]
        };
        var (connections, secrets, saves, _) = Create(preferences);
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        var added = new List<string>();
        var focus = new List<bool>();
        connections.ProfileAdded += draft => added.Add(draft.Name);
        connections.NameFocusRequested += focus.Add;

        connections.AddConnectionCommand.Execute(null);

        Assert.Equal(["Connection 2"], added);
        Assert.Equal([true], focus);
        Assert.Equal(4, connections.Profiles.Count);
        Assert.Same(connections.Profiles[3], connections.SelectedProfile);
        Assert.Equal("Connection 2", connections.ProfileName);
        Assert.Equal(0, connections.AuthenticationModeIndex);
        Assert.Equal(string.Empty, secrets.Runtime);
        Assert.Empty(saves.Requested);
    }

    [Fact]
    public async Task Saving_a_changed_profile_replaces_it_in_place_updates_its_row_and_reports_it()
    {
        var (connections, secrets, saves, persistence) = Create(Start with { SelectedProfileId = "primary" });
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        ProfileDraft primary = connections.SelectedProfile!;
        connections.ProfileName = " Primary renamed ";
        connections.WarningMessage = " Watch out ";
        connections.ColorHex = "#007F80";

        connections.SaveProfileCommand.Execute(null);
        Assert.False(connections.IsEditable);
        Assert.False(connections.AddConnectionCommand.CanExecute(null));
        Assert.False(connections.SaveProfileCommand.CanExecute(null));
        Assert.Equal("Primary", primary.Name);
        await saves.CompleteNext();

        InvestigationProfile saved = Assert.Single(saves.Requested).Profiles[0];
        Assert.Equal(["primary", "secondary"], persistence.Saved.Profiles.Select(profile => profile.Id));
        Assert.Equal(("Primary renamed", PrimaryRuntime, "Watch out", "#007F80"),
            (saved.Connection.Name, saved.Connection.RuntimeConnectionString, saved.WarningMessage, saved.ColorHex));
        Assert.Equal("Primary renamed", primary.Name);
        Assert.Equal("#007F80", primary.ColorHex);
        Assert.True(connections.IsEditable);
        Assert.True(connections.SaveProfileCommand.CanExecute(null));
        Assert.Equal("Profile saved. No connection was attempted.", connections.Status);
    }

    [Fact]
    public async Task Saving_a_new_connection_appends_it()
    {
        var (connections, secrets, saves, persistence) = Create();
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        connections.AddConnectionCommand.Execute(null);
        secrets.Runtime = PrimaryRuntime;
        secrets.Administration = PrimaryAdmin;

        connections.SaveProfileCommand.Execute(null);
        await saves.CompleteNext();

        Assert.Equal(3, persistence.Saved.Profiles.Count);
        Assert.Equal("Connection 1", persistence.Saved.Profiles[2].Connection.Name);
    }

    [Fact]
    public void An_invalid_profile_is_not_saved_and_asks_for_the_name()
    {
        var (connections, secrets, saves, _) = Create(Start with { SelectedProfileId = "primary" });
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        var focus = new List<bool>();
        connections.NameFocusRequested += focus.Add;
        secrets.Runtime = "not a connection string";

        connections.SaveProfileCommand.Execute(null);

        Assert.Empty(saves.Requested);
        Assert.Equal([false], focus);
        Assert.Equal(string.Join(" ", ConnectionProfileValidator.Validate(new ConnectionProfile("Primary", "not a connection string", PrimaryAdmin)).Errors),
            connections.Status);
        Assert.True(connections.IsEditable);
    }

    [Fact]
    public async Task A_failed_save_keeps_what_was_typed_and_says_why()
    {
        WorkspacePreferences start = Start with { SelectedProfileId = "primary" };
        var (connections, secrets, saves, persistence) = Create(start);
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        ProfileDraft primary = connections.SelectedProfile!;
        connections.WarningMessage = "Typed but not saved";

        connections.SaveProfileCommand.Execute(null);
        await saves.FailNext("disk full");

        Assert.Equal("Could not save profile: disk full", connections.Status);
        Assert.Equal("Typed but not saved", connections.WarningMessage);
        Assert.Equal("Check first", primary.WarningMessage);
        Assert.Same(start, persistence.Saved);
        Assert.True(connections.IsEditable);
    }

    [Fact]
    public async Task The_save_result_is_not_shown_on_a_profile_selected_while_it_was_saving()
    {
        var (connections, secrets, saves, _) = Create(Start with { SelectedProfileId = "primary" });
        connections.ProfileLoaded += secrets.Show;
        connections.Start();

        connections.SaveProfileCommand.Execute(null);
        connections.SelectedProfile = connections.Profiles[1];
        await saves.CompleteNext();

        Assert.Equal(string.Empty, connections.Status);
    }

    [Fact]
    public async Task Saving_is_unavailable_while_any_settings_save_runs()
    {
        var (connections, secrets, saves, persistence) = Create(Start with { SelectedProfileId = "primary" });
        connections.ProfileLoaded += secrets.Show;
        connections.Start();

        Task general = persistence.SaveAsync(current => current with { QueuePageSize = 200 });
        Assert.False(connections.SaveProfileCommand.CanExecute(null));
        connections.SaveProfileCommand.Execute(null);
        await saves.CompleteNext();
        await general;

        Assert.Single(saves.Requested);
        Assert.True(connections.SaveProfileCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(true, PrimaryRuntime, "Runtime connection string copied.")]
    [InlineData(false, PrimaryAdmin, "Administration connection string copied.")]
    public void Copying_puts_the_exact_connection_string_on_the_clipboard(bool runtime, string expected, string message)
    {
        var copied = new List<string>();
        var (connections, secrets, saves, _) = Create(Start with { SelectedProfileId = "primary" }, copied.Add);
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        var command = runtime ? connections.CopyRuntimeConnectionCommand : connections.CopyAdministrationConnectionCommand;

        Assert.False(command.CanExecute(null));
        if (runtime) connections.HasRuntimeConnection = true; else connections.HasAdministrationConnection = true;
        Assert.True(command.CanExecute(null));
        command.Execute(null);

        Assert.Equal([expected], copied);
        Assert.Equal(message, connections.Status);
        Assert.Empty(saves.Requested);
    }

    [Fact]
    public void Copying_while_the_clipboard_is_unavailable_says_so()
    {
        var (connections, secrets, _, _) = Create(Start with { SelectedProfileId = "primary" }, _ => throw new ExternalException("held"));
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        connections.HasRuntimeConnection = true;

        connections.CopyRuntimeConnectionCommand.Execute(null);

        Assert.Equal("Clipboard is unavailable. Try copying again.", connections.Status);
    }

    [Fact]
    public void Nothing_is_captured_or_loaded_before_start_and_starting_twice_loads_once()
    {
        var (connections, secrets, _, _) = Create();
        int loads = 0;
        connections.ProfileLoaded += draft => { loads++; secrets.Show(draft); };

        connections.SelectedProfile = connections.Profiles[0];
        Assert.Equal(0, loads);
        Assert.Equal(string.Empty, connections.ProfileName);

        connections.Start();
        connections.Start();
        Assert.Equal(1, loads);
    }

    [Fact]
    public void Clearing_the_selection_keeps_the_editor_on_the_last_profile()
    {
        var (connections, secrets, _, _) = Create(Start with { SelectedProfileId = "primary" });
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        connections.ProfileName = "Edited";

        connections.SelectedProfile = null;

        Assert.Equal("Edited", connections.Profiles[0].Name);
        Assert.Equal("Edited", connections.ProfileName);
    }

    [Fact]
    public async Task Saving_without_a_chosen_colour_keeps_the_profile_colour_and_trims_the_namespace()
    {
        var (connections, secrets, saves, _) = Create();
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        connections.ColorHex = null;
        connections.FullyQualifiedNamespace = "  orders.servicebus.windows.net  ";

        connections.SaveProfileCommand.Execute(null);
        await saves.CompleteNext();

        InvestigationProfile saved = Assert.Single(saves.Requested).Profiles.Single(profile => profile.Id == "secondary");
        Assert.Equal("#7540BF", saved.ColorHex);
        Assert.Equal("orders.servicebus.windows.net", saved.Connection.FullyQualifiedNamespace);
    }

    [Fact]
    public async Task A_save_finished_after_switching_updates_the_saved_profile_not_the_shown_one()
    {
        var (connections, secrets, saves, _) = Create(Start with { SelectedProfileId = "primary" });
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        ProfileDraft primary = connections.SelectedProfile!;
        connections.WarningMessage = "Saved warning";

        connections.SaveProfileCommand.Execute(null);
        connections.SelectedProfile = connections.Profiles[1];
        await saves.CompleteNext();

        Assert.Equal("Saved warning", primary.WarningMessage);
        Assert.Equal(string.Empty, connections.Profiles[1].WarningMessage);
        Assert.Equal("Secondary", connections.ProfileName);
    }

    [Fact]
    public async Task A_save_failure_after_switching_is_not_shown_on_the_other_profile()
    {
        var (connections, secrets, saves, _) = Create(Start with { SelectedProfileId = "primary" });
        connections.ProfileLoaded += secrets.Show;
        connections.Start();

        connections.SaveProfileCommand.Execute(null);
        connections.SelectedProfile = connections.Profiles[1];
        await saves.FailNext("disk full");

        Assert.Equal(string.Empty, connections.Status);
    }

    [Fact]
    public void Copying_an_empty_field_does_nothing()
    {
        var copied = new List<string>();
        var (connections, secrets, _, _) = Create(Start with { SelectedProfileId = "primary" }, copied.Add);
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        connections.HasRuntimeConnection = true;
        secrets.Runtime = string.Empty;

        connections.CopyRuntimeConnectionCommand.Execute(null);

        Assert.Empty(copied);
        Assert.Equal(string.Empty, connections.Status);
    }

    [Fact]
    public async Task Buttons_are_told_when_their_availability_changes()
    {
        var (connections, secrets, saves, _) = Create(Start with { SelectedProfileId = "primary" });
        connections.ProfileLoaded += secrets.Show;
        connections.Start();
        var told = new List<string>();
        connections.AddConnectionCommand.CanExecuteChanged += (_, _) => told.Add("add");
        connections.SaveProfileCommand.CanExecuteChanged += (_, _) => told.Add("save");
        connections.CopyRuntimeConnectionCommand.CanExecuteChanged += (_, _) => told.Add("runtime");
        connections.CopyAdministrationConnectionCommand.CanExecuteChanged += (_, _) => told.Add("administration");

        connections.HasRuntimeConnection = true;
        connections.HasAdministrationConnection = true;
        connections.SaveProfileCommand.Execute(null);
        await saves.CompleteNext();

        Assert.Equal(["runtime", "administration", "add", "save", "save", "add"], told);
    }

    private static (ConnectionProfilesViewModel Connections, FakeSecrets Secrets, SaveScript Saves, SettingsPersistence Persistence) Create(
        WorkspacePreferences? preferences = null, Action<string>? copy = null)
    {
        var saves = new SaveScript();
        var secrets = new FakeSecrets();
        var persistence = new SettingsPersistence(preferences ?? Start, saves.SaveAsync);
        return (new ConnectionProfilesViewModel(persistence, secrets, copy ?? (_ => { })), secrets, saves, persistence);
    }

    /// <summary>Stands in for the window's password fields.</summary>
    private sealed class FakeSecrets : IConnectionSecrets
    {
        public string Runtime { get; set; } = string.Empty;

        public string Administration { get; set; } = string.Empty;

        public void Show(ProfileDraft draft)
        {
            Runtime = draft.RuntimeConnectionString;
            Administration = draft.AdministrationConnectionString;
        }
    }

    private sealed class SaveScript
    {
        private readonly Queue<TaskCompletionSource> pending = new();

        public List<WorkspacePreferences> Requested { get; } = [];

        public Task SaveAsync(WorkspacePreferences preferences)
        {
            Requested.Add(preferences);
            var save = new TaskCompletionSource();
            pending.Enqueue(save);
            return save.Task;
        }

        public async Task CompleteNext()
        {
            pending.Dequeue().SetResult();
            await Task.Yield();
        }

        public async Task FailNext(string message)
        {
            pending.Dequeue().SetException(new InvalidOperationException(message));
            await Task.Yield();
        }
    }
}
