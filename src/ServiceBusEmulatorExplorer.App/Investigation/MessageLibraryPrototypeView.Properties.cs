using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeView
{
    private void PropertyNameEditor_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox input) input.Focus();
    }

    private void ApplicationPropertiesGrid_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (ApplicationPropertiesGrid.IsKeyboardFocusWithin) return;
            if (ApplicationPropertiesGrid.CurrentColumn?.GetCellContent(ApplicationPropertiesGrid.CurrentItem)
                is ComboBox { IsDropDownOpen: true }) return;
            if (CommitApplicationPropertyEdit()) RemoveEmptyProperties();
        }));
    }

    private void ApplicationPropertiesGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is PrototypeProperty { IsEmpty: true })
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                ApplicationPropertiesGrid.CancelEdit(DataGridEditingUnit.Row);
                var editing = (IEditableCollectionView)ApplicationPropertiesGrid.Items;
                if (editing.IsAddingNew) editing.CancelNew();
                RemoveEmptyProperties();
            }));
        }
        else
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(InvalidatePreview));
    }

    private void RemoveEmptyProperties()
    {
        var editing = (IEditableCollectionView)ApplicationPropertiesGrid.Items;
        if (editing.IsAddingNew || editing.IsEditingItem) return;
        foreach (var property in applicationProperties.Where(property => property.IsEmpty).ToArray())
            applicationProperties.Remove(property);
        InvalidatePreview();
    }

    private void DeleteProperty_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PrototypeProperty property } || !CommitApplicationPropertyEdit()) return;
        applicationProperties.Remove(property);
        InvalidatePreview();
    }

    private void ApplicationPropertiesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || e.OriginalSource is TextBoxBase or ComboBox or CheckBox) return;
        var selected = ApplicationPropertiesGrid.SelectedItems.OfType<PrototypeProperty>().ToArray();
        if (!CommitApplicationPropertyEdit()) return;
        foreach (var property in selected) applicationProperties.Remove(property);
        InvalidatePreview();
        e.Handled = true;
    }

    private bool CommitApplicationPropertyEdit()
    {
        var editing = (IEditableCollectionView)ApplicationPropertiesGrid.Items;
        if (editing.IsAddingNew && editing.CurrentAddItem is PrototypeProperty { IsEmpty: true })
        {
            ApplicationPropertiesGrid.CancelEdit(DataGridEditingUnit.Cell);
            ApplicationPropertiesGrid.CancelEdit(DataGridEditingUnit.Row);
            if (editing.IsAddingNew) editing.CancelNew();
            return true;
        }
        return ApplicationPropertiesGrid.CommitEdit(DataGridEditingUnit.Cell, true) &&
            ApplicationPropertiesGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }
}
