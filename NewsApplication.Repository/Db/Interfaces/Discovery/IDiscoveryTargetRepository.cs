using NewsApplication.Domain.DomainModels.Discovery;

namespace NewsApplication.Repository.Db.Interfaces.Discovery;

public interface IDiscoveryTargetRepository
{
    Task<DiscoveryTarget?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<DiscoveryTarget>> GetDueAsync(
        DateTimeOffset now, int limit, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}