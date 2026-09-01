namespace NewsApplication.Domain.DomainModels.Discovery;

/// <summary>
/// NewsSource.Classification values, stored exactly as the pipeline sends them.
///
/// These are strings rather than an enum on purpose. They arrive over the wire and the §5.3
/// upserts are hand-written SQL with NpgsqlParameters, so an enum would need a converter that
/// the raw SQL path bypasses entirely — and the failure mode is silent: rows written as
/// "NewsSource" that no query matching "NEWS_SOURCE" ever finds. Constants give the same
/// protection against typos without that trap.
/// </summary>
public static class SourceClassifications
{
    public const string NewsSource = "NEWS_SOURCE";
    public const string DiscoverySource = "DISCOVERY_SOURCE";

    /// <summary>Never persisted — rejects are reported only as a count in stats.classified
    /// and never appear in the sources array.</summary>
    public const string Reject = "REJECT";

    public static bool IsKnown(string? value) =>
        value is NewsSource or DiscoverySource;
}

/// <summary>
/// NewsSourceScope.PollingTier values — lowercase on the wire, and stored verbatim for the
/// same reason as SourceClassifications. This is what drives the RSS poller's cadence.
/// </summary>
public static class PollingTiers
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
    public const string Backup = "backup";

    public static bool IsKnown(string? value) =>
        value is High or Medium or Low or Backup;
}