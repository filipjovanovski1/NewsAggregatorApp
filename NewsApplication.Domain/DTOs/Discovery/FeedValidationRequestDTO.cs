namespace NewsApplication.Domain.DTOs.Discovery;

/// <summary>
/// One feed submitted to POST /feeds/validate. SourceFeedId is an opaque correlation key:
/// validation results must be joined back to database rows by this value, never by URL.
/// </summary>
public sealed record FeedValidationRequestDTO
{
    public Guid SourceFeedId { get; init; }

    public string Url { get; init; } = null!;

    public string? Domain { get; init; }
}
