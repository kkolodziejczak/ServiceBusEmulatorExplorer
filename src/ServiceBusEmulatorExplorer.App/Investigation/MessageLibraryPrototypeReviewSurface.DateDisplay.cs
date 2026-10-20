using System.Windows.Data;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation;
using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeReviewSurface
{
    internal void RefreshDatePresentation()
    {
        if (ScheduledGrid is null || HistoryGrid is null) return;
        DateDisplayFormat format = DatePresentation.GetFormat(this);
        workbenchRun.UseDateFormat(format);
        ScheduledGrid.Items.Refresh();
        AttemptDateColumn.Binding = new Binding(nameof(PrototypeCancellationAttempt.RequestedAtUtc))
        {
            Converter = new DateTimestampConverter(format)
        };
        CancellationTarget.Text = $"{ReviewProfile.Text} · localhost · {ReviewTarget.Text} · " + workbenchRun.DueTimes;
        UpdateReviewTiming();
    }
}
