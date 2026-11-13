using System.Windows.Data;
using ServiceBusWorkbench.Core.Investigation;
using ServiceBusWorkbench.Presentation;
using ServiceBusWorkbench.Presentation.Workbench;

namespace ServiceBusWorkbench.App.Investigation;

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
