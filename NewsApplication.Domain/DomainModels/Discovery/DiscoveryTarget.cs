using System.ComponentModel.DataAnnotations;

namespace NewsApplication.Domain.DomainModels.Discovery;

/// <summary>
/// Durable intent: "keep discovering news sources for this location". One row per location,
/// and the only table the dispatcher reads — everything else in the discovery schema is a
/// consequence of a job this produced.
///
/// The pipeline has no clock and no database, so all scheduling state lives here.
/// </summary>
public class DiscoveryTarget : BaseEntity
{
    [Required]
    [StringLength(2)]
    public string CountryIso2 { get; set; } = null!;

    public Country? Country { get; set; }

    /// <summary>Null for a country-level target. This nullability is why the uniqueness of
    /// (CountryIso2, CityId) is enforced by two partial indexes rather than a composite key —
    /// a Postgres primary key cannot contain a nullable column.</summary>
    public Guid? CityId { get; set; }

    public City? City { get; set; }

    /// <summary>Demand-driven ordering when more targets are due than capacity allows. Higher
    /// runs first.</summary>
    public int Priority { get; set; }

    /// <summary>30 | 90 | 180. Discovery is quarterly by default; article freshness comes from
    /// the RSS poller, not from re-running discovery.</summary>
    public int CadenceDays { get; set; } = 90;

    public DateTimeOffset NextDueAt { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>Drives the capped backoff: NextDueAt = now + min(2^n hours, 7 days), and at 5
    /// the target is disabled. Reset to 0 by any completed job.
    ///
    /// A 429 from POST /jobs must never touch this — it means the queue is full, not that the
    /// location is broken, and during the 249-country bootstrap most dispatches will get one.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>A "completed" run that returned zero sources feeds this counter, never
    /// ConsecutiveFailures. The location genuinely yielded nothing, which is a fact about the
    /// location rather than a fault to back off from.</summary>
    public int ConsecutiveEmptyRuns { get; set; }

    /// <summary>The kill switch. Cleared automatically at ConsecutiveFailures >= 5, and
    /// settable by hand from the admin view.</summary>
    public bool IsEnabled { get; set; } = true;

    public ICollection<DiscoveryJob> Jobs { get; set; } = new List<DiscoveryJob>();
}