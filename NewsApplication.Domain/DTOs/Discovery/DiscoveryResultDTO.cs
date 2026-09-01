namespace NewsApplication.Domain.DTOs.Discovery;

/*

 DiscoveryResultDTO is the payload the news-source-discovery pipeline POSTs back to
 /api/discovery/jobs/{jobId}/result when a run finishes. It is a wire contract, not a domain
 model: property names map from snake_case via DiscoveryJsonOptions, and nothing here is
 validated or interpreted — that happens in the import service.

 Two rules from the contract that shape this type:

 - Status is binding, Warnings is advisory. A "failed" run carries Sources == [] and a
   non-null Error, and must upsert nothing. Warnings can appear on a perfectly good run
   (the committed sample has one) and never block the upsert.

 - A zero-source "completed" is not a failure. It means the location genuinely yielded
   nothing, and feeds ConsecutiveEmptyRuns rather than ConsecutiveFailures.

 Status, and the Classification/PollingTier strings further down, stay as strings here on
 purpose: an unrecognised value from the pipeline should reach the import service and be
 rejected there with a real message, not throw inside the deserializer.

*/
public sealed record DiscoveryResultDTO
{
    /// <summary>Contract version, currently 1. Mandatory — reject anything else on import.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Echoes the job id .NET generated and sent on POST /jobs.</summary>
    public string? JobId { get; init; }

    /// <summary>"completed" or "failed". Binding.</summary>
    public string? Status { get; init; }

    public DiscoveryLocationDTO? Location { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>Null on a failed run — do not assume it is present.</summary>
    public DiscoveryStatsDTO? Stats { get; init; }

    /// <summary>Advisory only. Surface on the job row, never let it block the upsert.</summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>Null on success. On failure, Stage is what distinguishes a cheap re-dispatch
    /// ("queued") from an expensive one ("cancelled", the crawl budget is already spent).</summary>
    public DiscoveryErrorDTO? Error { get; init; }

    /// <summary>Always empty when Status != "completed". Rejects are never sent — they appear
    /// only as a count in Stats.Classified.</summary>
    public List<DiscoverySourceDTO> Sources { get; init; } = new();
}