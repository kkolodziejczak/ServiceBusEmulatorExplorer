using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeView
{
    private Point dragOrigin;
    private string? dragTag;
    private TreeViewItem? dropHighlight;
    private sealed record LibraryDrag(MessageLibraryPrototypeView Owner, string Tag);

    private void MoveLibraryItem_Click(string? tag)
    {
        if (tag is null) return;
        string sourceFolder = tag.StartsWith("folder:", StringComparison.Ordinal) ? LibraryFolders.Parent(tag[7..])
            : library.Templates.FirstOrDefault(template => "template:" + template.Name == tag)?.Folder ?? "";
        var dialog = new TemplateLocationDialog("Move to", tag, library.Folders, sourceFolder,
            (_, destination) => ValidateLibraryMove(tag, destination), moving: true) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) MoveLibraryItem(tag, dialog.FolderPath);
    }

    private string? ValidateLibraryMove(string tag, string destination) =>
        LibraryFolders.MoveProblem(tag, destination, library.Folders, library.TemplateNames);

    private void MoveLibraryItem(string tag, string destination)
    {
        if (ValidateLibraryMove(tag, destination) is not null) return;
        CancelRename();
        if (tag.StartsWith("template:", StringComparison.Ordinal))
        {
            library.MoveTemplate(tag[9..], destination);
            if (selectedTemplateName == tag[9..]) editorFolder = destination;
            revealedTemplateName = tag[9..];
            selectedLibraryTag = tag;
        }
        else
        {
            string replacement = LibraryFolders.Join(destination, LibraryFolders.Name(tag[7..]));
            RelocateFolder(tag[7..], replacement);
            library.MarkCreated(replacement);
            selectedLibraryTag = "folder:" + replacement;
        }
        selectedFolder = destination.Length == 0 ? null : destination;
        ExpandFolderPath(destination);
        RefreshLibraryTree();
        RevealLibraryItem(selectedLibraryTag!);
    }

    private void LibraryTree_DragStart(object sender, MouseButtonEventArgs e)
    {
        dragTag = null;
        if (e.OriginalSource is not DependencyObject source || FindAncestor<TextBox>(source) is not null ||
            FindAncestor<Button>(source) is not null) return;
        var item = FindAncestor<TreeViewItem>(source);
        dragOrigin = e.GetPosition(LibraryTree);
        dragTag = item?.Tag as string;
    }

    private void LibraryTree_DragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { dragTag = null; return; }
        if (dragTag is null || activeRenameInput is not null) return;
        Point current = e.GetPosition(LibraryTree);
        if (Math.Abs(current.X - dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        string tag = dragTag;
        dragTag = null;
        try { DragDrop.DoDragDrop(LibraryTree, new DataObject(typeof(LibraryDrag), new LibraryDrag(this, tag)), DragDropEffects.Move); }
        finally { ClearDropHighlight(); }
        e.Handled = true;
    }

    private void LibraryTree_DragOver(object sender, DragEventArgs e)
    {
        ClearDropHighlight();
        e.Effects = DragDropEffects.None;
        if (TryGetLibraryDrop(e, out _, out _, out var target))
        {
            e.Effects = DragDropEffects.Move;
            if (target?.Header is Grid header)
            {
                dropHighlight = target;
                header.Background = (Brush)FindResource("SelectionBrush");
            }
        }
        e.Handled = true;
    }

    private bool TryGetLibraryDrop(DragEventArgs e, out LibraryDrag? drag, out string destination, out TreeViewItem? target)
    {
        drag = e.Data.GetData(typeof(LibraryDrag)) as LibraryDrag;
        target = e.OriginalSource is DependencyObject source ? FindAncestor<TreeViewItem>(source) : null;
        string? targetTag = target?.Tag as string;
        destination = targetTag?.StartsWith("folder:", StringComparison.Ordinal) == true ? targetTag[7..]
            : targetTag?.StartsWith("template:", StringComparison.Ordinal) == true
                ? library.Find(targetTag[9..])?.Folder ?? "" : "";
        return drag?.Owner == this && ValidateLibraryMove(drag.Tag, destination) is null;
    }

    private void LibraryTree_DragLeave(object sender, DragEventArgs e) => ClearDropHighlight();

    private void ClearDropHighlight()
    {
        if (dropHighlight?.Header is Grid header) header.ClearValue(Panel.BackgroundProperty);
        dropHighlight = null;
    }

    private void LibraryTree_Drop(object sender, DragEventArgs e)
    {
        ClearDropHighlight();
        if (TryGetLibraryDrop(e, out var drag, out string destination, out _))
            MoveLibraryItem(drag!.Tag, destination);
        e.Handled = true;
    }
}
