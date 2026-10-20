namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>
/// Turning a captured Service Bus message into a Workbench draft: the time to live it had when enqueued and the
/// Compose type of each application property.
/// </summary>
public static class WorkbenchCapture
{
    /// <summary>
    /// The captured message's time to live in whole minutes, rounded up, when the user copies it and the message has
    /// both times; otherwise <c>null</c>, so the draft inherits the entity default.
    /// </summary>
    public static int? TtlMinutes(bool copy, DateTimeOffset? expiresAt, DateTimeOffset? enqueuedTime) =>
        copy && expiresAt is { } expiry && enqueuedTime is { } enqueued ? (int)Math.Ceiling((expiry - enqueued).TotalMinutes) : null;

    /// <summary>The Compose property type for a captured application property value; anything else is a string.</summary>
    public static string PropertyType(object? value) => value switch
    {
        bool => "boolean", int => "int", long => "long", decimal => "decimal",
        double => "double", Guid => "guid", DateTime or DateTimeOffset => "dateTimeUtc", _ => "string"
    };
}
