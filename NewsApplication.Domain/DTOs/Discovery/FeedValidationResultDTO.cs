namespace NewsApplication.Domain.DTOs.Discovery;

/// <summary>
/// Validation state returned for one requested feed. Url is the post-redirect canonical URL
/// and may differ from the submitted URL.
/// </summary>
public sealed record FeedValidationResultDTO
{
    public Guid SourceFeedId { get; init; }

    public bool Valid { get; init; }

    public string? Status { get; init; }

    public string? Url { get; init; }

    public string? Title { get; init; }

    public int? EntryCount { get; init; }

    public DateTimeOffset? LatestEntry { get; init; }

    public bool? HasFullContent { get; init; }

    public string? Language { get; init; }

    public double? ExternalLinkRatio { get; init; }

    public int? DistinctSources { get; init; }
}
