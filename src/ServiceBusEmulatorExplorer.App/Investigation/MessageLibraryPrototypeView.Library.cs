using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeView
{
    private readonly Dictionary<string, (TextBox Input, TextBlock Label, TextBlock Error, Grid Editor)> treeRenameControls = new(StringComparer.OrdinalIgnoreCase);

    private bool loadingDestinationSelection;
    private bool libraryInitialized;
    private string? revealedTemplateName;
    private TextBox? activeRenameInput;
    private readonly HashSet<string> createdFolders = new(StringComparer.OrdinalIgnoreCase);

    private bool IsFolderVisible(string path, IReadOnlyList<PrototypeTemplate> visible) =>
        (namespaceQuery.Length == 0 && TemplateSearch.Text.Trim().Length == 0) ||
        visible.Any(template => template.Folder.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            template.Folder.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)) ||
        createdFolders.Any(created => created.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            created.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase));

    private void UpdateTreeHeaderWidths()
    {
        if (LibraryTree.ActualWidth <= 0) return;
        foreach (var item in AllTreeItems())
        {
            if (item.Header is not Grid header) continue;
            int depth = 0;
            for (var parent = ItemsControl.ItemsControlFromItemContainer(item) as TreeViewItem;
                 parent is not null; parent = ItemsControl.ItemsControlFromItemContainer(parent) as TreeViewItem)
                depth++;
            header.MaxWidth = Math.Max(24, LibraryTree.ActualWidth - (depth + 1) * 18 - 27);
        }
    }

    private void ExpandFolderPath(string? path)
    {
        while (!string.IsNullOrEmpty(path))
        {
            collapsedFolders.Remove(path);
            path = ParentFolder(path);
        }
    }

    private void RefreshLibraryTree()
    {
        if (LibraryTree is null || TemplateSearch is null) return;
        if (activeRenameInput != LibraryRenameInput) CancelRename();
        refreshingTemplates = true;
        try
        {
            string search = TemplateSearch.Text.Trim();
            var matchingFolders = search.Length == 0 ? [] : folders.Where(path =>
                path.Split('/').Any(segment => segment.Contains(search, StringComparison.OrdinalIgnoreCase))).ToArray();
            var visible = templates.Where(template =>
                (namespaceQuery.Length == 0 || TemplateAssociations(template).Any(path => path.Contains(namespaceQuery, StringComparison.OrdinalIgnoreCase))) &&
                (search.Length == 0 || template.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 template.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 matchingFolders.Any(path => template.Folder.Equals(path, StringComparison.OrdinalIgnoreCase) || template.Folder.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))) ||
                ((draftIsNew && template.Name == selectedTemplateName) || template.Name == revealedTemplateName)).ToList();
            LibraryTree.Items.Clear();
            treeRenameControls.Clear();
            foreach (var folder in folders.Where(path => !HasParentFolder(path) && IsFolderVisible(path, visible)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                LibraryTree.Items.Add(CreateFolderTreeItem(folder, visible));
            foreach (var template in visible.Where(item => string.IsNullOrEmpty(item.Folder)))
                LibraryTree.Items.Add(CreateTemplateTreeItem(template));
            if (selectedLibraryTag is not null && FindTreeItem(selectedLibraryTag) is { } selected)
                selected.IsSelected = true;
            else if (selectedLibraryTag is not null)
            {
                selectedLibraryTag = null;
                selectedFolder = null;
            }
            foreach (var item in AllFolderItems())
                item.IsExpanded = !collapsedFolders.Contains(item.Tag!.ToString()![7..]);
        }
        finally { refreshingTemplates = false; UpdateTreeHeaderWidths(); }
    }

    private bool HasParentFolder(string path) => path.Contains('/', StringComparison.Ordinal);

    private TreeViewItem CreateFolderTreeItem(string path, IReadOnlyList<PrototypeTemplate> visible)
    {
        string name = path.Split('/')[^1];
        var item = new TreeViewItem { Tag = $"folder:{path}", IsExpanded = !collapsedFolders.Contains(path), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        item.Style = (Style)FindResource("WorkbenchTreeItem");
        SetTreeItemName(item, name);
        item.Header = CreateTreeHeader(item.Tag.ToString()!, name, true);
        item.ToolTip = path;
        AddRenameMenu(item);
        foreach (string childFolder in folders.Where(candidate =>
                     candidate.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase) &&
                     candidate.Count(ch => ch == '/') == path.Count(ch => ch == '/') + 1 && IsFolderVisible(candidate, visible))
                 .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase))
            item.Items.Add(CreateFolderTreeItem(childFolder, visible));
        foreach (var template in visible.Where(template => string.Equals(template.Folder, path, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase))
            item.Items.Add(CreateTemplateTreeItem(template));
        item.Expanded += (_, _) => collapsedFolders.Remove(path);
        item.Collapsed += (_, _) => collapsedFolders.Add(path);
        item.PreviewMouseRightButtonDown += (_, _) => item.IsSelected = true;
        return item;
    }

    private TreeViewItem CreateTemplateTreeItem(PrototypeTemplate template)
    {
        var item = new TreeViewItem { Tag = $"template:{template.Name}", HorizontalContentAlignment = HorizontalAlignment.Stretch };
        item.Style = (Style)FindResource("WorkbenchTreeItem");
        SetTreeItemName(item, template.Name);
        item.Header = CreateTreeHeader(item.Tag.ToString()!, template.Name, false);
        item.ToolTip = string.IsNullOrEmpty(template.FileName) ? template.Name : $"{template.Name}\n{template.FileName}";
        AddDestinationWarning(item, template);
        AddRenameMenu(item);
        item.PreviewMouseRightButtonDown += (_, _) => item.IsSelected = true;
        return item;
    }

    private static void SetTreeItemName(TreeViewItem item, string name)
    {
        System.Windows.Automation.AutomationProperties.SetName(item, name);
        System.Windows.Automation.AutomationProperties.SetAutomationId(item, "LibraryTreeItem");
    }

    private UIElement CreateTreeHeader(string tag, string name, bool folder)
    {
        var row = new Grid { MinHeight = 28, Margin = new Thickness(2, 1, 3, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Path
        {
            Data = Geometry.Parse(folder ? "M1,4 H7 L9,6 H17 V16 H1 Z" : "M3,1 H13 L17,5 V19 H3 Z M6,8 H14 M6,11 H14 M6,14 H12"),
            Stroke = (Brush)FindResource("PrimaryBrush"), StrokeThickness = 1.5,
            Width = folder ? 18 : 15, Height = folder ? 16 : 17, Stretch = Stretch.Uniform,
            Margin = new Thickness(3, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);
        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("ActionTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        var editor = new Grid { Visibility = Visibility.Collapsed };
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var input = new TextBox { Text = name, MinHeight = 28, Padding = new Thickness(5, 2, 5, 2),
            VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(input, "Rename " + name);
        System.Windows.Automation.AutomationProperties.SetAutomationId(input, "LibraryRenameInput");
        input.KeyDown += LibraryRenameInput_KeyDown;
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        editor.Children.Add(input);
        var saveName = CreateRenameAction("Save name", "LibraryRenameSave", "M1,6 L5,10 L13,1", SaveRename_Click);
        Grid.SetColumn(saveName, 1);
        editor.Children.Add(saveName);
        var cancelName = CreateRenameAction("Cancel rename", "LibraryRenameCancel", "M1,1 L11,11 M11,1 L1,11", CancelRename_Click);
        Grid.SetColumn(cancelName, 2);
        editor.Children.Add(cancelName);
        var error = new TextBlock { Foreground = (Brush)FindResource("DestructiveBrush"), FontSize = 11,
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(4, 1, 0, 0) };
        Grid.SetRow(error, 1);
        Grid.SetColumnSpan(error, 3);
        editor.Children.Add(error);
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        treeRenameControls[tag] = (input, label, error, editor);
        return row;
    }

    private Button CreateRenameAction(string label, string automationId, string geometry, RoutedEventHandler action)
    {
        var button = new Button
        {
            Style = (Style)FindResource("IconButton"), Width = 28, Height = 28, Padding = new Thickness(5),
            ToolTip = label, Content = new Path { Data = Geometry.Parse(geometry),
                Stroke = (Brush)FindResource("PrimaryBrush"), StrokeThickness = 1.5, Stretch = Stretch.Uniform }
        };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, automationId);
        button.Click += action;
        return button;
    }

    private void AddRenameMenu(TreeViewItem item)
    {
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "Rename" };
        System.Windows.Automation.AutomationProperties.SetName(rename, "Rename");
        System.Windows.Automation.AutomationProperties.SetAutomationId(rename, "LibraryContextRename");
        rename.Click += (_, _) => BeginRenameTreeItem(item.Tag?.ToString());
        menu.Items.Add(rename);
        var move = new MenuItem { Header = "Move to..." };
        System.Windows.Automation.AutomationProperties.SetAutomationId(move, "LibraryMoveTo");
        move.Click += (_, _) => MoveLibraryItem_Click(item.Tag?.ToString());
        menu.Items.Add(move);
        item.ContextMenu = menu;
    }

    private IEnumerable<TreeViewItem> AllFolderItems()
    {
        foreach (TreeViewItem root in LibraryTree.Items)
        {
            if (root.Tag?.ToString()?.StartsWith("folder:", StringComparison.Ordinal) != true) continue;
            foreach (var item in DescendantItems(root))
                if (item.Tag?.ToString()?.StartsWith("folder:", StringComparison.Ordinal) == true) yield return item;
        }
    }

    private static IEnumerable<TreeViewItem> DescendantItems(TreeViewItem parent)
    {
        yield return parent;
        foreach (TreeViewItem child in parent.Items)
            foreach (var item in DescendantItems(child)) yield return item;
    }

    private TreeViewItem? FindTreeItem(string tag) => AllTreeItems().FirstOrDefault(item =>
        string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<TreeViewItem> AllTreeItems()
    {
        foreach (TreeViewItem root in LibraryTree.Items)
            foreach (var item in DescendantItems(root)) yield return item;
    }

    private string GetCreationFolder()
    {
        if (selectedLibraryTag is null) return "";
        if (selectedLibraryTag.StartsWith("folder:", StringComparison.Ordinal)) return selectedLibraryTag[7..];
        return templates.FirstOrDefault(template => template.Name == selectedLibraryTag[9..])?.Folder ?? "";
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        string parent = GetCreationFolder();
        string? name = PromptForName("Add folder", "Folder name", "New folder",
            value => !value.Contains('/') && !value.Contains('\\') && !folders.Any(folder =>
                ParentFolder(folder).Equals(parent, StringComparison.OrdinalIgnoreCase) &&
                FolderName(folder).Equals(value, StringComparison.OrdinalIgnoreCase)),
            "Use a unique folder name without path separators.");
        if (name is null) return;
        string path = string.IsNullOrEmpty(parent) ? name : $"{parent}/{name}";
        AddFolderAndParents(path);
        createdFolders.Add(path);
        ExpandFolderPath(path);
        selectedFolder = path;
        selectedLibraryTag = $"folder:{path}";
        RefreshLibraryTree();
        RevealLibraryItem(selectedLibraryTag);
    }

    private void RevealLibraryItem(string tag) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => FindTreeItem(tag)?.BringIntoView()));

    private void AddFolderAndParents(string path)
    {
        int separator = path.LastIndexOf('/');
        if (separator > 0) AddFolderAndParents(path[..separator]);
        if (!folders.Contains(path, StringComparer.OrdinalIgnoreCase)) folders.Add(path);
    }

    private void CollapseAllFolders_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in AllFolderItems())
        {
            item.IsExpanded = false;
            collapsedFolders.Add(item.Tag!.ToString()![7..]);
        }
    }

    private void LibraryTree_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<TreeViewItem>(source) is not null) return;
        ClearLibrarySelection();
    }

    private void ClearLibrarySelection()
    {
        foreach (var item in AllTreeItems()) item.IsSelected = false;
        selectedLibraryTag = null;
        selectedFolder = null;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    private void LibraryTree_SelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (refreshingTemplates || e.NewValue is not TreeViewItem item || item.Tag is not string tag) return;
        selectedLibraryTag = tag;
        if (tag.StartsWith("folder:", StringComparison.Ordinal))
        {
            selectedFolder = tag[7..];
            return;
        }
        if (tag.StartsWith("template:", StringComparison.Ordinal)) OpenTemplateFromTree(tag[9..]);
    }

    private void OpenTemplateFromTree(string name)
    {
        var template = templates.FirstOrDefault(value => value.Name == name);
        if (template is null || name == selectedTemplateName) return;
        if (!TryLeaveCurrentDraft())
        {
            string? previous = selectedTemplateName.Length == 0 ? null : $"template:{selectedTemplateName}";
            selectedLibraryTag = previous;
            revealedTemplateName = selectedTemplateName.Length == 0 ? null : selectedTemplateName;
            RefreshLibraryTree();
            if (previous is not null) RevealLibraryItem(previous);
            return;
        }
        CancelRename();
        revealedTemplateName = null;
        selectedTemplateName = name;
        editorFolder = template.Folder;
        selectedFolder = template.Folder;
        currentBody = template.Body;
        currentAssociation = template.Topic.Length > 0 ? template.Topic : TemplateAssociations(template).FirstOrDefault() ?? "";
        currentDestinationKind = template.DestinationKind;
        draftIsNew = false;
        previewReady = false;
        RestoreSettings(savedSettings[name]);
        AuthorTitle.Text = name;
        AuthorTitle.ToolTip = name;
        FilterWarning.Visibility = Visibility.Collapsed;
        UpdateDestinationControls();
        ShowEditorBody();
        ShowPrepare();
        InvalidatePreview();
        ShowWizardStage(WizardStage.Compose);
        RefreshLibraryTree();
        TemplateContextRequested?.Invoke(currentAssociation.Length == 0 ? null : currentAssociation);
    }

    private bool TryLeaveCurrentDraft()
    {
        if (!HasUnsavedDraft()) return true;
        var guard = new MessageLibraryPrototypeDialog(PrototypeDialogMode.DraftGuard, currentProfileName,
            selectedDestination ?? "order-events", 1) { Owner = Window.GetWindow(this) };
        guard.DraftGuardText.Text = $"You have unsaved changes to {selectedTemplateName}. What do you want to do?";
        guard.ShowDialog();
        if (guard.DraftChoice == PrototypeDraftChoice.Cancel) return false;
        if (guard.DraftChoice == PrototypeDraftChoice.Save)
        {
            Save_Click(this, new RoutedEventArgs());
            return !HasUnsavedDraft();
        }
        if (draftIsNew)
        {
            templates.RemoveAll(template => template.Name == selectedTemplateName);
            savedSettings.Remove(selectedTemplateName);
        }
        else if (templates.FirstOrDefault(template => template.Name == selectedTemplateName) is { } saved)
        {
            currentBody = saved.Body;
            currentAssociation = saved.Topic;
            currentDestinationKind = saved.DestinationKind;
            RestoreSettings(savedSettings[selectedTemplateName]);
            UpdateDestinationControls();
        }
        draftIsNew = false;
        return true;
    }

    private void LibraryTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || selectedLibraryTag is null) return;
        BeginRenameTreeItem(selectedLibraryTag);
        e.Handled = true;
    }

    private void BeginRenameCurrentTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (selectedTemplateName.Length == 0) return;
        CancelRename();
        renamingTag = $"template:{selectedTemplateName}";
        AuthorTitle.Visibility = Visibility.Collapsed;
        LibraryRenameButton.Visibility = Visibility.Collapsed;
        LibraryRenameInput.Text = selectedTemplateName;
        LibraryRenameError.Visibility = Visibility.Collapsed;
        activeRenameInput = LibraryRenameInput;
        LibraryRenameEditor.Visibility = Visibility.Visible;
        LibraryRenameInput.Focus();
        LibraryRenameInput.SelectAll();
    }

    private void BeginRenameTreeItem(string? tag)
    {
        if (tag is null || !treeRenameControls.TryGetValue(tag, out var controls)) return;
        CancelRename();
        renamingTag = tag;
        controls.Input.Text = controls.Label.Text;
        controls.Label.Visibility = Visibility.Collapsed;
        controls.Editor.Visibility = Visibility.Visible;
        controls.Error.Visibility = Visibility.Collapsed;
        controls.Error.Text = "";
        activeRenameInput = controls.Input;
        controls.Input.Focus();
        controls.Input.SelectAll();
    }

    private void LibraryRenameInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void SaveRename_Click(object sender, RoutedEventArgs e) => CommitRename();

    private void CancelRename_Click(object sender, RoutedEventArgs e) => CancelRename();

    private void CommitRename()
    {
        if (renamingTag is null) return;
        TextBox input;
        TextBlock error;
        Action close;
        if (activeRenameInput is { } selectedInput && treeRenameControls.TryGetValue(renamingTag, out var treeControls) && selectedInput == treeControls.Input)
        {
            input = treeControls.Input;
            error = treeControls.Error;
            close = () => { treeControls.Editor.Visibility = Visibility.Collapsed; treeControls.Label.Visibility = Visibility.Visible; };
        }
        else if (renamingTag.StartsWith("template:", StringComparison.Ordinal))
        {
            input = LibraryRenameInput;
            error = LibraryRenameError;
            close = () => { LibraryRenameEditor.Visibility = Visibility.Collapsed; LibraryRenameButton.Visibility = Visibility.Visible; AuthorTitle.Visibility = Visibility.Visible; LibraryRenameError.Visibility = Visibility.Collapsed; };
        }
        else return;
        if (activeRenameInput != input) return;
        string value = input.Text.Trim();
        string oldName = renamingTag.StartsWith("folder:", StringComparison.Ordinal)
            ? renamingTag[7..].Split('/')[^1] : renamingTag[9..];
        bool folder = renamingTag.StartsWith("folder:", StringComparison.Ordinal);
        string? duplicate = folder
            ? (folders.Any(path => !path.Equals(renamingTag[7..], StringComparison.OrdinalIgnoreCase) && ParentFolder(path).Equals(ParentFolder(renamingTag[7..]), StringComparison.OrdinalIgnoreCase) && FolderName(path).Equals(value, StringComparison.OrdinalIgnoreCase)) ? "A folder with that name already exists here." : null)
            : (templates.Any(template => !template.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase) && template.Name.Equals(value, StringComparison.OrdinalIgnoreCase)) ? "A template with that name already exists here." : null);
        if (folder && (value.Contains('/') || value.Contains('\\'))) duplicate = "Use a folder name without path separators.";
        if (value.Length == 0 || duplicate is not null)
        {
            error.Text = duplicate ?? "Enter a name.";
            error.Visibility = Visibility.Visible;
            input.Focus();
            return;
        }
        if (folder) RenameFolder(renamingTag[7..], value);
        else RenameTemplate(oldName, value);
        string newTag = folder ? $"folder:{ReplaceFolderSegment(renamingTag[7..], value)}" : $"template:{value}";
        renamingTag = null;
        activeRenameInput = null;
        close();
        selectedLibraryTag = newTag;
        RefreshLibraryTree();
    }

    private void CancelRename()
    {
        if (renamingTag is null) return;
        string tag = renamingTag;
        TextBox? active = activeRenameInput;
        renamingTag = null;
        activeRenameInput = null;
        if (active is not null && treeRenameControls.TryGetValue(tag, out var controls) && active == controls.Input)
        {
            controls.Editor.Visibility = Visibility.Collapsed;
            controls.Label.Visibility = Visibility.Visible;
            controls.Error.Visibility = Visibility.Collapsed;
        }
        else
        {
            LibraryRenameEditor.Visibility = Visibility.Collapsed;
            LibraryRenameButton.Visibility = Visibility.Visible;
            LibraryRenameError.Visibility = Visibility.Collapsed;
            AuthorTitle.Visibility = Visibility.Visible;
        }
        renamingTag = null;
        activeRenameInput = null;
    }

    private void RenameTemplate(string oldName, string newName)
    {
        int index = templates.FindIndex(template => template.Name == oldName);
        if (index < 0) return;
        var template = templates[index] with { Name = newName };
        templates[index] = template;
        if (savedSettings.Remove(oldName, out var settings)) savedSettings[newName] = settings;
        if (revealedTemplateName == oldName) revealedTemplateName = newName;
        if (selectedTemplateName == oldName)
        {
            selectedTemplateName = newName;
            AuthorTitle.Text = newName;
            AuthorTitle.ToolTip = newName;
        }
    }

    private void RenameFolder(string path, string newName) => RelocateFolder(path, ReplaceFolderSegment(path, newName));

    private void RelocateFolder(string path, string replacement)
    {
        foreach (var set in new[] { collapsedFolders, createdFolders })
            foreach (string entry in set.Where(entry => entry.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                         entry.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                set.Remove(entry);
                set.Add(replacement + entry[path.Length..]);
            }
        for (int index = 0; index < folders.Count; index++)
            if (folders[index].Equals(path, StringComparison.OrdinalIgnoreCase) || folders[index].StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))
                folders[index] = replacement + folders[index][path.Length..];
        for (int index = 0; index < templates.Count; index++)
            if (templates[index].Folder.Equals(path, StringComparison.OrdinalIgnoreCase) || templates[index].Folder.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))
                templates[index] = templates[index] with { Folder = replacement + templates[index].Folder[path.Length..] };
        if (editorFolder?.Equals(path, StringComparison.OrdinalIgnoreCase) == true || editorFolder?.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase) == true)
            editorFolder = replacement + editorFolder[path.Length..];
        if (selectedFolder?.Equals(path, StringComparison.OrdinalIgnoreCase) == true || selectedFolder?.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase) == true)
            selectedFolder = replacement + selectedFolder[path.Length..];
    }

    private static string ParentFolder(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
    private static string FolderName(string path) => path.Split('/')[^1];
    private static string ReplaceFolderSegment(string path, string name) =>
        ParentFolder(path) is { Length: > 0 } parent ? $"{parent}/{name}" : name;
}
