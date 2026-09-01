using NewsApplication.Domain.DTOs.Discovery;

namespace NewsApplication.Service.Interfaces.Client;

public enum DiscoveryStartOutcome
{
    Accepted,
    InvalidRequest,
    Conflict,
    RateLimited,
    Unavailable
}

public sealed record DiscoveryStartResult(
    DiscoveryStartOutcome Outcome,
    TimeSpan? RetryAfter = null,
    int? QueuePosition = null,
    string? Error = null);

public interface IDiscoveryPipelineClient
{
    Task<DiscoveryStartResult> StartJobAsync(
        StartDiscoveryJobRequestDTO request,
        CancellationToken ct);

    Task<PipelineHealthDTO?> GetHealthAsync(CancellationToken ct);

    Task<IReadOnlyList<FeedValidationResultDTO>> ValidateFeedsAsync(
        IReadOnlyList<FeedValidationRequestDTO> feeds,
        CancellationToken ct);
}
