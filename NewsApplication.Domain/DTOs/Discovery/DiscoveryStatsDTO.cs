using System.Text.Json.Serialization;

namespace NewsApplication.Domain.DTOs.Discovery;

/// <summary>
/// Run counters, persisted whole onto DiscoveryJob.Stats as jsonb.
///
/// Every field is nullable, and that is load-bearing rather than defensive: QueriesRun and
/// QueriesEmpty are absent from the committed sample entirely, because that run reused the
/// discovery cache and never issued a search. Making them required throws on the first
/// cached run.
/// </summary>
public sealed record DiscoveryStatsDTO
{
    /// <summary>Candidate domains found before crawling and classification.</summary>
    public int? Discovered { get; init; }

    /// <summary>Absent when the run reused the discovery cache.</summary>
    public int? QueriesRun { get; init; }

    /// <summary>Absent when the run reused the discovery cache.</summary>
    public int? QueriesEmpty { get; init; }

    public int? CrawlFailures { get; init; }

    public int? ClassifyErrors { get; init; }

    public DiscoveryClassifiedCountsDTO? Classified { get; init; }

    /// <summary>How many sources are in the payload — i.e. Discovered minus rejects and failures.</summary>
    public int? Returned { get; init; }
}

/// <summary>
/// The classifier's verdict tally. REJECT is the only place rejected sites are ever reported;
/// they are not present in Sources.
///
/// These three keys are SCREAMING_SNAKE_CASE on the wire, not snake_case like every other
/// field in the payload, so they are pinned with [JsonPropertyName] rather than left to the
/// naming policy. SnakeCaseLower would produce "news_source", which only matches "NEWS_SOURCE"
/// by way of PropertyNameCaseInsensitive — true today, but far too quiet a dependency to rest
/// three counters on.
/// </summary>
public sealed record DiscoveryClassifiedCountsDTO
{
    [JsonPropertyName("NEWS_SOURCE")]
    public int? NewsSource { get; init; }

    [JsonPropertyName("DISCOVERY_SOURCE")]
    public int? DiscoverySource { get; init; }

    [JsonPropertyName("REJECT")]
    public int? Reject { get; init; }
}
