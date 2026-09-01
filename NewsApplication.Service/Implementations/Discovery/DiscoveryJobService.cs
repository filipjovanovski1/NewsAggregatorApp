using Microsoft.Extensions.Options;
using NewsApplication.Domain.DomainModels.Discovery;
using NewsApplication.Domain.DTOs.Discovery;
using NewsApplication.Repository.Db.Interfaces.Discovery;
using NewsApplication.Service.Implementations.Client;
using NewsApplication.Service.Interfaces.Client;
using NewsApplication.Service.Interfaces.Discovery;

namespace NewsApplication.Service.Implementations.Discovery;

public sealed class DiscoveryJobService : IDiscoveryJobService
{
    private readonly IDiscoveryJobRepository _jobs;
    private readonly INewsSourceRepository _sources;
    private readonly IDiscoveryPipelineClient _pipeline;
    private readonly DiscoveryPipelineOptions _options;

    public DiscoveryJobService(
        IDiscoveryJobRepository jobs,
        INewsSourceRepository sources,
        IDiscoveryPipelineClient pipeline,
        IOptions<DiscoveryPipelineOptions> options)
    {
        _jobs = jobs;
        _sources = sources;
        _pipeline = pipeline;
        _options = options.Value;
    }

    public async Task<DiscoveryStartResult> StartAsync(
        DiscoveryTarget target,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var job = await _jobs.GetPendingForTargetAsync(target.Id, ct);
        var createdNow = job is null;
        if (job is null)
        {
            job = new DiscoveryJob
            {
                Id = Guid.NewGuid(),
                DiscoveryTargetId = target.Id,
                Status = DiscoveryJobStatus.Pending,
                StartedAt = now
            };

            await _jobs.AddAsync(job, ct);
            await _jobs.SaveChangesAsync(ct); // Must commit before the pipeline can callback.
        }

        var knownDomains = await _sources.GetKnownDomainsAsync(
            target.CountryIso2, target.CityId, ct);
        var callbackBase = _options.CallbackBaseUrl.TrimEnd('/');
        var request = new StartDiscoveryJobRequestDTO
        {
            JobId = job.Id,
            CallbackUrl = $"{callbackBase}/api/discovery/jobs/{job.Id}/result",
            Iso2 = target.CountryIso2,
            Iso3 = target.Country?.Iso3,
            CountryName = target.Country?.Name,
            City = target.City?.Name,
            CityLocalName = target.City?.LocalName,
            CityId = target.CityId,
            KnownDomains = knownDomains.ToList()
        };

        DiscoveryStartResult result;
        try
        {
            result = await _pipeline.StartJobAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            result = new DiscoveryStartResult(
                DiscoveryStartOutcome.Unavailable,
                TimeSpan.FromMinutes(5),
                Error: ex.Message);
        }

        switch (result.Outcome)
        {
            case DiscoveryStartOutcome.Accepted:
                job.Status = DiscoveryJobStatus.Queued;
                break;
            case DiscoveryStartOutcome.InvalidRequest:
                job.Status = DiscoveryJobStatus.Failed;
                job.CompletedAt = now;
                job.ErrorStage = "dispatch";
                job.ErrorType = "invalid_request";
                job.ErrorMessage = result.Error;
                target.IsEnabled = false;
                break;
            case DiscoveryStartOutcome.Conflict:
                // A conflict for a newly minted id means another job is already in flight for
                // the target. Remove our unused row. A retry of an existing pending id remains.
                if (createdNow)
                    _jobs.Remove(job);
                break;
            case DiscoveryStartOutcome.RateLimited:
            case DiscoveryStartOutcome.Unavailable:
                job.Status = DiscoveryJobStatus.Pending;
                target.NextDueAt = now.Add(
                    result.RetryAfter ?? TimeSpan.FromMinutes(5));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        await _jobs.SaveChangesAsync(ct);
        return result;
    }
}
