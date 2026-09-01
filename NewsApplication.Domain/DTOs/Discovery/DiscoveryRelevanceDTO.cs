namespace NewsApplication.Domain.DTOs.Discovery;

/// <summary>
/// How relevant this source is to the location the run targeted — a fact about the
/// (source, location) pair, never about the site. The same domain scores differently for
/// different targets, which is why this lands on NewsSourceScope rather than NewsSource.
/// </summary>
public sealed record DiscoveryRelevanceDTO
{
    /// <summary>0–100 (note: a different scale from DiscoverySourceDTO.Confidence, which is 0–1).</summary>
    public double? Score { get; init; }

    /// <summary>"high" | "medium" | "low" | "backup". Drives the poller's cadence.</summary>
    public string? PollingTier { get; init; }

    public int? SearchOccurrences { get; init; }

    /// <summary>The search queries this source turned up in. Useful for judging a geographic
    /// leak by eye — a source matched only by country-name queries in a foreign language is
    /// the shape "dnevnik.bg on a North Macedonia run" takes.</summary>
    public List<string> MatchedQueries { get; init; } = new();
}
