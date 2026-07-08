namespace ServiceBusEmulatorExplorer.Core.Connection;

public interface IConnectionProfileStore
{
    Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken);
}
