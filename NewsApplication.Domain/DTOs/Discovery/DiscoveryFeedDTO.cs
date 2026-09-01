namespace NewsApplication.Domain.DTOs.Discovery;

/// <summary>
/// A validated RSS feed. This same shape comes back from POST /feeds/validate, which is what
/// refreshes these fields nightly between quarterly discovery runs — if they were only written
/// at discovery time, a feed found in January would report January's LatestEntry for ninety
/// days and every freshness-driven polling decision would rot with it.
///
/// Everything here is pipeline-owned. NewsSourceFeed.LastPolledAt and LastEtag are .NET poller
/// state, appear nowhere in this DTO, and must never be touched by an upsert built from it.
/// </summary>
public sealed record DiscoveryFeedDTO
{
    /// <summary>The post-redirect URL, which may differ from the one that was submitted for
    /// validation. This is the canonical thing to poll — persist it.</summary>
    public string? Url { get; init; }

    /// <summary>Null in the sample for several feeds.</summary>
    public string? Title { get; init; }

    public int? EntryCount { get; init; }

    public DateTimeOffset? LatestEntry { get; init; }

    /// <summary>Full article text in the feed vs headlines only.</summary>
    public bool? HasFullContent { get; init; }

    /// <summary>Null in the sample for several feeds.</summary>
    public string? Language { get; init; }

    /// <summary>Aggregator discriminator: how much of the feed links off-domain. High is strong
    /// evidence of an aggregator, but absence is not evidence against — aggregators that rewrite
    /// entry links to their own domain report 0.0, as every candidate in the sample does.</summary>
    public double? ExternalLinkRatio { get; init; }

    /// <summary>The other aggregator discriminator, and subject to the same caveat.</summary>
    public int? DistinctSources { get; init; }
}
