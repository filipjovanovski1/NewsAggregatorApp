using System.ComponentModel.DataAnnotations;

namespace NewsApplication.Domain.DomainModels.Discovery;

/// <summary>
/// A validated RSS feed — a table rather than a jsonb array of URLs, because the poller needs
/// somewhere to hang LastPolledAt, LastEtag and a per-feed IsActive, and because the
/// validation evidence below is exactly what should decide polling order.
///
/// Three groups of fields with three different owners, and the middle one is the one that
/// gets written wrong:
///
///   Url, Title                          -- discovery, per run
///   EntryCount .. IsActive              -- pipeline validation, per run AND per nightly
///                                          POST /feeds/validate
///   LastPolledAt, LastEtag              -- this app's poller, per poll
///
/// Discovery runs quarterly. If the middle group is only written at discovery time, a feed
/// found in January reports January's LatestEntry for ninety days and every freshness-driven
/// polling decision rots with it.
/// </summary>
public class NewsSourceFeed : BaseEntity
{
    public Guid NewsSourceId { get; set; }

    public NewsSource? NewsSource { get; set; }

    /// <summary>Unique per source. Validation reports the *post-redirect* URL, which may
    /// differ from the one submitted — persist what comes back, it is the canonical thing to
    /// poll. Join validation results on the feed id, never on this.</summary>
    [Required]
    public string Url { get; set; } = null!;

    public string? Title { get; set; }

    public string? Language { get; set; }

    public int? EntryCount { get; set; }

    public DateTimeOffset? LatestEntry { get; set; }

    /// <summary>Full article text in the feed vs headlines only.</summary>
    public bool? HasFullContent { get; set; }

    /// <summary>Aggregator discriminator: how much of the feed links off-domain. High is
    /// strong evidence; low is not evidence against, since aggregators that rewrite entry
    /// links to their own domain report 0.0 — as every candidate in the sample does.</summary>
    public double? ExternalLinkRatio { get; set; }

    public int? DistinctSources { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Poller state. The pipeline never sends this and no upsert built from a DTO may
    /// write it — clobbering it re-ingests the feed's whole backlog on the next poll.</summary>
    public DateTimeOffset? LastPolledAt { get; set; }

    /// <summary>Poller state, same rule as LastPolledAt. Sent as If-None-Match so an unchanged
    /// feed costs a 304 instead of a full body.</summary>
    public string? LastEtag { get; set; }
}