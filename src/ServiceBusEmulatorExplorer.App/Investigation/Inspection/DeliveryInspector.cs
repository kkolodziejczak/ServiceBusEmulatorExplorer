using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using ICSharpCode.AvalonEdit.Document;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation.Inspection;

/// <summary>
/// Maintains the selected delivery's inspection documents and session-only DLQ edits.
/// </summary>
public sealed class DeliveryInspector : ObservableObject
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Dictionary<DeliveryIdentity, CachedDocument> _documents = [];
    private readonly TextDocument _emptyDocument = new();
    private MessageDelivery? _current;
    private CachedDocument? _currentDocument;
    private TextDocument _document;

    public DeliveryInspector()
    {
        _document = _emptyDocument;
    }

    public MessageDelivery? Current => _current;

    /// <summary>The AvalonEdit document for the selected delivery.</summary>
    public TextDocument Document => _document;

    public bool IsDirty => _currentDocument is not null
        && !string.Equals(Document.Text, _currentDocument.OriginalDocumentText, StringComparison.Ordinal);

    public bool HasDrafts => _documents.Values.Any(document => document.IsDirty);

    public bool HasDraft(DeliveryIdentity identity) => _documents.TryGetValue(identity, out var document) && document.IsDirty;

    /// <summary>Whether the current editor text is valid JSON for a later replay operation.</summary>
    public bool IsValidJson => _currentDocument is not null
        && MessageInspectionPresenter.Present(Document.Text).State == JsonDocumentState.Json;

    public bool IsReadOnly => Current?.Identity.Bucket != MessageBucket.DeadLetter;

    public string JsonLabel => _currentDocument is null
        ? "JSON"
        : IsValidJson ? "JSON" : "Body (not JSON)";

    /// <summary>The exact UTF-8 text received, or Base64 when the original bytes are not valid UTF-8.</summary>
    public string RawText => _currentDocument?.RawText ?? string.Empty;

    public string RawDescription => _currentDocument?.RawDescription ?? "No message selected.";

    public string PropertiesText => _currentDocument?.PropertiesText ?? string.Empty;

    /// <summary>Selects a delivery while retaining any cached document and undo history for its identity.</summary>
    public void Select(MessageDelivery? delivery)
    {
        if (delivery is null)
        {
            SetCurrent(null, null);
            return;
        }

        if (!_documents.TryGetValue(delivery.Identity, out CachedDocument? cached))
        {
            cached = CreateDocument(delivery);
            _documents.Add(delivery.Identity, cached);
        }

        SetCurrent(delivery, cached);
    }

    /// <summary>Restores the selected document to its initial presented form.</summary>
    public void DiscardCurrent()
    {
        DiscardDocument(_currentDocument);
    }

    public void DiscardReplayedDraft(DeliveryIdentity identity, string sentText)
    {
        if (_documents.TryGetValue(identity, out var cached) && cached.Document.Text == sentText)
            DiscardDocument(cached);
    }

    private void DiscardDocument(CachedDocument? cached)
    {
        if (cached is null)
        {
            return;
        }

        cached.IsApplyingChange = true;
        try
        {
            cached.Document.Replace(0, cached.Document.TextLength, cached.OriginalDocumentText);
            cached.Document.UndoStack.ClearAll();
        }
        finally
        {
            cached.IsApplyingChange = false;
        }

        NotifyDocumentStateChanged();
    }

    /// <summary>Releases all session-only documents and drafts after the caller has obtained approval.</summary>
    public void Clear()
    {
        foreach (CachedDocument cached in _documents.Values)
        {
            cached.Document.TextChanged -= Document_TextChanged;
        }

        _documents.Clear();
        SetCurrent(null, null);
    }

    public void ForgetDeleted(IReadOnlySet<DeliveryIdentity> identities)
    {
        foreach (var identity in identities)
            if (_documents.Remove(identity, out var cached)) cached.Document.TextChanged -= Document_TextChanged;
        if (Current is not null && identities.Contains(Current.Identity)) SetCurrent(null, null);
        NotifyDocumentStateChanged();
    }

    private CachedDocument CreateDocument(MessageDelivery delivery)
    {
        (string rawText, string rawDescription) = DecodeBody(delivery.Message);
        InspectionText presentation = MessageInspectionPresenter.Present(rawText);
        var cached = new CachedDocument(
            new TextDocument(presentation.DisplayText),
            presentation.DisplayText,
            rawText,
            rawDescription,
            MessageInspectionPresenter.PresentProperties(delivery.Message).DisplayText);
        cached.Document.TextChanged += Document_TextChanged;
        return cached;
    }

    private static (string RawText, string RawDescription) DecodeBody(ExplorerMessage message)
    {
        if (message.RawBody is null)
        {
            InspectionText presentation = MessageInspectionPresenter.PresentBody(message);
            return (presentation.RawText, presentation.StateDescription);
        }

        byte[] bytes = message.RawBody.ToArray();
        try
        {
            string text = StrictUtf8.GetString(bytes);
            InspectionText presentation = MessageInspectionPresenter.Present(text);
            return (text, presentation.StateDescription);
        }
        catch (DecoderFallbackException)
        {
            return (
                Convert.ToBase64String(bytes),
                "Binary body; original bytes are not valid UTF-8 and are shown as Base64.");
        }
    }

    private void SetCurrent(MessageDelivery? delivery, CachedDocument? cached)
    {
        bool documentChanged = !ReferenceEquals(_currentDocument?.Document, cached?.Document);
        bool currentChanged = !EqualityComparer<MessageDelivery?>.Default.Equals(Current, delivery);
        TextDocument nextDocument = cached?.Document ?? _emptyDocument;

        if (currentChanged)
        {
            _current = delivery;
        }

        _currentDocument = cached;
        if (documentChanged)
        {
            _document = nextDocument;
        }

        if (currentChanged)
        {
            OnPropertyChanged(nameof(Current));
        }

        if (documentChanged)
        {
            OnPropertyChanged(nameof(Document));
        }

        if (documentChanged || currentChanged)
        {
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(JsonLabel));
            OnPropertyChanged(nameof(RawText));
            OnPropertyChanged(nameof(RawDescription));
            OnPropertyChanged(nameof(PropertiesText));
            OnPropertyChanged(nameof(IsValidJson));
        }
    }

    private void Document_TextChanged(object? sender, EventArgs e)
    {
        if (sender is not TextDocument document)
        {
            return;
        }

        CachedDocument? cached = _documents.Values.FirstOrDefault(candidate => ReferenceEquals(candidate.Document, document));
        if (cached is null || cached.IsApplyingChange)
        {
            return;
        }

        if (ReferenceEquals(cached, _currentDocument))
        {
            NotifyDocumentStateChanged();
        }
        else
        {
            OnPropertyChanged(nameof(HasDrafts));
        }
    }

    private void NotifyDocumentStateChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasDrafts));
        OnPropertyChanged(nameof(IsValidJson));
        OnPropertyChanged(nameof(JsonLabel));
    }

    private sealed class CachedDocument(
        TextDocument document,
        string originalDocumentText,
        string rawText,
        string rawDescription,
        string propertiesText)
    {
        public TextDocument Document { get; } = document;
        public string OriginalDocumentText { get; } = originalDocumentText;
        public string RawText { get; } = rawText;
        public string RawDescription { get; } = rawDescription;
        public string PropertiesText { get; } = propertiesText;
        public bool IsApplyingChange { get; set; }

        public bool IsDirty => !string.Equals(Document.Text, OriginalDocumentText, StringComparison.Ordinal);
    }
}
