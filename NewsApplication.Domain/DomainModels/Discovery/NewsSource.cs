using System.ComponentModel.DataAnnotations;

namespace NewsApplication.Domain.DomainModels.Discovery;

/// <summary>
/// A news site, one row per domain globally. Not per location: the same domain is discovered
/// again for every location it is relevant to, and what differs between those runs is the
/// relevance, which lives on NewsSourceScope.
///
/// DISCOVERY_SOURCE rows live here too, distinguished only by Classification — the poller
/// filters them out, recursive discovery reads them.
/// </summary>
public class NewsSource : BaseEntity
{
    /// <summary>Unique, and canonical: subdomains are normalized away at import (new.mia.mk
    /// and mia.mk are one row). The pipeline does not collapse them, and left alone they
    /// produce two poller schedules and every article ingested twice.</summary>
    [Required]
    public string Domain { get; set; } = null!;

    public string? Name { get; set; }

    public string? Url { get; set; }

    public string? Language { get; set; }

    /// <summary>SourceClassifications.NewsSource or .DiscoverySource, verbatim from the wire.</summary>
    public string? Classification { get; set; }

    /// <summary>0–1. Note the different scale from NewsSourceScope.Score, which is 0–100.</summary>
    public double? Confidence { get; set; }

    /// <summary>jsonb. Unbounded and messy — 73 entries on the worst source in the sample,
    /// some of it junk. No length assumption, no enum, no lookup table; lowercased on import.</summary>
    public List<string> Categories { get; set; } = new();

    public DateTimeOffset FirstDiscoveredAt { get; set; }

    /// <summary>Stamped on every run that returns this domain, including runs where
    /// source_facts_refreshed is false and nothing else about the source is written.</summary>
    public DateTimeOffset LastDiscoveredAt { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<NewsSourceFeed> Feeds { get; set; } = new List<NewsSourceFeed>();

    public ICollection<NewsSourceScope> Scopes { get; set; } = new List<NewsSourceScope>();
}