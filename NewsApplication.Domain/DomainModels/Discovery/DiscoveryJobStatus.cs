namespace NewsApplication.Domain.DomainModels.Discovery;

/// <summary>
/// Lifecycle of a single discovery run. Entirely .NET-owned — none of these values appear on
/// the wire, which is why this is a real enum while Classification and PollingTier stay
/// strings (see DiscoveryWireValues).
///
/// Queued and Running are deliberately distinct even though the pipeline does not currently
/// signal the transition between them, because they carry different sweep deadlines: a job
/// waiting for a worker is healthy and gets ~6h, a job actually crawling gets ~30min.
/// Conflating them would sweep queued jobs as Stale, increment ConsecutiveFailures, and
/// eventually disable every country queued behind a slow one.
/// </summary>
public enum DiscoveryJobStatus
{
    /// <summary>Row inserted, not yet dispatched — or dispatched and answered 429/503, which
    /// is a scheduling signal rather than a failure and leaves the job here.</summary>
    Pending = 0,

    /// <summary>The pipeline answered 202. Note this is not Running: capacity is one worker
    /// and a job can sit in its queue for a long time legitimately.</summary>
    Queued = 1,

    /// <summary>First evidence of actual work. Unused until the pipeline signals it.</summary>
    Running = 2,

    /// <summary>A "completed" callback landed. Zero sources still counts as completed.</summary>
    Completed = 3,

    /// <summary>A "failed" callback landed, or the dispatch was rejected as bad input (400).</summary>
    Failed = 4,

    /// <summary>No callback arrived within the sweep horizon — genuine pipeline death, since
    /// the pipeline calls back for every job it accepts, including on SIGTERM.</summary>
    Stale = 5,
}