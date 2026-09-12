using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class ExpandedControlsProof
{
    public static async Task Exercise(PrototypeWindow window, List<string> report, string output)
    {
        await GlobalWatch(window, report, output);
        await TopicWatch(window, report, output);
        await DeleteMessages(window, report, output);
        await ProfilePalette(window, report, output);
        await ConnectionHealth(window, report, output);
    }

    private static async Task GlobalWatch(PrototypeWindow owner, List<string> report, string output)
    {
        var roots = PrototypeData.CreateTree();
        var rules = new WatchRules();
        var changes = 0;
        var dialog = new GlobalWatchWindow(roots, rules, () => changes++) { Owner = owner };
        dialog.Show();
        await Settle();
        Toggle((CheckBox)dialog.FindName("GlobalActiveChoice"));
        Toggle((CheckBox)dialog.FindName("GlobalDlqChoice"));
        await Settle();
        Check(rules.GlobalActive && rules.GlobalDeadLetter && changes == 2, "Global Watch switches independently enable both buckets", report);
        var topic = roots.SelectMany(PrototypeData.Flatten).First(node => node.Kind == "Topic");
        Toggle(ProofCapture.Descendants(dialog).OfType<CheckBox>().First(choice => Equals(choice.Tag, topic.Path)));
        await Settle();
        Check(topic.Children.All(child => !rules.IsWatched(child.Path, false, roots) && !rules.IsWatched(child.Path, true, roots)),
            "Unchecking a topic removes Active and DLQ watches for every child", report);
        Toggle(ProofCapture.Descendants(dialog).OfType<CheckBox>().First(choice => Equals(choice.Tag, topic.Path)));
        await Settle();
        var child = topic.Children.First();
        Toggle(ProofCapture.Descendants(dialog).OfType<CheckBox>().First(choice => Equals(choice.Tag, child.Path)));
        await Settle();
        Check(rules.GetIncludedState(topic.Path, roots) == (topic.Children.Count > 1 ? (bool?)null : false),
            "Unchecking a child updates its parent inclusion state truthfully", report);
        var topicGroup = roots.First(root => root.Children.Contains(topic));
        Toggle(ProofCapture.Descendants(dialog).OfType<CheckBox>().First(choice => Equals(choice.Tag, topicGroup.Path)));
        await Settle();
        Check(topicGroup.Children.SelectMany(PrototypeData.Flatten).Where(node => node.Kind == "Subscription")
            .All(node => rules.IsIncluded(node.Path, roots)), "Checking Topics selects all subscriptions and clears child exceptions", report);        ((TextBox)dialog.FindName("InclusionSearch")).Text = "no-matching-watch-entity";
        await Settle();
        Check(((FrameworkElement)dialog.FindName("NoEntitiesMatch")).IsVisible, "Global inclusion filter displays its no-results state", report);
        ((TextBox)dialog.FindName("InclusionSearch")).Clear();
        await Settle();
        var added = new EntityNode { Name = "newly-discovered", Path = "newly-discovered", Kind = "Queue" };
        roots.Add(added);
        Check(rules.IsWatched(added.Path, false, roots) && rules.IsWatched(added.Path, true, roots),
            "Global rules include newly discovered queues without adding explicit watches", report);
        ProofCapture.Save(dialog, output, "global-watch");
        dialog.Width = 460; dialog.Height = 520;
        await Settle();
        ProofCapture.CheckBounds(dialog, [ProofCapture.Control<Button>(dialog, "DoneButton"), (TextBox)dialog.FindName("InclusionSearch")]);
        ProofCapture.Save(dialog, output, "global-watch-compact");
        Click(dialog, "DoneButton");
        await Settle();
    }

    private static async Task TopicWatch(PrototypeWindow window, List<string> report, string output)
    {
        var original = window.Workspace.SelectedEntity;
        var topic = window.Workspace.Roots.SelectMany(PrototypeData.Flatten).First(node => node.Kind == "Topic");
        ProofCapture.Descendants(window).OfType<TreeViewItem>().First(item => ReferenceEquals(item.DataContext, topic)).IsSelected = true;
        await Settle();
        Check(((DataGridColumn)window.FindName("SourceColumn")).Visibility == Visibility.Visible, "Topic browsing shows message source subscriptions", report);
        Click(window, "WatchButton");
        await Settle();
        var active = (CheckBox)window.FindName("WatchActiveChoice");
        Check(active.IsEnabled, "Topic Watch selector enables Active choice", report);
        if (active.IsChecked != true) Toggle(active);
        await Settle();
        var rules = (WatchRules)typeof(PrototypeWindow).GetField("watchRules", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Check(topic.Children.All(child => rules.IsWatched(child.Path, false, window.Workspace.Roots)), "Topic Watch applies to its subscriptions", report);
        ((Popup)window.FindName("WatchPopup")).IsOpen = false;
        ProofCapture.Save(window, output, "topic-watch");
        window.SetWatched(topic.Path, false, false);
        if (original is not null) ProofCapture.Descendants(window).OfType<TreeViewItem>().First(item => ReferenceEquals(item.DataContext, original)).IsSelected = true;
        await Settle();
    }

    private static async Task DeleteMessages(PrototypeWindow owner, List<string> report, string output)
    {
        var workspace = new Workspace();
        var all = workspace.SnapshotMessages().ToArray();
        var dlq = all.First(row => row.IsDeadLetter && row.Source == "order-events/billing");
        var active = all.First(row => !row.IsDeadLetter && row.Source == dlq.Source);
        MessageRow[] targets = [active, dlq];
        var cancelled = await Confirm(owner, targets, false, output, "delete-confirmation", report);
        Check(cancelled == false && workspace.SnapshotMessages().Count == all.Length, "Cancelling delete confirmation preserves sample messages", report);
        var confirmed = await Confirm(owner, targets, true, output, "delete-confirmation-accepted", report);
        var entity = workspace.Roots.SelectMany(PrototypeData.Flatten).First(node => node.Path == active.Source);
        var beforeActive = int.Parse(entity.MessageCount);
        var beforeDlq = int.Parse(entity.DlqCount);
        Check(confirmed == true && workspace.DeleteMessages(targets) == 2, "Confirmed selection deletes one Active and one DLQ sample message", report);
        Check(workspace.SnapshotMessages().Count == all.Length - 2 && !workspace.SnapshotMessages().Any(row => targets.Any(target => target.Key == row.Key))
            && entity.MessageCount == (beforeActive - 1).ToString() && entity.DlqCount == (beforeDlq - 1).ToString(),
            "Delete targets exact messages and decrements corresponding Active and DLQ totals", report);
        Check(workspace.DeleteMessages(targets) == 0, "Repeated deletion does not remove extra messages or decrement totals twice", report);
        await DeleteFromWorkspace(report);
    }

    private static async Task DeleteFromWorkspace(List<string> report)
    {
        var window = new PrototypeWindow();
        window.ConfigureApplicationLifetime(true);
        try
        {
            window.Show();
            await Settle();
            var selected = window.Workspace.Messages.Where(row => row.IsSelected).ToArray();
            var entity = window.Workspace.SelectedEntity!;
            var before = int.Parse(entity.MessageCount);
            Check(selected.Length == 2 && selected.All(row => !row.IsDeadLetter), "Workspace delete proof starts with two checked Active messages", report);
            foreach (var accept in new[] { false, true })
            {
                Exception? failure = null;
                _ = window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var dialog = Application.Current.Windows.OfType<DeleteMessagesWindow>().Single();
                        if (accept) ((TextBox)dialog.FindName("DeleteConfirmationInput")).Text = "DELETE";
                        Click(dialog, accept ? "ConfirmDeleteButton" : "CancelButton");
                    }
                    catch (Exception error)
                    {
                        failure = error;
                        foreach (var dialog in Application.Current.Windows.OfType<DeleteMessagesWindow>().ToArray()) dialog.Close();
                    }
                }), DispatcherPriority.ApplicationIdle);
                Click(window, "DeleteButton");
                if (failure is not null) throw failure;
                await Settle();
                if (!accept)
                    Check(selected.All(row => window.Workspace.SnapshotMessages().Any(stored => stored.Key == row.Key))
                        && entity.MessageCount == before.ToString(), "Main Delete action cancellation preserves checked messages and count", report);
            }
            Check(selected.All(row => !window.Workspace.SnapshotMessages().Any(stored => stored.Key == row.Key))
                && entity.MessageCount == (before - selected.Length).ToString()
                && !window.Workspace.Messages.Any(row => selected.Any(deleted => deleted.Key == row.Key)),
                "Main Delete action, typed confirmation and model removal delete exactly checked messages and update displayed count", report);
        }
        finally { window.Close(); }
        await Settle();
    }
    private static async Task<bool?> Confirm(PrototypeWindow owner, IReadOnlyList<MessageRow> targets, bool accept, string output, string name, List<string> report)
    {
        var dialog = new DeleteMessagesWindow(targets) { Owner = owner };
        Exception? failure = null;
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var confirm = (Button)dialog.FindName("ConfirmDeleteButton");
                var input = (TextBox)dialog.FindName("DeleteConfirmationInput");
                Check(!confirm.IsEnabled && input.Text.Length == 0, "Delete confirmation begins disabled with an empty confirmation field", report);
                ProofCapture.Save(dialog, output, name + "-disabled");
                if (accept)
                {
                    foreach (var invalid in new[] { "wrong", "delete", "DELETE ", " DELETE" })
                    {
                        input.Text = invalid;
                        Check(!confirm.IsEnabled, "Delete stays disabled for non-exact confirmation: '" + invalid + "'", report);
                    }
                    input.Text = "DELETE";
                    Check(confirm.IsEnabled, "Exact uppercase DELETE enables confirmation", report);
                    input.Clear();
                    Check(!confirm.IsEnabled, "Clearing typed DELETE disables confirmation again", report);
                    input.Text = "DELETE";
                    Check(confirm.IsEnabled, "Retyping exact DELETE restores the enabled action", report);
                }                ProofCapture.CheckBounds(dialog, ProofCapture.Descendants(dialog).OfType<Button>());
                ProofCapture.Save(dialog, output, name);
                Click(dialog, accept ? "ConfirmDeleteButton" : "CancelButton");
            }
            catch (Exception error) { failure = error; dialog.Close(); }
        }), DispatcherPriority.ApplicationIdle);
        var result = dialog.ShowDialog();
        if (failure is not null) throw failure;
        await Settle();
        return result;
    }

    private static async Task ProfilePalette(PrototypeWindow window, List<string> report, string output)
    {
        ProofCapture.Descendants(window).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Settings")
            .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Settle();
        var settings = Application.Current.Windows.OfType<PrototypeSettingsWindow>().Single();
        ((TabItem)settings.FindName("ConnectionsTab")).IsSelected = true;
        await Settle();
        var profile = (PrototypeConnectionProfile)((ListBox)settings.FindName("ProfilesList")).SelectedItem;
        var original = profile.ColorHex;
        var picker = (ListBox)settings.FindName("ColorPicker");
        picker.SelectedIndex = 2;
        Click(settings, "SaveProfileButton");
        await Settle();
        Check(profile.ColorHex == ((ProfileAccent)picker.SelectedItem).ColorHex, "Saving a connection profile retains its chosen palette color", report);
        ProofCapture.Save(settings, output, "settings-palette");
        settings.Width = 460; settings.Height = 520;
        picker.BringIntoView();
        await Settle();
        ProofCapture.Save(settings, output, "settings-palette-compact");
        picker.SelectedValue = original;
        Click(settings, "SaveProfileButton");
        Click(settings, "DoneButton");
        await Settle();
    }

    private static async Task ConnectionHealth(PrototypeWindow window, List<string> report, string output)
    {
        var selector = (ComboBox)window.FindName("ConnectionSelector");
        var original = selector.SelectedItem;
        var label = (TextBlock)window.FindName("ConnectionHealthText");
        var dot = (System.Windows.Shapes.Ellipse)window.FindName("ConnectionHealthDot");
        if (!window.Workspace.IsConnected) Click(window, "ConnectionButton");
        Call(window, "ClearConnectionWarning");
        await Settle();
        Check(label.Text == "Connected" && ((SolidColorBrush)dot.Fill).Color == (Color)ColorConverter.ConvertFromString("#17823B"),
            "Connected health renders a green indicator and explicit label", report);
        Call(window, "MarkConnectionWarning", "Sample diagnostic warning");
        await Settle();
        Check(label.Text == "Warning" && ((SolidColorBrush)dot.Fill).Color == (Color)ColorConverter.ConvertFromString("#A56700")
            && label.ToolTip?.ToString() == "Sample diagnostic warning", "Connection warning renders amber with diagnostic detail", report);
        ProofCapture.Save(window, output, "connection-warning");
        selector.SelectedItem = selector.Items.Cast<PrototypeConnectionProfile>().First(profile => !ReferenceEquals(profile, original));
        await Settle();
        Check(!window.Workspace.IsConnected && label.Text == "Disconnected" && ((SolidColorBrush)dot.Fill).Color == (Color)ColorConverter.ConvertFromString("#C83B3B"),
            "Header profile selector switches connection and renders disconnected health in red", report);
        selector.SelectedItem = original;
        Click(window, "ConnectionButton");
        Call(window, "ClearConnectionWarning");
        await Settle();
        Check(window.Workspace.IsConnected && label.Text == "Connected", "Returning to the original profile and connecting restores green health", report);
        var width = window.Width; var height = window.Height;
        window.Width = 980; window.Height = 640;
        await Settle();
        ProofCapture.CheckBounds(window, [selector, label]);
        ProofCapture.Save(window, output, "expanded-controls-compact");
        window.Width = width; window.Height = height;
        await Settle();
        ProofCapture.Save(window, output, "expanded-controls");
    }

    private static void Call(PrototypeWindow window, string method, params object[] arguments) =>
        typeof(PrototypeWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static void Toggle(ToggleButton button) => ((IToggleProvider)(UIElementAutomationPeer.CreatePeerForElement(button) ?? new ToggleButtonAutomationPeer(button)).GetPattern(PatternInterface.Toggle)).Toggle();
    private static void Click(Window window, string name) => ((ButtonBase)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
