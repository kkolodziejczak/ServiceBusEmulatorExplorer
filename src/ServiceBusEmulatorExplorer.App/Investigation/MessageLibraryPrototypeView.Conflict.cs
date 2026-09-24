using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeView
{
    private const string ExternalChangeSample = "External change sample";
    private bool externalRevisionApplied;
    private bool externalConflictPending;

    private bool RefreshExternalChangeSample()
    {
        if (selectedTemplateName != ExternalChangeSample) return false;
        if (!externalRevisionApplied)
        {
            bool dirty = HasUnsavedDraft();
            int index = templates.FindIndex(template => template.Name == ExternalChangeSample);
            templates[index] = templates[index] with
            {
                Body = "{\n  \"customerId\": \"$(CustomerId)\",\n  \"externalRevision\": 2\n}"
            };
            externalRevisionApplied = true;
            externalConflictPending = dirty;
            if (!dirty) ReloadExternalChangeSample();
        }
        if (externalConflictPending) ResolveExternalChangeConflict();
        else RefreshTemplateList();
        return true;
    }

    private void ResolveExternalChangeConflict()
    {
        var saved = templates.Single(template => template.Name == ExternalChangeSample);
        var dialog = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Conflict,
            currentProfileName, selectedDestination ?? "", 1) { Owner = Window.GetWindow(this) };
        dialog.ConflictFileName.Text = "External change sample.sbetemplate.json (in-memory sample)";
        dialog.SetConflictVersions(Fingerprint(saved.Body), Fingerprint(currentBody));
        dialog.ShowDialog();
        switch (dialog.DraftChoice)
        {
            case PrototypeDraftChoice.Reload:
                ReloadExternalChangeSample();
                break;
            case PrototypeDraftChoice.SaveAs:
                SaveAs_Click(this, new RoutedEventArgs());
                if (selectedTemplateName != ExternalChangeSample) externalConflictPending = false;
                break;
            default:
                AuthorSubtitle.Text = "External change pending · Your draft is preserved. Refresh to resolve.";
                break;
        }
    }

    private void ReloadExternalChangeSample()
    {
        var saved = templates.Single(template => template.Name == ExternalChangeSample);
        currentBody = saved.Body;
        currentAssociations.Clear();
        currentAssociations.AddRange(TemplateAssociations(saved));
        explicitDestination = false;
        UpdateAssociations();
        RestoreSettings(savedSettings[ExternalChangeSample]);
        draftIsNew = false;
        externalConflictPending = false;
        ShowEditorBody();
        InvalidatePreview();
        AuthorSubtitle.Text = "Reloaded the simulated external revision. No file was read.";
        RefreshTemplateList();
    }

    private static string Fingerprint(string body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
}
