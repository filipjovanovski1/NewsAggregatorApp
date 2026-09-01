using NewsApplication.Domain.DomainModels.Discovery;

namespace NewsApplication.Repository.Db.Interfaces.Discovery;

public interface IDiscoveryJobRepository
{
    Task AddAsync(DiscoveryJob job, CancellationToken ct);
    Task<DiscoveryJob?> GetAsync(Guid id, CancellationToken ct);
    Task<DiscoveryJob?> GetPendingForTargetAsync(Guid targetId, CancellationToken ct);
    void Remove(DiscoveryJob job);
    Task<IReadOnlyList<DiscoveryJob>> GetStaleQueuedAsync(
        DateTimeOffset cutoff, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
