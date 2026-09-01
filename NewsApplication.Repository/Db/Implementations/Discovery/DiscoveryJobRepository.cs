using Microsoft.EntityFrameworkCore;
using NewsApplication.Domain.DomainModels.Discovery;
using NewsApplication.Repository.Db.Interfaces.Discovery;

namespace NewsApplication.Repository.Db.Implementations.Discovery;

public sealed class DiscoveryJobRepository : IDiscoveryJobRepository
{
    private readonly ApplicationDbContext _db;

    public DiscoveryJobRepository(ApplicationDbContext db) => _db = db;

    public Task AddAsync(DiscoveryJob job, CancellationToken ct) =>
        _db.DiscoveryJobs.AddAsync(job, ct).AsTask();

    public Task<DiscoveryJob?> GetAsync(Guid id, CancellationToken ct) =>
        _db.DiscoveryJobs
            .Include(x => x.DiscoveryTarget)
                .ThenInclude(x => x!.Country)
            .Include(x => x.DiscoveryTarget)
                .ThenInclude(x => x!.City)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<DiscoveryJob?> GetPendingForTargetAsync(Guid targetId, CancellationToken ct) =>
        _db.DiscoveryJobs
            .Where(x => x.DiscoveryTargetId == targetId &&
                        x.Status == DiscoveryJobStatus.Pending)
            .OrderBy(x => x.StartedAt)
            .FirstOrDefaultAsync(ct);

    public void Remove(DiscoveryJob job) => _db.DiscoveryJobs.Remove(job);

    public async Task<IReadOnlyList<DiscoveryJob>> GetStaleQueuedAsync(
        DateTimeOffset cutoff,
        CancellationToken ct) =>
        await _db.DiscoveryJobs
            .Where(x => x.Status == DiscoveryJobStatus.Queued && x.StartedAt <= cutoff)
            .Include(x => x.DiscoveryTarget)
            .OrderBy(x => x.StartedAt)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
