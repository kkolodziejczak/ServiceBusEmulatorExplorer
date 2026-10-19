namespace ServiceBusEmulatorExplorer.Core.Investigation;

/// <summary>
/// A side-effect-free review candidate: the delivery, its payload and the reserved replay ID, frozen when the
/// workspace prepared it. Reviewing it sends nothing; only the workspace sends it.
/// </summary>
public sealed class PreparedReplay
{
    public PreparedReplay(string profileId, string namespaceFingerprint, MessageDelivery delivery,
        ReplayReservation reservation, string? editedBody)
    {
        ProfileId = profileId;
        NamespaceFingerprint = namespaceFingerprint;
        Delivery = delivery;
        Reservation = reservation;
        EditedBody = editedBody;
        OriginalFingerprint = ReplayLineage.Fingerprint(delivery);
    }

    public Guid AttemptId { get; } = Guid.NewGuid();
    public string ProfileId { get; }
    public string NamespaceFingerprint { get; }
    public MessageDelivery Delivery { get; }
    public ReplayReservation Reservation { get; }
    public string? EditedBody { get; }
    public string OriginalFingerprint { get; }
}
