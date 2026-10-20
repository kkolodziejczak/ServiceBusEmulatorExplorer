using System.IO;
using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>Only allowlisted categories cross the durable-history boundary; never exception text.</summary>
internal static class ReplaySendDiagnostics
{
    public static ReplaySendFailure Classify(Exception exception) => exception switch
    {
        OperationCanceledException => ReplaySendFailure.Cancelled,
        TimeoutException => ReplaySendFailure.Timeout,
        UnauthorizedAccessException => ReplaySendFailure.Authorization,
        IOException => ReplaySendFailure.Transport,
        ServiceBusException failure => failure.Reason switch
        {
            ServiceBusFailureReason.ServiceTimeout => ReplaySendFailure.Timeout,
            ServiceBusFailureReason.MessagingEntityNotFound or ServiceBusFailureReason.MessagingEntityDisabled => ReplaySendFailure.EntityUnavailable,
            ServiceBusFailureReason.ServiceBusy => ReplaySendFailure.ServiceBusy,
            ServiceBusFailureReason.QuotaExceeded => ReplaySendFailure.QuotaExceeded,
            ServiceBusFailureReason.ServiceCommunicationProblem => ReplaySendFailure.Transport,
            _ => ReplaySendFailure.Unknown
        },
        _ => ReplaySendFailure.Unknown
    };
}
