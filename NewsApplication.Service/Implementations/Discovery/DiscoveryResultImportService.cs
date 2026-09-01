using NewsApplication.Domain.DomainModels.Discovery;
using NewsApplication.Domain.DTOs.Discovery;
using NewsApplication.Repository.Db.Interfaces.Discovery;
using NewsApplication.Service.Interfaces.Discovery;

namespace NewsApplication.Service.Implementations.Discovery;

public sealed class DiscoveryResultImportService : IDiscoveryResultImportService
{
    private readonly IDiscoveryJobRepository _jobs;
    private readonly INewsSourceRepository _sources;

    public DiscoveryResultImportService(
        IDiscoveryJobRepository jobs,
        INewsSourceRepository sources)
    {
        _jobs = jobs;
        _sources = sources;
    }

    public async Task<DiscoveryImportOutcome> ImportAsync(
        Guid jobId,
        DiscoveryResultDTO result,
        CancellationToken ct)
    {
        var job = await _jobs.GetAsync(jobId, ct);
        if (job is null)
            return DiscoveryImportOutcome.NotFound;
        if (job.Status == DiscoveryJobStatus.Completed)
            return DiscoveryImportOutcome.AlreadyCompleted;

        await _sources.ImportResultAsync(job, result, ct);
        return DiscoveryImportOutcome.Imported;
    }
}
