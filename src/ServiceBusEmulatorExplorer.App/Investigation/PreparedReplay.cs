using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>A side-effect-free review candidate. Only the workspace can prepare one.</summary>
public sealed class PreparedReplay
{
    internal PreparedReplay(string profileId, string namespaceFingerprint, MessageDelivery delivery,
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
