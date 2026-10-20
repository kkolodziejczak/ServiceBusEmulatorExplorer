using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeView
{
    private IReadOnlyList<PrototypePreparedMessage> preparedMessages = [];
    private string? preparationError;

    /// <summary>
    /// The Workbench's clock: when prepared messages occur, and the scheduling time of the reviews it opens. The system
    /// clock unless a screenshot or test fixes it.
    /// </summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>Creates each prepared message's event ID; new GUIDs unless a screenshot or test fixes them.</summary>
    public Func<Guid> NewEventId { get; set; } = Guid.NewGuid;

    private void ClearPreparedMessages()
    {
        preparedMessages = [];
        preparationError = null;
        if (SingleEventId is not null) SingleEventId.Text = "Waiting for valid inputs";
        if (SingleOccurredAt is not null) SingleOccurredAt.Text = "Waiting for valid inputs";
    }

    private bool PrepareMessages(IEnumerable<CsvPreviewRow> rows)
    {
        var template = new PreparationTemplate(currentBody,
            variables.Select(variable => new TemplateVariable(variable.Name, variable.Type, variable.Source, variable.HasDefault,
                variable.DefaultValue)).ToArray(),
            [.. applicationProperties.Select(property => property.Value), PropertySubject.Text, PropertyCorrelationId.Text,
                PropertySessionId.Text, PropertyReplyTo.Text, PropertyReplySessionId.Text, PropertyPartitionKey.Text],
            CustomMessageId.IsChecked == true ? CustomMessageIdInput.Text : null);
        var result = WorkbenchPreparation.Prepare(rows.Select(row => new PreparationRow(row.Row, row.CustomerId, row.Amount!.Value)),
            template, Clock.GetUtcNow(), NewEventId);
        if (result.Messages is not { } prepared)
        {
            preparationError = result.Error;
            PreviewText.Text = "No preview generated.";
            return false;
        }

        preparedMessages = prepared.Select(message => new PrototypePreparedMessage(message.Row, message.MessageId, message.EventId,
            message.OccurredAt, message.Body) { VariableValues = message.VariableValues }).ToArray();
        if (SingleMode.IsChecked == true && prepared.Count == 1)
        {
            SingleEventId.Text = prepared[0].EventId;
            SingleOccurredAt.Text = prepared[0].OccurredAt;
        }
        return true;
    }
}
