using Microsoft.EntityFrameworkCore;
using NewsApplication.Domain.DomainModels.Discovery;
using NewsApplication.Repository.Db.Interfaces.Discovery;

namespace NewsApplication.Repository.Db.Implementations.Discovery;

public sealed class DiscoveryTargetRepository : IDiscoveryTargetRepository
{
    private readonly ApplicationDbContext _db;

    public DiscoveryTargetRepository(ApplicationDbContext db) => _db = db;

    public Task<DiscoveryTarget?> GetAsync(Guid id, CancellationToken ct) =>
        _db.DiscoveryTargets
            .Include(x => x.Country)
            .Include(x => x.City)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<DiscoveryTarget>> GetDueAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken ct)
    {
        if (limit <= 0)
            return [];

        return await _db.DiscoveryTargets
            .Where(x => x.IsEnabled &&
                        x.NextDueAt <= now &&
                        !x.Jobs.Any(j => j.Status == DiscoveryJobStatus.Queued ||
                                         j.Status == DiscoveryJobStatus.Running))
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.NextDueAt)
            .Include(x => x.Country)
            .Include(x => x.City)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
