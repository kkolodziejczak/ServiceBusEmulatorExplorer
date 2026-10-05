using System.IO;
using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

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

    public static string Describe(ReplaySendFailure failure) => failure switch
    {
        ReplaySendFailure.Cancelled => "Send cancelled or connection changed. Check status before retrying.",
        ReplaySendFailure.Timeout => "Send timed out. Check status before retrying.",
        ReplaySendFailure.Authorization => "Authorization failed. Check the connection credentials and permissions.",
        ReplaySendFailure.EntityUnavailable => "Destination unavailable. Check the destination entity.",
        ReplaySendFailure.ServiceBusy => "Service busy. Check status before retrying.",
        ReplaySendFailure.QuotaExceeded => "Broker quota exceeded. Check destination capacity.",
        ReplaySendFailure.Transport => "Transport error. Check the connection and status before retrying.",
        ReplaySendFailure.Unknown => "Send failed. Check the connection and status before retrying.",
        _ => ""
    };
}
