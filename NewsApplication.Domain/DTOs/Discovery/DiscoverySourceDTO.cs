namespace NewsApplication.Domain.DTOs.Discovery;

/// <summary>
/// One discovered site. Maps onto NewsSource (per domain, global), its NewsSourceFeed rows,
/// and one NewsSourceScope row for the location this run targeted.
/// </summary>
public sealed record DiscoverySourceDTO
{
    /// <summary>Unique key for NewsSource. Note the pipeline does not collapse subdomains —
    /// "mia.mk" and "new.mia.mk" can both arrive in one run as distinct sources.</summary>
    public string? Domain { get; init; }

    public string? Name { get; init; }

    public string? Url { get; init; }

    /// <summary>False when the pipeline recognised the domain from known_domains and skipped
    /// the crawl. Such a source still arrives here and must still stamp the current
    /// DiscoveryJobId on its scope row so it survives the staleness sweep — but only the scope
    /// row and NewsSource.LastDiscoveredAt get written; the site facts and feeds are stale
    /// echoes and must be left alone.</summary>
    public bool SourceFactsRefreshed { get; init; }

    public string? Language { get; init; }

    /// <summary>"NEWS_SOURCE" or "DISCOVERY_SOURCE". Both are stored in NewsSource; the poller
    /// filters on it. Rejects never reach this DTO.</summary>
    public string? Classification { get; init; }

    /// <summary>0–1.</summary>
    public double? Confidence { get; init; }

    /// <summary>Unbounded and messy — the committed sample's worst offender carries 73 entries,
    /// some of them junk. No length assumption anywhere downstream; lowercase on the way in.</summary>
    public List<string> Categories { get; init; } = new();

    /// <summary>Can legitimately be empty — the sample has sources with no feeds at all.</summary>
    public List<DiscoveryFeedDTO> Feeds { get; init; } = new();

    public DiscoveryRelevanceDTO? Relevance { get; init; }

    public DiscoveryEvidenceDTO? Evidence { get; init; }
}