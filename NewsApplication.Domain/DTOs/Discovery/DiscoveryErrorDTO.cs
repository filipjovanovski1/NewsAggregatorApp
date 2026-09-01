namespace NewsApplication.Domain.DTOs.Discovery;

/// <summary>
/// Present only when Status == "failed". Flattened onto DiscoveryJob.Error.
///
/// Stage carries the operational meaning. The pipeline calls back for every job it accepts,
/// including on SIGTERM, so a deploy produces ordinary failure payloads rather than silence:
///
///   "queued"    — never started, free to re-dispatch immediately
///   "cancelled" — an in-flight crawl was killed, so crawl budget is already spent
///
/// Both arrive in this same shape, which is why the callback needs no second deserializer.
/// </summary>
public sealed record DiscoveryErrorDTO
{
    public string? Stage { get; init; }

    public string? Type { get; init; }

    public string? Message { get; init; }
}