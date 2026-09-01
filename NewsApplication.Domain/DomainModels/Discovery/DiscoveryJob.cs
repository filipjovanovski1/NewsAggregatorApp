using NewsApplication.Domain.DTOs.Discovery;

namespace NewsApplication.Domain.DomainModels.Discovery;

/// <summary>
/// One execution of a DiscoveryTarget.
///
/// The row is inserted *before* POST /jobs is called, so a lost response cannot leave the
/// pipeline running a job this side has no record of. That also means Id is minted here and
/// sent outbound — it is not database-generated, and the configuration pins that with
/// ValueGeneratedNever.
/// </summary>
public class DiscoveryJob : BaseEntity
{
    public Guid DiscoveryTargetId { get; set; }

    public DiscoveryTarget? DiscoveryTarget { get; set; }

    public DiscoveryJobStatus Status { get; set; } = DiscoveryJobStatus.Pending;

    /// <summary>When this side created the row and dispatched it — not the pipeline's
    /// started_at, which can be much later if the job sat in the queue.</summary>
    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The error block flattened. Stage is the useful one operationally: "queued"
    /// means the job never started and is free to re-dispatch, "cancelled" means it burned
    /// crawl budget partway through.</summary>
    public string? ErrorStage { get; set; }

    public string? ErrorType { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>The whole stats block, not just a count — the ratio of discovered to returned
    /// to rejected is what tells you whether a location is genuinely quiet or the classifier
    /// is throwing everything away. Every field inside is nullable; queries_run and
    /// queries_empty are absent entirely whenever a run reuses the discovery cache.</summary>
    public DiscoveryStatsDTO? Stats { get; set; }

    /// <summary>Advisory only. Warnings coexist with a completed run and never change what
    /// gets upserted — they are surfaced here so the admin view can show them.</summary>
    public List<string> Warnings { get; set; } = new();
}