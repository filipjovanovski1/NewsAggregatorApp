namespace NewsApplication.Domain.DTOs.Ingestion;

public sealed record FeedClaimRequestDTO(
    string WorkerId,
    int MaxItems,
    int LeaseSeconds);

public sealed record FeedClaimResponseDTO(IReadOnlyList<FeedClaimDTO> Claims);

public sealed record FeedClaimDTO(
    Guid LeaseId,
    long LeaseVersion,
    DateTimeOffset LeaseExpiresAt,
    Guid FeedId,
    string FeedUrl,
    Guid SourceId,
    string SourceDomain,
    string? SourceName,
    string? Language,
    string? Etag,
    string? LastModified,
    DateTimeOffset? LastPolledAt,
    string? PollingTier);

public sealed record CompleteFeedPollDTO(
    long LeaseVersion,
    FeedPollMetadataDTO Feed,
    IReadOnlyList<IngestedArticleDTO> Articles);

public sealed record FeedPollMetadataDTO(
    string RequestedUrl,
    string FinalUrl,
    int HttpStatus,
    string? Etag,
    string? LastModified,
    DateTimeOffset PolledAt);

public sealed record IngestedArticleDTO(
    string ExternalEntryId,
    string EntryUrl,
    string CanonicalUrl,
    string Title,
    string? Description,
    string? ContentText,
    string? Author,
    string? Language,
    string? ImageUrl,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ModifiedAt,
    IReadOnlyList<string> Categories,
    string? ContentHash,
    string ExtractionStatus,
    string? Extractor,
    string? ExtractorVersion);

public sealed record FailFeedPollDTO(
    long LeaseVersion,
    DateTimeOffset FailedAt,
    string Stage,
    string ErrorType,
    string Message,
    int? HttpStatus,
    int? RetryAfterSeconds);

public enum FeedIngestionWriteOutcome
{
    Applied,
    AlreadyApplied,
    NotFound,
    StaleLease
}
