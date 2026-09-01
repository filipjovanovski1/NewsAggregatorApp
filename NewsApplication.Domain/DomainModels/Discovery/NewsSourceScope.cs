using System.ComponentModel.DataAnnotations;

namespace NewsApplication.Domain.DomainModels.Discovery;

/// <summary>
/// How relevant one source is to one location — the join that makes the whole schema work.
///
/// A score is a fact about a (source, location) pair, never about the site. aljazeera.com will
/// be discovered again for Sarajevo and Doha with a different score each time, and
/// balkaninsight.com will show up for half the Balkans. Flattening Score and PollingTier onto
/// NewsSource means the second city silently overwrites the first, which is the one thing here
/// that is expensive to undo later.
/// </summary>
public class NewsSourceScope : BaseEntity
{
    public Guid NewsSourceId { get; set; }

    public NewsSource? NewsSource { get; set; }

    [Required]
    [StringLength(2)]
    public string CountryIso2 { get; set; } = null!;

    public Country? Country { get; set; }

    /// <summary>Null for a country-level run. Same nullable-key problem as DiscoveryTarget —
    /// hence the surrogate Id above plus two partial unique indexes.</summary>
    public Guid? CityId { get; set; }

    public City? City { get; set; }

    /// <summary>0–100. Note the different scale from NewsSource.Confidence, which is 0–1.</summary>
    public double? Score { get; set; }

    /// <summary>One of PollingTiers. Drives how often the RSS poller reads this source's
    /// feeds — 10 minutes at high, 6 hours at backup.</summary>
    public string? PollingTier { get; set; }

    public int? SearchOccurrences { get; set; }

    /// <summary>The search queries this source turned up in. Not part of the minimum schema,
    /// kept because it is the evidence for the still-open geographic-leak decision: a source
    /// matched only by foreign-language country-name queries is the shape "dnevnik.bg on a
    /// North Macedonia run" takes. Adding it after the bootstrap would mean re-running
    /// discovery to backfill it.</summary>
    public List<string> MatchedQueries { get; set; } = new();

    public DateTimeOffset DiscoveredAt { get; set; }

    /// <summary>The run that last confirmed this pairing. Sources the pipeline skipped as
    /// already-known still arrive in the payload and must still stamp this, or the sweep below
    /// marks them stale.</summary>
    public Guid DiscoveryJobId { get; set; }

    public DiscoveryJob? DiscoveryJob { get; set; }

    /// <summary>Set by the staleness sweep that runs after every completed job: any scope row
    /// for that (CountryIso2, CityId) whose DiscoveryJobId is not the job just completed no
    /// longer appears for the location.
    ///
    /// A flag rather than a delete, so the history of what was once relevant survives. Without
    /// the sweep, a source that was high tier for MK in January and absent from the April run
    /// keeps its January tier forever — invisibly, which is why it gets forgotten.</summary>
    public bool IsStale { get; set; }
}
