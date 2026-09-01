namespace NewsApplication.Domain.DTOs.Discovery;

/// <summary>
/// Why the classifier decided what it decided. Advisory: useful for the admin view and for
/// auditing misclassifications, and not something the upsert depends on.
/// </summary>
public sealed record DiscoveryEvidenceDTO
{
    /// <summary>Free-text LLM rationale.</summary>
    public string? Reason { get; init; }

    public int? ArticleLikePaths { get; init; }

    public bool? HasDatePatterns { get; init; }

    public int? AuthorCount { get; init; }
}
