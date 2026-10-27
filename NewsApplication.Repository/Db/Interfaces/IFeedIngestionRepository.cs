using NewsApplication.Domain.DTOs.Ingestion;

namespace NewsApplication.Repository.Db.Interfaces;

public interface IFeedIngestionRepository
{
    Task<IReadOnlyList<FeedClaimDTO>> ClaimAsync(
        FeedClaimRequestDTO request,
        DateTimeOffset now,
        CancellationToken ct);

    Task<FeedIngestionWriteOutcome> CompleteAsync(
        Guid leaseId,
        string idempotencyKey,
        CompleteFeedPollDTO request,
        DateTimeOffset now,
        CancellationToken ct);

    Task<FeedIngestionWriteOutcome> FailAsync(
        Guid leaseId,
        FailFeedPollDTO request,
        DateTimeOffset now,
        CancellationToken ct);
}
